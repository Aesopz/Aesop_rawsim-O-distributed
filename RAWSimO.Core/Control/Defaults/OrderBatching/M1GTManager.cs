using RAWSimO.Core.Configurations;
using RAWSimO.Core.Elements;
using RAWSimO.Core.IO;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using RAWSimO.Core.Metrics;
using RAWSimO.Core.Bots;
using RAWSimO.Core.Waypoints;
using RAWSimO.SolverWrappers;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static RAWSimO.Core.Management.ResourceManager;
using static System.Collections.Specialized.BitVector32;
//using static RAWSimO.Core.Control.Defaults.TaskAllocation.BalancedBotManager;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    /// <summary>
    /// Pod的比较器
    /// </summary>
    public class M1GTPodComparer : IEqualityComparer<Pod>
    {
        /// <summary>
        /// equal
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Equals(Pod x, Pod y)
        {
            return x.ID == y.ID;
        }
        /// <summary>
        /// gethashcode
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public int GetHashCode(Pod obj)
        {
            return obj.ID.GetHashCode();
        }
    }

    /// <summary>
    /// Implements a manager that uses information of the backlog to exploit similarities in orders when assigning them.
    /// </summary>
    public class M1GTManager : OrderManager
    {
        /// <summary>
        /// Creates a new instance of this manager.
        /// </summary>
        /// <param name="instance">The instance this manager belongs to.</param>
        public M1GTManager(Instance instance) : base(instance) { _config = instance.ControllerConfig.OrderBatchingConfig as M1GTConfiguration; }

        /// <summary>
        /// The config of this controller.
        /// </summary>
        private M1GTConfiguration _config;
        private int _m1gtDecisionId = 0;
        private bool _m1gtValidationBatchArmed = false;
        private M1GPhysicalTravelTimeEstimator _physicalEtaEstimator;
        private Dictionary<string, M1GPhysicalTravelTimeResult> _physicalEtaCache = new Dictionary<string, M1GPhysicalTravelTimeResult>();
        private M1GPhysicalTravelTimeStats _physicalEtaStats = new M1GPhysicalTravelTimeStats();
        private Dictionary<int, int> _forcedDecisionPolicy = null;

        private sealed class M1GTCandidate
        {
            public int DecisionId;
            public int Rank;
            public double Objective;
            public int UnusedCapacitySum;
            public List<Symbol> Xps = new List<Symbol>();
            public List<Symbol> Yos = new List<Symbol>();
            public List<Symbol> Yaos = new List<Symbol>();
            public List<Symbol> Yrp = new List<Symbol>();
            public List<Symbol> Dops = new List<Symbol>();
        }

        private sealed class ObjectiveCalibration
        {
            public bool UseShortestTimeObjective;
            public double TimePerDistanceScale = 1.0;
            public double TravelWeight = 1.0;
            public double OrderReward = -40.0;
            public double UnusedCapacityPenalty = 1000.0;
            public int RatioSampleCount;
            public string EstimatorName = "";
            public int PathSampleCount;
            public int FallbackCount;
            public double AveragePhysicalEta = double.NaN;
            public double MedianPhysicalEta = double.NaN;
            public double AverageDistanceRatio = double.NaN;
            public double MedianDistanceRatio = double.NaN;
        }

        /// <summary>
        /// Checks whether another order is assignable to the given station.
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if there is another open slot, <code>false</code> otherwise.</returns>
        private bool IsAssignable(OutputStation station)
        { return station.Active && station.CapacityReserved + station.CapacityInUse < station.Capacity; }
        /// <summary>
        /// Checks whether another order is assignable to the given station.
        /// </summary>
        /// <param name="station">The station to check.</param>
        /// <returns><code>true</code> if there is another open slot and another one reserved for fast-lane, <code>false</code> otherwise.</returns>
        private bool IsAssignableKeepFastLaneSlot(OutputStation station)
        { return station.Active && station.CapacityReserved + station.CapacityInUse < station.Capacity - 1; }
        /// <summary>
        /// 已完成分配的变量集合
        /// </summary>
        public Dictionary<ItemDescription, int> _itemofPiSKU;
        /// <summary>
        /// Od约束是否需要执行
        /// </summary>
        private bool IsOd = false;
        /// <summary>
        /// order进入Od的截止时间
        /// </summary>
        private double DueTimeOrderofMP = TimeSpan.FromMinutes(30).TotalSeconds;
        /// <summary>
        /// 决策变量的命名
        /// </summary>
        private Dictionary<int, List<Symbol>> _IsvariableNames = new Dictionary<int, List<Symbol>>();

        private Dictionary<int, int> ForcedDecisionPolicy
        {
            get
            {
                if (_forcedDecisionPolicy == null)
                    _forcedDecisionPolicy = LoadForcedDecisionPolicy(_config != null ? _config.ForcedDecisionPolicyPath : "");
                return _forcedDecisionPolicy;
            }
        }

        private static Dictionary<int, int> LoadForcedDecisionPolicy(string path)
        {
            var policy = new Dictionary<int, int>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return policy;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;
                var line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;
                int decisionId;
                int rank;
                if (!int.TryParse(Unquote(parts[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out decisionId))
                    continue;
                if (!int.TryParse(Unquote(parts[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out rank))
                    continue;
                if (decisionId > 0 && rank > 0)
                    policy[decisionId] = rank;
            }
            return policy;
        }

        private static string Unquote(string value)
        {
            if (value == null)
                return "";
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
            return value;
        }

        private RAWSimO.Core.Waypoints.Waypoint GetBotReferenceWaypoint(Bot bot)
        {
            if (bot == null)
                return null;
            if (bot.CurrentWaypoint != null)
                return bot.CurrentWaypoint;
            if (Instance != null && Instance.WaypointGraph != null && bot.Tier != null)
                return Instance.WaypointGraph.GetClosestWaypoint(bot.Tier, bot.X, bot.Y);
            return null;
        }

        private RAWSimO.Core.Waypoints.Waypoint GetPodReferenceWaypoint(Pod pod)
        {
            if (pod == null)
                return null;
            if (pod.Waypoint != null)
                return pod.Waypoint;
            if (pod.Bot != null && pod.Bot.CurrentWaypoint != null)
                return pod.Bot.CurrentWaypoint;
            if (Instance != null && Instance.WaypointGraph != null && pod.Tier != null)
                return Instance.WaypointGraph.GetClosestWaypoint(pod.Tier, pod.X, pod.Y);
            return null;
        }

        private M1GPhysicalTravelTimeEstimator PhysicalEtaEstimator
        {
            get
            {
                if (_physicalEtaEstimator == null)
                    _physicalEtaEstimator = new M1GPhysicalTravelTimeEstimator(Instance);
                return _physicalEtaEstimator;
            }
        }

        private void ResetPhysicalEtaDecisionCache()
        {
            _physicalEtaCache = new Dictionary<string, M1GPhysicalTravelTimeResult>();
            _physicalEtaStats = new M1GPhysicalTravelTimeStats();
        }

        private double GetBotOrientation(Bot bot)
        {
            var normalBot = bot as BotNormal;
            return normalBot != null ? normalBot.Orientation : double.NaN;
        }

        private Bot GetRepresentativeBot()
        {
            return Instance.Bots.OrderBy(b => b.MaxVelocity).ThenBy(b => b.TurnSpeed).ThenBy(b => b.ID).FirstOrDefault();
        }

        private M1GPhysicalTravelTimeResult GetBotPodPhysicalEta(Bot bot, Pod pod)
        {
            var botWaypoint = GetBotReferenceWaypoint(bot);
            var podWaypoint = GetPodReferenceWaypoint(pod);
            double startOrientation = GetBotOrientation(bot);
            string key = PhysicalEtaEstimator.BuildBotPodCacheKey(bot, botWaypoint, podWaypoint, startOrientation);
            if (!_physicalEtaCache.ContainsKey(key))
            {
                var result = PhysicalEtaEstimator.Estimate(
                    bot,
                    botWaypoint,
                    podWaypoint,
                    false,
                    startOrientation,
                    () => Distances.CalculateShortestTimePath(botWaypoint, podWaypoint, Instance));
                _physicalEtaCache[key] = result;
                _physicalEtaStats.Add(result, EstimateBotPodDistance(bot, pod));
            }
            return _physicalEtaCache[key];
        }

        private M1GPhysicalTravelTimeResult GetPodStationPhysicalEta(Pod pod, OutputStation station)
        {
            var podWaypoint = GetPodReferenceWaypoint(pod);
            var queueWaypoint = GetStationQueueBoundaryWaypoint(pod, station);
            Bot representativeBot = GetRepresentativeBot();
            string key = PhysicalEtaEstimator.BuildPodStationCacheKey(podWaypoint, queueWaypoint, representativeBot);
            if (!_physicalEtaCache.ContainsKey(key))
            {
                var result = PhysicalEtaEstimator.Estimate(
                    representativeBot,
                    podWaypoint,
                    queueWaypoint,
                    true,
                    double.NaN,
                    () => Distances.CalculateShortestTimePathPodSafe(podWaypoint, queueWaypoint, Instance));
                _physicalEtaCache[key] = result;
                _physicalEtaStats.Add(result, EstimatePodStationDistance(pod, station));
            }
            return _physicalEtaCache[key];
        }

        private double EstimateBotPodDistance(Bot bot, Pod pod)
        {
            if (bot == null || pod == null)
                return double.PositiveInfinity;

            var botWaypoint = GetBotReferenceWaypoint(bot);
            var podWaypoint = GetPodReferenceWaypoint(pod);
            if (botWaypoint != null && podWaypoint != null)
                return Distances.CalculateShortestPath(botWaypoint, podWaypoint, Instance);

            double botX = botWaypoint != null ? botWaypoint.X : bot.X;
            double botY = botWaypoint != null ? botWaypoint.Y : bot.Y;
            double podX = podWaypoint != null ? podWaypoint.X : pod.X;
            double podY = podWaypoint != null ? podWaypoint.Y : pod.Y;
            return Math.Abs(botX - podX) + Math.Abs(botY - podY);
        }

        private double EstimateBotPodTime(Bot bot, Pod pod)
        {
            if (bot == null || pod == null)
                return double.PositiveInfinity;

            var botWaypoint = GetBotReferenceWaypoint(bot);
            var podWaypoint = GetPodReferenceWaypoint(pod);
            if (botWaypoint != null && podWaypoint != null)
                return GetBotPodPhysicalEta(bot, pod).Eta;

            double distance = EstimateBotPodDistance(bot, pod);
            return double.IsInfinity(distance) || bot.MaxVelocity <= 0.0 ? double.PositiveInfinity : distance / bot.MaxVelocity;
        }

        private double EstimatePodStationDistance(Pod pod, OutputStation station)
        {
            Waypoint queueWaypoint = GetStationQueueBoundaryWaypoint(pod, station);
            if (pod == null || station == null || queueWaypoint == null)
                return double.PositiveInfinity;

            var podWaypoint = GetPodReferenceWaypoint(pod);
            if (podWaypoint != null &&
                DistanceSet.ContainsKey(queueWaypoint.ID) &&
                DistanceSet[queueWaypoint.ID].ContainsKey(podWaypoint.ID))
                return DistanceSet[queueWaypoint.ID][podWaypoint.ID];

            if (podWaypoint != null)
                return Distances.CalculateShortestPathPodSafe1(podWaypoint, queueWaypoint, Instance);

            double podX = podWaypoint != null ? podWaypoint.X : pod.X;
            double podY = podWaypoint != null ? podWaypoint.Y : pod.Y;
            return Math.Abs(podX - queueWaypoint.X) + Math.Abs(podY - queueWaypoint.Y);
        }

        private double EstimatePodStationTime(Pod pod, OutputStation station)
        {
            Waypoint queueWaypoint = GetStationQueueBoundaryWaypoint(pod, station);
            if (pod == null || station == null || queueWaypoint == null)
                return double.PositiveInfinity;

            var podWaypoint = GetPodReferenceWaypoint(pod);
            if (podWaypoint != null)
                return GetPodStationPhysicalEta(pod, station).Eta;

            double distance = EstimatePodStationDistance(pod, station);
            double velocity = Instance.Bots.Any() ? Instance.Bots.Min(b => b.MaxVelocity) : 0.0;
            return double.IsInfinity(distance) || velocity <= 0.0 ? double.PositiveInfinity : distance / velocity;
        }

        private double EstimateBotPodObjectiveCost(Bot bot, Pod pod)
        {
            return _config.UseShortestTimeObjective ? EstimateBotPodTime(bot, pod) : EstimateBotPodDistance(bot, pod);
        }

        private double EstimatePodStationObjectiveCost(Pod pod, OutputStation station)
        {
            return _config.UseShortestTimeObjective ? EstimatePodStationTime(pod, station) : EstimatePodStationDistance(pod, station);
        }

        private IEnumerable<Waypoint> GetStationQueueBoundaryCandidates(OutputStation station)
        {
            if (station == null || station.Queues == null)
                return Enumerable.Empty<Waypoint>();

            return station.Queues
                .SelectMany(q => new[] { q.Key }.Concat(q.Value ?? Enumerable.Empty<Waypoint>()))
                .Where(w => w != null && w != station.Waypoint)
                .Distinct();
        }

        private Waypoint GetStationQueueBoundaryWaypoint(Pod pod, OutputStation station)
        {
            if (station == null)
                return null;

            var candidates = GetStationQueueBoundaryCandidates(station).ToList();
            if (candidates.Count == 0)
                return station.Waypoint;

            var podWaypoint = GetPodReferenceWaypoint(pod);
            if (podWaypoint == null)
                return candidates.OrderBy(w => w.GetDistance(station.Waypoint)).FirstOrDefault();

            return candidates
                .OrderBy(w => EstimatePodQueueWaypointDistance(podWaypoint, w))
                .ThenBy(w => w.ID)
                .FirstOrDefault();
        }

        private double EstimatePodQueueWaypointDistance(Waypoint podWaypoint, Waypoint queueWaypoint)
        {
            if (podWaypoint == null || queueWaypoint == null)
                return double.PositiveInfinity;
            if (DistanceSet.ContainsKey(queueWaypoint.ID) &&
                DistanceSet[queueWaypoint.ID].ContainsKey(podWaypoint.ID))
                return DistanceSet[queueWaypoint.ID][podWaypoint.ID];
            return Distances.CalculateShortestPathPodSafe1(podWaypoint, queueWaypoint, Instance);
        }

        private ObjectiveCalibration BuildObjectiveCalibration(IEnumerable<Symbol> deVarNamexps, IEnumerable<Symbol> deVarNameyrp,
            Dictionary<OutputStation, int> Cs, HashSet<Bot> Ra)
        {
            var calibration = new ObjectiveCalibration
            {
                UseShortestTimeObjective = _config.UseShortestTimeObjective,
                OrderReward = _config.BaseOrderReward,
                UnusedCapacityPenalty = _config.BaseUnusedCapacityPenalty
            };

            if (!calibration.UseShortestTimeObjective)
                return calibration;

            calibration.EstimatorName = M1GPhysicalTravelTimeEstimator.EstimatorName;
            double scale = _config.TimePerDistanceScale;
            List<double> ratios = new List<double>();
            foreach (var xps in deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)))
            {
                double time = EstimatePodStationTime(xps.pod, xps.outputstation);
                double distance = EstimatePodStationDistance(xps.pod, xps.outputstation);
                if (scale <= 0.0)
                    AddTimeDistanceRatio(ratios, time, distance);
            }

            foreach (var yrp in deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null))
            {
                double time = EstimateBotPodTime(yrp.robot, yrp.pod);
                double distance = EstimateBotPodDistance(yrp.robot, yrp.pod);
                if (scale <= 0.0)
                    AddTimeDistanceRatio(ratios, time, distance);
            }

            if (scale <= 0.0)
            {
                ratios.Sort();
                if (ratios.Count > 0)
                    scale = ratios[ratios.Count / 2];
                else
                    scale = 1.0;
            }

            calibration.TimePerDistanceScale = scale;
            calibration.OrderReward = _config.BaseOrderReward * scale;
            calibration.UnusedCapacityPenalty = _config.BaseUnusedCapacityPenalty * scale;
            calibration.RatioSampleCount = ratios.Count;
            calibration.PathSampleCount = _physicalEtaStats.PathSampleCount;
            calibration.FallbackCount = _physicalEtaStats.FallbackCount;
            calibration.AveragePhysicalEta = _physicalEtaStats.AverageEta;
            calibration.MedianPhysicalEta = _physicalEtaStats.MedianEta;
            calibration.AverageDistanceRatio = _physicalEtaStats.AverageDistanceRatio;
            calibration.MedianDistanceRatio = _physicalEtaStats.MedianDistanceRatio;
            return calibration;
        }

        private static void AddTimeDistanceRatio(List<double> ratios, double time, double distance)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time <= 0.0)
                return;
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0.0)
                return;
            ratios.Add(time / distance);
        }

        private bool CandidateUsedFallback(IEnumerable<Symbol> xpsList, IEnumerable<Symbol> yrpList)
        {
            return yrpList.Any(v => GetBotPodPhysicalEta(v.robot, v.pod).UsedFallback) ||
                xpsList.Any(v => GetPodStationPhysicalEta(v.pod, v.outputstation).UsedFallback);
        }

        private void RecordSelectedPathEstimates(IEnumerable<Symbol> selectedXps, IEnumerable<Symbol> selectedYrp)
        {
            foreach (var yrp in selectedYrp)
            {
                var xps = selectedXps.FirstOrDefault(v => v.pod.ID == yrp.pod.ID);
                if (xps == null)
                    continue;

                var botWaypoint = GetBotReferenceWaypoint(yrp.robot);
                var podWaypoint = GetPodReferenceWaypoint(yrp.pod);
                var stationWaypoint = GetStationQueueBoundaryWaypoint(yrp.pod, xps.outputstation);
                Instance.M1GPathTrace.RecordEstimate(
                    yrp.robot,
                    yrp.pod,
                    xps.outputstation,
                    botWaypoint,
                    podWaypoint,
                    stationWaypoint,
                    EstimateBotPodObjectiveCost(yrp.robot, yrp.pod),
                    EstimatePodStationObjectiveCost(yrp.pod, xps.outputstation),
                    Instance.Controller.CurrentTime);
            }
        }

        private M1GTCandidate ExtractCandidate(int decisionId, int rank, double objective,
            Dictionary<int, List<Symbol>> variableNames, VariableCollection<string> variablesBinary,
            VariableCollection<string> variablesInteger3)
        {
            var candidate = new M1GTCandidate { DecisionId = decisionId, Rank = rank, Objective = objective };
            for (int i = 1; i < variableNames.Count + 1; i++)
            {
                foreach (var itemName in variableNames[i])
                {
                    if (i == 5)
                    {
                        candidate.UnusedCapacitySum += (int)Math.Round(variablesInteger3[itemName.name].GetValue());
                        continue;
                    }
                    if (Math.Round(variablesBinary[itemName.name].GetValue()) == 0)
                        continue;

                    if (i == 1)
                        candidate.Xps.Add(itemName);
                    else if (i == 2)
                        candidate.Yos.Add(itemName);
                    else if (i == 3)
                        candidate.Yaos.Add(itemName);
                    else if (i == 4)
                        candidate.Yrp.Add(itemName);
                    else if (i == 6)
                        candidate.Dops.Add(itemName);
                }
            }
            return candidate;
        }

        private bool AddNoGoodConstraint(LinearModel wrapper, VariableCollection<string> variablesBinary, M1GTCandidate candidate)
        {
            var coreNames = candidate.Xps.Concat(candidate.Yrp)
                .Select(s => s.name)
                .Distinct()
                .ToList();
            if (coreNames.Count == 0)
                return false;

            wrapper.AddConstr(LinearExpression.Sum(coreNames.Select(name => variablesBinary[name]), wrapper) <= coreNames.Count - 1,
                "m1gt_nogood_" + candidate.DecisionId.ToString(CultureInfo.InvariantCulture) + "_" + candidate.Rank.ToString(CultureInfo.InvariantCulture));
            wrapper.Update();
            return true;
        }

        private List<M1GTCandidate> CollectCandidates(LinearModel wrapper, VariableCollection<string> variablesBinary,
            VariableCollection<string> variablesInteger3, Dictionary<int, List<Symbol>> variableNames, int decisionId, int topK)
        {
            var candidates = new List<M1GTCandidate>();
            int limit = Math.Max(1, topK);
            for (int rank = 1; rank <= limit; rank++)
            {
                wrapper.Optimize();
                if (!wrapper.HasSolution())
                    break;

                var candidate = ExtractCandidate(decisionId, rank, wrapper.GetObjectiveValue(), variableNames, variablesBinary, variablesInteger3);
                candidates.Add(candidate);
                if (rank == limit || !AddNoGoodConstraint(wrapper, variablesBinary, candidate))
                    break;
            }
            return candidates;
        }

        // Collect all MILP-optimal candidates (same best objective) up to maxSearch re-solves.
        private List<M1GTCandidate> CollectAllOptimalCandidates(LinearModel wrapper,
            VariableCollection<string> variablesBinary,
            VariableCollection<string> variablesInteger3,
            Dictionary<int, List<Symbol>> variableNames, int decisionId, int maxSearch = 20)
        {
            var candidates = new List<M1GTCandidate>();
            double bestObjective = double.MaxValue;
            for (int rank = 1; rank <= maxSearch; rank++)
            {
                wrapper.Optimize();
                if (!wrapper.HasSolution())
                    break;
                double obj = wrapper.GetObjectiveValue();
                if (rank == 1)
                    bestObjective = obj;
                else if (obj > bestObjective + 1e-6)
                    break;
                var candidate = ExtractCandidate(decisionId, rank, obj, variableNames, variablesBinary, variablesInteger3);
                candidates.Add(candidate);
                if (!AddNoGoodConstraint(wrapper, variablesBinary, candidate))
                    break;
            }
            return candidates;
        }

        // Dry-run of the unusedDopsPods logic: returns number of executable transports
        // without modifying any ResourceManager state.
        private int SimulateExecutableTransports(M1GTCandidate candidate, HashSet<Bot> ra)
        {
            var activeYrp = candidate.Yrp.Where(v => ra.Contains(v.robot)).ToList();
            var activePods = new HashSet<Pod>(activeYrp.Select(v => v.pod));

            var stationOrders = new Dictionary<OutputStation, List<Order>>();
            foreach (var yaos in candidate.Yaos)
            {
                if (!stationOrders.ContainsKey(yaos.outputstation))
                    stationOrders[yaos.outputstation] = new List<Order>();
                stationOrders[yaos.outputstation].Add(yaos.order);
            }

            var allUnusedPods = new HashSet<Pod>();
            foreach (var stationEntry in stationOrders)
            {
                var availableCounts = new Dictionary<ItemDescription, Dictionary<Pod, int>>();
                foreach (var xps in candidate.Xps.Where(v => v.outputstation.ID == stationEntry.Key.ID && activePods.Contains(v.pod)))
                {
                    foreach (var item in xps.pod.ItemDescriptionsContained.Where(v => xps.pod.CountAvailable(v) > 0))
                    {
                        if (!availableCounts.ContainsKey(item))
                            availableCounts[item] = new Dictionary<Pod, int>();
                        if (availableCounts[item].ContainsKey(xps.pod))
                            availableCounts[item][xps.pod] += xps.pod.CountAvailable(item);
                        else
                            availableCounts[item][xps.pod] = xps.pod.CountAvailable(item);
                    }
                }

                var dopsPodsSelected = new HashSet<Pod>();
                var dopsPodsUsed = new HashSet<Pod>();
                foreach (var order in stationEntry.Value)
                {
                    var itemDemands = order.Positions.ToDictionary(p => p.Key, p => p.Value);
                    var orderDopsPods = new HashSet<Pod>();
                    foreach (var dops in candidate.Dops.Where(v => v.order.ID == order.ID && activePods.Contains(v.pod)))
                    {
                        dopsPodsSelected.Add(dops.pod);
                        orderDopsPods.Add(dops.pod);
                    }
                    foreach (var itemDemand in itemDemands)
                    {
                        int number = itemDemand.Value;
                        while (number > 0)
                        {
                            if (!availableCounts.ContainsKey(itemDemand.Key) || availableCounts[itemDemand.Key].Count == 0)
                                break;
                            Pod pod;
                            if (availableCounts[itemDemand.Key].Keys.Any(v => orderDopsPods.Contains(v)))
                            {
                                pod = availableCounts[itemDemand.Key].Keys.First(v => orderDopsPods.Contains(v));
                                orderDopsPods.Remove(pod);
                                dopsPodsUsed.Add(pod);
                            }
                            else
                                pod = availableCounts[itemDemand.Key].Keys.First();
                            int numpods = availableCounts[itemDemand.Key].Keys.Count(v => orderDopsPods.Contains(v));
                            if (availableCounts[itemDemand.Key][pod] >= number)
                            {
                                if (numpods > 0 && number > 1)
                                {
                                    Pod pod1 = availableCounts[itemDemand.Key].Keys.First(v => orderDopsPods.Contains(v));
                                    orderDopsPods.Remove(pod1);
                                    dopsPodsUsed.Add(pod1);
                                    if (availableCounts[itemDemand.Key][pod] >= availableCounts[itemDemand.Key][pod1])
                                    {
                                        availableCounts[itemDemand.Key][pod] -= number - numpods;
                                        number = numpods;
                                    }
                                    else
                                    {
                                        availableCounts[itemDemand.Key][pod] -= 1;
                                        number = 1;
                                    }
                                }
                                else
                                {
                                    availableCounts[itemDemand.Key][pod] -= number;
                                    number = 0;
                                }
                                if (availableCounts[itemDemand.Key][pod] == 0)
                                    availableCounts[itemDemand.Key].Remove(pod);
                            }
                            else
                            {
                                number -= availableCounts[itemDemand.Key][pod];
                                availableCounts[itemDemand.Key].Remove(pod);
                            }
                        }
                    }
                }
                foreach (var pod in dopsPodsSelected.Where(v => !dopsPodsUsed.Contains(v)))
                    allUnusedPods.Add(pod);
            }
            return activeYrp.Count - allUnusedPods.Count;
        }

        private void WriteDecisionSnapshotFingerprint(int decisionId, HashSet<Order> pendingOrders, HashSet<Bot> ra, HashSet<Bot> rb,
            HashSet<Pod> pa, HashSet<Pod> pb, Dictionary<Pod, Bot> podToBot, Dictionary<OutputStation, int> cs)
        {
            if (decisionId != _config.ValidationDecisionId)
                return;

            var parts = new List<string>();
            parts.Add("time=" + Instance.Controller.CurrentTime.ToString(IOConstants.FORMATTER));
            parts.Add("bots=" + string.Join(";", Instance.Bots.OrderBy(b => b.ID).Select(b =>
                b.ID.ToString(CultureInfo.InvariantCulture) + ":" +
                (b.CurrentWaypoint != null ? b.CurrentWaypoint.ID.ToString(CultureInfo.InvariantCulture) : "-1") + ":" +
                b.X.ToString(IOConstants.FORMATTER) + ":" +
                b.Y.ToString(IOConstants.FORMATTER) + ":" +
                (b.Pod != null ? b.Pod.ID.ToString(CultureInfo.InvariantCulture) : "-1") + ":" +
                (b.CurrentTask != null ? b.CurrentTask.Type.ToString() : "null"))));
            parts.Add("pods=" + string.Join(";", Instance.Pods.OrderBy(p => p.ID).Select(p =>
                p.ID.ToString(CultureInfo.InvariantCulture) + ":" +
                (p.Waypoint != null ? p.Waypoint.ID.ToString(CultureInfo.InvariantCulture) : "-1") + ":" +
                (p.Bot != null ? p.Bot.ID.ToString(CultureInfo.InvariantCulture) : "-1") + ":" +
                p.X.ToString(IOConstants.FORMATTER) + ":" +
                p.Y.ToString(IOConstants.FORMATTER))));
            parts.Add("pendingOrders=" + string.Join(";", pendingOrders.OrderBy(o => o.ID).Select(o =>
                o.ID.ToString(CultureInfo.InvariantCulture) + ":" +
                string.Join("|", o.Positions.OrderBy(p => p.Key.ID).Select(p =>
                    p.Key.ID.ToString(CultureInfo.InvariantCulture) + "=" + p.Value.ToString(CultureInfo.InvariantCulture))))));
            parts.Add("ra=" + string.Join("|", ra.OrderBy(b => b.ID).Select(b => b.ID.ToString(CultureInfo.InvariantCulture))));
            parts.Add("rb=" + string.Join("|", rb.OrderBy(b => b.ID).Select(b => b.ID.ToString(CultureInfo.InvariantCulture))));
            parts.Add("pa=" + string.Join("|", pa.OrderBy(p => p.ID).Select(p => p.ID.ToString(CultureInfo.InvariantCulture))));
            parts.Add("pb=" + string.Join("|", pb.OrderBy(p => p.ID).Select(p => p.ID.ToString(CultureInfo.InvariantCulture))));
            parts.Add("podToBot=" + string.Join("|", podToBot.OrderBy(v => v.Key.ID).Select(v =>
                v.Key.ID.ToString(CultureInfo.InvariantCulture) + "=" + v.Value.ID.ToString(CultureInfo.InvariantCulture))));
            parts.Add("cs=" + string.Join("|", cs.OrderBy(v => v.Key.ID).Select(v =>
                v.Key.ID.ToString(CultureInfo.InvariantCulture) + "=" + v.Value.ToString(CultureInfo.InvariantCulture))));

            string payload = string.Join("\n", parts);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "");

            string directory = Instance.SettingConfig.StatisticsDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = "Results";
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "m1gt_decision_snapshot_fingerprint.csv");
            bool writeHeader = !File.Exists(path);
            using (var writer = new StreamWriter(path, true))
            {
                if (writeHeader)
                    writer.WriteLine("decision_id,decision_time,snapshot_sha256,payload");
                writer.WriteLine(string.Join(",",
                    decisionId.ToString(CultureInfo.InvariantCulture),
                    Instance.Controller.CurrentTime.ToString(IOConstants.FORMATTER),
                    hash,
                    Csv(payload)));
            }
        }

        private void WriteCandidateManifest(IEnumerable<M1GTCandidate> candidates, ObjectiveCalibration calibration)
        {
            string directory = Instance.SettingConfig.StatisticsDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = "Results";
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "m1gt_top5_candidates.csv");
            bool writeHeader = !File.Exists(path);
            using (var writer = new StreamWriter(path, true))
            {
                if (writeHeader)
                    writer.WriteLine("decision_id,decision_time,candidate_rank,milp_objective,estimated_leg1_objective,estimated_leg2_objective,estimated_travel_objective,selected_yos_count,selected_yaos_count,unused_capacity_sum,objective_order_term,objective_capacity_term,objective_total,estimated_leg1_distance,estimated_leg2_distance,estimated_leg1_time,estimated_leg2_time,estimated_fallback,selected_xps,selected_yrp,selected_yaos,selected_dops");
                foreach (var candidate in candidates)
                {
                    double leg1 = candidate.Yrp.Sum(v => EstimateBotPodObjectiveCost(v.robot, v.pod));
                    double leg2 = candidate.Xps.Sum(v => EstimatePodStationObjectiveCost(v.pod, v.outputstation));
                    double orderTerm = candidate.Yos.Count * calibration.OrderReward;
                    double capacityTerm = candidate.UnusedCapacitySum * calibration.UnusedCapacityPenalty;
                    double objectiveTotal = leg1 + leg2 + orderTerm + capacityTerm;
                    double leg1Distance = candidate.Yrp.Sum(v => EstimateBotPodDistance(v.robot, v.pod));
                    double leg2Distance = candidate.Xps.Sum(v => EstimatePodStationDistance(v.pod, v.outputstation));
                    double leg1Time = candidate.Yrp.Sum(v => EstimateBotPodTime(v.robot, v.pod));
                    double leg2Time = candidate.Xps.Sum(v => EstimatePodStationTime(v.pod, v.outputstation));
                    bool estimatedFallback = CandidateUsedFallback(candidate.Xps, candidate.Yrp);
                    writer.WriteLine(string.Join(",",
                        candidate.DecisionId.ToString(CultureInfo.InvariantCulture),
                        Instance.Controller.CurrentTime.ToString(IOConstants.FORMATTER),
                        candidate.Rank.ToString(CultureInfo.InvariantCulture),
                        candidate.Objective.ToString(IOConstants.FORMATTER),
                        leg1.ToString(IOConstants.FORMATTER),
                        leg2.ToString(IOConstants.FORMATTER),
                        (leg1 + leg2).ToString(IOConstants.FORMATTER),
                        candidate.Yos.Count.ToString(CultureInfo.InvariantCulture),
                        candidate.Yaos.Count.ToString(CultureInfo.InvariantCulture),
                        candidate.UnusedCapacitySum.ToString(CultureInfo.InvariantCulture),
                        orderTerm.ToString(IOConstants.FORMATTER),
                        capacityTerm.ToString(IOConstants.FORMATTER),
                        objectiveTotal.ToString(IOConstants.FORMATTER),
                        leg1Distance.ToString(IOConstants.FORMATTER),
                        leg2Distance.ToString(IOConstants.FORMATTER),
                        leg1Time.ToString(IOConstants.FORMATTER),
                        leg2Time.ToString(IOConstants.FORMATTER),
                        estimatedFallback ? "1" : "0",
                        Csv(SymbolList(candidate.Xps)),
                        Csv(SymbolList(candidate.Yrp)),
                        Csv(SymbolList(candidate.Yaos)),
                        Csv(SymbolList(candidate.Dops))));
                }
            }
        }

        private void WriteObjectiveCalibration(int decisionId, ObjectiveCalibration calibration)
        {
            string directory = Instance.SettingConfig.StatisticsDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = "Results";
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "m1gt_objective_calibration.csv");
            bool writeHeader = !File.Exists(path);
            using (var writer = new StreamWriter(path, true))
            {
                if (writeHeader)
                    writer.WriteLine("decision_id,decision_time,use_shortest_time_objective,time_per_distance_scale,ratio_sample_count,travel_weight,order_reward,unused_capacity_penalty,estimator_name,path_sample_count,fallback_count,avg_physical_eta,median_physical_eta,avg_distance_ratio,median_distance_ratio");
                writer.WriteLine(string.Join(",",
                    decisionId.ToString(CultureInfo.InvariantCulture),
                    Instance.Controller.CurrentTime.ToString(IOConstants.FORMATTER),
                    calibration.UseShortestTimeObjective ? "1" : "0",
                    calibration.TimePerDistanceScale.ToString(IOConstants.FORMATTER),
                    calibration.RatioSampleCount.ToString(CultureInfo.InvariantCulture),
                    calibration.TravelWeight.ToString(IOConstants.FORMATTER),
                    calibration.OrderReward.ToString(IOConstants.FORMATTER),
                    calibration.UnusedCapacityPenalty.ToString(IOConstants.FORMATTER),
                    calibration.EstimatorName,
                    calibration.PathSampleCount.ToString(CultureInfo.InvariantCulture),
                    calibration.FallbackCount.ToString(CultureInfo.InvariantCulture),
                    FormatDouble(calibration.AveragePhysicalEta),
                    FormatDouble(calibration.MedianPhysicalEta),
                    FormatDouble(calibration.AverageDistanceRatio),
                    FormatDouble(calibration.MedianDistanceRatio)));
            }
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value))
                return "";
            if (double.IsPositiveInfinity(value))
                return "Infinity";
            if (double.IsNegativeInfinity(value))
                return "-Infinity";
            return value.ToString(IOConstants.FORMATTER);
        }

        private void WriteSelectedExecutableCandidate(M1GTCandidate candidate, IEnumerable<Symbol> executableXps, IEnumerable<Symbol> executableYrp, ObjectiveCalibration calibration)
        {
            if (candidate == null || candidate.DecisionId != _config.ValidationDecisionId)
                return;

            var xpsList = executableXps.ToList();
            var yrpList = executableYrp.ToList();
            string directory = Instance.SettingConfig.StatisticsDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = "Results";
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "m1gt_selected_executable_candidate.csv");
            bool writeHeader = !File.Exists(path);
            using (var writer = new StreamWriter(path, true))
            {
                if (writeHeader)
                    writer.WriteLine("decision_id,decision_time,candidate_rank,milp_objective,original_xps_count,original_yrp_count,executable_xps_count,executable_yrp_count,selected_yos_count,selected_yaos_count,unused_capacity_sum,objective_order_term,objective_capacity_term,executable_estimated_objective,executable_estimated_leg1_objective,executable_estimated_leg2_objective,executable_estimated_leg1_distance,executable_estimated_leg2_distance,executable_estimated_leg1_time,executable_estimated_leg2_time,executable_estimated_fallback,executable_xps,executable_yrp");

                double leg1 = yrpList.Sum(v => EstimateBotPodObjectiveCost(v.robot, v.pod));
                double leg2 = xpsList.Sum(v => EstimatePodStationObjectiveCost(v.pod, v.outputstation));
                double orderTerm = candidate.Yos.Count * calibration.OrderReward;
                double capacityTerm = candidate.UnusedCapacitySum * calibration.UnusedCapacityPenalty;
                double executableObjective = leg1 + leg2 + orderTerm + capacityTerm;
                double leg1Distance = yrpList.Sum(v => EstimateBotPodDistance(v.robot, v.pod));
                double leg2Distance = xpsList.Sum(v => EstimatePodStationDistance(v.pod, v.outputstation));
                double leg1Time = yrpList.Sum(v => EstimateBotPodTime(v.robot, v.pod));
                double leg2Time = xpsList.Sum(v => EstimatePodStationTime(v.pod, v.outputstation));
                bool estimatedFallback = CandidateUsedFallback(xpsList, yrpList);
                writer.WriteLine(string.Join(",",
                    candidate.DecisionId.ToString(CultureInfo.InvariantCulture),
                    Instance.Controller.CurrentTime.ToString(IOConstants.FORMATTER),
                    candidate.Rank.ToString(CultureInfo.InvariantCulture),
                    candidate.Objective.ToString(IOConstants.FORMATTER),
                    candidate.Xps.Count.ToString(CultureInfo.InvariantCulture),
                    candidate.Yrp.Count.ToString(CultureInfo.InvariantCulture),
                    xpsList.Count.ToString(CultureInfo.InvariantCulture),
                    yrpList.Count.ToString(CultureInfo.InvariantCulture),
                    candidate.Yos.Count.ToString(CultureInfo.InvariantCulture),
                    candidate.Yaos.Count.ToString(CultureInfo.InvariantCulture),
                    candidate.UnusedCapacitySum.ToString(CultureInfo.InvariantCulture),
                    orderTerm.ToString(IOConstants.FORMATTER),
                    capacityTerm.ToString(IOConstants.FORMATTER),
                    executableObjective.ToString(IOConstants.FORMATTER),
                    leg1.ToString(IOConstants.FORMATTER),
                    leg2.ToString(IOConstants.FORMATTER),
                    leg1Distance.ToString(IOConstants.FORMATTER),
                    leg2Distance.ToString(IOConstants.FORMATTER),
                    leg1Time.ToString(IOConstants.FORMATTER),
                    leg2Time.ToString(IOConstants.FORMATTER),
                    estimatedFallback ? "1" : "0",
                    Csv(SymbolList(xpsList)),
                    Csv(SymbolList(yrpList))));
            }
        }

        private void ArmValidationStopForSelection(int decisionId, IEnumerable<Symbol> selectedXps, IEnumerable<Symbol> selectedYrp)
        {
            if (!_config.StopAfterValidationBatch || decisionId != _config.ValidationDecisionId)
                return;

            var leg2Keys = new List<string>();
            var xpsList = selectedXps.ToList();
            foreach (var yrp in selectedYrp)
            {
                var xps = xpsList.FirstOrDefault(v => v.pod.ID == yrp.pod.ID);
                if (xps == null)
                    continue;
                leg2Keys.Add(yrp.robot.ID.ToString(CultureInfo.InvariantCulture) + "|" +
                    yrp.pod.ID.ToString(CultureInfo.InvariantCulture) + "|" +
                    xps.outputstation.ID.ToString(CultureInfo.InvariantCulture));
            }
            _m1gtValidationBatchArmed = true;
            Instance.ArmM1GTValidationBatch(leg2Keys, true);
        }

        private static string SymbolList(IEnumerable<Symbol> symbols)
        {
            return string.Join("|", symbols.Select(s => s.name));
        }

        private static string Csv(string value)
        {
            if (value == null)
                return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        /// <summary>
        /// Checks whether an item matching the description is contained in this pod. 
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public int IsAvailabletoPiSKU(ItemDescription item) { return _itemofPiSKU.ContainsKey(item) ? _itemofPiSKU[item] : 0; }
        /// <summary>
        /// 生成PiSKU
        /// </summary>
        /// <param name="allPods"></param>
        /// <returns></returns>
        public Dictionary<ItemDescription, List<Pod>> GeneratePiSKU(IEnumerable<Pod> allPods)
        {
            Dictionary<ItemDescription, List<Pod>> PiSKU = new Dictionary<ItemDescription, List<Pod>>();
            _itemofPiSKU = new Dictionary<ItemDescription, int>();
            //生成PiSKU
            foreach (Pod pod in allPods)
            {
                IEnumerable<ItemDescription> ListofSKUidofpod = pod.ItemDescriptionsContained.Where(v => pod.IsContained(v));
                foreach (var sku in ListofSKUidofpod)
                {
                    if (PiSKU.ContainsKey(sku))
                    {
                        PiSKU[sku].Add(pod);
                        _itemofPiSKU[sku] += pod.CountAvailable(sku);
                    }
                    else
                    {
                        List<Pod> listofpod = new List<Pod>() { pod };
                        PiSKU.Add(sku, listofpod);
                        _itemofPiSKU[sku] = pod.CountAvailable(sku);
                    }
                }
            }
            return PiSKU;
        }
        /// <summary>
        /// 生成OiSKU
        /// </summary>
        /// <param name="pendingOrders"></param>
        /// <returns></returns>
        public Dictionary<ItemDescription, List<Order>> GenerateOiSKU(HashSet<Order> pendingOrders)
        {
            Dictionary<ItemDescription, List<Order>> OiSKU = new Dictionary<ItemDescription, List<Order>>();
            //生成OiSKU
            foreach (var order in pendingOrders)
            {
                IEnumerable<KeyValuePair<ItemDescription, int>> ListofSKUidoforder = order.Positions;
                foreach (var sku in ListofSKUidoforder)
                {
                    if (OiSKU.ContainsKey(sku.Key))
                        OiSKU[sku.Key].Add(order);
                    else
                    {
                        List<Order> listoforder = new List<Order>() { order };
                        OiSKU.Add(sku.Key, listoforder);
                    }
                }
            }
            return OiSKU;
        }
        /// <summary>
        /// 产生Ps
        /// </summary>
        /// <param name="Cs"></param>
        /// <returns></returns>
        public Dictionary<OutputStation, HashSet<Pod>> GeneratePs(Dictionary<OutputStation, int> Cs)
        {
            Dictionary<OutputStation, HashSet<Pod>> inboundPods = new Dictionary<OutputStation, HashSet<Pod>>();
            foreach (var station in Cs.Keys)
            {
                HashSet<Pod> hspod = new HashSet<Pod>();
                foreach (var pod in station.InboundPods.ToList())
                {
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod) ||
                        Instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        hspod.Add(pod);
                    }
                    else
                    {
                        station.UnregisterInboundPod(pod);
                    }
                }
                inboundPods.Add(station, hspod);
                //foreach (var pod in station.InboundPods)
                //{
                //    if (Instance.ResourceManager._usedPods[pod].CurrentTask is RestTask)
                //        inboundPods[station].Remove(pod);
                //}
            }
            return inboundPods;
        }
        /// <summary>
        /// 产生Od
        /// </summary>
        /// <param name="pendingOrders"></param>
        /// <param name="PiSKU"></param>
        /// <returns></returns>
        public HashSet<Order> GenerateOd(HashSet<Order> pendingOrders, Dictionary<ItemDescription, List<Pod>> PiSKU)
        {
            HashSet<Order> Od = new HashSet<Order>();
            foreach (Order order in pendingOrders)
            {
                order.Timestay = order.DueTime - (Instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(Instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
                if (order.Timestay < DueTimeOrderofMP)
                {
                    bool Isadd = true;
                    IEnumerable<KeyValuePair<ItemDescription, int>> ListofSKUidoforder = order.Positions;
                    foreach (var sku in ListofSKUidoforder)
                    {
                        if (PiSKU[sku.Key].All(v => v.CountAvailable(sku.Key) >= sku.Value && Instance.ResourceManager.UnusedPods.Contains(v)))
                            continue;
                        else
                            Isadd = false;
                    }
                    if (Isadd)
                        Od.Add(order);
                }
            }
            int i = 0;
            foreach (Order order in pendingOrders.OrderBy(v => v.Timestay).ThenBy(u => u.DueTime))
            {
                order.sequence = i;
                i++;
            }
            return Od;
        }
        /// <summary>
        /// 产生Cs
        /// </summary>
        /// <returns></returns>
        public new Dictionary<OutputStation, int> GenerateCs()
        {
            Dictionary<OutputStation, int> Cs = new Dictionary<OutputStation, int>();
            foreach (var station in Instance.OutputStations.Where(v => v.Capacity - v.CapacityReserved - v.CapacityInUse > 0))
                Cs.Add(station, station.Capacity - station.CapacityReserved - station.CapacityInUse);
            return Cs;
        }
        /// <summary>
        /// 产生决策变量的name
        /// </summary>
        /// <param name="PiSKU"></param>
        /// <param name="OiSKU"></param>
        /// <param name="allPods"></param>
        /// <param name="pendingOrders"></param>
        /// <param name="Cs"></param>
        /// <param name="R"></param>
        /// <param name="Pa1"></param>
        /// <param name="Pa"></param>
        /// <returns></returns>
        public Dictionary<int, List<Symbol>> CreatedeVarName(Dictionary<ItemDescription, List<Pod>> PiSKU, Dictionary<ItemDescription, List<Order>> OiSKU,
            IEnumerable<Pod> allPods, HashSet<Order> pendingOrders, Dictionary<OutputStation, int> Cs, HashSet<Bot> R, HashSet<Pod> Pa1, out HashSet<Pod> Pa)
        {
            Dictionary<int, List<Symbol>> variableNames = new Dictionary<int, List<Symbol>>();
            List<Symbol> deVarNamexps = new List<Symbol>();  //pod p ∈ P is assigned to station s ∈ S
            foreach (var pod in allPods)
            {
                foreach (var outputstation in Cs.Keys)
                    deVarNamexps.Add(new Symbol { pod = pod, outputstation = outputstation, name = "xps" + "_" + pod.ID.ToString() + "_" + outputstation.ID.ToString() });
            }
            variableNames.Add(1, deVarNamexps);
            List<Symbol> deVarNameyos = new List<Symbol>();  //order o ∈ O is assigned to station s ∈ S
            List<Symbol> deVarNameyaos = new List<Symbol>();  //order o ∈ O is assigned to station s ∈ S
            foreach (var order in pendingOrders)
            {
                foreach (var outputstation in Cs.Keys)
                {
                    deVarNameyos.Add(new Symbol { order = order, outputstation = outputstation, name = "yos" + "_" + order.ID.ToString() + "_" + outputstation.ID.ToString() });
                    deVarNameyaos.Add(new Symbol { order = order, outputstation = outputstation, name = "yaos" + "_" + order.ID.ToString() + "_" + outputstation.ID.ToString() });
                }
            }
            variableNames.Add(2, deVarNameyos);
            variableNames.Add(3, deVarNameyaos);
            //List<Symbol> deVarNameyios = new List<Symbol>(); //SKU i ∈ Io of order o ∈ O is assigned to station s ∈ S
            //foreach (var order in pendingOrders)
            //{
            //    IEnumerable<KeyValuePair<ItemDescription, int>> ListofSKUID = order.Positions;
            //    foreach (var outputstation in Cs.Keys)
            //    {
            //        foreach (var skuid in ListofSKUID)
            //            deVarNameyios.Add(new Symbol
            //            {
            //                order = order,
            //                outputstation = outputstation,
            //                skui = skuid.Key,
            //                name = "yios" + "_" + skuid.Key.ID.ToString() + "_" +
            //                                        order.ID.ToString() + "_" + outputstation.ID.ToString()
            //            });
            //    }
            //}
            //variableNames.Add(3, deVarNameyios);
            //List<Symbol> deVarNameziops = new List<Symbol>();
            List<Symbol> deVarNamedops = new List<Symbol>();
            HashSet<Pod> podset = new HashSet<Pod>();
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))
            {
                List<Pod> listofpod = PiSKU[sku.Key];
                foreach (var pod in listofpod.Where(v => Pa1.Contains(v)))
                {
                    podset.Add(pod);
                    foreach (var outputstation in Cs.Keys)
                    {
                        foreach (var order in sku.Value)
                        {
                            //deVarNameziops.Add(new Symbol
                            //{
                            //    pod = pod,
                            //    order = order,
                            //    outputstation = outputstation,
                            //    skui = sku.Key,
                            //    name = "ziops" + "_" + sku.Key.ID.ToString() + "_" +
                            //    order.ID.ToString() + "_" + pod.ID.ToString() + "_" + outputstation.ID.ToString()
                            //});
                            if (deVarNamedops.Where(v => v.order.ID == order.ID && v.pod.ID == pod.ID && v.outputstation.ID == outputstation.ID).Count() == 0)
                            {
                                int waypointID;
                                if (pod.Waypoint != null)
                                    waypointID = pod.Waypoint.ID;
                                else if (pod.Bot.CurrentWaypoint != null)
                                    waypointID = pod.Bot.CurrentWaypoint.ID;
                                else
                                    waypointID = 0;
                                deVarNamedops.Add(new Symbol
                                {
                                    pod = pod,
                                    order = order,
                                    outputstation = outputstation,
                                    podwaypointID = waypointID,
                                    name = "dops" + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + outputstation.ID.ToString()
                                });
                            }
                        }
                    }
                }
            }
            //variableNames.Add(4, deVarNameziops);
            variableNames.Add(6, deVarNamedops.Distinct().ToList());
            List<Symbol> deVarNameyrp = new List<Symbol>();
            foreach (var robot in R)
            {
                foreach (var pod in allPods)
                    deVarNameyrp.Add(new Symbol { pod = pod, robot = robot, name = "yrp" + "_" + robot.ID.ToString() + "_" + pod.ID.ToString() });
            }
            variableNames.Add(4, deVarNameyrp);
            List<Symbol> deVarNameus = new List<Symbol>();
            foreach (var outputstation in Cs.Keys)
                deVarNameus.Add(new Symbol { outputstation = outputstation, name = "us" + "_" + outputstation.ID.ToString() });
            variableNames.Add(5, deVarNameus);
            Pa = new HashSet<Pod>();
            foreach (var pod in Pa1.Where(v => podset.Contains(v)))
            {
                Pa.Add(pod);
            }
            return variableNames;
        }
        /// <summary>
        /// Initializes this controller.
        /// </summary>
        /// <param name="PiSKU"></param>
        /// <param name="OiSKU"></param>
        /// <param name="variableNames"></param>
        /// <param name="Cs"></param>
        /// <param name="pendingOrders"></param>
        /// <param name="inboundPods"></param>
        /// <param name="Ra"></param>
        /// <param name="Rb"></param>
        /// <param name="R"></param>
        /// <param name="Pb"></param>
        /// <param name="Pa"></param>
        /// <param name="PodToBot"></param>
        /// <returns></returns>
        private HashSet<Pod> Initialize(out Dictionary<ItemDescription, List<Pod>> PiSKU, out Dictionary<ItemDescription, List<Order>> OiSKU,
            out Dictionary<int, List<Symbol>> variableNames, out Dictionary<OutputStation, int> Cs, out HashSet<Order> pendingOrders,
            out Dictionary<OutputStation, HashSet<Pod>> inboundPods, out HashSet<Bot> Ra, out HashSet<Bot> Rb,
            out HashSet<Bot> R, out HashSet<Pod> Pb, out HashSet<Pod> Pa, out Dictionary<Pod, Bot> PodToBot)
        {
            HashSet<Order> pendingOrders1 = new HashSet<Order>(_pendingOrders.Where(o => o.Positions.All(p => Instance.StockInfo.GetActualStock(p.Key) >= p.Value)));
            OiSKU = GenerateOiSKU(pendingOrders1);
            Cs = GenerateCs();
            inboundPods = GeneratePs(Cs);
            HashSet<ItemDescription> ItemofOiSKU = new HashSet<ItemDescription>(OiSKU.Keys);
            HashSet<Pod> allPods = new HashSet<Pod>();
            HashSet<Order> Od = new HashSet<Order>();
            Ra = new HashSet<Bot>();
            Rb = new HashSet<Bot>();
            R = new HashSet<Bot>();
            Pb = new HashSet<Pod>();
            PodToBot = new Dictionary<Pod, Bot>();
            HashSet<Pod> Pa1 = new HashSet<Pod>();
            foreach (var pods in inboundPods)//allPods包含三部分，第一部分是正在到达工作站路上的pod
            {
                foreach (Pod pod in pods.Value)
                {
                    if (PodToBot.ContainsKey(pod)) continue; // 同一 pod 已處理過（出現於多個 station）
                    if (Instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        allPods.Add(pod);
                        Rb.Add(Instance.ResourceManager._usedPods[pod]);
                        R.Add(Instance.ResourceManager._usedPods[pod]);
                        PodToBot[pod] = Instance.ResourceManager._usedPods[pod];
                        Pb.Add(pod);
                    }
                    else if (Instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        var bot = Instance.ResourceManager.BottoPod.Where(V => V.Value.ID == pod.ID).First().Key;
                        allPods.Add(pod);
                        Rb.Add(bot);
                        R.Add(bot);
                        PodToBot[pod] = bot;
                        Pb.Add(pod);
                    }
                    else
                    {
                        // Snapshot can contain a station inbound pod before its pod-bot ownership is visible.
                        // Exclude it from the model for this decision.
                    }
                }
            }
            foreach (var pod in Instance.ResourceManager.UnusedPods.Where(v =>
                v.IsAvailabletoOiSKU(ItemofOiSKU) &&
                !Instance.ResourceManager.BottoPod.ContainsValue(v) &&
                !Instance.ResourceManager._usedPods.ContainsKey(v) &&
                v.Waypoint != null &&
                v.Waypoint.PodStorageLocation))
            {
                allPods.Add(pod);
                Pa1.Add(pod);
            }
            //foreach (var bot in Instance._outputstationbots.Where(v => v.CurrentTask is ExtractTask || v.CurrentTask is ParkPodTask))
            //    RR.Add(bot);
            foreach (var bot in Instance._outputstationbots)
            {
                //if (bot.Pod == null && (!Instance.ResourceManager._usedPods.ContainsValue(bot) || bot.CurrentTask is DummyTask) && !Instance.ResourceManager.BottoPod.ContainsKey(bot)) //
                //{
                //    R.Add(bot);
                //    Ra.Add(bot);
                //}
                if (bot.Pod == null && !Instance.ResourceManager._usedPods.ContainsValue(bot) && !Instance.ResourceManager.BottoPod.ContainsKey(bot)) //
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
                else if (bot.Pod == null && !Instance.ResourceManager.BottoPod.ContainsKey(bot) && !Rb.Contains(bot) && bot.CurrentTask is RestTask && bot.GetInfoDestinationWaypoint() == null)
                {
                    R.Add(bot);
                    Ra.Add(bot);
                }
            }
            //if (R.Count() == 0) 
            //    Thread.Sleep(1);
            PiSKU = GeneratePiSKU(allPods);
            pendingOrders = new HashSet<Order>(pendingOrders1.Where(o => o.Positions.All(p => IsAvailabletoPiSKU(p.Key) >= p.Value)));
            Od = GenerateOd(pendingOrders, PiSKU);
            if (Od.Count > Cs.Values.Sum())
                pendingOrders = new HashSet<Order>(Od);
            OiSKU = GenerateOiSKU(pendingOrders);
            variableNames = CreatedeVarName(PiSKU, OiSKU, allPods, pendingOrders, Cs, R, Pa1, out Pa);
            return allPods;
        }
        /// <summary>
        /// Clear the queue of a station.
        /// </summary>
        /// <param name="Cs"></param>
        public void StationQueueClear(Dictionary<OutputStation, int> Cs)
        {
            foreach (var station in Cs.Where(v => v.Value > 0))
            {
                //Instance.Controller.Allocator.ClearQueue(station.Key);
                Instance.ResourceManager._QueueZiops[station.Key].Clear();
            }
        }
        /// <summary>
        /// 运用数学规划方法进行求解
        /// </summary>
        /// <param name="type"></param>
        /// <param name="PiSKU"></param>
        /// <param name="OiSKU"></param>
        /// <param name="Pods"></param>
        /// <param name="Cs"></param>
        /// <param name="variableNames"></param>
        /// <param name="pendingOrders"></param>
        /// <param name="inboundPods"></param>
        /// <param name="Ra"></param>
        /// <param name="Rb"></param>
        /// <param name="R"></param>
        /// <param name="Pb"></param>
        /// <param name="Pa"></param>
        /// <param name="PodToBot"></param>
        /// <returns></returns>
        public Dictionary<Symbol, int> solve(SolverType type, Dictionary<ItemDescription, List<Pod>> PiSKU, Dictionary<ItemDescription, List<Order>> OiSKU,
            IEnumerable<Pod> Pods, Dictionary<OutputStation, int> Cs, Dictionary<int, List<Symbol>> variableNames, HashSet<Order> pendingOrders, Dictionary<OutputStation,
                HashSet<Pod>> inboundPods, HashSet<Bot> Ra, HashSet<Bot> Rb, HashSet<Bot> R, HashSet<Pod> Pb, HashSet<Pod> Pa, Dictionary<Pod, Bot> PodToBot)
        {
            LinearModel wrapper = new LinearModel(type, (string s) => { Console.Write(s); });
            Dictionary<Symbol, int> NewZiops = new Dictionary<Symbol, int>();
            Dictionary<Symbol, int> NewZiops1 = new Dictionary<Symbol, int>();
            List<Symbol> deVarNamexps = variableNames[1];
            List<Symbol> deVarNameyos = variableNames[2];
            List<Symbol> deVarNameyaos = variableNames[3];
            List<Symbol> deVarNameyrp = variableNames[4];
            List<Symbol> deVarNameus = variableNames[5];
            List<Symbol> deVarNamedops = variableNames[6];
            ResetPhysicalEtaDecisionCache();
            ObjectiveCalibration objectiveCalibration = BuildObjectiveCalibration(deVarNamexps, deVarNameyrp, Cs, Ra);
            double w1 = objectiveCalibration.TravelWeight;
            double w2 = objectiveCalibration.OrderReward;
            double w3 = objectiveCalibration.UnusedCapacityPenalty;
            //double w4 = 2;
            VariableCollection<string> variablesBinary = new VariableCollection<string>(wrapper, VariableType.Binary, 0, 1, (string s) => { return s; });
            VariableCollection<string> variablesInteger2 = new VariableCollection<string>(wrapper, VariableType.Integer, 0, 5, (string s) => { return s; });
            VariableCollection<string> variablesInteger3 = new VariableCollection<string>(wrapper, VariableType.Integer, 0, 6, (string s) => { return s; });
            if (Ra.Count() > 0)
                wrapper.SetObjective((LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * EstimatePodStationObjectiveCost(v.pod, v.outputstation)), wrapper)
                    + LinearExpression.Sum(deVarNameyrp.Where(u => Ra.Contains(u.robot) && Instance.ResourceManager.UnusedPods.Contains(u.pod) && u.pod.Waypoint != null).Select(v => variablesBinary[v.name] *
                    EstimateBotPodObjectiveCost(v.robot, v.pod)), wrapper)) * w1 + LinearExpression.Sum(deVarNameyos.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesInteger3[v.name])) * w3, OptimizationSense.Minimize);
            else
                wrapper.SetObjective(LinearExpression.Sum(deVarNamexps.Where(u => Cs.Keys.Contains(u.outputstation) && Instance.ResourceManager.UnusedPods.Contains(u.pod)).Select(v => variablesBinary[v.name] * EstimatePodStationObjectiveCost(v.pod, v.outputstation)), wrapper) * w1
                    + LinearExpression.Sum(deVarNameyos.Select(v => variablesBinary[v.name])) * w2
                    + LinearExpression.Sum(deVarNameus.Select(v => variablesInteger3[v.name])) * w3, OptimizationSense.Minimize);
            foreach (var order in pendingOrders)//每个订单最多只能分配给一个工作站
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyos.Where(v => v.order.ID == order.ID).Select(v => variablesBinary[v.name])) <= 1, "shi2");
            foreach (var order in pendingOrders)//当订单分配给工作站时，订单一定能够被分配给工作站
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["yaos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] <= variablesBinary
                        ["yos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()], "shi3");
            }
            foreach (var station in Cs.Keys)//分派到工作站的订单数量必须等于工作站的可利用容量减去未被利用的容量
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyaos.Where(v => v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])) == Cs[station] - variablesInteger3["us" + "_" + station.ID.ToString()], "shi4");
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))//由分配给工作站w的货架p满足订单o中SKU i 数量不能大于货架p中存储的SKU i 的数量
            {
                foreach (var instance in Cs.Keys)
                {
                    wrapper.AddConstr(LinearExpression.Sum(deVarNameyos.Where(v => v.outputstation.ID == instance.ID && sku.Value.Contains(v.order)).Select(v => v.order.PositionOverallCount(sku.Key)
                    * variablesBinary[v.name])) <= LinearExpression.Sum(deVarNamexps.Where(v => v.outputstation.ID == instance.ID && PiSKU[sku.Key].Contains(v.pod)).Select(v =>
                    v.pod.CountAvailable(sku.Key) * variablesBinary[v.name])), "shi5");
                }
            }
            foreach (var pod in Pods)//货架最多只能分配给1个工作站
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi6");
            foreach (var station in inboundPods)
            {
                foreach (var pod in station.Value)
                {
                    if (!Pb.Contains(pod))
                        continue;
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.Key.ID.ToString()] == 1, "shi7");//继承系统中已经分配而未释放的货架
                    wrapper.AddConstr(variablesBinary["yrp" + "_" + PodToBot[pod].ID.ToString() + "_" + pod.ID.ToString()] == 1, "shi11");//继承系统中已经分配的机器人
                }
            }
            foreach (var pod in Pods)//当货架被分配给工作站时，必须有对应的机器人分配给货架。
                wrapper.AddConstr(LinearExpression.Sum(deVarNamexps.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])), "shi8");
            foreach (var pod in Pods)//每个货架最多只能被一个机器人运输
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.pod.ID == pod.ID).Select(v => variablesBinary[v.name])) <= 1, "shi9");
            foreach (var robot in R)//每个机器人最多只能分配给一个货架
                wrapper.AddConstr(LinearExpression.Sum(deVarNameyrp.Where(v => v.robot.ID == robot.ID).Select(v => variablesBinary[v.name])) <= 1, "shi10");
            foreach (var sku in OiSKU.Where(v => PiSKU.ContainsKey(v.Key)))//当ziops和yaos都大于0时，dops一定大于0
            {
                List<Pod> listofpod = PiSKU[sku.Key];
                foreach (var pod in listofpod.Where(v => Pa.Contains(v)))
                {
                    foreach (var order in sku.Value)
                    {
                        foreach (var station in Cs.Keys)
                        {
                            wrapper.AddConstr(2 * variablesBinary["dops" + "_" + order.ID.ToString() + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                                <= variablesBinary["yaos" + "_" + order.ID.ToString() + "_" + station.ID.ToString()] + variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()], "shi12");
                        }
                    }
                }
            }
            foreach (var pod in Pa)//保证新分配的pod中最少有一个对应的订单分配
            {
                foreach (var station in Cs.Keys)
                    wrapper.AddConstr(variablesBinary["xps" + "_" + pod.ID.ToString() + "_" + station.ID.ToString()]
                        <= LinearExpression.Sum(deVarNamedops.Where(v => v.pod.ID == pod.ID && v.outputstation.ID == station.ID).Select(v => variablesBinary[v.name])), "shi13");
            }
            wrapper.Update();
            int m1gtDecisionId = ++_m1gtDecisionId;
            WriteObjectiveCalibration(m1gtDecisionId, objectiveCalibration);
            bool validationDecision = m1gtDecisionId == _config.ValidationDecisionId;
            int forcedRank = 0;
            bool forcedPolicyDecision = ForcedDecisionPolicy.TryGetValue(m1gtDecisionId, out forcedRank);
            if (validationDecision)
                WriteDecisionSnapshotFingerprint(m1gtDecisionId, pendingOrders, Ra, Rb, Pa, Pb, PodToBot, Cs);
            List<M1GTCandidate> candidates;
            M1GTCandidate selectedCandidate;
            if (validationDecision || forcedPolicyDecision)
            {
                candidates = CollectCandidates(wrapper, variablesBinary, variablesInteger3, variableNames, m1gtDecisionId, _config.TopK);
                WriteCandidateManifest(candidates, objectiveCalibration);
                int candidateRank = validationDecision ? Math.Max(1, _config.ValidationCandidateRank) : Math.Max(1, forcedRank);
                selectedCandidate = candidates.FirstOrDefault(c => c.Rank == candidateRank) ?? candidates.FirstOrDefault();
            }
            else
            {
                candidates = CollectAllOptimalCandidates(wrapper, variablesBinary, variablesInteger3, variableNames, m1gtDecisionId);
                selectedCandidate = candidates.Count <= 1
                    ? candidates.FirstOrDefault()
                    : candidates.OrderBy(c => SimulateExecutableTransports(c, Ra)).ThenBy(c => c.Rank).First();
            }
            if (selectedCandidate != null)
            {
                Dictionary<OutputStation, List<Order>> _availableStationorder = new Dictionary<OutputStation, List<Order>>();
                List<Symbol> IsdeVarNamexps = new List<Symbol>(selectedCandidate.Xps);
                List<Symbol> IsdeVarNameyaos = new List<Symbol>(selectedCandidate.Yaos);
                List<Symbol> IsdeVarNameyos = new List<Symbol>(selectedCandidate.Yos);
                List<Symbol> IsdeVarNameyrp = new List<Symbol>();
                List<Symbol> IsdeVarNamedops = new List<Symbol>(selectedCandidate.Dops);

                foreach (var itemName in IsdeVarNameyaos)
                {
                    if (_availableStationorder.ContainsKey(itemName.outputstation))
                        _availableStationorder[itemName.outputstation].Add(itemName.order);
                    else
                    {
                        List<Order> listorder = new List<Order> { itemName.order };
                        _availableStationorder.Add(itemName.outputstation, listorder);
                    }
                }

                foreach (var itemName in selectedCandidate.Yrp)
                {
                    if (Ra.Contains(itemName.robot))
                    {
                        IsdeVarNameyrp.Add(itemName);
                        Instance.ResourceManager.BottoPod.Add(itemName.robot, itemName.pod);
                        Instance.ResourceManager.ClaimPod(itemName.pod, itemName.robot, BotTaskType.Extract);
                        foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == itemName.pod.ID))
                            xps.outputstation.RegisterInboundPod(itemName.pod);
                    }
                }
                _IsvariableNames[1] = IsdeVarNameyaos;
                // 估算路徑紀錄延後到 unused dops pods 清理之後，避免被解除分配的 (bot,pod,station)
                // 仍寫入估算 → 永遠不會被 BeginActualSegment 匹配，造成孤兒記錄汙染統計。
                //模型外求Ziops
                DateTime A = DateTime.Now;
                if (_availableStationorder.Count > 0)
                {
                    foreach (var _currentStationorder in _availableStationorder)
                    {
                        Dictionary<ItemDescription, Dictionary<Pod, int>> _availableCounts = new Dictionary<ItemDescription, Dictionary<Pod, int>>();
                        // Get current pod content
                        foreach (var itemName in IsdeVarNamexps.Where(v => v.outputstation.ID == _currentStationorder.Key.ID))
                        {
                            foreach (var item in itemName.pod.ItemDescriptionsContained.Where(v => itemName.pod.CountAvailable(v) > 0))
                            {
                                if (_availableCounts.ContainsKey(item))
                                {
                                    if (_availableCounts[item].ContainsKey(itemName.pod))
                                        _availableCounts[item][itemName.pod] += itemName.pod.CountAvailable(item);
                                    else
                                        _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                                else
                                {
                                    Dictionary<Pod, int> Counts = new Dictionary<Pod, int>();
                                    _availableCounts.Add(item, Counts);
                                    if (_availableCounts[item].ContainsKey(itemName.pod))
                                        _availableCounts[item][itemName.pod] += itemName.pod.CountAvailable(item);
                                    else
                                        _availableCounts[item].Add(itemName.pod, itemName.pod.CountAvailable(item));
                                }
                            }
                        }
                        HashSet<Pod> dopsPodsSelected = new HashSet<Pod>();
                        HashSet<Pod> dopsPodsUsed = new HashSet<Pod>();
                        // Check all assigned orders
                        foreach (var order in _currentStationorder.Value)
                        {
                            // Get demand for items caused by order
                            Dictionary<ItemDescription, int> itemDemands = new Dictionary<ItemDescription, int>();
                            foreach (var item in order.Positions)
                                itemDemands.Add(item.Key, item.Value);
                            HashSet<Pod> orderDopsPods = new HashSet<Pod>();
                            if (IsdeVarNamedops.Where(v => v.order.ID == order.ID).Count() > 0)
                            {
                                foreach (var item in IsdeVarNamedops.Where(v => v.order.ID == order.ID))
                                {
                                    dopsPodsSelected.Add(item.pod);
                                    orderDopsPods.Add(item.pod);
                                }
                            }
                            // Check whether sufficient inventory is still available in the pod (also make sure it is was available in the beginning, not all values were updated at the beginning of this function / see above)
                            // Update remaining pod content
                            foreach (var itemDemand in itemDemands)
                            {
                                int number = itemDemand.Value;
                                while (number > 0)
                                {
                                    Pod pod;
                                    if (_availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count() > 0) //优先检索新分配的货架
                                    {
                                        pod = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                        orderDopsPods.Remove(pod);
                                        dopsPodsUsed.Add(pod);
                                    }
                                    else
                                        pod = _availableCounts[itemDemand.Key].Keys.First();
                                    Symbol name = new Symbol
                                    {
                                        pod = pod,
                                        order = order,
                                        outputstation = _currentStationorder.Key,
                                        skui = itemDemand.Key,
                                        name = "ziops" + "_" + itemDemand.Key.ID.ToString() + "_" +
                                        order.ID.ToString() + "_" + pod.ID.ToString() + "_" + _currentStationorder.Key.ID.ToString()
                                    };
                                    if (_availableCounts[itemDemand.Key][pod] >= number)
                                    {
                                        int numpods = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).Count();
                                        if (numpods > 0 && number > 1)
                                        {
                                            Pod pod1 = _availableCounts[itemDemand.Key].Keys.Where(v => orderDopsPods.Contains(v)).First();
                                            orderDopsPods.Remove(pod1);
                                            dopsPodsUsed.Add(pod1);
                                            if (_availableCounts[itemDemand.Key][pod] >= _availableCounts[itemDemand.Key][pod1])
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= number - numpods;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number - numpods);
                                                NewZiops.Add(name, number - numpods);
                                                number = numpods;
                                            }
                                            else
                                            {
                                                _availableCounts[itemDemand.Key][pod] -= 1;
                                                Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, 1);
                                                NewZiops.Add(name, 1);
                                                number = 1;
                                            }
                                        }
                                        else
                                        {
                                            _availableCounts[itemDemand.Key][pod] -= number;
                                            NewZiops.Add(name, number);
                                            Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, number);
                                            number = 0;
                                        }
                                        if (_availableCounts[itemDemand.Key][pod] == 0)
                                            _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                    else
                                    {
                                        NewZiops.Add(name, _availableCounts[itemDemand.Key][pod]);
                                        Instance.ResourceManager._Ziops[_currentStationorder.Key].Add(name, _availableCounts[itemDemand.Key][pod]);
                                        number -= _availableCounts[itemDemand.Key][pod];
                                        _availableCounts[itemDemand.Key].Remove(pod);
                                    }
                                }

                            }
                        }
                        HashSet<Pod> unusedDopsPods = new HashSet<Pod>(dopsPodsSelected.Where(v => !dopsPodsUsed.Contains(v)));
                        if (unusedDopsPods.Count > 0)
                        {
                            foreach (var pod in unusedDopsPods)
                            {
                                Symbol name2 = IsdeVarNameyrp.Where(v => v.pod.ID == pod.ID).First();
                                IsdeVarNameyrp.Remove(name2);
                                Instance.ResourceManager.BottoPod.Remove(name2.robot);
                                Instance.ResourceManager.ReleasePod(name2.pod);
                                foreach (var xps in IsdeVarNamexps.Where(v => v.pod.ID == name2.pod.ID))
                                    xps.outputstation.UnregisterInboundPod(name2.pod);
                                Symbol name1 = IsdeVarNamexps.Where(v => v.pod.ID == pod.ID).First();
                                IsdeVarNamexps.Remove(name1);
                            }

                        }
                    }
                }
                // 在所有 unused dops pods 已從 IsdeVarNameyrp / IsdeVarNamexps 移除後再記錄估算
                WriteSelectedExecutableCandidate(selectedCandidate, IsdeVarNamexps, IsdeVarNameyrp, objectiveCalibration);
                ArmValidationStopForSelection(selectedCandidate.DecisionId, IsdeVarNamexps, IsdeVarNameyrp);
                if (_availableStationorder.Count > 0)
                    RecordSelectedPathEstimates(IsdeVarNamexps, IsdeVarNameyrp);
                Instance.Observer.TimeOrderBatchingbyziops((DateTime.Now - A).TotalSeconds);
            }
            //else
            //    Thread.Sleep(1);
            return NewZiops;
        }
        /// <summary>
        /// This is called to decide about potentially pending orders.
        /// This method is being timed for statistical purposes and is also ONLY called when <code>SituationInvestigated</code> is <code>false</code>.
        /// Hence, set the field accordingly to react on events not tracked by this outer skeleton.
        /// </summary>
        protected override void DecideAboutPendingOrders()
        {
            if (_m1gtValidationBatchArmed && _config.StopAfterValidationBatch)
                return;

            DateTime A = DateTime.Now;
            Dictionary<ItemDescription, List<Pod>> PiSKU;
            Dictionary<ItemDescription, List<Order>> OiSKU;
            Dictionary<int, List<Symbol>> variableNames;
            Dictionary<OutputStation, int> Cs;
            Dictionary<OutputStation, HashSet<Pod>> inboundPods;
            HashSet<Order> pendingOrders;
            HashSet<Bot> Ra;//可以参与分配的robot集合
            HashSet<Bot> Rb;//不可以参与分配的robot集合，即已经分配还没释放的robot集合
            HashSet<Bot> R;//所有的robot集合
            HashSet<Pod> Pb;//已被分配的pod集合
            HashSet<Pod> Pa;//未被分配的pod集合
            Dictionary<Pod, Bot> PodToBot;//已经被分配的pod-bot对
            //HashSet<Symbol> linshiSymbol;
            _IsvariableNames.Clear();
            //if(Instance.ResourceManager.BottoPod.Count()>0)
            //    Thread.Sleep(1);
            // 对相关参数进行初始化（更新）
            HashSet<Pod> allPods = Initialize(out PiSKU, out OiSKU, out variableNames, out Cs, out pendingOrders, out inboundPods, out Ra, out Rb, out R, out Pb, out Pa, out PodToBot);
            if (R.Count() > 0 && pendingOrders.Count > 0) //运用Gurobi求解
            {
                Dictionary<Symbol, int> NewZiops = solve(SolverType.Gurobi, PiSKU, OiSKU, allPods, Cs, variableNames, pendingOrders, inboundPods, Ra, Rb, R, Pb, Pa, PodToBot);
                if (_IsvariableNames.ContainsKey(1) && _IsvariableNames[1].Count() > 0)
                {
                    // 将相应的order分配给station
                    //List<Symbol> IsdeVarNamexps = _IsvariableNames[0];
                    List<Symbol> IsdeVarNameyaos = _IsvariableNames[1];
                    //_IsvariableNames.Remove(0);
                    foreach (var symbol in NewZiops)
                    {
                        for (int i = 0; i < symbol.Value; i++)
                            symbol.Key.pod.JustRegisterItem(symbol.Key.skui); //将pod中选中的item进行标记
                    }
                    while (IsdeVarNameyaos.Count > 0)
                    {
                        // Assign the order
                        AllocateOrder(IsdeVarNameyaos.First().order, IsdeVarNameyaos.First().outputstation);
                        // Log fast lane assignment
                        Instance.StatCustomControllerInfo.CustomLogOB1++;
                        IsdeVarNameyaos.RemoveAt(0);
                    }
                }
                Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - A).TotalSeconds);
            }
            //else
            //    Instance.Observer.TimeOrderBatchingbyMP((DateTime.Now - A).TotalSeconds);
        }

        #region IOptimize Members

        /// <summary>
        /// Signals the current time to the mechanism. The mechanism can decide to block the simulation thread in order consume remaining real-time.
        /// </summary>
        /// <param name="currentTime">The current simulation time.</param>
        public override void SignalCurrentTime(double currentTime) { /* Ignore since this simple manager is always ready. */ }

        #endregion

        #region Custom stat tracking

        /// <summary>
        /// Contains the aggregated scorer values.
        /// </summary>
        private double[] _statScorerValues = null;
        /// <summary>
        /// Contains the number of assignments done.
        /// </summary>
        private double _statAssignments = 0;
        /// <summary>
        /// The callback indicates a reset of the statistics.
        /// </summary>
        public override void StatReset()
        {
            _statScorerValues = null;
            _statAssignments = 0;
        }
        /// <summary>
        /// The callback that indicates that the simulation is finished and statistics have to submitted to the instance.
        /// </summary>
        public override void StatFinish()
        {
            Instance.StatCustomControllerInfo.CustomLogOBString =
                _statScorerValues == null ? "" :
                string.Join(IOConstants.DELIMITER_CUSTOM_CONTROLLER_FOOTPRINT.ToString(), _statScorerValues.Select(e => e / _statAssignments).Select(e => e.ToString
                (IOConstants.FORMATTER)));
        }

        #endregion
    }

}

