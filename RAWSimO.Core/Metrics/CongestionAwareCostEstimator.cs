using RAWSimO.Core.Elements;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RAWSimO.Core.Metrics
{
    /// <summary>
    /// Phase C cost estimator. Replaces M1G's static distance with a congestion-aware time
    /// estimate of the form
    ///     T_leg = Σ_e (a + b · dist[e]) + residual[e, carrying, density]
    /// where (a, b) are kinematic coefficients, the residual is looked up in a per-edge table
    /// trained offline (Phase B, design_note.md §10), and density is the live count of nearby
    /// bots at the starting waypoint of each path edge.
    ///
    /// Read-only with respect to all simulator state. Path is computed by a local Dijkstra over
    /// Waypoint.Paths — does NOT use any reservation table or path planner.
    /// </summary>
    public class CongestionAwareCostEstimator
    {
        // Fallback constants when the model file cannot be loaded (degenerate to pure distance).
        private const double DefaultKinematicA = 2.0;
        private const double DefaultKinematicB = 0.7;
        private const double LocalDensityRadiusM = 3.0;
        private const int MaxDensityBand = 2;

        private readonly Instance _instance;
        private readonly string _explicitPath;
        private bool _loaded;
        private bool _loadFailed;
        private double _a = DefaultKinematicA;
        private double _b = DefaultKinematicB;
        private readonly Dictionary<long, double> _edgeResidual = new Dictionary<long, double>();
        private readonly Dictionary<long, double> _typeResidual = new Dictionary<long, double>();
        private readonly Dictionary<int, double> _globalResidual = new Dictionary<int, double>();
        // Stable string-id → small int for EdgeType to share keys with edge/type maps.
        private readonly Dictionary<string, int> _edgeTypeIds = new Dictionary<string, int>();
        // EdgeType per directed (from, to) pair, computed once from waypoint flags.
        private readonly Dictionary<long, int> _edgeTypeByPair = new Dictionary<long, int>();
        // Cache of waypoint id → Waypoint reference for fast dijkstra (kept short-lived).

        public CongestionAwareCostEstimator(Instance instance, string explicitPath = null)
        {
            _instance = instance;
            _explicitPath = explicitPath;
        }

        // ── Public API ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate the time (seconds) for a bot to travel <paramref name="from"/> → <paramref name="to"/>
        /// while empty (carrying = 0) or loaded (carrying = 1).
        /// Returns +∞ if the path is unreachable.
        /// </summary>
        public double EstimateLegTime(Waypoint from, Waypoint to, bool carrying)
        {
            if (from == null || to == null) return double.PositiveInfinity;
            if (from == to) return 0.0;
            EnsureLoaded();

            var path = ShortestPath(from, to, podSafe: carrying);
            if (path == null || path.Count < 2) return double.PositiveInfinity;

            int c = carrying ? 1 : 0;
            double total = 0.0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var u = path[i];
                var v = path[i + 1];
                double dist = u[v];               // edge length in metres
                int dband = ComputeDensityBand(u);
                double residual = LookupResidual(u.ID, v.ID, c, dband);
                total += _a + _b * dist + residual;
            }
            return total;
        }

        // ── Loading ─────────────────────────────────────────────────────────────────────────

        private void EnsureLoaded()
        {
            if (_loaded || _loadFailed) return;
            try
            {
                string path = ResolveModelPath();
                if (path == null || !File.Exists(path))
                {
                    _instance.LogInfo($"CongestionAwareCostEstimator: model file not found, falling back to kinematic-only (a={_a}, b={_b}).");
                    _loadFailed = true; _loaded = true;
                    return;
                }
                LoadFromFile(path);
                _loaded = true;
                _instance.LogInfo($"CongestionAwareCostEstimator: loaded {_edgeResidual.Count} EDGE, {_typeResidual.Count} TYPE, {_globalResidual.Count} GLOBAL rows from {path}. a={_a:F4}, b={_b:F4}");
            }
            catch (Exception ex)
            {
                _instance.LogInfo($"CongestionAwareCostEstimator: load failed ({ex.Message}); using kinematic fallback.");
                _loadFailed = true; _loaded = true;
            }
        }

        private string ResolveModelPath()
        {
            // 1. Explicit path from config has top priority.
            if (!string.IsNullOrEmpty(_explicitPath) && File.Exists(_explicitPath))
                return _explicitPath;
            // 2. Default Material location (alongside the layout / setting / config files).
            string materialDefault = Path.Combine(
                Environment.CurrentDirectory,
                "Material", "Instances", "CoreBenchmark", "congestion_cost_model.csv");
            if (File.Exists(materialDefault)) return materialDefault;
            // 3. Working directory.
            string cwdPath = Path.Combine(Environment.CurrentDirectory, "congestion_cost_model.csv");
            if (File.Exists(cwdPath)) return cwdPath;
            // 4. App base directory.
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "congestion_cost_model.csv");
            if (File.Exists(appPath)) return appPath;
            return null;
        }

        private void LoadFromFile(string path)
        {
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    // key = value form
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(1, eq - 1).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "kinematic_a") _a = double.Parse(val, inv);
                    else if (key == "kinematic_b") _b = double.Parse(val, inv);
                    continue;
                }
                if (line.StartsWith("KIND;")) continue; // header
                string[] parts = line.Split(';');
                if (parts.Length < 8) continue;
                string kind = parts[0];
                int from = int.Parse(parts[1], inv);
                int to = int.Parse(parts[2], inv);
                string edgeType = parts[3];
                int carrying = int.Parse(parts[4], inv);
                int density = int.Parse(parts[5], inv);
                double residual = double.Parse(parts[6], inv);

                int etId = GetOrCreateEdgeTypeId(edgeType);
                switch (kind)
                {
                    case "EDGE":
                        _edgeResidual[EncodeEdgeKey(from, to, carrying, density)] = residual;
                        // Remember the type associated with this directed pair (for fallback lookup).
                        _edgeTypeByPair[EncodePair(from, to)] = etId;
                        break;
                    case "TYPE":
                        _typeResidual[EncodeTypeKey(etId, carrying, density)] = residual;
                        break;
                    case "GLOBAL":
                        _globalResidual[EncodeGlobalKey(carrying, density)] = residual;
                        break;
                }
            }
        }

        // ── Lookups ─────────────────────────────────────────────────────────────────────────

        private double LookupResidual(int from, int to, int carrying, int density)
        {
            if (_loadFailed) return 0.0;
            if (_edgeResidual.TryGetValue(EncodeEdgeKey(from, to, carrying, density), out var r))
                return r;
            int etId;
            if (_edgeTypeByPair.TryGetValue(EncodePair(from, to), out etId))
            {
                if (_typeResidual.TryGetValue(EncodeTypeKey(etId, carrying, density), out r))
                    return r;
            }
            else
            {
                // Edge wasn't seen in training; classify it on the fly.
                etId = ClassifyEdgeOnDemand(from, to);
                if (etId >= 0 && _typeResidual.TryGetValue(EncodeTypeKey(etId, carrying, density), out r))
                    return r;
            }
            if (_globalResidual.TryGetValue(EncodeGlobalKey(carrying, density), out r))
                return r;
            return 0.0;
        }

        private int GetOrCreateEdgeTypeId(string label)
        {
            if (string.IsNullOrEmpty(label)) return -1;
            if (_edgeTypeIds.TryGetValue(label, out var id)) return id;
            id = _edgeTypeIds.Count;
            _edgeTypeIds[label] = id;
            return id;
        }

        private int ClassifyEdgeOnDemand(int fromId, int toId)
        {
            // Best-effort live classification when an edge was never observed in training.
            var graph = _instance.WaypointGraph;
            if (graph == null) return -1;
            // O(N) scan; this is the unobserved-edge cold path, expected to be rare.
            Waypoint fromWp = null, toWp = null;
            foreach (var wp in _instance.Waypoints)
            {
                if (wp.ID == fromId) fromWp = wp;
                if (wp.ID == toId) toWp = wp;
                if (fromWp != null && toWp != null) break;
            }
            if (fromWp == null || toWp == null) return -1;
            string label;
            if (toWp.OutputStation != null || toWp.InputStation != null || toWp.IsQueueWaypoint)
                label = "StationEntrance";
            else if (toWp.PodStorageLocation || fromWp.PodStorageLocation)
                label = "PodStorageAccess";
            else
                label = "Aisle";
            return _edgeTypeIds.TryGetValue(label, out var id) ? id : -1;
        }

        // ── Density ─────────────────────────────────────────────────────────────────────────

        private int ComputeDensityBand(Waypoint wp)
        {
            int count = 0;
            foreach (var b in _instance.Bots)
            {
                if (b.Tier != wp.Tier) continue;
                double dx = b.X - wp.X;
                double dy = b.Y - wp.Y;
                if (Math.Abs(dx) + Math.Abs(dy) < LocalDensityRadiusM)
                    count++;
            }
            return count > MaxDensityBand ? MaxDensityBand : count;
        }

        // ── Dijkstra ────────────────────────────────────────────────────────────────────────

        private List<Waypoint> ShortestPath(Waypoint from, Waypoint to, bool podSafe)
        {
            // Standard Dijkstra over Waypoint.Paths. Optimised for small layouts; for larger
            // layouts replace the open-list scan with a heap.
            var dist = new Dictionary<Waypoint, double> { [from] = 0.0 };
            var prev = new Dictionary<Waypoint, Waypoint>();
            var open = new HashSet<Waypoint> { from };
            var closed = new HashSet<Waypoint>();

            while (open.Count > 0)
            {
                // Pick the open waypoint with smallest known distance.
                Waypoint current = null;
                double bestDist = double.PositiveInfinity;
                foreach (var w in open)
                {
                    double d = dist[w];
                    if (d < bestDist) { bestDist = d; current = w; }
                }
                if (current == null) break;
                if (current == to) break;
                open.Remove(current); closed.Add(current);

                foreach (var nxt in current.Paths)
                {
                    if (closed.Contains(nxt)) continue;
                    if (podSafe && nxt.PodStorageLocation && nxt != to) continue;
                    double cand = bestDist + current[nxt];
                    if (!dist.TryGetValue(nxt, out var existing) || cand < existing)
                    {
                        dist[nxt] = cand;
                        prev[nxt] = current;
                        open.Add(nxt);
                    }
                }
            }
            if (!dist.ContainsKey(to)) return null;

            var path = new List<Waypoint>();
            var cur = to;
            while (cur != null && cur != from)
            {
                path.Add(cur);
                if (!prev.TryGetValue(cur, out var p)) return null;
                cur = p;
            }
            path.Add(from);
            path.Reverse();
            return path;
        }

        // ── Key packing ─────────────────────────────────────────────────────────────────────

        private static long EncodeEdgeKey(int from, int to, int carrying, int density)
            => ((long)from << 32) | ((long)(uint)to << 8) | ((long)(uint)(carrying << 4 | density));

        private static long EncodeTypeKey(int etId, int carrying, int density)
            => ((long)etId << 16) | ((long)(uint)(carrying << 4 | density));

        private static int EncodeGlobalKey(int carrying, int density)
            => (carrying << 4) | density;

        private static long EncodePair(int from, int to)
            => ((long)from << 32) | (uint)to;
    }
}
