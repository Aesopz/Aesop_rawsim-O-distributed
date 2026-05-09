using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Geometrics;
using RAWSimO.Core.IO;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RAWSimO.Core.Metrics
{
    internal sealed class M1GPhysicalTravelTimeEstimator
    {
        public const string EstimatorName = "physical_path_eta";

        private readonly Instance _instance;

        public M1GPhysicalTravelTimeEstimator(Instance instance)
        {
            _instance = instance;
        }

        public M1GPhysicalTravelTimeResult Estimate(Bot bot, Waypoint start, Waypoint target, bool carryingPod, double startOrientation, Func<double> fallback)
        {
            if (start == null || target == null)
                return M1GPhysicalTravelTimeResult.Fallback(double.PositiveInfinity, start, target);
            if (start == target)
                return new M1GPhysicalTravelTimeResult(new List<Waypoint> { start }, 0.0, 0.0, false);

            var path = FindShortestPath(start, target, carryingPod);
            if (path.Nodes.Count >= 2 && !double.IsInfinity(path.Distance))
            {
                double eta = CalculatePhysicalEta(bot, path.Nodes, startOrientation);
                if (!double.IsNaN(eta) && !double.IsInfinity(eta))
                    return new M1GPhysicalTravelTimeResult(path.Nodes, path.Distance, eta, false);
            }

            double fallbackEta = fallback != null ? fallback() : double.PositiveInfinity;
            return M1GPhysicalTravelTimeResult.Fallback(fallbackEta, start, target);
        }

        public string BuildBotPodCacheKey(Bot bot, Waypoint start, Waypoint target, double startOrientation)
        {
            return "bp|" +
                Id(bot) + "|" +
                Id(start) + "|" +
                Id(target) + "|" +
                F(startOrientation) + "|loaded=0";
        }

        public string BuildPodStationCacheKey(Waypoint start, Waypoint target, Bot representativeBot)
        {
            return "ps|" +
                Id(start) + "|" +
                Id(target) + "|loaded=1|" +
                BuildPhysicsKey(representativeBot);
        }

        public string BuildPhysicsKey(Bot bot)
        {
            if (bot == null)
                return "none";
            return F(bot.MaxAcceleration) + "|" + F(bot.MaxDeceleration) + "|" + F(bot.MaxVelocity) + "|" + F(bot.TurnSpeed);
        }

        public double CalculatePhysicalEta(Bot bot, IList<Waypoint> nodes, double startOrientation)
        {
            if (nodes == null || nodes.Count < 2)
                return 0.0;

            var normalBot = bot as BotNormal;
            if (normalBot == null)
                return CalculateFallbackKinematicEta(bot, nodes, startOrientation);

            double eta = 0.0;
            double currentOrientation = startOrientation;
            for (int i = 1; i < nodes.Count; i++)
            {
                Waypoint from = nodes[i - 1];
                Waypoint to = nodes[i];
                double segmentOrientation = Circle.GetOrientation(from.X, from.Y, to.X, to.Y);
                if (!double.IsNaN(currentOrientation))
                    eta += normalBot.Physics.getTimeNeededToTurn(currentOrientation, segmentOrientation);
                eta += normalBot.Physics.getTimeNeededToMove(0, from.GetDistance(to));
                currentOrientation = segmentOrientation;
            }
            return eta;
        }

        private double CalculateFallbackKinematicEta(Bot bot, IList<Waypoint> nodes, double startOrientation)
        {
            if (bot == null || bot.MaxVelocity <= 0.0)
                return double.PositiveInfinity;

            double eta = 0.0;
            double currentOrientation = startOrientation;
            for (int i = 1; i < nodes.Count; i++)
            {
                Waypoint from = nodes[i - 1];
                Waypoint to = nodes[i];
                double segmentOrientation = Circle.GetOrientation(from.X, from.Y, to.X, to.Y);
                if (!double.IsNaN(currentOrientation) && bot.TurnSpeed > 0.0)
                    eta += Math.Abs(Circle.GetOrientationDifference(currentOrientation, segmentOrientation)) / (2.0 * Math.PI) * bot.TurnSpeed;
                eta += from.GetDistance(to) / bot.MaxVelocity;
                currentOrientation = segmentOrientation;
            }
            return eta;
        }

        public M1GPhysicalPathResult FindShortestPath(Waypoint start, Waypoint target, bool carryingPod)
        {
            if (start == null || target == null)
                return new M1GPhysicalPathResult(new List<Waypoint>(), double.PositiveInfinity);
            if (start == target)
                return new M1GPhysicalPathResult(new List<Waypoint> { start }, 0.0);

            var open = new Dictionary<Waypoint, SearchNode>();
            var closed = new HashSet<Waypoint>();
            open[start] = new SearchNode(start, null, 0.0, start.GetDistance(target));

            while (open.Count > 0)
            {
                var currentPair = open.ArgMin(v => v.Value.DistanceTraveled + v.Value.DistanceToGoal);
                Waypoint current = currentPair.Key;
                SearchNode currentData = currentPair.Value;
                if (current == target)
                    return new M1GPhysicalPathResult(BuildPath(currentData), currentData.DistanceTraveled);

                open.Remove(current);
                closed.Add(current);

                foreach (Waypoint successor in current.Paths)
                {
                    if (closed.Contains(successor))
                        continue;
                    if (carryingPod && successor.PodStorageLocation && successor != target)
                        continue;

                    double distance = currentData.DistanceTraveled + current[successor];
                    double heuristic = successor.GetDistance(target);
                    if (successor.Tier != target.Tier)
                        heuristic += _instance.WrongTierPenaltyDistance;

                    if (!open.ContainsKey(successor))
                        open[successor] = new SearchNode(successor, currentData, distance, heuristic);
                    else if (open[successor].DistanceTraveled > distance)
                    {
                        open[successor].Parent = currentData;
                        open[successor].DistanceTraveled = distance;
                        open[successor].DistanceToGoal = heuristic;
                    }
                }
            }

            return new M1GPhysicalPathResult(new List<Waypoint>(), double.PositiveInfinity);
        }

        private static List<Waypoint> BuildPath(SearchNode node)
        {
            var nodes = new List<Waypoint>();
            while (node != null)
            {
                nodes.Add(node.Waypoint);
                node = node.Parent;
            }
            nodes.Reverse();
            return nodes;
        }

        private static string Id(Bot bot)
        {
            return bot == null ? "-1" : bot.ID.ToString(CultureInfo.InvariantCulture);
        }

        private static string Id(Waypoint waypoint)
        {
            return waypoint == null ? "-1" : waypoint.ID.ToString(CultureInfo.InvariantCulture);
        }

        private static string F(double value)
        {
            if (double.IsNaN(value))
                return "NaN";
            if (double.IsPositiveInfinity(value))
                return "Infinity";
            if (double.IsNegativeInfinity(value))
                return "-Infinity";
            return value.ToString(IOConstants.FORMATTER);
        }

        private sealed class SearchNode
        {
            public SearchNode(Waypoint waypoint, SearchNode parent, double distanceTraveled, double distanceToGoal)
            {
                Waypoint = waypoint;
                Parent = parent;
                DistanceTraveled = distanceTraveled;
                DistanceToGoal = distanceToGoal;
            }

            public Waypoint Waypoint;
            public SearchNode Parent;
            public double DistanceTraveled;
            public double DistanceToGoal;
        }
    }

    internal sealed class M1GPhysicalPathResult
    {
        public M1GPhysicalPathResult(List<Waypoint> nodes, double distance)
        {
            Nodes = nodes;
            Distance = distance;
        }

        public List<Waypoint> Nodes { get; private set; }
        public double Distance { get; private set; }
    }

    internal sealed class M1GPhysicalTravelTimeResult
    {
        public M1GPhysicalTravelTimeResult(List<Waypoint> nodes, double distance, double eta, bool usedFallback)
        {
            Nodes = nodes;
            Distance = distance;
            Eta = eta;
            UsedFallback = usedFallback;
        }

        public List<Waypoint> Nodes { get; private set; }
        public double Distance { get; private set; }
        public double Eta { get; private set; }
        public bool UsedFallback { get; private set; }

        public static M1GPhysicalTravelTimeResult Fallback(double eta, Waypoint start, Waypoint target)
        {
            var nodes = new List<Waypoint>();
            if (start != null)
                nodes.Add(start);
            if (target != null && target != start)
                nodes.Add(target);
            double distance = start != null && target != null ? start.GetDistance(target) : double.PositiveInfinity;
            return new M1GPhysicalTravelTimeResult(nodes, distance, eta, true);
        }
    }

    internal sealed class M1GPhysicalTravelTimeStats
    {
        private readonly List<double> _etas = new List<double>();
        private readonly List<double> _ratios = new List<double>();

        public int PathSampleCount { get; private set; }
        public int FallbackCount { get; private set; }

        public void Add(M1GPhysicalTravelTimeResult result, double distance)
        {
            if (result == null)
                return;
            PathSampleCount++;
            if (result.UsedFallback)
                FallbackCount++;
            if (!double.IsNaN(result.Eta) && !double.IsInfinity(result.Eta))
                _etas.Add(result.Eta);
            if (!double.IsNaN(result.Eta) && !double.IsInfinity(result.Eta) && result.Eta > 0.0 &&
                !double.IsNaN(distance) && !double.IsInfinity(distance) && distance > 0.0)
                _ratios.Add(result.Eta / distance);
        }

        public double AverageEta { get { return Average(_etas); } }
        public double MedianEta { get { return Median(_etas); } }
        public double AverageDistanceRatio { get { return Average(_ratios); } }
        public double MedianDistanceRatio { get { return Median(_ratios); } }

        private static double Average(List<double> values)
        {
            return values.Count == 0 ? double.NaN : values.Average();
        }

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
                return double.NaN;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }
    }
}
