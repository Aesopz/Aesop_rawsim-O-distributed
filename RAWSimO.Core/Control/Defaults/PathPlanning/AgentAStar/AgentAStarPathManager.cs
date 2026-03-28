using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Statistics;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.Toolbox;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar
{
    /// <summary>
    /// Decentralized per-agent A* PathManager.
    ///
    /// Each bot independently plans its own path via BotAStarPlanner (SpaceAStar).
    /// No centralized batch replanning (PathFinder.FindPaths is never called for real work).
    /// No reservation gate: RegisterNextWaypoint() always returns true.
    ///
    /// Design:
    /// - PathManager.cs Change 1: RegisterNextWaypoint is virtual → this override bypasses the gate.
    /// - PathManager.cs Change 2: ResolveQueueDestination() helper exposes private _queueManagers.
    /// - _lastCallTimeStamp trick: prevents base.Update() from calling _reoptimize().
    ///   base.Update() still runs: queue management + reservation table maintenance.
    /// - AgentAStarGraphHolder: no-op stub so _initReservationTable() can init without crash.
    ///
    /// Plan 2 (GymPathManager) inherits this class from RAWSimO.GymServer (different assembly).
    /// Hence this class is public (not internal).
    /// _planners is protected so GymPathManager subclass can access per-bot planners if needed.
    /// </summary>
    public class AgentAStarPathManager : PathManager
    {
        private readonly DecentralAStarPathPlanningConfiguration _config;
        private readonly Graph _graph;

        /// <summary>
        /// Per-bot planners. Private because BotAStarPlanner is internal to this assembly.
        /// GymPathManager (Plan 2) inherits Update() behavior without needing direct access to this dict.
        /// </summary>
        private Dictionary<BotNormal, BotAStarPlanner> _planners;

        /// <summary>Exposes the navigation graph for GymServer (Plan 2).</summary>
        public Graph ExposedGraph => _graph;

        /// <summary>Exposes the waypoint↔nodeId mapping for GymServer (Plan 2).</summary>
        public BiDictionary<Waypoint, int> ExposedWaypointIds => _waypointIds;

        /// <summary>
        /// Creates a new AgentAStarPathManager.
        /// </summary>
        /// <param name="instance">The simulation instance.</param>
        /// <param name="config">AgentAStar-specific configuration.</param>
        public AgentAStarPathManager(Instance instance, DecentralAStarPathPlanningConfiguration config)
            : base(instance)
        {
            _config = config;

            // 1. Build graph + queue managers (GenerateGraph is protected in PathManager).
            //    This also initializes _waypointIds and _queueManagers internally.
            _graph = GenerateGraph();

            // 2. Provide graph to base via no-op stub so _initReservationTable() works.
            //    _initReservationTable() requires PathFinder.Graph != null.
            PathFinder = new AgentAStarGraphHolder(
                _graph,
                instance.SettingConfig.Seed,
                PathPlanningCommunicator.DUMMY_COMMUNICATOR);

            // 3. Create one BotAStarPlanner per bot, staggered over one ReplanInterval.
            // Staggering prevents all N bots from running A* simultaneously every interval,
            // which would cause periodic CPU spikes proportional to bot count.
            _planners = new Dictionary<BotNormal, BotAStarPlanner>();
            var bots = instance.Bots.Cast<BotNormal>().ToList();
            int botCount = bots.Count;
            for (int i = 0; i < botCount; i++)
            {
                double stagger = botCount > 1 ? (double)i / botCount * config.ReplanInterval : 0.0;
                _planners[bots[i]] = new BotAStarPlanner(bots[i], _graph, _waypointIds, config, this, stagger);
            }

            // 4. Initialize StatDataPoints (normally done lazily inside _reoptimize, which we bypass).
            //    Required to avoid NullReferenceException in StatFlushPathFinding().
            StatDataPoints = new List<PathFindingDatapoint>();
        }

        /// <summary>
        /// Mostly decentralized: no reservation table check for normal navigation.
        /// Exception: queue waypoints respect QueueManager.LockedWaypoints so bots cannot
        /// physically enter an occupied queue slot — same gate behaviour as other path planners,
        /// but limited to queue areas only. This prevents startup overlap at stations without
        /// requiring a global reservation table.
        /// </summary>
        public override bool RegisterNextWaypoint(
            BotNormal botNormal, double currentTime,
            double blockCurrentWaypointUntil, double rotationDuration,
            Waypoint waypointStart, Waypoint waypointEnd)
        {
            // Block entry into a queue slot that is already locked by another bot.
            if (waypointEnd != null && IsQueueWaypointLocked(waypointEnd, botNormal))
                return false;
            return true;
        }

        /// <summary>
        /// Per-update: run queue management + reservation table via base (but NOT _reoptimize),
        /// then run each bot's individual A* planner.
        /// </summary>
        public override void Update(double lastTime, double currentTime)
        {
            // Block base from calling _reoptimize() (centralized batch replanning).
            // Clocking check in PathManager.Update() (line 441):
            //   if (_lastCallTimeStamp + Clocking > currentTime) return;
            // Setting _lastCallTimeStamp = double.MaxValue/2 makes the condition always true,
            // so base.Update() returns before _reoptimize(). Queue management and reservation
            // table maintenance still run (they execute before the clocking check).
            // RISK: fails if the clocking check is removed from PathManager.Update().
            _lastCallTimeStamp = double.MaxValue / 2;
            base.Update(lastTime, currentTime);

            // Per-bot independent path planning.
            // RequestReoptimization flag is reset inside TryReplan() after each bot's replan.
            foreach (var planner in _planners.Values)
                planner.TryReplan(currentTime);
        }
    }
}
