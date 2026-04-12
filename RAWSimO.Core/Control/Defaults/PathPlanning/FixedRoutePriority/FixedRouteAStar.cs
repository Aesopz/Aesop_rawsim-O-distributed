using System;
using System.Collections.Generic;
using RAWSimO.Core.Waypoints;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    /// <summary>
    /// Simple A* that returns waypoint IDs. Ignores timing (no space-time dimension).
    /// </summary>
    public static class FixedRouteAStar
    {
        public static List<int> FindPath(
            Waypoint start,
            Waypoint goal,
            IEnumerable<Waypoint> allWaypoints,
            HashSet<int> blockedNodes,
            bool canTunnel)
        {
            if (start == null || goal == null) return null;
            if (start.ID == goal.ID) return new List<int> { start.ID };

            var wpById = new Dictionary<int, Waypoint>();
            foreach (var wp in allWaypoints) wpById[wp.ID] = wp;

            var open = new SortedSet<(double f, int id)>(Comparer<(double, int)>.Create((a, b) =>
                a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2)));

            var gScore = new Dictionary<int, double>();
            var cameFrom = new Dictionary<int, int>();

            gScore[start.ID] = 0;
            open.Add((Heuristic(start, goal), start.ID));

            while (open.Count > 0)
            {
                var (_, curId) = open.Min;
                open.Remove(open.Min);

                if (curId == goal.ID)
                    return ReconstructPath(cameFrom, curId);

                if (!wpById.TryGetValue(curId, out var cur)) continue;

                foreach (var neighbor in cur.Paths)
                {
                    int nid = neighbor.ID;
                    // Skip blocked nodes (except goal)
                    if (nid != goal.ID && blockedNodes != null && blockedNodes.Contains(nid))
                        continue;
                    // Skip pod storage if not tunneling (except goal)
                    if (!canTunnel && nid != goal.ID && neighbor.PodStorageLocation)
                        continue;

                    double dist = cur[neighbor];
                    double tentative = gScore.GetValueOrDefault(curId, double.MaxValue / 2) + dist;
                    if (tentative < gScore.GetValueOrDefault(nid, double.MaxValue / 2))
                    {
                        cameFrom[nid] = curId;
                        gScore[nid] = tentative;
                        open.Add((tentative + Heuristic(neighbor, goal), nid));
                    }
                }
            }
            return null; // no path
        }

        private static double Heuristic(Waypoint a, Waypoint b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
        {
            var path = new List<int> { current };
            while (cameFrom.ContainsKey(current))
            {
                current = cameFrom[current];
                path.Insert(0, current);
            }
            return path;
        }
    }
}
