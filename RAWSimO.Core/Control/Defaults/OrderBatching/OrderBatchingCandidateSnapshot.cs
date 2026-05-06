using RAWSimO.Core.Elements;
using RAWSimO.Core.Items;
using RAWSimO.Core.Management;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.OrderBatching
{
    internal sealed class OrderBatchingCandidateSnapshot
    {
        public Dictionary<OutputStation, int> Cs { get; private set; }
        public Dictionary<OutputStation, HashSet<Pod>> InboundPodsPerStation { get; private set; }
        public HashSet<Pod> CandidateUnusedPods { get; private set; }
        public HashSet<Bot> AvailableRobots { get; private set; }
        public HashSet<Bot> BusyRobots { get; private set; }
        public HashSet<Bot> AllCandidateRobots { get; private set; }
        public HashSet<Pod> ExistingAssignedPods { get; private set; }
        public Dictionary<Pod, Bot> ExistingPodToBot { get; private set; }
        public HashSet<Pod> AllPods { get; private set; }
        public HashSet<Order> StockFeasiblePendingOrders { get; private set; }
        public HashSet<Order> LateOrders { get; private set; }

        private OrderBatchingCandidateSnapshot()
        {
            Cs = new Dictionary<OutputStation, int>();
            InboundPodsPerStation = new Dictionary<OutputStation, HashSet<Pod>>();
            CandidateUnusedPods = new HashSet<Pod>();
            AvailableRobots = new HashSet<Bot>();
            BusyRobots = new HashSet<Bot>();
            AllCandidateRobots = new HashSet<Bot>();
            ExistingAssignedPods = new HashSet<Pod>();
            ExistingPodToBot = new Dictionary<Pod, Bot>();
            AllPods = new HashSet<Pod>();
            StockFeasiblePendingOrders = new HashSet<Order>();
            LateOrders = new HashSet<Order>();
        }

        public static OrderBatchingCandidateSnapshot Build(Instance instance, IEnumerable<Order> pendingOrders, double dueTimeOrderOfMp)
        {
            var snapshot = new OrderBatchingCandidateSnapshot();

            snapshot.Cs = instance.OutputStations
                .Where(station => station.Capacity - station.CapacityReserved - station.CapacityInUse > 0)
                .ToDictionary(station => station, station => station.Capacity - station.CapacityReserved - station.CapacityInUse);

            foreach (var station in instance.OutputStations)
            {
                var inboundPods = new HashSet<Pod>();
                foreach (var pod in station.InboundPods.ToList())
                {
                    if (instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        inboundPods.Add(pod);
                        continue;
                    }

                    if (!instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        station.UnregisterInboundPod(pod);
                        continue;
                    }

                    if (instance.ResourceManager._usedPods[pod].CurrentTask is RestTask)
                    {
                        instance.ResourceManager.ReleasePod(pod);
                        station.UnregisterInboundPod(pod);
                        continue;
                    }

                    inboundPods.Add(pod);
                }
                snapshot.InboundPodsPerStation[station] = inboundPods;
            }

            snapshot.CandidateUnusedPods = new HashSet<Pod>(instance.ResourceManager.UnusedPods.Where(pod =>
                !instance.ResourceManager.BottoPod.ContainsValue(pod) &&
                !instance.ResourceManager._usedPods.ContainsKey(pod)));

            foreach (var stationPods in snapshot.InboundPodsPerStation.Values)
            {
                foreach (var pod in stationPods)
                {
                    if (snapshot.ExistingPodToBot.ContainsKey(pod))
                        continue;

                    if (instance.ResourceManager._usedPods.ContainsKey(pod))
                    {
                        var bot = instance.ResourceManager._usedPods[pod];
                        snapshot.ExistingAssignedPods.Add(pod);
                        snapshot.ExistingPodToBot[pod] = bot;
                        snapshot.BusyRobots.Add(bot);
                        snapshot.AllCandidateRobots.Add(bot);
                    }
                    else if (instance.ResourceManager.BottoPod.ContainsValue(pod))
                    {
                        var bot = instance.ResourceManager.BottoPod.First(pair => pair.Value.ID == pod.ID).Key;
                        snapshot.ExistingAssignedPods.Add(pod);
                        snapshot.ExistingPodToBot[pod] = bot;
                        snapshot.BusyRobots.Add(bot);
                        snapshot.AllCandidateRobots.Add(bot);
                    }
                }
            }

            foreach (var bot in instance._outputstationbots)
            {
                if (bot.Pod == null &&
                    !instance.ResourceManager._usedPods.ContainsValue(bot) &&
                    !instance.ResourceManager.BottoPod.ContainsKey(bot))
                {
                    snapshot.AvailableRobots.Add(bot);
                    snapshot.AllCandidateRobots.Add(bot);
                }
            }

            snapshot.AllPods = new HashSet<Pod>(snapshot.ExistingAssignedPods.Concat(snapshot.CandidateUnusedPods));
            snapshot.StockFeasiblePendingOrders = new HashSet<Order>(pendingOrders.Where(order =>
                order.Positions.All(position => instance.StockInfo.GetActualStock(position.Key) >= position.Value)));

            foreach (Order order in snapshot.StockFeasiblePendingOrders)
            {
                order.Timestay = order.DueTime - (instance.SettingConfig.StartTime.AddSeconds(Convert.ToInt32(instance.Controller.CurrentTime)) - order.TimePlaced).TotalSeconds;
            }

            int sequence = 0;
            foreach (Order order in snapshot.StockFeasiblePendingOrders.OrderBy(order => order.Timestay).ThenBy(order => order.DueTime))
            {
                order.sequence = sequence;
                sequence++;
            }

            snapshot.LateOrders = new HashSet<Order>(snapshot.StockFeasiblePendingOrders.Where(order =>
                order.Timestay < dueTimeOrderOfMp &&
                order.Positions.All(position => snapshot.CandidateUnusedPods.Sum(pod => pod.CountAvailable(position.Key)) >= position.Value)));

            return snapshot;
        }
    }
}
