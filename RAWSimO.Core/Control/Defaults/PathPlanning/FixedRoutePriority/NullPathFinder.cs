using RAWSimO.MultiAgentPathFinding;
using RAWSimO.MultiAgentPathFinding.Elements;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    /// <summary>
    /// Minimal PathFinder stub — only provides Graph so that
    /// PathManager._initReservationTable() can initialise the ReservationTable.
    /// FindPaths() is intentionally empty; all path planning is done by FRPWS.
    /// </summary>
    internal class NullPathFinder : PathFinder
    {
        public NullPathFinder(Graph graph, int seed, PathPlanningCommunicator communicator)
            : base(graph, seed, communicator) { }

        public override void FindPaths(double currentTime, List<Agent> agents)
        { /* intentionally empty */ }
    }
}
