using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Statistics;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    /// <summary>
    /// Fixed-Route Priority Wait Scheduler v3 (FRPWS v3).
    ///
    /// Centralized CBS-style space-time scheduler that inherits directly from PathManager
    /// to leverage the built-in ReservationTable and QueueManager safety nets.
    ///
    /// Flow:
    ///   1. base.Update() → QueueManager + ReservationTable.Reorganize()
    ///   2. On clocking interval: RebuildSchedules()
    ///   3. RegisterNextWaypoint() uses base implementation (ReservationTable check)
    ///      → rejection triggers RequestReoptimization → next tick re-schedules
    /// </summary>
    public class FixedRoutePrioritySchedulerPathManager : PathManager
    {
        private readonly FixedRoutePrioritySchedulerPathPlanningConfiguration _cfg;
        private readonly Dictionary<int, Waypoint> _wpById;
        private readonly ResourceRegistry _registry = new ResourceRegistry();
        private double _lastRebuildTime = double.MinValue;
        private bool _registryBuilt = false;

        public FixedRoutePrioritySchedulerPathManager(
            Instance instance,
            FixedRoutePrioritySchedulerPathPlanningConfiguration config)
            : base(instance)
        {
            _cfg = config;
            _wpById = instance.Waypoints.ToDictionary(wp => wp.ID, wp => wp);

            // Build graph + queue managers (GenerateGraph initializes _waypointIds, _queueManagers)
            var graph = GenerateGraph();

            // Provide graph to base so _initReservationTable() works
            PathFinder = new NullPathFinder(
                graph,
                instance.SettingConfig.Seed,
                PathPlanningCommunicator.DUMMY_COMMUNICATOR);

            StatDataPoints = new List<PathFindingDatapoint>();
        }

        // ---------------------------------------------------------------
        // IUpdateable
        // ---------------------------------------------------------------

        public override void Update(double lastTime, double currentTime)
        {
            // Suppress _reoptimize: clear all RequestReoptimization flags BEFORE base.Update()
            // so the base never triggers its centralized _reoptimize() batch replanner.
            // base.Update() still runs: queue management + reservation table reorganize.
            foreach (var bot in Instance.Bots)
                ((BotNormal)bot).RequestReoptimization = false;

            base.Update(lastTime, currentTime);

            // Build registry once
            if (!_registryBuilt)
            {
                _registry.Build(Instance.Waypoints);
                _registryBuilt = true;
            }

            // FRPWS clocking
            if (_lastRebuildTime + _cfg.Clocking > currentTime)
                return;

            _lastRebuildTime = currentTime;
            RebuildSchedules(currentTime);
        }

        // ---------------------------------------------------------------
        // Core CBS-style scheduler
        // ---------------------------------------------------------------

        private void RebuildSchedules(double currentTime)
        {
            // 1. Collect active bots: stopped, has destination, not queueing
            var activeBots = Instance.Bots
                .Cast<BotNormal>()
                .Where(b => !b.IsResting()
                            && b.DestinationWaypoint != null
                            && b.CurrentWaypoint != null
                            && b.CanSafelySwapPathNow()
                            && !b.IsQueueing)
                .ToList();

            if (activeBots.Count == 0) return;

            var activeBotSet = new HashSet<BotNormal>(activeBots);

            // 2. Hard obstacles: non-active bots' current AND next waypoints
            var hardObstacles = new HashSet<int>();
            foreach (var b in Instance.Bots.Cast<BotNormal>())
            {
                if (activeBotSet.Contains(b)) continue;
                if (b.CurrentWaypoint != null)
                    hardObstacles.Add(b.CurrentWaypoint.ID);
                if (b.NextWaypoint != null)
                    hardObstacles.Add(b.NextWaypoint.ID);
            }

            // 3. Build initial plans for all active bots
            var plans = new List<BotPlan>();
            foreach (var bot in activeBots)
            {
                var plan = BuildInitialPlan(bot, hardObstacles, currentTime);
                if (plan != null)
                    plans.Add(plan);
            }

            if (plans.Count == 0) return;

            // 3b. Build frozen plans for in-transit bots
            var frozenPlans = new List<BotPlan>();
            foreach (var b in Instance.Bots.Cast<BotNormal>())
            {
                if (activeBotSet.Contains(b)) continue;
                if (b.CurrentWaypoint == null || b.NextWaypoint == null) continue;
                var fp = BuildFrozenPlan(b, currentTime);
                if (fp != null && fp.Points.Count >= 2)
                    frozenPlans.Add(fp);
            }
            var allPlans = new List<BotPlan>(plans);
            allPlans.AddRange(frozenPlans);

            // 4. Iterative conflict resolution
            var frozenSet = new HashSet<BotPlan>(frozenPlans);
            for (int iter = 0; iter < _cfg.MaxConflictResolutionIterations; iter++)
            {
                var conflict = SpaceTimeConflictScanner.FindEarliest(allPlans, _registry);
                if (conflict == null) break;

                bool aFrozen = frozenSet.Contains(conflict.PlanA);
                bool bFrozen = frozenSet.Contains(conflict.PlanB);

                if (aFrozen && bFrozen)
                {
                    allPlans.Remove(conflict.PlanA);
                    allPlans.Remove(conflict.PlanB);
                    continue;
                }

                ResolveConflict(conflict, frozenSet);

                plans.RemoveAll(p => !p.IsFeasible);
                allPlans.RemoveAll(p => !p.IsFeasible && !frozenSet.Contains(p));
                if (plans.Count == 0) break;
            }

            // 5. Apply schedules
            ApplySchedules(plans, currentTime);
        }

        // ---------------------------------------------------------------
        // Build initial BotPlan from A* path + naive schedule
        // ---------------------------------------------------------------

        private BotPlan BuildInitialPlan(BotNormal bot, HashSet<int> hardObstacles, double currentTime)
        {
            var blocked = new HashSet<int>(hardObstacles);

            Waypoint dest = ResolveQueueDestination(bot, bot.DestinationWaypoint);
            if (dest == null) return null;

            bool canTunnel = _cfg.CanTunnel && (bot.Pod == null);

            List<int> pathIds = FixedRouteAStar.FindPath(
                bot.CurrentWaypoint, dest, Instance.Waypoints, blocked, canTunnel);

            if (pathIds == null || pathIds.Count == 0) return null;

            double speed = bot.MaxVelocity > 0 ? bot.MaxVelocity : 1.0;

            var plan = new BotPlan { Bot = bot };
            plan.Points.Add(new SchedulePoint
            {
                NodeId = pathIds[0],
                ArriveTime = currentTime,
                DepartTime = currentTime
            });

            double t = currentTime;
            for (int i = 0; i < pathIds.Count - 1; i++)
            {
                if (!_wpById.TryGetValue(pathIds[i], out var fromWp)) return null;
                if (!_wpById.TryGetValue(pathIds[i + 1], out var toWp)) return null;
                double travelTime = fromWp[toWp] / speed;
                t += travelTime;
                plan.Points.Add(new SchedulePoint
                {
                    NodeId = pathIds[i + 1],
                    ArriveTime = t,
                    DepartTime = t
                });
            }

            return plan;
        }

        // ---------------------------------------------------------------
        // Build a frozen plan for an in-transit bot
        // ---------------------------------------------------------------

        private BotPlan BuildFrozenPlan(BotNormal bot, double currentTime)
        {
            if (bot.CurrentWaypoint == null || bot.NextWaypoint == null) return null;
            double speed = bot.MaxVelocity > 0 ? bot.MaxVelocity : 1.0;

            var plan = new BotPlan { Bot = bot };
            plan.Points.Add(new SchedulePoint
            {
                NodeId = bot.CurrentWaypoint.ID,
                ArriveTime = currentTime - 100.0,
                DepartTime = currentTime
            });

            double distToNext = Math.Sqrt(
                Math.Pow(bot.X - bot.NextWaypoint.X, 2) +
                Math.Pow(bot.Y - bot.NextWaypoint.Y, 2));
            double arrNext = currentTime + distToNext / speed;

            plan.Points.Add(new SchedulePoint
            {
                NodeId = bot.NextWaypoint.ID,
                ArriveTime = arrNext,
                DepartTime = arrNext
            });

            return plan;
        }

        // ---------------------------------------------------------------
        // Conflict resolution
        // ---------------------------------------------------------------

        private void ResolveConflict(SpaceTimeConflict conflict, HashSet<BotPlan> frozenSet)
        {
            BotPlan planA = conflict.PlanA;
            BotPlan planB = conflict.PlanB;

            bool aFrozen = frozenSet.Contains(planA);
            bool bFrozen = frozenSet.Contains(planB);

            BotPlan winner, loser;

            if (aFrozen && !bFrozen) { winner = planA; loser = planB; }
            else if (bFrozen && !aFrozen) { winner = planB; loser = planA; }
            else if (conflict.Zone == ZoneClassification.Corridor)
            {
                int idxA = planA.IndexOf(conflict.NodeId);
                int idxB = planB.IndexOf(conflict.NodeId);
                double arrA = idxA >= 0 ? planA.Points[idxA].ArriveTime : double.MaxValue;
                double arrB = idxB >= 0 ? planB.Points[idxB].ArriveTime : double.MaxValue;
                if (arrA <= arrB) { winner = planA; loser = planB; }
                else { winner = planB; loser = planA; }
            }
            else
            {
                if (PriorityRules.AHigherThanB(planA.Bot, planB.Bot))
                { winner = planA; loser = planB; }
                else
                { winner = planB; loser = planA; }
            }

            if (conflict.Kind == ConflictKind.Vertex)
            {
                int loserConflictIdx = loser.IndexOf(conflict.NodeId);
                int winnerConflictIdx = winner.IndexOf(conflict.NodeId);

                if (loserConflictIdx < 0 || winnerConflictIdx < 0)
                {
                    loser.IsFeasible = false;
                    return;
                }

                // Incumbent rule: bot already at conflict node takes priority
                if (loserConflictIdx == 0 && winnerConflictIdx > 0)
                {
                    var tmp = loser; loser = winner; winner = tmp;
                    var tmpIdx = loserConflictIdx; loserConflictIdx = winnerConflictIdx; winnerConflictIdx = tmpIdx;
                }

                int holdIdx = loserConflictIdx - 1;
                if (holdIdx < 0)
                {
                    loser.IsFeasible = false;
                    return;
                }

                double winnerClear = winner.Points[winnerConflictIdx].DepartTime + SpaceTimeConflictScanner.MARGIN;
                double loserArrival = loser.Points[loserConflictIdx].ArriveTime;
                double waitNeeded = Math.Max(0.0, winnerClear - loserArrival) + SpaceTimeConflictScanner.MARGIN / 2.0;

                loser.InsertHold(holdIdx, waitNeeded);
            }
            else // EdgeSwap
            {
                int loserFromIdx = FindConflictStartIndex(loser, conflict);
                int winnerFromIdx = FindConflictStartIndex(winner, conflict);

                if (loserFromIdx < 0 || winnerFromIdx < 0)
                {
                    loser.IsFeasible = false;
                    return;
                }

                int holdIdx = loserFromIdx;
                var winnerEdge = winner.EdgeWindow(winnerFromIdx);
                double winnerClear = winnerEdge.end + SpaceTimeConflictScanner.MARGIN;
                double loserDepart = loser.Points[holdIdx].DepartTime;
                double waitNeeded = Math.Max(0.0, winnerClear - loserDepart) + SpaceTimeConflictScanner.MARGIN / 2.0;

                loser.InsertHold(holdIdx, waitNeeded);
            }

            if (loser.TotalAddedWait > _cfg.MaxAdditionalWaitPerBot)
                loser.IsFeasible = false;
        }

        private int FindConflictStartIndex(BotPlan plan, SpaceTimeConflict conflict)
        {
            for (int i = 0; i < plan.Points.Count - 1; i++)
            {
                int from = plan.Points[i].NodeId;
                int to = plan.Points[i + 1].NodeId;
                if ((from == conflict.NodeId && to == conflict.NodeId2) ||
                    (from == conflict.NodeId2 && to == conflict.NodeId))
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------
        // Apply schedules to bots
        // ---------------------------------------------------------------

        private void ApplySchedules(List<BotPlan> plans, double currentTime)
        {
            foreach (var plan in plans)
            {
                if (!plan.IsFeasible) continue;
                if (plan.Points.Count <= 1) continue;

                var bot = plan.Bot;

                // Initial wait at current node
                double initialWait = plan.Points[0].DepartTime - currentTime;
                if (initialWait > 0.001)
                    bot.WaitUntil(currentTime + initialWait);

                // Build path with intermediate stops.
                // StopAtNode=true only at: (1) conflict hold points, (2) turn points, (3) destination.
                // Straight-line intermediate nodes get StopAtNode=false so the bot cruises through
                // without stopping — RegisterNextWaypoint + getIntermediateNodes handles the
                // reservation for all waypoints on the straight segment automatically.
                var path = new Path();
                for (int i = 1; i < plan.Points.Count; i++)
                {
                    bool isLast = (i == plan.Points.Count - 1);
                    double holdTime = plan.Points[i].WaitDuration;

                    if (holdTime > 0.001)
                    {
                        // Conflict hold point → must stop and wait
                        path.AddLast(plan.Points[i].NodeId, true, holdTime);
                    }
                    else if (isLast)
                    {
                        // Final destination → must stop
                        path.AddLast(plan.Points[i].NodeId, true, 0.0);
                    }
                    else
                    {
                        // Check if direction changes at this node (turn point).
                        // A→B→C: if angle(A→B) != angle(B→C), bot must stop at B to rotate.
                        bool isTurn = IsTurnPoint(plan.Points, i);
                        path.AddLast(plan.Points[i].NodeId, isTurn, 0.0);
                    }
                }

                if (path.Count > 0)
                    bot.Path = path;
            }
        }

        /// <summary>
        /// Returns true if the direction changes at Points[idx] — i.e. the segment
        /// (idx-1 → idx) has a different angle than (idx → idx+1).
        /// When true, the bot must stop at this node to rotate.
        /// </summary>
        private bool IsTurnPoint(List<SchedulePoint> points, int idx)
        {
            if (idx <= 0 || idx >= points.Count - 1) return true;

            if (!_wpById.TryGetValue(points[idx - 1].NodeId, out var wpPrev)) return true;
            if (!_wpById.TryGetValue(points[idx].NodeId, out var wpCur)) return true;
            if (!_wpById.TryGetValue(points[idx + 1].NodeId, out var wpNext)) return true;

            // Compute incoming and outgoing direction vectors
            double dxIn = wpCur.X - wpPrev.X;
            double dyIn = wpCur.Y - wpPrev.Y;
            double dxOut = wpNext.X - wpCur.X;
            double dyOut = wpNext.Y - wpCur.Y;

            // Same direction if cross product ≈ 0 and dot product > 0 (not 180° reversal)
            double cross = dxIn * dyOut - dyIn * dxOut;
            double dot = dxIn * dxOut + dyIn * dyOut;

            return Math.Abs(cross) > 0.001 || dot <= 0;
        }
    }
}
