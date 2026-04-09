using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Algorithms.AStar;
using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.Toolbox;
using System.Collections.Generic;
using System.Diagnostics;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar
{
    /// <summary>
    /// Per-bot A* path planner. One instance per BotNormal in AgentAStarPathManager.
    ///
    /// TryReplan() creates a new SpaceAStar instance each call (SpaceAStar cannot be
    /// reused across bots — _agent, _startAngle, _successorEdges are not reset by Clear()).
    /// biasedCost is NOT used — conflict resolution is delegated to Plan 2 / Plan 3.
    /// </summary>
    internal class BotAStarPlanner
    {
        private readonly BotNormal _bot;
        private readonly Graph _graph;
        private readonly BiDictionary<Waypoint, int> _waypointIds;
        private readonly DecentralAStarPathPlanningConfiguration _config;
        private readonly AgentAStarPathManager _pathManager;

        /// <summary>Simulation time of last successful replan.</summary>
        private double _lastPlanTime;

        public BotAStarPlanner(
            BotNormal bot,
            Graph graph,
            BiDictionary<Waypoint, int> waypointIds,
            DecentralAStarPathPlanningConfiguration config,
            AgentAStarPathManager pathManager,
            double initialDelay = 0.0)
        {
            _bot = bot;
            _graph = graph;
            _waypointIds = waypointIds;
            _config = config;
            _pathManager = pathManager;
            // Stagger initial replan across the first interval so all bots don't spike at t=0.
            // Bot with delay=0 replans immediately; bot with delay=ReplanInterval replans at t=ReplanInterval.
            _lastPlanTime = -initialDelay;
        }

        /// <summary>
        /// Attempts to (re)plan the bot's path. Called every simulation update from
        /// AgentAStarPathManager.Update().
        /// </summary>
        /// <param name="currentTime">Current simulation time.</param>
        public void TryReplan(double currentTime)
        {
            // Step 1: Skip if interval not elapsed AND no reopt requested AND has a path.
            // Proceed if: interval elapsed OR forced reopt OR no path yet.
            bool intervalElapsed = currentTime - _lastPlanTime >= _config.ReplanInterval;
            if (!intervalElapsed && !_bot.RequestReoptimization && _bot.Path != null)
                return;

            // Clear the flag here (not in a global loop every step) — mirrors _reoptimize() behavior.
            _bot.RequestReoptimization = false;

            // Step 2: Skip bots in a queue — queue manager handles their movement directly.
            if (_bot.IsQueueing)
                return;

            // Step 3: Resolve destination (handles queue waypoints via protected helper).
            // notifyBotNewDestination() does NOT resolve queue position — must do it here.
            Waypoint destination = _pathManager.ResolveQueueDestination(_bot, _bot.DestinationWaypoint);

            // Step 4: Determine start waypoint (mirrors getBotAgentDictionary line 342).
            // NextWaypoint = the waypoint the bot is currently heading toward (may be null if just starting).
            Waypoint startWaypoint = _bot.NextWaypoint ?? _bot.CurrentWaypoint;

            // Skip if no start, no destination, or already at destination.
            if (startWaypoint == null || destination == null || startWaypoint == destination)
                return;

            // Skip cross-tier navigation (SpaceAStar handles single-tier only).
            if (startWaypoint.Tier.ID != destination.Tier.ID)
                return;

            int startNode = _waypointIds[startWaypoint];
            int endNode = _waypointIds[destination];

            // Build Agent (mirrors getBotAgentDictionary structure).
            var agent = new Agent
            {
                ID = _bot.ID,
                NextNode = startNode,
                DestinationNode = endNode,
                FinalDestinationNode = endNode,
                OrientationAtNextNode = _bot.GetTargetOrientation(),
                ArrivalTimeAtNextNode = currentTime,
                ReservationsToNextNode = new List<ReservationTable.Interval>(),
                Physics = _bot.Physics,
                // Empty bots may tunnel through pod-storage waypoints when CanTunnel=true
                // (same rule as the default centralized planners, PathManager.cs line 377).
                // Loaded bots must use aisle-only paths — pod waypoints are IsObstacle=true
                // after UpdateLocksAndObstacles() runs each tick.
                CanGoThroughObstacles = _config.CanTunnel && _bot.Pod == null,
                FixedPosition = _bot.hasFixedPosition(),
                Resting = _bot.IsResting(),
                RequestReoptimization = _bot.RequestReoptimization,
                Queueing = _bot.IsQueueing,
                NextNodeObject = startWaypoint,
                DestinationNodeObject = destination,
            };

            // Step 5: Create new SpaceAStar per call (NOT reused — see §2.3 of plan).
            var aStar = new SpaceAStar(
                _graph,
                startNode,
                endNode,
                _bot.GetTargetOrientation(),
                _bot.Physics,
                agent);

            // Step 6: Initialize open/closed sets (explicit even though base ctor already calls it).
            aStar.Clear(startNode, endNode);

            // Step 7: Run A* (one-shot blocking call — Search() is not iterative).
            var sw = Stopwatch.StartNew();
            bool found = aStar.Search();
            sw.Stop();

            // Monitoring: log if A* took longer than expected.
            // RuntimeLimitPerAgentMs is a threshold, not a hard interrupt limit.
            if (sw.ElapsedMilliseconds > _config.RuntimeLimitPerAgentMs)
                System.Console.WriteLine(
                    $"[AgentAStar] Warning: bot {_bot.ID} A* took {sw.ElapsedMilliseconds}ms " +
                    $"(threshold: {_config.RuntimeLimitPerAgentMs}ms)");

            // Step 8: Build and assign path if goal was found.
            if (found)
            {
                var path = new Path();
                List<ReservationTable.Interval> reservations;
                aStar.getReservationsAndPath(currentTime, ref path, out reservations);
                // reservations discarded — not stored to shared _reservationTable.
                // No collision gate: AgentAStar is fully decentralized.

                // [Stage A] Safe application of new path:
                // - if mid-segment: defer until safe
                // - if safe: apply immediately
                // - otherwise: keep existing path
                if (_bot.IsMidSegment())
                {
                    // Defer application until the bot finishes current segment
                    _bot.SetPendingPlannedPath(path, currentTime);
                }
                else if (_bot.CanSafelySwapPathNow())
                {
                    // Safe to apply immediately
                    _bot.Path = path;
                }
                // else: keep existing path, try again at next interval

                _lastPlanTime = currentTime;
            }
            // Step 9: If not found, keep existing path and retry at next interval.
        }
    }
}
