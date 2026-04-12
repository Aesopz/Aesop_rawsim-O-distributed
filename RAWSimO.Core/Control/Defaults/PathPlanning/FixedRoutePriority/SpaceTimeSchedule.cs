using System;
using System.Collections.Generic;
using RAWSimO.Core.Bots;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    // -----------------------------------------------------------------------
    // Data structures
    // -----------------------------------------------------------------------

    public class SchedulePoint
    {
        public int NodeId;
        public double ArriveTime;
        public double DepartTime;
        public double WaitDuration => DepartTime - ArriveTime;
    }

    public class BotPlan
    {
        public BotNormal Bot;
        public List<SchedulePoint> Points = new List<SchedulePoint>();
        public bool IsFeasible = true;
        public double TotalAddedWait = 0.0;

        /// <summary>Physical edge window: [DepartTime_i, ArriveTime_{i+1}]</summary>
        public (double start, double end) EdgeWindow(int fromIdx)
            => (Points[fromIdx].DepartTime, Points[fromIdx + 1].ArriveTime);

        /// <summary>
        /// Insert wait at holdIdx: DepartTime[holdIdx] += waitDuration,
        /// all subsequent Arrive/Depart times right-shift by waitDuration.
        /// </summary>
        public void InsertHold(int holdIdx, double waitDuration)
        {
            if (waitDuration <= 1e-9) return;
            Points[holdIdx].DepartTime += waitDuration;
            for (int i = holdIdx + 1; i < Points.Count; i++)
            {
                Points[i].ArriveTime += waitDuration;
                Points[i].DepartTime += waitDuration;
            }
            TotalAddedWait += waitDuration;
        }

        /// <summary>Returns index of the first SchedulePoint with the given nodeId, or -1.</summary>
        public int IndexOf(int nodeId)
            => Points.FindIndex(p => p.NodeId == nodeId);
    }

    // -----------------------------------------------------------------------
    // Conflict types
    // -----------------------------------------------------------------------

    public enum ConflictKind { Vertex, EdgeSwap }

    public class SpaceTimeConflict
    {
        public BotPlan PlanA;
        public BotPlan PlanB;
        public ConflictKind Kind;
        /// <summary>For Vertex: the shared node. For EdgeSwap: the node A departs from.</summary>
        public int NodeId;
        /// <summary>For EdgeSwap: the node A arrives at (= B departs from).</summary>
        public int NodeId2;
        public double EarliestTime;
        /// <summary>Zone of the conflict node.</summary>
        public ZoneClassification Zone;
    }

    // -----------------------------------------------------------------------
    // Conflict scanner
    // -----------------------------------------------------------------------

    public static class SpaceTimeConflictScanner
    {
        /// <summary>Physical safety margin in seconds.</summary>
        public const double MARGIN = 0.5;

        /// <summary>
        /// Scan all pairs in plans and return the earliest conflict, or null if none.
        /// </summary>
        public static SpaceTimeConflict FindEarliest(List<BotPlan> plans, ResourceRegistry registry)
        {
            SpaceTimeConflict earliest = null;

            for (int i = 0; i < plans.Count; i++)
            {
                for (int j = i + 1; j < plans.Count; j++)
                {
                    var c = CheckPair(plans[i], plans[j], registry);
                    if (c != null && (earliest == null || c.EarliestTime < earliest.EarliestTime))
                        earliest = c;
                }
            }
            return earliest;
        }

        /// <summary>
        /// Check a pair of BotPlans for the earliest conflict between them.
        /// </summary>
        public static SpaceTimeConflict CheckPair(BotPlan a, BotPlan b, ResourceRegistry registry)
        {
            SpaceTimeConflict earliest = null;

            // Build node occupancy windows for A
            var aWindows = BuildVertexWindows(a);
            var bWindows = BuildVertexWindows(b);

            // --- Vertex conflicts ---
            foreach (var kvA in aWindows)
            {
                if (!bWindows.TryGetValue(kvA.Key, out var bList)) continue;
                foreach (var (aArr, aDep, aIdx) in kvA.Value)
                {
                    foreach (var (bArr, bDep, bIdx) in bList)
                    {
                        double aStart = aArr - MARGIN;
                        double aEnd   = aDep + MARGIN;
                        double bStart = bArr - MARGIN;
                        double bEnd   = bDep + MARGIN;
                        if (aStart < bEnd && bStart < aEnd)
                        {
                            double t = Math.Max(aArr, bArr);
                            if (earliest == null || t < earliest.EarliestTime)
                                earliest = new SpaceTimeConflict
                                {
                                    PlanA = a, PlanB = b,
                                    Kind = ConflictKind.Vertex,
                                    NodeId = kvA.Key,
                                    EarliestTime = t,
                                    Zone = registry.GetZone(kvA.Key)
                                };
                        }
                    }
                }
            }

            // --- EdgeSwap conflicts ---
            for (int ai = 0; ai < a.Points.Count - 1; ai++)
            {
                int aFrom = a.Points[ai].NodeId;
                int aTo   = a.Points[ai + 1].NodeId;

                for (int bi = 0; bi < b.Points.Count - 1; bi++)
                {
                    int bFrom = b.Points[bi].NodeId;
                    int bTo   = b.Points[bi + 1].NodeId;

                    // A goes aFrom→aTo, B goes aTo→aFrom (swap)
                    if (aTo != bFrom || aFrom != bTo) continue;

                    var (aDepFrom, aArrTo) = a.EdgeWindow(ai);
                    var (bDepFrom, bArrTo) = b.EdgeWindow(bi);

                    double aWinStart = aDepFrom - MARGIN;
                    double aWinEnd   = aArrTo   + MARGIN;
                    double bWinStart = bDepFrom - MARGIN;
                    double bWinEnd   = bArrTo   + MARGIN;

                    if (aWinStart < bWinEnd && bWinStart < aWinEnd)
                    {
                        double t = Math.Max(aDepFrom, bDepFrom);
                        if (earliest == null || t < earliest.EarliestTime)
                            earliest = new SpaceTimeConflict
                            {
                                PlanA = a, PlanB = b,
                                Kind = ConflictKind.EdgeSwap,
                                NodeId  = aFrom,
                                NodeId2 = aTo,
                                EarliestTime = t,
                                Zone = registry.GetZone(aFrom)
                            };
                    }
                }
            }

            return earliest;
        }

        // Build dict: nodeId → list of (arriveTime, departTime, pointIndex) for a plan
        private static Dictionary<int, List<(double arr, double dep, int idx)>> BuildVertexWindows(BotPlan plan)
        {
            var d = new Dictionary<int, List<(double, double, int)>>();
            for (int i = 0; i < plan.Points.Count; i++)
            {
                var p = plan.Points[i];
                if (!d.TryGetValue(p.NodeId, out var list))
                { list = new List<(double, double, int)>(); d[p.NodeId] = list; }
                list.Add((p.ArriveTime, p.DepartTime, i));
            }
            return d;
        }
    }
}
