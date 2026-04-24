using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Elements;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.MultiAgentPathFinding.Toolbox
{
    /// <summary>
    /// ECBS 專用的 deadlock handler（複製自 <see cref="DeadlockHandler"/>，專為
    /// energy-aware ECBS 設計，不影響 CBS / WHCA* 等共用原版）。
    ///
    /// 相較於共用版，本類別認得「committed wait」：當 agent 已經被 PathManager
    /// 註冊了一段尚未耗盡的時空預約（<see cref="Agent.ArrivalTimeAtNextNode"/> 在未來），
    /// 這個 agent 是在履行上一輪 ECBS 規劃的合法等待，不應被視為 livelock。
    ///
    /// 另外新增 <see cref="InPlaceWait"/> 作為預設的「脫離假 deadlock」手段，
    /// 避免 <see cref="RandomHop"/> 隨機挪動破壞 ECBS 的能耗最佳性。
    /// </summary>
    public class EnergyDeadlockHandler
    {
        /// <summary>
        /// ε 用於 committed wait 判定；避免 float 精度誤差造成的邊界誤判。
        /// </summary>
        private const double COMMITTED_WAIT_EPSILON = 0.5;

        /// <summary>
        /// The maximum wait time
        /// </summary>
        public double MaximumWaitTime;

        /// <summary>
        /// The length of a wait step
        /// </summary>
        public double LengthOfAWaitStep = 5;

        /// <summary>
        /// The time of the agent last move
        /// </summary>
        private Dictionary<int, double> _waitingSince;

        /// <summary>
        /// The node of the agent last move
        /// </summary>
        private Dictionary<int, int> _waitNode;

        /// <summary>
        /// The graph
        /// </summary>
        private Graph _graph;

        /// <summary>
        /// The Random Component
        /// </summary>
        private Random _rnd;

        public EnergyDeadlockHandler(Graph graph, int seed)
        {
            _graph = graph;
            _waitingSince = new Dictionary<int, double>();
            _waitNode = new Dictionary<int, int>();
            _rnd = new Random(seed);
        }

        /// <summary>
        /// 更新 agent 狀態。相較於共用版，committed-wait 的 agent 每個 tick 都會把
        /// 計時器重置；避免 wait 時間超過 <see cref="MaximumWaitTime"/> 時被誤判。
        /// </summary>
        public void Update(List<Agent> agents, double currentTime)
        {
            foreach (var agent in agents.Where(a => a.FixedPosition))
            {
                _waitingSince[agent.ID] = currentTime;
                _waitNode[agent.ID] = agent.NextNode;
            }

            foreach (var agent in agents.Where(a => !a.FixedPosition))
            {
                if (!_waitingSince.ContainsKey(agent.ID))
                {
                    _waitingSince.Add(agent.ID, currentTime);
                    _waitNode.Add(agent.ID, agent.NextNode);
                }
                else if (agent.ReservationsToNextNode.Count > 0
                      || agent.NextNode != _waitNode[agent.ID]
                      || agent.ArrivalTimeAtNextNode > currentTime + COMMITTED_WAIT_EPSILON)
                {
                    // committed wait 也被視為「正在進展」，計時器重置。
                    _waitingSince[agent.ID] = currentTime;
                    _waitNode[agent.ID] = agent.NextNode;
                }
            }
        }

        /// <summary>
        /// 當 agent 尚有 committed wait 或仍在移動 (FixedPosition) 時，不視為 deadlock。
        /// </summary>
        public bool IsInDeadlock(Agent agent, double currentTime)
        {
            if (agent.FixedPosition)
                return false;
            if (agent.ArrivalTimeAtNextNode > currentTime + COMMITTED_WAIT_EPSILON)
                return false;
            if (!_waitingSince.ContainsKey(agent.ID))
                return false;
            return currentTime - _waitingSince[agent.ID] > MaximumWaitTime;
        }

        /// <summary>
        /// 「就地再等一輪」。用於輕度誤觸發 / 短暫 merge conflict：
        /// 比 RandomHop 低能耗（~90 J × LengthOfAWaitStep），且不會破壞 ECBS 的下一輪規劃。
        /// </summary>
        public void InPlaceWait(Agent agent)
        {
            agent.Path.Clear();
            agent.Path.AddLast(agent.NextNode, true, LengthOfAWaitStep);
        }

        /// <summary>
        /// 最後防線：只有連續多輪仍無進展（真循環 livelock）才呼叫。
        /// 隨機選取可行鄰邊移動，破壞對稱性。與共用版行為一致。
        /// </summary>
        public bool RandomHop(Agent agent, ReservationTable reservationTable = null, double currentTime = 0.0, bool finalReservation = false, bool insertReservation = false)
        {
            var possibleEdges = new List<Edge>(_graph.Edges[agent.NextNode]);
            Shuffle(possibleEdges);
            foreach (var edge in possibleEdges.Where(e => !e.ToNodeInfo.IsLocked && (agent.CanGoThroughObstacles || !e.ToNodeInfo.IsObstacle)))
            {
                if (reservationTable != null)
                {
                    var intervals = reservationTable.CreateIntervals(currentTime, currentTime, 0, agent.Physics, agent.NextNode, edge.To, finalReservation);
                    if (reservationTable.IntersectionFree(intervals))
                    {
                        if (insertReservation)
                            reservationTable.Add(intervals);

                        agent.Path.Clear();
                        agent.Path.AddLast(edge.To, true, _rnd.NextDouble() * LengthOfAWaitStep);
                        return true;
                    }
                }
                else
                {
                    agent.Path.Clear();
                    agent.Path.AddLast(edge.To, true, _rnd.NextDouble() * LengthOfAWaitStep);
                    return true;
                }
            }

            return false;
        }

        private void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                int k = (_rnd.Next(0, n) % n);
                n--;
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
