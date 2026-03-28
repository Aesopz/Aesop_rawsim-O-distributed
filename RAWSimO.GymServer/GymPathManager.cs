using RAWSimO.Core;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar;
using RAWSimO.Core.Waypoints;

namespace RAWSimO.GymServer
{
    /// <summary>
    /// Extends AgentAStarPathManager. When not in baseline mode, gym policy controls
    /// bot.DestinationWaypoint. When in baseline mode, BotManager controls destination as usual.
    /// </summary>
    internal class GymPathManager : AgentAStarPathManager
    {
        private bool _useBaselineMode = false;

        public GymPathManager(Instance instance, DecentralAStarPathPlanningConfiguration config)
            : base(instance, config) { }

        public void SetGymDestination(BotNormal bot, Waypoint target)
        {
            if (!_useBaselineMode)
                bot.DestinationWaypoint = target;
        }

        public void EnableBaselineMode() => _useBaselineMode = true;
        public void DisableBaselineMode() => _useBaselineMode = false;
        public bool IsBaselineMode => _useBaselineMode;
    }
}
