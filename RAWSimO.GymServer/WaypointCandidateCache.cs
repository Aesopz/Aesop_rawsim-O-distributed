using RAWSimO.Core.Bots;
using RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar;
using RAWSimO.Core.Waypoints;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.Toolbox;
using System.Collections.Generic;

namespace RAWSimO.GymServer
{
    /// <summary>Cardinal direction indices (match Python DIRECTION_* constants).</summary>
    internal static class CardinalDirection
    {
        public const int North = 0;
        public const int South = 1;
        public const int East  = 2;
        public const int West  = 3;
        public const int Stay  = 4;
        public const int Count = 4; // number of directional slots (Stay is not a slot)

        /// <summary>
        /// Classify an edge angle into a cardinal direction index.
        /// Angle convention (matches Edge.cs): 0°=East, increasing clockwise.
        ///   EAST  =   0° (a <= 45  || a > 315)
        ///   SOUTH =  90° (45  < a <= 135)
        ///   WEST  = 180° (135 < a <= 225)
        ///   NORTH = 270° (225 < a <= 315)
        /// </summary>
        public static int ClassifyAngle(short angle)
        {
            int a = ((angle % 360) + 360) % 360;
            if (a <= 45 || a > 315) return East;
            if (a > 45  && a <= 135) return South;
            if (a > 135 && a <= 225) return West;
            return North; // 225 < a <= 315
        }
    }

    /// <summary>
    /// Maps each bot's current waypoint to 4 cardinal-direction neighbours.
    /// Index 0=North, 1=South, 2=East, 3=West; null means no edge in that direction.
    /// </summary>
    internal class WaypointCandidateCache
    {
        private readonly Graph _graph;
        private readonly BiDictionary<Waypoint, int> _waypointIds;
        private readonly Dictionary<BotNormal, Waypoint?[]> _cache = new();

        public WaypointCandidateCache(AgentAStarPathManager pathManager)
        {
            _graph = pathManager.ExposedGraph;
            _waypointIds = pathManager.ExposedWaypointIds;
        }

        public Waypoint?[] GetCandidates(BotNormal bot)
        {
            var result = new Waypoint?[CardinalDirection.Count]; // [N, S, E, W]

            if (bot.CurrentWaypoint == null || !_waypointIds.ValuesFirst.Contains(bot.CurrentWaypoint))
            {
                _cache[bot] = result;
                return result;
            }

            int startId = _waypointIds[bot.CurrentWaypoint];
            if (_graph.Edges[startId] == null)
            {
                _cache[bot] = result;
                return result;
            }

            foreach (var edge in _graph.Edges[startId])
            {
                int dir = CardinalDirection.ClassifyAngle(edge.Angle);
                if (result[dir] == null)
                    result[dir] = _waypointIds[edge.To];
            }

            _cache[bot] = result;
            return result;
        }

        public Waypoint?[] GetCached(BotNormal bot)
        {
            return _cache.TryGetValue(bot, out var result) ? result : GetCandidates(bot);
        }
    }
}
