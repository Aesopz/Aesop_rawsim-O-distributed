using System.Collections.Generic;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Methods;

namespace RAWSimO.Core.Metrics
{
    /// <summary>
    /// BAED (Blocking-Aware Effective Distance) cost estimator.
    /// Walks the static shortest path between two waypoints, queries the live WCHA*
    /// reservation table at each intermediate node's projected arrival time, and accumulates
    /// expected entry-delay (the time the bot would have to wait before the next node is free).
    ///
    /// First-version minimum scope:
    ///  - no DelayCap
    ///  - no station starvation / overload modifier
    ///  - no Regime-B RRA* projection (anything beyond the reservation window contributes 0 delay)
    ///  - returns total entry-delay seconds; caller converts to distance equivalent via reference speed.
    /// </summary>
    public static class BAEDEstimator
    {
        /// <summary>
        /// Half-width of the probe interval used when querying overlap at a node.
        /// Small but non-zero so the disjoint-interval-tree treats it as a proper interval.
        /// </summary>
        private const double ProbeWidth = 0.05;

        /// <summary>
        /// Compute total expected entry-delay (seconds) along the static shortest path
        /// from <paramref name="from"/> to <paramref name="to"/>, starting at the current
        /// simulator time and with the given physics + starting speed.
        ///
        /// Returns 0 when:
        ///   - any argument is null,
        ///   - the live reservation table is not available,
        ///   - the waypoint &lt;-&gt; graph mapping is missing,
        ///   - the underlying path lookup fails.
        /// In all those cases the caller falls back to plain physical distance, preserving baseline behaviour.
        /// </summary>
        public static double ComputeEntryDelaySeconds(
            Instance instance,
            Waypoint from,
            Waypoint to,
            RAWSimO.MultiAgentPathFinding.Physic.Physics physics,
            double startSpeed)
        {
            if (instance == null || from == null || to == null || physics == null)
                return 0.0;

            var pm = instance.Controller?.PathManager;
            if (pm == null)
                return 0.0;

            // Live reservation table is held by WHCAnStarMethod (or its subclass WHCAnStarMethod).
            var whca = pm.PathFinder as WHCAnStarMethod;
            if (whca == null || whca._reservationTable == null)
                return 0.0;

            int fromNode, toNode;
            if (!pm.TryGetGraphNodeId(from, out fromNode) || !pm.TryGetGraphNodeId(to, out toNode))
                return 0.0;
            if (fromNode == toNode)
                return 0.0;

            var resTable = whca._reservationTable;
            double t0 = instance.Controller.CurrentTime;

            List<int> nodes;
            List<double> times;
            // GetCheckPointNodes uses the same graph the reservation table is keyed on.
            // It returns the ordered list of intermediate nodes (start ... end) and their projected arrival times
            // assuming purely kinematic motion (no congestion).
            bool ok = resTable.GetCheckPointNodes(t0, startSpeed, physics, fromNode, toNode, out nodes, out times);
            if (!ok || nodes == null || times == null || nodes.Count != times.Count || nodes.Count == 0)
                return 0.0;

            double totalDelay = 0.0;
            // Start from index 1 because index 0 is the start node (bot already there, no entry-delay there).
            for (int i = 1; i < nodes.Count; i++)
            {
                int nodeId = nodes[i];
                if (nodeId < 0) continue; // sentinel "no valid node" from graph helpers
                double tArr = times[i];
                var probe = new ReservationTable.Interval(nodeId, tArr, tArr + ProbeWidth);

                // Fast path: if the probe interval is fully free, no entry-delay contribution.
                // IntersectionFree is the cheaper, defensive query; some node ids (elevator / sink) may
                // not have an interval tree allocated, which throws — swallow that path to 0 delay.
                bool free;
                try { free = resTable.IntersectionFree(probe); }
                catch { continue; }
                if (free) continue;

                ReservationTable.Interval overlap;
                try { overlap = resTable.GetOverlappingInterval(probe); }
                catch { continue; }

                // An empty / no-overlap result has Start >= End. Real overlap → wait until overlap.End.
                if (overlap.End > tArr && overlap.Start <= tArr + ProbeWidth)
                {
                    double delay = overlap.End - tArr;
                    if (delay > 0.0)
                        totalDelay += delay;
                }
            }
            return totalDelay;
        }

        /// <summary>
        /// Convenience wrapper that picks a bot's physics + current speed and returns the entry-delay
        /// from the bot's reference waypoint to <paramref name="to"/>.
        /// Returns 0 on any missing piece (so caller can safely add to physical distance unconditionally).
        /// </summary>
        public static double ComputeEntryDelaySecondsForBot(Instance instance, Bot bot, Waypoint to)
        {
            if (bot == null || to == null)
                return 0.0;
            var botWp = bot.CurrentWaypoint;
            if (botWp == null)
                return 0.0;
            var bn = bot as BotNormal;
            if (bn == null || bn.Physics == null)
                return 0.0;
            // Use 0 as starting speed to keep the estimate conservative (slightly over-estimates time)
            // and to avoid sampling a fluctuating live velocity that may be momentarily near zero.
            // The reservation-table check itself is the dominant signal; this kinematic shift is small.
            return ComputeEntryDelaySeconds(instance, botWp, to, bn.Physics, 0.0);
        }

        /// <summary>
        /// Entry-delay along the carrying-pod leg from a pod's current location to a station.
        /// Uses a default physics object retrieved from the pod's bot (when the pod is being carried)
        /// or from any registered bot as a fall-back. Returns 0 on missing data.
        /// </summary>
        public static double ComputeEntryDelaySecondsForPodLeg(Instance instance, Waypoint podWp, Waypoint stationWp)
        {
            if (instance == null || podWp == null || stationWp == null)
                return 0.0;
            // Pick any bot's physics as a representative for kinematic timing along this leg.
            // The reservation-table lookup itself does not depend on which bot's physics we pass,
            // only on the projected arrival times produced by GetCheckPointNodes.
            RAWSimO.MultiAgentPathFinding.Physic.Physics phys = null;
            foreach (var b in instance.Bots)
            {
                var bn = b as BotNormal;
                if (bn != null && bn.Physics != null) { phys = bn.Physics; break; }
            }
            if (phys == null)
                return 0.0;
            return ComputeEntryDelaySeconds(instance, podWp, stationWp, phys, 0.0);
        }
    }
}
