using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Elements;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar
{
    /// <summary>
    /// No-op PathFinder stub used by AgentAStarPathManager.
    ///
    /// Purpose: PathManager's private _initReservationTable() requires PathFinder.Graph to be set.
    /// This stub satisfies that requirement by holding the graph while keeping FindPaths empty.
    /// Per-bot path planning is done by BotAStarPlanner, not here.
    /// </summary>
    internal class AgentAStarGraphHolder : PathFinder
    {
        /// <summary>
        /// Creates the stub, providing the graph to the base PathFinder.
        /// </summary>
        public AgentAStarGraphHolder(Graph graph, int seed, PathPlanningCommunicator communicator)
            : base(graph, seed, communicator) { }

        /// <summary>
        /// Intentionally empty — per-bot planning is done by BotAStarPlanner in AgentAStarPathManager.Update().
        /// </summary>
        public override void FindPaths(double currentTime, List<Agent> agents)
        { /* intentionally empty */ }
    }
}
