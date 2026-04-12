using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    public static class PriorityRules
    {
        /// <summary>Returns 0=Idle, 1=Pickup, 2=Return, 3=Delivery</summary>
        public static int GetTaskTier(Bot bot)
        {
            var stage = ((BotNormal)bot).GetTrafficTaskStage();
            switch (stage)
            {
                case TrafficTaskStage.Delivery: return 3;
                case TrafficTaskStage.Return:   return 2;
                case TrafficTaskStage.Pickup:   return 1;
                default:                        return 0;
            }
        }

        /// <summary>Returns 1 for vertical, 0 for horizontal movement</summary>
        public static int GetDirectionTier(Bot bot)
        {
            if (bot.CurrentWaypoint == null) return 0;
            var botNormal = bot as BotNormal;
            var dest = botNormal?.DestinationWaypoint;
            if (dest == null) return 0;
            double dx = System.Math.Abs(dest.X - bot.CurrentWaypoint.X);
            double dy = System.Math.Abs(dest.Y - bot.CurrentWaypoint.Y);
            return dy > dx ? 1 : 0;
        }

        /// <summary>Returns true if A has higher priority than B</summary>
        public static bool AHigherThanB(Bot a, Bot b)
        {
            int ta = GetTaskTier(a), tb = GetTaskTier(b);
            if (ta != tb) return ta > tb;
            int da = GetDirectionTier(a), db = GetDirectionTier(b);
            if (da != db) return da > db;
            return a.ID < b.ID;
        }
    }
}
