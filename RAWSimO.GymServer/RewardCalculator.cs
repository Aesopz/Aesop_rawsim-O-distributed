using RAWSimO.Core;
using RAWSimO.Core.Bots;
using System.Collections.Generic;

namespace RAWSimO.GymServer
{
    internal class RewardCalculator
    {
        private readonly Instance _instance;
        private readonly Dictionary<BotNormal, double> _prevOrdersCompleted;
        private readonly Dictionary<BotNormal, double> _prevIdleTime;

        private const double ALPHA = 1.0;
        private const double BETA  = 0.5;
        private const double GAMMA = 0.1;

        public RewardCalculator(Instance instance, IEnumerable<BotNormal> bots)
        {
            _instance = instance;
            _prevOrdersCompleted = new Dictionary<BotNormal, double>();
            _prevIdleTime = new Dictionary<BotNormal, double>();
            foreach (var bot in bots)
            {
                _prevOrdersCompleted[bot] = 0;
                _prevIdleTime[bot] = 0;
            }
        }

        public double ComputeReward(BotNormal bot, double dt, int collisions)
        {
            // Throughput: use StatNumberOfPickups as proxy — increments when bot picks up items
            double currentOrders = bot.StatNumberOfPickups;
            double ordersCompleted = currentOrders - _prevOrdersCompleted[bot];
            _prevOrdersCompleted[bot] = currentOrders;

            // Idle ratio: bot is idle when it has no NextWaypoint and no DestinationWaypoint
            bool isIdle = (bot.NextWaypoint == null && bot.DestinationWaypoint == null);
            double idleRatio = isIdle ? 1.0 : 0.0;

            double reward = ALPHA * ordersCompleted
                          - BETA  * collisions
                          - GAMMA * idleRatio;
            return reward;
        }

        public void Reset(IEnumerable<BotNormal> bots)
        {
            foreach (var bot in bots)
            {
                _prevOrdersCompleted[bot] = 0;
                _prevIdleTime[bot] = 0;
            }
        }
    }
}
