using System.Collections.Generic;
using System.Linq;
using RAWSimO.Core.Waypoints;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.FixedRoutePriority
{
    public enum ZoneClassification { Intersection, Corridor, Other }

    public class ResourceRegistry
    {
        private readonly Dictionary<int, ZoneClassification> _nodeZone = new Dictionary<int, ZoneClassification>();

        public void Build(IEnumerable<Waypoint> waypoints)
        {
            _nodeZone.Clear();
            foreach (var wp in waypoints)
            {
                int degree = wp.Paths.Count();
                if (degree >= 3)
                    _nodeZone[wp.ID] = ZoneClassification.Intersection;
                else if (degree == 2)
                    _nodeZone[wp.ID] = ZoneClassification.Corridor;
                else
                    _nodeZone[wp.ID] = ZoneClassification.Other;
            }
        }

        public ZoneClassification GetZone(int nodeId)
        {
            return _nodeZone.TryGetValue(nodeId, out var z) ? z : ZoneClassification.Other;
        }

    }
}
