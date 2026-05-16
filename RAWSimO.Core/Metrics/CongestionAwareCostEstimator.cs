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
        // Step E: residual values now keyed but also carry (mean, std, p90). Mean lookups go through
        // the legacy dictionaries; the distributional table lets Step F integrate over density bands.
        private readonly Dictionary<long, double> _edgeResidual = new Dictionary<long, double>();
        private readonly Dictionary<long, double> _typeResidual = new Dictionary<long, double>();
        private readonly Dictionary<int, double> _globalResidual = new Dictionary<int, double>();
        // Per-(from,to,carrying) lookup: residual indexed by density band (size MaxDensityBand+1).
        // Allows Step F's Σ_d PMF[d] · ρ[e, c, d] without re-hashing on every band.
        private readonly Dictionary<long, double[]> _edgeResidualByBand = new Dictionary<long, double[]>();
        private readonly Dictionary<long, double[]> _typeResidualByBand = new Dictionary<long, double[]>();
        private readonly Dictionary<int, double[]> _globalResidualByBand = new Dictionary<int, double[]>();
        // Step F controls (set by M1GManager from config before first use).
        private bool _useProbabilisticDensity;
        private double _densityKernelSigma = 1.5;
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

        /// <summary>
        /// Step F toggle. Enables Poisson-binomial expectation over density bands when querying the
        /// residual at each edge; the deterministic count path is used when false.
        /// </summary>
        public void ConfigureProbabilisticDensity(bool enabled, double kernelSigma)
        {
            _useProbabilisticDensity = enabled;
            _densityKernelSigma = kernelSigma > 0.0 ? kernelSigma : 1.5;
        }

        // ── Public API ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate the time (seconds) for a bot to travel <paramref name="from"/> → <paramref name="to"/>
        /// while empty (carrying = 0) or loaded (carrying = 1). Density per edge is evaluated at the
        /// simulator's current time. For Phase D's joint (r,p,s) cost use <see cref="EstimateLegTimeFrom"/>
        /// instead, which lets you specify the segment's start time.
        /// Returns +∞ if the path is unreachable.
        /// </summary>
        public double EstimateLegTime(Waypoint from, Waypoint to, bool carrying)
        {
            double tStart = _instance.Controller != null ? _instance.Controller.CurrentTime : 0.0;
            return EstimateLegTimeFrom(from, to, carrying, tStart);
        }

        /// <summary>
        /// Time-aware leg estimate: each edge is evaluated at the projected arrival time
        /// <paramref name="tStart"/> + Σ previous-edge times. Density at an edge's from-waypoint is
        /// computed by projecting every other bot's position to that projected arrival time using
        /// its remaining Path and the same kinematic model used in training.
        /// </summary>
        public double EstimateLegTimeFrom(Waypoint from, Waypoint to, bool carrying, double tStart)
        {
            if (from == null || to == null) return double.PositiveInfinity;
            if (from == to) return 0.0;
            EnsureLoaded();

            var path = ShortestPath(from, to, podSafe: carrying);
            if (path == null || path.Count < 2) return double.PositiveInfinity;

            int c = carrying ? 1 : 0;
            double total = 0.0;
            double tCursor = tStart;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var u = path[i];
                var v = path[i + 1];
                double dist = u[v];
                double residual;
                if (_useProbabilisticDensity)
                {
                    // Step F: Σ_d PMF[d] · ρ[edge, c, d] where PMF is Poisson-binomial over per-bot
                    // Bernoulli "near wp at tCursor" probabilities.
                    var pmf = ComputeDensityPMFAt(u, tCursor);
                    residual = ExpectedResidual(u.ID, v.ID, c, pmf);
                }
                else
                {
                    int dband = ComputeDensityBandAt(u, tCursor);
                    residual = LookupResidual(u.ID, v.ID, c, dband);
                }
                double dt = _a + _b * dist + residual;
                total += dt;
                tCursor += dt;
            }
            return total;
        }

        /// <summary>
        /// Phase D joint (r,p,s) cost: time for bot to reach <paramref name="podWp"/>, lift the pod,
        /// then transport it to <paramref name="stationWp"/>. Leg 2's density is evaluated forward in
        /// time starting from the projected pickup moment.
        /// </summary>
        public double EstimateThreeTupleTime(Waypoint botWp, Waypoint podWp, Waypoint stationWp,
                                              double tNow, double tLift)
        {
            if (botWp == null || podWp == null || stationWp == null) return double.PositiveInfinity;
            double leg1 = EstimateLegTimeFrom(botWp, podWp, carrying: false, tStart: tNow);
            if (double.IsInfinity(leg1)) return double.PositiveInfinity;
            double tPickup = tNow + leg1 + tLift;
            double leg2 = EstimateLegTimeFrom(podWp, stationWp, carrying: true, tStart: tPickup);
            if (double.IsInfinity(leg2)) return double.PositiveInfinity;
            return leg1 + tLift + leg2;
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
                // STD/P90 columns are written by Step E's export_for_csharp.py; older CSV
                // files without them remain readable.

                int etId = GetOrCreateEdgeTypeId(edgeType);
                int clampedBand = density < 0 ? 0 : (density > MaxDensityBand ? MaxDensityBand : density);
                switch (kind)
                {
                    case "EDGE":
                        _edgeResidual[EncodeEdgeKey(from, to, carrying, density)] = residual;
                        StoreByBand(_edgeResidualByBand, EncodePairCarry(from, to, carrying), clampedBand, residual);
                        // Remember the type associated with this directed pair (for fallback lookup).
                        _edgeTypeByPair[EncodePair(from, to)] = etId;
                        break;
                    case "TYPE":
                        _typeResidual[EncodeTypeKey(etId, carrying, density)] = residual;
                        StoreByBand(_typeResidualByBand, EncodeTypeCarryKey(etId, carrying), clampedBand, residual);
                        break;
                    case "GLOBAL":
                        _globalResidual[EncodeGlobalKey(carrying, density)] = residual;
                        StoreByBand(_globalResidualByBand, carrying, clampedBand, residual);
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

        private static void StoreByBand(Dictionary<long, double[]> table, long key, int band, double value)
        {
            double[] arr;
            if (!table.TryGetValue(key, out arr))
            {
                arr = new double[MaxDensityBand + 1];
                table[key] = arr;
            }
            arr[band] = value;
        }

        private static void StoreByBand(Dictionary<int, double[]> table, int key, int band, double value)
        {
            double[] arr;
            if (!table.TryGetValue(key, out arr))
            {
                arr = new double[MaxDensityBand + 1];
                table[key] = arr;
            }
            arr[band] = value;
        }

        // Step F: Σ_d PMF[d] · ρ[edge, c, d]. Falls back EDGE → TYPE → GLOBAL using by-band tables.
        private double ExpectedResidual(int from, int to, int carrying, double[] pmf)
        {
            if (_loadFailed || pmf == null) return 0.0;
            double[] band;
            if (_edgeResidualByBand.TryGetValue(EncodePairCarry(from, to, carrying), out band))
                return DotPMF(pmf, band);
            int etId;
            if (_edgeTypeByPair.TryGetValue(EncodePair(from, to), out etId)
                && _typeResidualByBand.TryGetValue(EncodeTypeCarryKey(etId, carrying), out band))
                return DotPMF(pmf, band);
            etId = ClassifyEdgeOnDemand(from, to);
            if (etId >= 0 && _typeResidualByBand.TryGetValue(EncodeTypeCarryKey(etId, carrying), out band))
                return DotPMF(pmf, band);
            if (_globalResidualByBand.TryGetValue(carrying, out band))
                return DotPMF(pmf, band);
            return 0.0;
        }

        private static double DotPMF(double[] pmf, double[] residualByBand)
        {
            double acc = 0.0;
            int n = Math.Min(pmf.Length, residualByBand.Length);
            for (int i = 0; i < n; i++) acc += pmf[i] * residualByBand[i];
            return acc;
        }

        // ── Density ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Step F: compute the Poisson-binomial PMF over density bands {0, 1, ≥2} at waypoint
        /// <paramref name="wp"/> at projected time <paramref name="tTarget"/>. Each other bot
        /// contributes a Bernoulli p_j based on its kinematically projected position softened by a
        /// Gaussian kernel of width <see cref="_densityKernelSigma"/> (in meters). Aggregation is
        /// done via O(N) DP on the Poisson-binomial recurrence; the tail (d ≥ 2) collapses to a
        /// single bucket so the residual table's 3 bands suffice.
        /// </summary>
        private double[] ComputeDensityPMFAt(Waypoint wp, double tTarget)
        {
            double tNow = _instance.Controller != null ? _instance.Controller.CurrentTime : 0.0;
            // Three-bucket PMF: index 0 = "0 bots within radius", index 1 = "exactly 1",
            // index 2 = "2 or more". After processing every bot the buckets sum to 1.
            var pmf = new double[MaxDensityBand + 1];
            pmf[0] = 1.0;
            double sigma = _densityKernelSigma;
            double invSigma2 = sigma > 0.0 ? 1.0 / (2.0 * sigma * sigma) : 0.0;
            foreach (var b in _instance.Bots)
            {
                if (b.Tier != wp.Tier) continue;
                double bx, by;
                ProjectBotPosition(b, tTarget - tNow, out bx, out by);
                double dx = bx - wp.X;
                double dy = by - wp.Y;
                double r2 = dx * dx + dy * dy;
                double p;
                if (sigma <= 0.0)
                {
                    p = Math.Abs(dx) + Math.Abs(dy) < LocalDensityRadiusM ? 1.0 : 0.0;
                }
                else
                {
                    // Soft membership: Gaussian-kernel weight centred on wp. Captures projection
                    // uncertainty without needing a per-bot std estimate (which would require
                    // walking each bot's path against the std column).
                    p = Math.Exp(-r2 * invSigma2);
                    if (p < 1e-6) continue; // negligible — keep DP cheap
                    if (p > 1.0) p = 1.0;
                }
                // Poisson-binomial DP with absorbing tail at index = MaxDensityBand:
                //   new_pmf[0]   = pmf[0] * (1-p)
                //   new_pmf[k]   = pmf[k-1] * p + pmf[k] * (1-p)        for 0 < k < tail
                //   new_pmf[tail]= pmf[tail-1] * p + pmf[tail] * 1      (anything that lands in
                //                                                       the tail stays there)
                int tail = MaxDensityBand;
                double prev = pmf[0];
                pmf[0] = prev * (1.0 - p);
                for (int k = 1; k < tail; k++)
                {
                    double cur = pmf[k];
                    pmf[k] = prev * p + cur * (1.0 - p);
                    prev = cur;
                }
                pmf[tail] = prev * p + pmf[tail]; // tail absorbs all "≥ tail" mass
            }
            return pmf;
        }

        private int ComputeDensityBandAt(Waypoint wp, double tTarget)
        {
            double tNow = _instance.Controller != null ? _instance.Controller.CurrentTime : 0.0;
            int count = 0;
            foreach (var b in _instance.Bots)
            {
                if (b.Tier != wp.Tier) continue;
                double bx, by;
                ProjectBotPosition(b, tTarget - tNow, out bx, out by);
                double dx = bx - wp.X;
                double dy = by - wp.Y;
                if (Math.Abs(dx) + Math.Abs(dy) < LocalDensityRadiusM)
                    count++;
            }
            return count > MaxDensityBand ? MaxDensityBand : count;
        }

        /// <summary>
        /// Project bot position <paramref name="dt"/> seconds into the future. Walks the bot's
        /// remaining <see cref="MultiAgentPathFinding.Elements.Path"/> at the kinematic baseline
        /// (a + b·dist per edge) and reports the waypoint the bot is approximately at when the
        /// budgeted time runs out. Falls back to the bot's current (X,Y) when no path is available.
        /// </summary>
        private void ProjectBotPosition(Bot b, double dt, out double x, out double y)
        {
            x = b.X; y = b.Y;
            if (dt <= 0.0 || b == null) return;
            // Quick path: bot is idle (no NextWaypoint and no Path) → stays put.
            var bn = b as Bots.BotNormal;
            if (bn == null) return;
            var nextWp = bn.NextWaypoint;
            var curWp = bn.CurrentWaypoint;
            if (nextWp == null && (bn.Path == null || bn.Path.Count == 0)) return;

            double remaining = dt;
            Waypoint cursor = nextWp ?? curWp;
            // Cost of finishing the in-progress segment, if any.
            if (nextWp != null && curWp != null)
            {
                double segDist = curWp.GetDistance(nextWp);
                double segDt = _a + _b * segDist;
                if (remaining < segDt) return; // still mid-segment → current X,Y is fine
                remaining -= segDt;
                x = nextWp.X; y = nextWp.Y;
            }
            // Walk the remaining Path actions.
            if (bn.Path == null || bn.Path.Count == 0) return;
            var pm = _instance.Controller != null ? _instance.Controller.PathManager : null;
            if (pm == null) return;
            foreach (var action in bn.Path.Actions)
            {
                Waypoint next = pm.GetWaypointByNodeId(action.Node);
                if (next == null || next == cursor) continue;
                double segDist = cursor.GetDistance(next);
                double segDt = _a + _b * segDist;
                if (remaining < segDt)
                {
                    // Bot still mid-edge → approximate at the from-waypoint of the edge.
                    x = cursor.X; y = cursor.Y;
                    return;
                }
                remaining -= segDt;
                cursor = next;
                x = cursor.X; y = cursor.Y;
                if (remaining <= 0.0) return;
            }
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

        private static long EncodePairCarry(int from, int to, int carrying)
            => ((long)from << 33) | ((long)(uint)to << 1) | (long)(carrying & 1);

        private static long EncodeTypeCarryKey(int etId, int carrying)
            => ((long)etId << 1) | (long)(carrying & 1);
    }
}
