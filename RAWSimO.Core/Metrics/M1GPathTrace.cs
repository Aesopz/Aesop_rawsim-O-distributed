using RAWSimO.Core.Bots;
using RAWSimO.Core.Elements;
using RAWSimO.Core.Geometrics;
using RAWSimO.Core.IO;
using RAWSimO.Core.Waypoints;
using RAWSimO.Toolbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RAWSimO.Core.Metrics
{
    public enum M1GPathTraceSegmentType
    {
        BotToPod,
        PodToStation
    }

    public class M1GPathTrace
    {
        private class SegmentRecord
        {
            public int Id;
            public int DecisionId;
            public M1GPathTraceSegmentType SegmentType;
            public double DecisionTime;
            public int BotId;
            public int PodId;
            public int StationId;
            public int EstimatedStartWaypointId;
            public int EstimatedEndWaypointId;
            public double EstimatedTravelTime;
            public double EstimatedObjectiveCost;
            public double EstimatedDistance;
            public string EstimatedPathNodes;
            public bool MatchedToExecution;
            public double ActualStartTime = double.NaN;
            public double ActualEndTime = double.NaN;
            public int ActualStartWaypointId = -1;
            public int ActualEndWaypointId = -1;
            public string ActualEndKind = "";
            public List<int> ActualPathNodes = new List<int>();
            public double ActualDuration = double.NaN;
            public double ActualDistance = double.NaN;
            public double ActualPreDispatchDistance = 0.0;
            public double ActualEnergy = double.NaN;
            public double ActualWaitTime = double.NaN;
            public int ActualTurnCount;
        }

        private readonly Instance _instance;
        private readonly List<SegmentRecord> _records = new List<SegmentRecord>();
        private readonly Dictionary<string, Queue<SegmentRecord>> _pendingByKey = new Dictionary<string, Queue<SegmentRecord>>();
        private int _nextDecisionId = 1;
        private int _nextRecordId = 1;

        public M1GPathTrace(Instance instance)
        {
            _instance = instance;
        }

        public void Reset()
        {
            _records.Clear();
            _pendingByKey.Clear();
            _nextDecisionId = 1;
            _nextRecordId = 1;
        }

        public void RecordEstimate(Bot bot, Pod pod, OutputStation station,
            Waypoint botWaypoint, Waypoint podWaypoint, Waypoint stationWaypoint,
            double botPodTravelTime, double podStationTravelTime, double decisionTime)
        {
            if (bot == null || pod == null || station == null)
                return;

            int decisionId = _nextDecisionId++;
            // Leg1 起始方向 = bot 當前方向
            var leg1Path = FindShortestPathNodes(botWaypoint, podWaypoint, emulatePodCarrying: false);
            AddEstimatedSegmentWithPath(decisionId, M1GPathTraceSegmentType.BotToPod, decisionTime, bot, pod, station,
                botWaypoint, podWaypoint, botPodTravelTime, leg1Path, GetBotOrientation(bot));
            // Leg2 起始方向 = leg1 路徑最後一段的方向（bot 抵達 pod 時面向）
            double leg2StartOrientation = GetEndOrientation(leg1Path.Nodes, GetBotOrientation(bot));
            var leg2Path = FindShortestPathNodes(podWaypoint, stationWaypoint, emulatePodCarrying: true);
            AddEstimatedSegmentWithPath(decisionId, M1GPathTraceSegmentType.PodToStation, decisionTime, bot, pod, station,
                podWaypoint, stationWaypoint, podStationTravelTime, leg2Path, leg2StartOrientation);
        }

        private static double GetBotOrientation(Bot bot)
        {
            var normalBot = bot as BotNormal;
            return normalBot != null ? normalBot.Orientation : double.NaN;
        }

        private static double GetEndOrientation(IList<Waypoint> nodes, double fallback)
        {
            if (nodes == null || nodes.Count < 2)
                return fallback;
            var from = nodes[nodes.Count - 2];
            var to = nodes[nodes.Count - 1];
            return Circle.GetOrientation(from.X, from.Y, to.X, to.Y);
        }

        public int BeginActualSegment(Bot bot, Pod pod, OutputStation station, M1GPathTraceSegmentType segmentType,
            Waypoint actualStart, Waypoint actualEnd, double currentTime)
        {
            if (bot == null || pod == null || station == null)
                return 0;

            SegmentRecord record = null;
            string key = BuildKey(bot.ID, pod.ID, station.ID, segmentType);
            if (_pendingByKey.ContainsKey(key) && _pendingByKey[key].Count > 0)
                record = _pendingByKey[key].Dequeue();

            if (record == null)
            {
                record = new SegmentRecord
                {
                    Id = _nextRecordId++,
                    DecisionId = 0,
                    SegmentType = segmentType,
                    DecisionTime = double.NaN,
                    BotId = bot.ID,
                    PodId = pod.ID,
                    StationId = station.ID,
                    EstimatedStartWaypointId = -1,
                    EstimatedEndWaypointId = -1,
                    EstimatedTravelTime = double.NaN,
                    EstimatedObjectiveCost = double.NaN,
                    EstimatedDistance = double.NaN,
                    EstimatedPathNodes = ""
                };
                _records.Add(record);
            }

            record.MatchedToExecution = record.DecisionId != 0;
            record.ActualStartTime = double.NaN;
            record.ActualStartWaypointId = actualStart != null ? actualStart.ID : -1;
            record.ActualEndWaypointId = actualEnd != null ? actualEnd.ID : -1;
            record.ActualEndKind = "";
            record.ActualPathNodes.Clear();
            if (actualStart != null)
                record.ActualPathNodes.Add(actualStart.ID);
            return record.Id;
        }

        public void StartActualSegment(int recordId, double currentTime)
        {
            if (recordId == 0)
                return;
            SegmentRecord record = _records.FirstOrDefault(r => r.Id == recordId);
            if (record == null)
                return;
            record.ActualStartTime = currentTime;
        }

        public void AppendActualWaypoint(int recordId, Waypoint waypoint)
        {
            if (recordId == 0 || waypoint == null)
                return;
            SegmentRecord record = _records.FirstOrDefault(r => r.Id == recordId);
            if (record == null)
                return;
            if (record.ActualPathNodes.Count == 0 || record.ActualPathNodes[record.ActualPathNodes.Count - 1] != waypoint.ID)
                record.ActualPathNodes.Add(waypoint.ID);
        }

        public void AppendActualPath(int recordId, IEnumerable<Waypoint> waypoints)
        {
            if (recordId == 0 || waypoints == null)
                return;
            SegmentRecord record = _records.FirstOrDefault(r => r.Id == recordId);
            if (record == null)
                return;

            var nodes = waypoints.Where(w => w != null).Select(w => w.ID).ToList();
            if (nodes.Count == 0)
                return;

            if (record.ActualPathNodes.Count == 0)
            {
                record.ActualPathNodes.AddRange(nodes);
                return;
            }

            int overlap = Math.Min(record.ActualPathNodes.Count, nodes.Count);
            while (overlap > 0)
            {
                bool matches = true;
                for (int i = 0; i < overlap; i++)
                {
                    if (record.ActualPathNodes[record.ActualPathNodes.Count - overlap + i] != nodes[i])
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches)
                    break;
                overlap--;
            }

            for (int i = overlap; i < nodes.Count; i++)
                record.ActualPathNodes.Add(nodes[i]);
        }

        public void CompleteActualSegment(int recordId, double currentTime, BotNormal.TripRecord trip)
        {
            CompleteActualSegmentAtBoundary(recordId, currentTime, trip, null, "TripDestination");
        }

        public bool CompleteActualSegmentAtBoundary(int recordId, double currentTime, BotNormal.TripRecord trip, Waypoint actualEnd, string actualEndKind)
        {
            if (recordId == 0)
                return false;
            SegmentRecord record = _records.FirstOrDefault(r => r.Id == recordId);
            if (record == null)
                return false;
            if (!double.IsNaN(record.ActualDuration))
                return false;
            record.ActualEndTime = currentTime;
            record.ActualDuration = double.IsNaN(record.ActualStartTime) ? trip.DurationSec : currentTime - record.ActualStartTime;
            if (actualEnd != null)
            {
                record.ActualEndWaypointId = actualEnd.ID;
                AppendActualWaypoint(recordId, actualEnd);
            }
            record.ActualPreDispatchDistance = CalculatePreDispatchDistance(record);
            record.ActualDistance = trip.DistanceM;
            record.ActualEnergy = trip.EnergyJ;
            record.ActualWaitTime = trip.WaitTimeSec;
            record.ActualTurnCount = trip.TurnCount;
            record.ActualEndKind = actualEndKind ?? "";
            return true;
        }

        public void WriteFiles(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return;
            Directory.CreateDirectory(directory);
            WriteSegments(Path.Combine(directory, "m1g_path_estimate_actual_segments.csv"));
            WriteSummary(Path.Combine(directory, "m1g_path_estimate_actual_summary.csv"));
        }

        private void AddEstimatedSegmentWithPath(int decisionId, M1GPathTraceSegmentType segmentType, double decisionTime,
            Bot bot, Pod pod, OutputStation station, Waypoint from, Waypoint to, double estimatedObjectiveCost,
            PathResult pathNodes, double startOrientation)
        {
            double estimatedTravelTime = CalculateIdealTravelTime(bot, pathNodes.Nodes, startOrientation);
            // 無法計算理想時間時保持 NaN：estimatedObjectiveCost 是距離(m)，不可作為時間(s) 的 fallback。
            if (double.IsInfinity(estimatedTravelTime))
                estimatedTravelTime = double.NaN;
            // BotToPod 加入 pod 取放時間，使估算範圍與實際量測範圍一致（均含 lift-up）。
            if (segmentType == M1GPathTraceSegmentType.BotToPod && !double.IsNaN(estimatedTravelTime))
                estimatedTravelTime += bot.PodTransferTime;
            var record = new SegmentRecord
            {
                Id = _nextRecordId++,
                DecisionId = decisionId,
                SegmentType = segmentType,
                DecisionTime = decisionTime,
                BotId = bot.ID,
                PodId = pod.ID,
                StationId = station.ID,
                EstimatedStartWaypointId = from != null ? from.ID : -1,
                EstimatedEndWaypointId = to != null ? to.ID : -1,
                EstimatedTravelTime = estimatedTravelTime,
                EstimatedObjectiveCost = estimatedObjectiveCost,
                EstimatedDistance = pathNodes.Distance,
                EstimatedPathNodes = string.Join("|", pathNodes.Nodes.Select(n => n.ID.ToString(CultureInfo.InvariantCulture)))
            };
            _records.Add(record);

            string key = BuildKey(bot.ID, pod.ID, station.ID, segmentType);
            if (!_pendingByKey.ContainsKey(key))
                _pendingByKey[key] = new Queue<SegmentRecord>();
            _pendingByKey[key].Enqueue(record);
        }

        private double CalculatePathDistance(IEnumerable<int> nodeIds)
        {
            var ids = nodeIds.ToList();
            if (ids.Count < 2)
                return 0.0;

            double distance = 0.0;
            for (int i = 1; i < ids.Count; i++)
            {
                var from = _instance.Controller.PathManager.GetWaypointByNodeId(ids[i - 1]);
                var to = _instance.Controller.PathManager.GetWaypointByNodeId(ids[i]);
                if (from == null || to == null)
                    return double.NaN;
                if (from.Paths.Contains(to))
                    distance += from[to];
                else
                    distance += from.GetDistance(to);
            }
            return distance;
        }

        private double CalculateIdealTravelTime(Bot bot, IList<Waypoint> nodes, double startOrientation = double.NaN)
        {
            var normalBot = bot as BotNormal;
            if (normalBot == null || nodes == null || nodes.Count < 2)
                return 0.0;

            // 下界估算：初始轉向（決策當下 bot 方向→第一段方向）+ 整段路徑以完整連續行駛計算。
            // 刻意忽略中途各轉彎的「停止→轉→加速」懲罰，確保 estimated ≤ actual。
            // 實際 WHCA* 因找到較少轉彎的路徑或受壅塞影響，actual 必然 ≥ 此下界。
            double initTurn = 0.0;
            if (!double.IsNaN(startOrientation))
            {
                double firstSegOrientation = Circle.GetOrientation(nodes[0].X, nodes[0].Y, nodes[1].X, nodes[1].Y);
                initTurn = normalBot.Physics.getTimeNeededToTurn(startOrientation, firstSegOrientation);
            }

            double totalDistance = 0.0;
            for (int i = 1; i < nodes.Count; i++)
                totalDistance += nodes[i - 1].GetDistance(nodes[i]);

            return initTurn + normalBot.Physics.getTimeNeededToMove(0, totalDistance);
        }

        private double CalculatePreDispatchDistance(SegmentRecord record)
        {
            if (record.EstimatedStartWaypointId < 0 ||
                record.ActualStartWaypointId < 0 ||
                record.EstimatedStartWaypointId == record.ActualStartWaypointId)
                return 0.0;

            var estimatedStart = _instance.Controller.PathManager.GetWaypointByNodeId(record.EstimatedStartWaypointId);
            var actualStart = _instance.Controller.PathManager.GetWaypointByNodeId(record.ActualStartWaypointId);
            if (estimatedStart == null || actualStart == null)
                return 0.0;

            // leg2 (PodToStation) bot 攜帶 pod，路徑不可穿越其他 pod storage；leg1 為空車。
            bool carrying = record.SegmentType == M1GPathTraceSegmentType.PodToStation;
            return FindShortestPathNodes(estimatedStart, actualStart, emulatePodCarrying: carrying).Distance;
        }

        private PathResult FindShortestPathNodes(Waypoint start, Waypoint destination, bool emulatePodCarrying)
        {
            if (start == null || destination == null)
                return new PathResult(new List<Waypoint>(), double.PositiveInfinity);
            if (start == destination)
                return new PathResult(new List<Waypoint> { start }, 0.0);

            var open = new Dictionary<Waypoint, SearchNode>();
            var closed = new HashSet<Waypoint>();
            open[start] = new SearchNode(start, null, 0.0, start.GetDistance(destination));

            while (open.Count > 0)
            {
                var currentPair = open.ArgMin(v => v.Value.DistanceTraveled + v.Value.DistanceToGoal);
                var current = currentPair.Key;
                var currentData = currentPair.Value;
                if (current == destination)
                    return new PathResult(BuildPath(currentData), currentData.DistanceTraveled);

                open.Remove(current);
                closed.Add(current);

                foreach (var successor in current.Paths)
                {
                    if (closed.Contains(successor))
                        continue;
                    if (emulatePodCarrying && successor.PodStorageLocation && successor != destination)
                        continue;

                    double additionalDistance = successor.Tier != destination.Tier ? _instance.WrongTierPenaltyDistance : 0.0;
                    double distance = currentData.DistanceTraveled + current[successor];
                    double heuristic = successor.GetDistance(destination) + additionalDistance;
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

            return new PathResult(new List<Waypoint>(), double.PositiveInfinity);
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

        private void WriteSegments(string path)
        {
            using (var writer = CreateWriter(path))
            {
                writer.WriteLine("decision_id,segment_id,segment_type,matched_to_milp,decision_time,bot_id,pod_id,station_id,estimated_start_wp,estimated_end_wp,estimated_travel_time,estimated_objective_cost,estimated_distance,estimated_path_nodes,actual_start_time,actual_end_time,actual_duration,actual_distance,actual_pre_dispatch_distance,actual_wait_time,actual_energy,actual_turn_count,actual_start_wp,actual_end_wp,actual_end_kind,actual_path_nodes,time_gap,distance_gap,wait_share");
                foreach (var record in _records.OrderBy(r => r.Id))
                {
                    double timeGap = double.IsNaN(record.ActualDuration) || double.IsNaN(record.EstimatedTravelTime) ? double.NaN : record.ActualDuration - record.EstimatedTravelTime;
                    double distanceGap = double.IsNaN(record.ActualDistance) || double.IsNaN(record.EstimatedDistance) ? double.NaN : record.ActualDistance - record.EstimatedDistance;
                    double waitShare = double.IsNaN(record.ActualDuration) || record.ActualDuration <= 0.0 ? double.NaN : record.ActualWaitTime / record.ActualDuration;
                    writer.WriteLine(string.Join(",",
                        record.DecisionId.ToString(CultureInfo.InvariantCulture),
                        record.Id.ToString(CultureInfo.InvariantCulture),
                        record.SegmentType.ToString(),
                        record.MatchedToExecution ? "1" : "0",
                        F(record.DecisionTime),
                        record.BotId.ToString(CultureInfo.InvariantCulture),
                        record.PodId.ToString(CultureInfo.InvariantCulture),
                        record.StationId.ToString(CultureInfo.InvariantCulture),
                        record.EstimatedStartWaypointId.ToString(CultureInfo.InvariantCulture),
                        record.EstimatedEndWaypointId.ToString(CultureInfo.InvariantCulture),
                        F(record.EstimatedTravelTime),
                        F(record.EstimatedObjectiveCost),
                        F(record.EstimatedDistance),
                        Csv(record.EstimatedPathNodes),
                        F(record.ActualStartTime),
                        F(record.ActualEndTime),
                        F(record.ActualDuration),
                        F(record.ActualDistance),
                        F(record.ActualPreDispatchDistance),
                        F(record.ActualWaitTime),
                        F(record.ActualEnergy),
                        record.ActualTurnCount.ToString(CultureInfo.InvariantCulture),
                        record.ActualStartWaypointId.ToString(CultureInfo.InvariantCulture),
                        record.ActualEndWaypointId.ToString(CultureInfo.InvariantCulture),
                        Csv(record.ActualEndKind),
                        Csv(string.Join("|", record.ActualPathNodes.Select(n => n.ToString(CultureInfo.InvariantCulture)))),
                        F(timeGap),
                        F(distanceGap),
                        F(waitShare)));
                }
            }
        }

        private void WriteSummary(string path)
        {
            using (var writer = CreateWriter(path))
            {
                writer.WriteLine("segment_type,records,matched_records,completed_records,avg_estimated_travel_time,avg_actual_duration,avg_time_gap,avg_time_gap_ratio,avg_estimated_distance,avg_actual_distance,avg_distance_gap,avg_actual_wait_time,avg_wait_share,avg_actual_turn_count");
                foreach (var group in _records.GroupBy(r => r.SegmentType).OrderBy(g => g.Key.ToString()))
                    writer.WriteLine(BuildSummaryLine(group.Key.ToString(), group));
                writer.WriteLine(BuildSummaryLine("All", _records));
            }
        }

        private string BuildSummaryLine(string name, IEnumerable<SegmentRecord> records)
        {
            var list = records.ToList();
            var completed = list.Where(r => !double.IsNaN(r.ActualDuration)).ToList();
            double avgEstimate = Avg(completed.Select(r => r.EstimatedTravelTime));
            double avgActual = Avg(completed.Select(r => r.ActualDuration));
            double avgGap = Avg(completed.Select(r => double.IsNaN(r.EstimatedTravelTime) ? double.NaN : r.ActualDuration - r.EstimatedTravelTime));
            double avgGapRatio = Avg(completed.Select(r => double.IsNaN(r.EstimatedTravelTime) || r.EstimatedTravelTime == 0.0 ? double.NaN : (r.ActualDuration - r.EstimatedTravelTime) / r.EstimatedTravelTime));
            double avgEstimateDistance = Avg(completed.Select(r => r.EstimatedDistance));
            double avgActualDistance = Avg(completed.Select(r => r.ActualDistance));
            double avgDistanceGap = Avg(completed.Select(r => double.IsNaN(r.EstimatedDistance) ? double.NaN : r.ActualDistance - r.EstimatedDistance));
            double avgWait = Avg(completed.Select(r => r.ActualWaitTime));
            double avgWaitShare = Avg(completed.Select(r => r.ActualDuration <= 0.0 ? double.NaN : r.ActualWaitTime / r.ActualDuration));
            double avgTurns = Avg(completed.Select(r => (double)r.ActualTurnCount));
            return string.Join(",",
                Csv(name),
                list.Count.ToString(CultureInfo.InvariantCulture),
                list.Count(r => r.MatchedToExecution).ToString(CultureInfo.InvariantCulture),
                completed.Count.ToString(CultureInfo.InvariantCulture),
                F(avgEstimate),
                F(avgActual),
                F(avgGap),
                F(avgGapRatio),
                F(avgEstimateDistance),
                F(avgActualDistance),
                F(avgDistanceGap),
                F(avgWait),
                F(avgWaitShare),
                F(avgTurns));
        }

        private static double Avg(IEnumerable<double> values)
        {
            var clean = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            return clean.Count == 0 ? double.NaN : clean.Average();
        }

        private static string BuildKey(int botId, int podId, int stationId, M1GPathTraceSegmentType segmentType)
        {
            return botId.ToString(CultureInfo.InvariantCulture) + "|" +
                podId.ToString(CultureInfo.InvariantCulture) + "|" +
                stationId.ToString(CultureInfo.InvariantCulture) + "|" +
                segmentType.ToString();
        }

        private static string F(double value)
        {
            if (double.IsNaN(value))
                return "";
            if (double.IsPositiveInfinity(value))
                return "Infinity";
            if (double.IsNegativeInfinity(value))
                return "-Infinity";
            return value.ToString(IOConstants.FORMATTER);
        }

        private static StreamWriter CreateWriter(string path)
        {
            try
            {
                return new StreamWriter(path, false);
            }
            catch (IOException)
            {
                string directory = Path.GetDirectoryName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                string extension = Path.GetExtension(path);
                string fallback = Path.Combine(directory, name + "_" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + extension);
                return new StreamWriter(fallback, false);
            }
        }

        private static string Csv(string value)
        {
            if (value == null)
                return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private class SearchNode
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

        private class PathResult
        {
            public PathResult(List<Waypoint> nodes, double distance)
            {
                Nodes = nodes;
                Distance = distance;
            }

            public List<Waypoint> Nodes;
            public double Distance;
        }
    }
}
