using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Statistics
{
    /// <summary>
    /// One snapshot of the congestion features observed when a bot commits to a new segment.
    /// Stage A.2 of the congestion-aware cost estimator — see design_note.md §9.2.
    /// </summary>
    public class CongestionFeatureDatapoint
    {
        /// <summary>Simulation time the row was sampled (ReadyTime of the segment).</summary>
        public double TimeStamp;
        /// <summary>Bot identifier.</summary>
        public int BotId;
        /// <summary>Waypoint id the segment starts from.</summary>
        public int FromNode;
        /// <summary>Waypoint id the segment ends at.</summary>
        public int ToNode;
        /// <summary>Number of OTHER bots currently committed to (approaching) ToNode.</summary>
        public int BotsApproachingTo;
        /// <summary>1 if any other bot's CurrentWaypoint equals ToNode.</summary>
        public int DownstreamOccupied;
        /// <summary>Number of OTHER bots currently committed to (approaching) FromNode.</summary>
        public int MergePressure;
        /// <summary>Sum of bots inside the station-queue that owns ToNode (0 if ToNode is not in any queue).</summary>
        public int StationQueueLength;
        /// <summary>Number of OTHER bots whose Manhattan distance to <see cref="FromNode"/> is &lt; <see cref="LocalDensityRadiusM"/> m.</summary>
        public int LocalBotDensity;
        /// <summary>
        /// Option B re-train: Gaussian-kernel "soft count" of OTHER bots near FromNode, computed as
        /// Σ exp(-r²/(2σ²)) over same-tier bots, with r in metres and σ = <see cref="SoftDensitySigmaM"/>.
        /// Matches the kernel used in CongestionAwareCostEstimator.ComputeDensityPMFAt so train and
        /// deploy features share the same statistic.
        /// </summary>
        public double SoftBotDensity;
        /// <summary>1 if ToNode is a station entry waypoint OR a queue waypoint.</summary>
        public int IsStationEntrance;
        /// <summary>1 if FromNode has in-degree &gt;= 2 in the static waypoint graph.</summary>
        public int IsMergeEdge;
        /// <summary>1 if FromNode has in-degree == 1 AND out-degree == 1 (corridor middle).</summary>
        public int IsBottleneckEdge;
        /// <summary>Coarse categorical label derived from waypoint flags.</summary>
        public string EdgeType;

        /// <summary>Default constructor for deserialisation.</summary>
        public CongestionFeatureDatapoint() { }
        /// <summary>Reconstruct from serialised line.</summary>
        public CongestionFeatureDatapoint(string line)
        {
            string[] values = line.Split(IOConstants.DELIMITER_VALUE);
            ReflectionTools.ParseStringToFields(this, typeof(CongestionFeatureDatapoint), values, IOConstants.FORMATTER);
        }
        /// <summary>Serialise this row.</summary>
        public string GetLine()
        {
            return string.Join(IOConstants.DELIMITER_VALUE.ToString(), ReflectionTools.ConvertFields(this, typeof(CongestionFeatureDatapoint), IOConstants.FORMATTER, IOConstants.EXPORT_FORMAT_SHORTEST_BY_ROUNDING).ToArray());
        }
        /// <summary>CSV header.</summary>
        public static string GetHeader()
        {
            return string.Join(IOConstants.DELIMITER_VALUE.ToString(), ReflectionTools.ConvertFieldsToDescriptions(typeof(CongestionFeatureDatapoint)));
        }
    }

    /// <summary>
    /// Builds congestion-feature snapshots from currently observable simulator state.
    /// Read-only: never mutates Bot / Waypoint / Station / Graph state.
    /// </summary>
    internal static class CongestionFeatureLogger
    {
        /// <summary>Radius (metres) used for LocalBotDensity counting.</summary>
        public const double LocalDensityRadiusM = 3.0;
        /// <summary>
        /// Gaussian kernel width (metres) used for <see cref="CongestionFeatureDatapoint.SoftBotDensity"/>.
        /// Must match the estimator-side default to keep train/test density semantics aligned (see
        /// <see cref="RAWSimO.Core.Metrics.CongestionAwareCostEstimator"/>).
        /// </summary>
        public const double SoftDensitySigmaM = 1.5;

        // Topology caches, initialised lazily once per Instance.
        private static Instance _cachedInstance;
        private static Dictionary<int, int> _inDegreeByWaypointId;
        private static Dictionary<int, int> _outDegreeByWaypointId;
        // Map a queue waypoint id to the queue's "anchor" (station entry waypoint) so we can
        // sum bots on every waypoint of the same queue list.
        private static Dictionary<int, List<Waypoint>> _queueByQueueWaypointId;

        private static void EnsureCache(Instance instance)
        {
            if (_cachedInstance == instance && _inDegreeByWaypointId != null) return;
            _cachedInstance = instance;

            var indeg = new Dictionary<int, int>();
            var outdeg = new Dictionary<int, int>();
            foreach (var wp in instance.Waypoints)
            {
                if (!outdeg.ContainsKey(wp.ID)) outdeg[wp.ID] = 0;
                if (!indeg.ContainsKey(wp.ID)) indeg[wp.ID] = 0;
            }
            foreach (var wp in instance.Waypoints)
            {
                int outCount = 0;
                foreach (var nbr in wp.Paths)
                {
                    outCount++;
                    if (!indeg.ContainsKey(nbr.ID)) indeg[nbr.ID] = 0;
                    indeg[nbr.ID]++;
                }
                outdeg[wp.ID] = outCount;
            }
            _inDegreeByWaypointId = indeg;
            _outDegreeByWaypointId = outdeg;

            // Build queue map. Both OutputStation and InputStation expose Queues : Dictionary<entry-Waypoint, List<Waypoint>>.
            var queueMap = new Dictionary<int, List<Waypoint>>();
            foreach (var s in instance.OutputStations)
                IndexQueues(s.Queues, queueMap);
            foreach (var s in instance.InputStations)
                IndexQueues(s.Queues, queueMap);
            _queueByQueueWaypointId = queueMap;
        }

        private static void IndexQueues(Dictionary<Waypoint, List<Waypoint>> queues, Dictionary<int, List<Waypoint>> dest)
        {
            if (queues == null) return;
            foreach (var kv in queues)
            {
                var list = kv.Value;
                if (list == null) continue;
                foreach (var wp in list)
                    dest[wp.ID] = list;
                // Also map the entry waypoint itself.
                dest[kv.Key.ID] = list;
            }
        }

        /// <summary>
        /// Build a congestion-feature row for the segment <paramref name="from"/> → <paramref name="to"/>
        /// that <paramref name="self"/> is about to begin at <paramref name="readyTime"/>.
        /// </summary>
        public static CongestionFeatureDatapoint Sample(Instance instance, Bot self, Waypoint from, Waypoint to, double readyTime)
        {
            EnsureCache(instance);

            // BotNormal does not call Waypoint.AddBotApproaching (only BotHazard does), so
            // BotCountApproaching is unreliable. Compute traffic features directly from each bot's
            // live CurrentWaypoint / NextWaypoint state.
            int approachingTo = 0;
            int mergePressure = 0;
            int downstreamOccupied = 0;
            int localDensity = 0;
            double softDensity = 0.0;
            double invTwoSigma2 = 1.0 / (2.0 * SoftDensitySigmaM * SoftDensitySigmaM);
            HashSet<Waypoint> queueWaypoints = null;
            if (_queueByQueueWaypointId.TryGetValue(to.ID, out var ql))
                queueWaypoints = new HashSet<Waypoint>(ql);
            int stationQueueLen = 0;

            foreach (var b in instance.Bots)
            {
                if (b == self) continue;
                if (b.Tier != self.Tier) continue;
                if (!(b is Bots.BotNormal bn)) continue;

                if (bn.NextWaypoint == to)
                    approachingTo++;
                if (bn.NextWaypoint == from)
                    mergePressure++;
                if (bn.CurrentWaypoint == to && bn.NextWaypoint == null)
                    downstreamOccupied = 1;

                if (queueWaypoints != null && bn.CurrentWaypoint != null && queueWaypoints.Contains(bn.CurrentWaypoint))
                    stationQueueLen++;

                double dx = b.X - from.X;
                double dy = b.Y - from.Y;
                if (Math.Abs(dx) + Math.Abs(dy) < LocalDensityRadiusM)
                    localDensity++;
                double r2 = dx * dx + dy * dy;
                softDensity += Math.Exp(-r2 * invTwoSigma2);
            }

            bool isStationEntrance = to.OutputStation != null || to.InputStation != null || to.IsQueueWaypoint;
            int indegFrom = _inDegreeByWaypointId.TryGetValue(from.ID, out var i) ? i : 0;
            int outdegFrom = _outDegreeByWaypointId.TryGetValue(from.ID, out var o) ? o : 0;
            bool isMerge = indegFrom >= 2;
            bool isBottleneck = indegFrom == 1 && outdegFrom == 1;

            string edgeType;
            if (isStationEntrance) edgeType = "StationEntrance";
            else if (to.IsQueueWaypoint || from.IsQueueWaypoint) edgeType = "StationQueue";
            else if (to.PodStorageLocation || from.PodStorageLocation) edgeType = "PodStorageAccess";
            else if (isMerge) edgeType = "Merge";
            else if (isBottleneck) edgeType = "Corridor";
            else edgeType = "Aisle";

            return new CongestionFeatureDatapoint
            {
                TimeStamp = readyTime,
                BotId = self.ID,
                FromNode = from.ID,
                ToNode = to.ID,
                BotsApproachingTo = approachingTo,
                DownstreamOccupied = downstreamOccupied,
                MergePressure = mergePressure,
                StationQueueLength = stationQueueLen,
                LocalBotDensity = localDensity,
                SoftBotDensity = softDensity,
                IsStationEntrance = isStationEntrance ? 1 : 0,
                IsMergeEdge = isMerge ? 1 : 0,
                IsBottleneckEdge = isBottleneck ? 1 : 0,
                EdgeType = edgeType,
            };
        }
    }
}
