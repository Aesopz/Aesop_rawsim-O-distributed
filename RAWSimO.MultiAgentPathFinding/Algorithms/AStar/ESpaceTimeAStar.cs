using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.MultiAgentPathFinding.Toolbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.MultiAgentPathFinding.Algorithms.AStar
{
    /// <summary>
    /// Energy-aware Space-Time A* (based on WHCA*)
    /// </summary>
    public class ESpaceTimeAStar : AStarBase
    {
        /// <summary>
        /// backpointer of the generated nodes
        /// </summary>
        public List<int> NodeBackpointerId;

        /// <summary>
        /// backpointer of the node, where we turned last time
        /// </summary>
        public List<int> NodeBackpointerLastStopId;

        /// <summary>
        /// The mapping of generated nodes to the 2D Edge
        /// null = Wait Edge
        /// </summary>
        public List<Edge> NodeBackpointerEdge;

        /// <summary>
        /// The mapping of generated nodes to time
        /// NodeTime[100] = point in time of the 100th generated node
        /// NodeTime is the time the agent reaches the node. This is excluding possible rotations.
        /// </summary>
        public List<double> NodeTime;

        /// <summary>
        /// temporary backpointer of the generated nodes
        /// </summary>
        public List<int> NodeBackpointerIdTemp;

        /// <summary>
        /// temporary backpointer of the node, where we turned last time
        /// </summary>
        public List<int> NodeBackpointerLastTurnIdTemp;

        /// <summary>
        /// The temporary mapping of generated nodes to the 2D Edge
        /// null = Wait Edge
        /// </summary>
        public List<Edge> NodeBackpointerEdgeTemp;

        /// <summary>
        /// The temporary mapping of generated nodes to time
        /// NodeTime[100] = point in time of the 100th generated node
        /// </summary>
        public List<double> NodeTimeTemp;

        /// <summary>
        /// The start angle of the agent
        /// </summary>
        public short StartAngle;

        /// <summary>
        /// The wait steps before start
        /// </summary>
        public int WaitStepsBeforeStart = 0;

        /// <summary>
        /// Number of back pointer calls since last successor call
        /// </summary>
        public Dictionary<int, double> BiasedCost;

        /// <summary>
        /// Class is initiated.
        /// </summary>
        protected bool _init = false;

        /// <summary>
        /// Number of generated nodes. Needed for id assignment.
        /// </summary>
        protected int _numNodeId = 0;

        /// <summary>
        /// Length of a time step.
        /// </summary>
        protected double _lengthOfAWaitStep;

        /// <summary>
        /// Length of a window.
        /// </summary>
        protected double _lengthOfAWindow;

        /// <summary>
        /// 2D Graph
        /// </summary>
        protected Graph _graph;

        /// <summary>
        /// Agent
        /// </summary>
        protected Agent _agent;

        /// <summary>
        /// reservation Table
        /// </summary>
        protected ReservationTable _reservationTable;

        /// <summary>
        /// The RRA* Algorithm
        /// </summary>
        protected ReverseResumableAStar _RRAStar;

        /// <summary>
        /// The tie breaking is turned on
        /// </summary>
        protected bool _tieBreaking;

        /// <summary>
        /// Pareto bi-criteria frontier per state key (node2d, dir).
        /// Time-monotonic tie-breaking broke optimality (energy A* is NOT time-monotonic);
        /// energy dominance alone pruned earlier-arrival labels that avoided later conflicts.
        /// Pareto dominance on (t, E) is the minimal sound pruning: only labels strictly
        /// dominated in BOTH time and energy are removed. Keeps A* optimal under admissible h.
        /// Bucket index: node2d * 4 + dirIndex (0°/90°/180°/270° => 0/1/2/3).
        /// Lazy allocated; most buckets stay null.
        /// </summary>
        protected List<(double t, double E)>[] _paretoFrontiers;

        /// <summary>
        /// Upper bound on goal energy — maintained as branch-and-bound prune.
        /// First goal sets UB; subsequent expansions with f >= UB are skipped.
        /// Preserves optimality (only skips provably non-improving nodes).
        /// </summary>
        private double _upperBound = double.PositiveInfinity;

        /// <summary>
        /// Low-level search timer. Constructor starts it.
        /// </summary>
        private Stopwatch _searchTimer;

        /// <summary>
        /// Optional low-level timeout in milliseconds. Default long.MaxValue (no timeout).
        /// External callers (e.g. ECBSMethod) may set this to bound search wall-clock time.
        /// When exceeded, Successors() returns empty => Search terminates gracefully.
        /// </summary>
        private long _searchTimeoutMs = long.MaxValue;

        /// <summary>
        /// Get/set low-level search timeout in ms. Default is no timeout.
        /// </summary>
        public long SearchTimeoutMilliseconds
        {
            get { return _searchTimeoutMs; }
            set { _searchTimeoutMs = value; }
        }

        /// <summary>
        /// A reservation from the end to infinity must be possible
        /// </summary>
        public bool FinalReservation = false;

        /// <summary>
        /// Aesop ECBS needed
        /// </summary>
        public List<double> NodeEnergy;
        public List<double> NodeEnergyTemp;

        // 能耗查表：_moveEnergyPerKg[k] = 走 k 格直線、mTotal=1 的機械能（焦耳/公斤）
        private double[] _moveEnergyPerKg;
        private double _edgeLen;
        private const int MaxHopCells = 128;

        // Method B (min-turns lower bound) precomputed constants.
        // Per-turn minimum energy/time is a 90° turn (smaller than 180°, and 90° is
        // the minimum non-trivial turn on a cardinal grid).
        private double _turnE90PerKg;   // E_turn for 90° at mTotal=1
        private double _turnTime90Sec;  // idle time during a 90° turn

        /// <summary>
        /// Initializes a new instance of the <see cref="ESpaceTimeAStar"/> class.
        /// </summary>
        public ESpaceTimeAStar(Graph graph, double lengthOfAWaitStep, double lengthOfAWindow, ReservationTable reservationTable, Agent agent, ReverseResumableAStar rraStar, bool tieBreaking = true)
            : base(0, -1)
        {
            this._graph = graph;
            this._lengthOfAWaitStep = lengthOfAWaitStep;
            this._lengthOfAWindow = lengthOfAWindow;
            this._reservationTable = reservationTable;
            this._agent = agent;
            this._RRAStar = rraStar;
            this._tieBreaking = tieBreaking;

            //built mappings
            //NodeTime = new List<double>();
            //NodeBackpointerId = new List<int>();
            //NodeBackpointerLastStopId = new List<int>();
            //NodeBackpointerEdge = new List<Edge>();
            //NodeTimeTemp = new List<double>();
            //NodeBackpointerIdTemp = new List<int>();
            //NodeBackpointerLastTurnIdTemp = new List<int>();
            //NodeBackpointerEdgeTemp = new List<Edge>();

            //aesop changed
            NodeTime = new List<double>();
            NodeEnergy = new List<double>();

            NodeBackpointerId = new List<int>();
            NodeBackpointerLastStopId = new List<int>();
            NodeBackpointerEdge = new List<Edge>();

            NodeTimeTemp = new List<double>();
            NodeEnergyTemp = new List<double>();

            NodeBackpointerIdTemp = new List<int>();
            NodeBackpointerLastTurnIdTemp = new List<int>();
            NodeBackpointerEdgeTemp = new List<Edge>();
            //aesop changed

            // 預算 90° turn 的能耗與時間下界（Method B：minTurns × E_90 為 admissible 下界）
            double halfPi = Math.PI / 2.0;
            _turnE90PerKg = EnergyModel.ComputeTurnEnergy(1.0, halfPi, agent.Physics.TurnSpeed);
            _turnTime90Sec = agent.Physics.getTimeNeededToTurn(0.0, halfPi);

            // 預算移動能耗查表（只需做一次）
            // Filter out zero-distance edges (e.g. elevator edges) to get a valid hop length.
            _edgeLen = graph.Edges.SelectMany(e => e)
                .Where(e => e.Distance > 0)
                .Select(e => e.Distance)
                .FirstOrDefault();
            if (_edgeLen <= 0) _edgeLen = 1.0;
            _moveEnergyPerKg = new double[MaxHopCells + 1];
            for (int k = 1; k <= MaxHopCells; k++)
                _moveEnergyPerKg[k] = EnergyModel.ComputeMoveEnergy(
                    1.0,
                    agent.Physics.Acceleration,
                    agent.Physics.Deceleration,
                    agent.Physics.MaxSpeed,
                    k * _edgeLen);

            StartAngle = Graph.RadToDegree(agent.OrientationAtNextNode);

            //NodeTime.Add(agent.ArrivalTimeAtNextNode);
            //NodeBackpointerId.Add(-1);
            //NodeBackpointerLastStopId.Add(0);
            //NodeBackpointerEdge.Add(null);
            //_numNodeId++;

            //aesop changed
            NodeTime.Add(agent.ArrivalTimeAtNextNode);
            NodeEnergy.Add(0.0);

            NodeBackpointerId.Add(-1);
            NodeBackpointerLastStopId.Add(0);
            NodeBackpointerEdge.Add(null);

            _numNodeId++;
            //aesop changed

            _init = true;

            //the node 0 has no assigned value yet.
            Q.ChangeKey(Open[0], h(0));

            if (_tieBreaking)
            {
                // Pareto frontier per (node2d, dir) — 4 directions (0/90/180/270)
                _paretoFrontiers = new List<(double t, double E)>[graph.NodeCount * 4];
                // Lazy allocation: buckets stay null until first visit
            }

            // Start low-level search timer (no timeout by default)
            _searchTimer = Stopwatch.StartNew();
        }

        /// <summary>
        /// Maps angle (0/90/180/270) to index 0..3. Handles negatives / >=360 safely.
        /// </summary>
        private static int DirIndex(short angle)
        {
            int a = ((angle % 360) + 360) % 360;
            return a / 90;
        }

        /// <summary>
        /// Check if label (t, E) is Pareto-dominated by any label in the frontier for (node2d, dir).
        /// If not dominated, add it to the frontier and remove any existing labels it dominates.
        /// Returns true if the new label should be pruned (dominated).
        /// Dominance: (t_old, E_old) ≺ (t_new, E_new) iff t_old ≤ t_new ∧ E_old ≤ E_new ∧ (strict in one).
        /// </summary>
        private bool CheckAndAddParetoLabel(int node2d, short dir, double t, double E)
        {
            int bucket = node2d * 4 + DirIndex(dir);
            var frontier = _paretoFrontiers[bucket];
            if (frontier == null)
            {
                frontier = new List<(double t, double E)>(2);
                frontier.Add((t, E));
                _paretoFrontiers[bucket] = frontier;
                return false;
            }

            // Scan: if any existing label dominates new, prune; else remove labels that new dominates.
            const double EPS = 1e-9;
            for (int i = 0; i < frontier.Count; i++)
            {
                double tOld = frontier[i].t;
                double EOld = frontier[i].E;
                // existing dominates new?
                if (tOld <= t + EPS && EOld <= E + EPS &&
                    (tOld < t - EPS || EOld < E - EPS))
                {
                    return true; // new is dominated => prune
                }
            }

            // Remove labels dominated by new
            for (int i = frontier.Count - 1; i >= 0; i--)
            {
                double tOld = frontier[i].t;
                double EOld = frontier[i].E;
                if (t <= tOld + EPS && E <= EOld + EPS &&
                    (t < tOld - EPS || E < EOld - EPS))
                {
                    frontier.RemoveAt(i);
                }
            }

            frontier.Add((t, E));
            return false;
        }

        /// <summary>
        /// Method B: Manhattan-style admissible lower bound on remaining turn count.
        /// Given current 2D node, goal node, and current heading (degrees, cardinal),
        /// returns the minimum number of turns required to reach the goal.
        /// Angle convention: 0°=+X (E), 90°=+Y (N), 180°=-X (W), 270°=-Y (S).
        /// </summary>
        private int ComputeMinTurnsLowerBound(int node2d, int goalNode, short currentAngleDeg)
        {
            double dx = _graph.PositionX[goalNode] - _graph.PositionX[node2d];
            double dy = _graph.PositionY[goalNode] - _graph.PositionY[node2d];

            const double EPS = 1e-6;
            bool hasX = Math.Abs(dx) > EPS;
            bool hasY = Math.Abs(dy) > EPS;

            if (!hasX && !hasY) return 0;

            // Decompose current heading into unit (curDirX, curDirY), ±1 on exactly one axis.
            int curDirX = 0, curDirY = 0;
            int a = ((currentAngleDeg % 360) + 360) % 360;
            switch (a)
            {
                case 0:   curDirX =  1; break; // East  (+X)
                case 90:  curDirY =  1; break; // North (+Y)
                case 180: curDirX = -1; break; // West  (-X)
                case 270: curDirY = -1; break; // South (-Y)
            }

            // Required axis signs (0 if no motion needed on that axis)
            int needDirX = dx > EPS ? 1 : (dx < -EPS ? -1 : 0);
            int needDirY = dy > EPS ? 1 : (dy < -EPS ? -1 : 0);

            // Case 1: only one axis of motion required.
            if (hasX && !hasY) return (curDirX == needDirX) ? 0 : 1;
            if (hasY && !hasX) return (curDirY == needDirY) ? 0 : 1;

            // Case 2: both axes required → at least one turn regardless.
            //   - If current heading already aligns with one needed direction → 1 turn.
            //   - Else (wrong-sign same-axis, or wrong axis) → 2 turns.
            if (curDirX == needDirX || curDirY == needDirY) return 1;
            return 2;
        }

        /// <summary>
        /// Sets the back pointer.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="node">The node.</param>
        /// <param name="g">The g value for the node.</param>
        /// <param name="h">The h value for the node.</param>
        protected override void setBackPointer(int parent, int node, double g, double h)
        {
            //no parent discarding - backpointer already set
            //NodeBackpointer[node] = parent;
        }

        /// <summary>
        /// heuristic value for the node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>
        /// h value
        /// </returns>
        //public override double h(int node)
        //{
        //    if (!_init)
        //        return 0;

        //    var node2d = NodeTo2D(node);

        //    //already found in RRA*?
        //    if (_RRAStar.Closed.Contains(node2d))
        //        return
        //            // Costs obtained by RRA*
        //            _RRAStar.g(node2d) +
        //            // Costs for turning
        //            ((node2d == _RRAStar.StartNode) ? 0 : _agent.Physics.getTimeNeededToTurn(Graph.DegreeToRad(GetLastStopAngleAfterTurn(node)), Graph.DegreeToRad(_RRAStar.getAngle(node2d)))) +
        //            // Biased costs
        //            ((this.BiasedCost == null || !this.BiasedCost.ContainsKey(node2d)) ? 0 : this.BiasedCost[node2d]);
        //    //find RRA* solution
        //    if (_RRAStar.Search(node2d))
        //        return
        //            // Costs obtained by RRA*
        //            _RRAStar.g(node2d) +
        //            // Costs for turning
        //            ((node2d == _RRAStar.StartNode) ? 0 : _agent.Physics.getTimeNeededToTurn(Graph.DegreeToRad(GetLastStopAngleAfterTurn(node)), Graph.DegreeToRad(_RRAStar.getAngle(node2d)))) +
        //            // Biased costs
        //            ((this.BiasedCost == null || !this.BiasedCost.ContainsKey(node2d)) ? 0 : this.BiasedCost[node2d]);
        //    // No solution
        //    return double.PositiveInfinity;
        //} 測試完改回來

        //aesop changed — ECBS h() 與 CBS h() 語意對齊
        // CBS:  h = T_rra(time) + T_turn(time) + BiasedCost
        // ECBS: h = h_move(energy) + h_support(energy) + h_turn(energy) + h_turn_support(energy)
        public override double h(int node)
        {
            if (!_init) return 0.0;
            int node2d = NodeTo2D(node);
            if (node2d == _agent.DestinationNode) return 0.0;

            double mTotal = _agent.CurrentEnergyState.TotalWeight;

            // RRA*.g = 靜態圖上從 node2d 到 goal 的旅行時間下界
            // （假設已在最高速，同 CBS h() 的設計）
            double T_rra;
            if (_RRAStar.Closed.Contains(node2d))
                T_rra = _RRAStar.g(node2d);
            else if (_RRAStar.Search(node2d))
                T_rra = _RRAStar.g(node2d);
            else
            {
                // fallback：Euclidean 弱 heuristic
                double dist_fb = _graph.getDistance(node2d, _RRAStar.GoalNode);
                double h_fb_move = (dist_fb > 0.0) ? EnergyModel.ComputeMoveEnergyLowerBound(mTotal, dist_fb) : 0.0;
                double h_fb_supp = (dist_fb > 0.0) ? EnergyModel.P_IDLE * (dist_fb / _agent.Physics.MaxSpeed) : 0.0;
                return h_fb_move + h_fb_supp;
            }

            if (T_rra <= 0.0) return 0.0;

            // ─── h_move: 圖路徑距離的摩擦能耗下界 ───
            // 最高速假設下只有摩擦（無 E1/E4）→ ≤ 真實 move energy → admissible
            double d_graph = _RRAStar.getPathDistance(node2d);
            double h_move = (d_graph > 0.0) ? EnergyModel.ComputeMoveEnergyLowerBound(mTotal, d_graph) : 0.0;

            // ─── h_support: P_IDLE × T_rra ───
            // T_rra ≤ 真實剩餘時間 → admissible
            double h_support = EnergyModel.P_IDLE * T_rra;

            // ─── h_turn: 當前朝向 → RRA* 首段方向的單次轉向能耗 ───
            // 雖為 1-step lower bound（非全程），但中型基準實測比 Manhattan min-turns 版（Method B）
            // 更接近 CBS 基線。Method B 的嚴格下界在 HighwayHallway 單向佈局下雖 admissible，
            // 但反而使 A* 探索到 per-order energy 更差的路徑（+4.29% 能耗 vs 基線 +2.07%）。
            double h_turn = 0.0;
            double h_turn_support = 0.0;
            if (node2d != _RRAStar.StartNode)
            {
                double turnRad = Math.Abs(
                    Graph.DegreeToRad(GetLastStopAngleAfterTurn(node)) -
                    Graph.DegreeToRad(_RRAStar.getAngle(node2d)));
                if (turnRad > Math.PI) turnRad = 2.0 * Math.PI - turnRad;
                if (turnRad > 0.0)
                {
                    h_turn = EnergyModel.ComputeTurnEnergy(mTotal, turnRad, _agent.Physics.TurnSpeed);
                    h_turn_support = EnergyModel.P_IDLE * _agent.Physics.getTimeNeededToTurn(0.0, turnRad);
                }
            }

            return h_move + h_support + h_turn + h_turn_support;
        }
        //aesop changed

        /// <summary>
        /// g value for the node
        /// </summary>
        /// <param name="node"></param>
        /// <returns>
        /// g value
        /// </returns>
        //public override double g(int node)
        //{
        //    return NodeTime[node];
        //}

        //aesop changed
        public override double g(int node)
        {
            return NodeEnergy[node];
        }
        //aesop changed

        /// <summary>
        /// g value for the node, if the backpointer would come from parent
        /// </summary>
        /// <param name="parent">The temporary parent.</param>
        /// <param name="node">The node.</param>
        /// <returns>
        /// g value
        /// </returns>
        /// <exception cref="System.NotImplementedException"></exception>
        //public override double gPrime(int parent, int node)
        //{
        //    //no parent discarding possible
        //    return NodeTime[node];
        //}

        //aesop changed
        public override double gPrime(int parent, int node)
        {
            return NodeEnergy[node];
        }
        //aesop changed

        /// <summary>
        /// Condition to stop searching.
        /// </summary>
        /// <param name="n">The expanded node.</param>
        /// <returns></returns>
        protected override bool StopCondition(int n)
        {
            //goal node found or time limit exceeded
            if ((NodeTo2D(n) == _agent.DestinationNode || NodeTime[n] >= _lengthOfAWindow) && (!FinalReservation || _reservationTable.IntersectionFree(NodeTo2D(n), NodeTime[n], double.PositiveInfinity)))
            {
                GoalNode = n;
                // Branch-and-bound: tighten upper bound on goal energy.
                // Since g = NodeEnergy is cumulative, any future expansion with
                // g(n') + h(n') >= _upperBound cannot improve => safe to prune.
                if (NodeEnergy[n] < _upperBound)
                    _upperBound = NodeEnergy[n];
            }

            //base will check goal node
            return base.StopCondition(n);
        }

        /// <summary>
        /// Successors of the specified n.
        /// Example:
        ///  (i)                    (a')
        ///   |                      |
        ///   v                      v
        ///  (i+1)-->(i+2)-->(n)-->(i+3)-->(i+4)-->(i+5)
        ///                   |      |
        ///                   v      v
        ///                 (i+5)  (i+6)
        ///  Given:
        ///  - n:   the current node
        ///  - i+1: the node of the last turn
        ///  - a' : an other agent that crossed (i+3)
        ///  - i+3: if we would stop here the agent (a') would crash with the current agent. Even if we would directly start again. We have to pass (i+3) asap, to get over it before a'.
        /// 
        ///  Expected result:
        ///  - (n) is a wait successor: backpointer := n | time := time[n] + wait time
        ///  - (i+5) is a successor: backpointer := n | time := time[n] + turn time 90° + drive time one hop
        ///  - (i+4) is a successor because it is the nearest free node to (n) in this direction: backpointer := (i+1) | time := time[i+1] + turn time 90° + drive time 4 hops
        ///</summary>
        /// <param name="n">The n.</param>
        /// <returns>
        /// Successors
        /// </returns>
        protected override IEnumerable<int> Successors(int n)
        {
            // Optional low-level timeout (default long.MaxValue => never triggers)
            if (_searchTimer.ElapsedMilliseconds > _searchTimeoutMs)
                yield break;

            // Branch-and-bound upper-bound pruning.
            // f(n) = g(n) + h(n) is a lower bound on any goal energy reachable from n.
            // If f(n) >= UB, no descendant can improve the best-known goal => safe prune.
            if (NodeEnergy[n] + h(n) >= _upperBound)
                yield break;

            // Keep track whether at least one successor was generated
            bool successorGenerated = false;

            // Pareto bi-criteria dominance pruning on (t, E) per (node2d, dir).
            // This replaces CBS-style time-monotonic tie-breaking (which is unsound
            // for energy A*) and naive energy-dominance (which over-prunes earlier
            // arrivals that avoid future reservation conflicts).
            // Only strictly-dominated labels are pruned => optimality preserved.
            if (_tieBreaking)
            {
                int node2dHere = NodeTo2D(n);
                short dirHere = GetLastStopAngleAfterTurn(n);
                if (CheckAndAddParetoLabel(node2dHere, dirHere, NodeTime[n], NodeEnergy[n]))
                    yield break;
            }

            List<int> checkPointNodes = new List<int>();
            List<double> checkPointDistances = new List<double>();
            List<double> checkPointTimes;

            //wait successor
            if (_reservationTable.IntersectionFree(NodeTo2D(n), NodeTime[n], NodeTime[n] + _lengthOfAWaitStep))
            {
                //add successor
                successorGenerated = true;
                NodeTime.Add(NodeTime[n] + _lengthOfAWaitStep);
                NodeEnergy.Add(NodeEnergy[n] + EnergyModel.ComputeWaitEnergy(0, _lengthOfAWaitStep));
                NodeBackpointerId.Add(n);
                NodeBackpointerLastStopId.Add(_numNodeId);
                NodeBackpointerEdge.Add(null);
                yield return _numNodeId;
                _numNodeId++;
            }

            if (FinalReservation && NodeTime[n] >= _lengthOfAWindow)
            {
                //only generate wait nodes
                yield break;
            }

            //just create wait successors if possible
            if (WaitStepsBeforeStart > 0 && successorGenerated)
            {
                WaitStepsBeforeStart--;
                yield break;
            }

            //current angle
            short lastStopAngleAfterTurn = GetLastStopAngleAfterTurn(n);

            //for each direction
            foreach (var direction in _graph.Edges[NodeTo2D(n)])
            {

                //clear temporary data structures
                NodeTimeTemp.Clear();
                NodeEnergyTemp.Clear();
                NodeBackpointerIdTemp.Clear();
                NodeBackpointerLastTurnIdTemp.Clear();
                NodeBackpointerEdgeTemp.Clear();

                //initiate checkpoints
                checkPointTimes = null;
                checkPointNodes.Clear();
                checkPointDistances.Clear();

                //Our aim is to calculate the distance for a hop.
                //A hop is defined as follows: The longest distance between two nodes, where no turn is necessary.
                //The end of the hop is "node". In this method we search for the start ("lastTurnNode") and calculate the g as follows:
                //g(node) = time needed to get to "lastTurnNode" + time needed to turn + time needed to get to node

                //angle i had before my current angle: lastTurnAngle
                //my current angle: thisAngle
                //the angle i want to go to: direction.Angle


                int lastStopId = NodeBackpointerLastStopId[n];

                if (direction.Angle != lastStopAngleAfterTurn)
                {
                    //we turn at n
                    lastStopId = n;
                    checkPointDistances.Add(0.0);
                    checkPointNodes.Add(NodeTo2D(n));
                }
                else
                {

                    //get the intermediate checkpoints
                    AddCheckPointDistances(lastStopId, n, checkPointNodes, checkPointDistances);

                }

                //time needed to get to the target orientation
                //last stop angle after turn of the last stop <=> last stop angle before turn
                short lastStopAngleBeforeTurn = GetLastStopAngleAfterTurn(lastStopId);
                double timeToTurn = _agent.Physics.getTimeNeededToTurn(Graph.DegreeToRad(lastStopAngleBeforeTurn), Graph.DegreeToRad(direction.Angle));

                //check if there is enough free time to rotate in the direction
                if (timeToTurn > 0 && !_reservationTable.IntersectionFree(NodeTo2D(lastStopId), NodeTime[lastStopId], NodeTime[lastStopId] + timeToTurn))
                    continue;

                //generate successors 
                var currentNode = NodeTo2D(n);
                var agentAngle = direction.Angle;
                var backpointerNode = n;

                //get drive distance
                var driveDistance = checkPointDistances[checkPointDistances.Count - 1];
                int hopCells = (driveDistance < 1e-9) ? 0 : (int)Math.Round(driveDistance / _edgeLen);

                // turnE 與 supportTurn 在此方向內固定，移到 while 外計算一次
                double mTotal = _agent.CurrentEnergyState.TotalWeight;
                double turnE = 0.0;
                if (lastStopId == n)
                {
                    double turnRad = Math.Abs(Graph.DegreeToRad(lastStopAngleAfterTurn) - Graph.DegreeToRad(direction.Angle));
                    if (turnRad > Math.PI) turnRad = 2.0 * Math.PI - turnRad;
                    if (turnRad > 0.0)
                        turnE = EnergyModel.ComputeTurnEnergy(mTotal, turnRad, _agent.Physics.TurnSpeed);
                }
                double supportTurn = (timeToTurn > 0.0) ? EnergyModel.P_IDLE * timeToTurn : 0.0;

                var foundNext = true;
                var pathFree = false;
                //try to find the first node in this direction, which path is free
                while (foundNext && !pathFree)
                {
                    foundNext = false;

                    //search for next edges in same direction
                    foreach (var edge in _graph.Edges[currentNode])
                    {

                        if (Math.Abs(edge.Angle - agentAngle) < 2)
                        {
                            //skip blocked edges
                            if (edge.ToNodeInfo.IsLocked || (!_agent.CanGoThroughObstacles && edge.ToNodeInfo.IsObstacle))
                            {
                                //blocked
                                foundNext = false;
                                break;
                            }

                            //cumulate
                            driveDistance += edge.Distance;
                            hopCells++;

                            //add checkpoint
                            checkPointDistances.Add(driveDistance);
                            checkPointNodes.Add(edge.To);

                            //check whether it is intersection free
                            var timeToMove = _agent.Physics.getTimeNeededToMove(0f, NodeTime[lastStopId] + timeToTurn, driveDistance, checkPointDistances, out checkPointTimes);

                            //check if driving action is collision free
                            pathFree = _reservationTable.IntersectionFree(checkPointNodes, checkPointTimes, false);

                            // 查表計算 moveE（O(1)，不再重算物理積分）
                            int tableKey = (hopCells <= MaxHopCells) ? hopCells : MaxHopCells;
                            double moveE = mTotal * _moveEnergyPerKg[tableKey] + EnergyModel.P_IDLE * timeToMove;

                            //add node to temp => will be added, if a valid successor will be found
                            NodeTimeTemp.Add(NodeTime[lastStopId] + timeToTurn + timeToMove);
                            // BUG FIX: use NodeEnergy[lastStopId], NOT NodeEnergy[n].
                            // When n != lastStopId (continuing a straight hop), NodeEnergy[n]
                            // already includes ME[prev_cells]. Adding ME[k] on top double-counts.
                            // This mirrors SpaceTimeAStar: NodeTime[lastStopId] + timeToMove.
                            NodeEnergyTemp.Add(NodeEnergy[lastStopId] + turnE + supportTurn + moveE);
                            NodeBackpointerIdTemp.Add(backpointerNode);
                            NodeBackpointerLastTurnIdTemp.Add(lastStopId);
                            NodeBackpointerEdgeTemp.Add(edge);

                            if (pathFree)
                            {
                                //treat only the last one as successor
                                int succ = _numNodeId + NodeTimeTemp.Count - 1;
                                _numNodeId += NodeTimeTemp.Count;

                                //add temporary successors
                                NodeTime.AddRange(NodeTimeTemp);
                                NodeEnergy.AddRange(NodeEnergyTemp);
                                NodeBackpointerId.AddRange(NodeBackpointerIdTemp);
                                NodeBackpointerLastStopId.AddRange(NodeBackpointerLastTurnIdTemp);
                                NodeBackpointerEdge.AddRange(NodeBackpointerEdgeTemp);

                                // Return it
                                yield return succ;
                            }

                            backpointerNode = _numNodeId + NodeTimeTemp.Count - 1;
                            currentNode = edge.To;
                            foundNext = true;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds the check point distances from 3d nodeFrom to 3d nodeTo.
        /// Warning: No Stops allowed between the nodes.
        /// </summary>
        /// <param name="nodeFrom">The node from.</param>
        /// <param name="nodeTo">The node to.</param>
        /// <param name="checkPointNodes">The check point nodes.</param>
        /// <param name="checkPointDistances">The check point distances.</param>
        public void AddCheckPointDistances(int nodeFrom, int nodeTo, List<int> checkPointNodes, List<double> checkPointDistances)
        {
            //we turn at lastStopId
            var tmpNode = nodeTo;
            var driveDistance = 0.0;

            //skip all previous without angle change
            //search backwards to the last node where the agent has turned
            while (tmpNode != nodeFrom)
            {

                checkPointDistances.Add(driveDistance);
                checkPointNodes.Add(NodeTo2D(tmpNode));

                driveDistance += NodeBackpointerEdge[tmpNode].Distance;

                tmpNode = NodeBackpointerId[tmpNode];
            }
            //add checkpoint
            checkPointDistances.Add(driveDistance);
            checkPointNodes.Add(NodeTo2D(tmpNode));

            //correct the distances
            //we have to fix the order due to backwards search.
            for (int i = 0; i < checkPointDistances.Count; i++)
                checkPointDistances[i] = driveDistance - checkPointDistances[i];
            checkPointNodes.Reverse();
            checkPointDistances.Reverse();
        }

        /// <summary>
        /// Gets the angle of the last stop node after turning.
        /// </summary>
        /// <param name="n">The node.</param>
        /// <returns>angle after turning</returns>
        public short GetLastStopAngleAfterTurn(int n)
        {
            if (NodeBackpointerEdge[n] != null)
                return NodeBackpointerEdge[n].Angle;

            //n is a wait or start node
            var stopNode = n;

            //search for the stop node
            while (NodeBackpointerEdge[stopNode] == null)
            {
                //n is a start node
                if (NodeBackpointerId[stopNode] == -1)
                    return StartAngle;
                stopNode = NodeBackpointerId[stopNode];
            }

            //n was a wait node
            return NodeBackpointerEdge[stopNode].Angle;

        }

        /// <summary>
        /// Convert a node id into the corresponding node id of the graph.
        /// </summary>
        /// <param name="node3d">generated node of WHCA*</param>
        /// <returns>graph node</returns>
        public int NodeTo2D(int node3d)
        {
            var tmpNode = node3d;

            //skip wait nodes
            while (NodeBackpointerEdge[tmpNode] == null)
            {
                if (tmpNode == 0)
                    return _agent.NextNode;
                tmpNode = NodeBackpointerId[tmpNode];
            }

            return NodeBackpointerEdge[tmpNode].To;
        }

        /// <summary>
        /// Adds the WHCA* start nodes.
        /// </summary>
        /// <param name="aStar">a star.</param>
        /// <param name="physics">The physics.</param>
        /// <param name="path">The path.</param>
        /// <param name="reservations">The reservations.</param>
        public void GetPathAndReservations(ref Path path, out List<ReservationTable.Interval> reservations)
        {
            GetPathAndReservations(ref path, out reservations, GoalNode, 0.0);
        }

        /// <summary>
        /// Adds the WHCA* start nodes.
        /// </summary>
        /// <param name="aStar">a star.</param>
        /// <param name="physics">The physics.</param>
        /// <param name="path">The path.</param>
        /// <param name="reservations">The reservations.</param>
        public void GetPathAndReservations(ref Path path, out List<ReservationTable.Interval> reservations, int specifiedNode, double startTime)
        {
            //set Path
            if (path != null)
                path.Clear();
            var node = specifiedNode;
            reservations = new List<ReservationTable.Interval>();

            //add path determined by WHCA*
            while (node >= 0)
            {
                //create action
                var waitTime = 0.0;

                //wait time = stay on one place
                while (node == NodeBackpointerLastStopId[node])
                {
                    if (node == 0)
                        break;

                    waitTime += NodeTime[node] - NodeTime[NodeBackpointerId[node]];
                    node = NodeBackpointerId[node];
                }

                //add wait time to reservation table
                if (waitTime > 0)
                    reservations.Insert(0, new ReservationTable.Interval(NodeTo2D(node), NodeTime[node], NodeTime[node] + waitTime));

                //add action
                if (path != null)
                    path.AddFirst(NodeTo2D(node), true, waitTime);

                //we got to the start node due to wait steps => leave
                if (node == 0)
                    break;

                //get checkpoints
                List<double> checkPointTimes = null;
                var checkpointDistances = new List<double>();
                var checkpointNodes = new List<int>();

                AddCheckPointDistances(NodeBackpointerLastStopId[node], node, checkpointNodes, checkpointDistances);
                var turnTime = _agent.Physics.getTimeNeededToTurn(Graph.DegreeToRad(GetLastStopAngleAfterTurn(node)), Graph.DegreeToRad(GetLastStopAngleAfterTurn(NodeBackpointerLastStopId[node])));
                _agent.Physics.getTimeNeededToMove(0, NodeTime[NodeBackpointerLastStopId[node]] + turnTime, checkpointDistances[checkpointDistances.Count - 1], checkpointDistances, out checkPointTimes);

                for (int i = checkpointNodes.Count - 1; i >= 0; i--)
                {
                    if (i == 0)
                        reservations.Insert(0, new ReservationTable.Interval(checkpointNodes[i], NodeTime[NodeBackpointerLastStopId[node]], checkPointTimes[i + 1])); //you can not take checkPointTimes[i] because the checkPointTimes include the turn
                    else if (i == checkpointNodes.Count - 1)
                        reservations.Insert(0, new ReservationTable.Interval(checkpointNodes[i], checkPointTimes[i - 1], checkPointTimes[i]));
                    else
                        reservations.Insert(0, new ReservationTable.Interval(checkpointNodes[i], checkPointTimes[i - 1], checkPointTimes[i + 1]));

                    //create action for intermediate nodes
                    if (path != null && 0 < i && i < checkpointNodes.Count - 1)
                        path.AddFirst(checkpointNodes[i], false, 0.0);

                    //stop condition
                    if (reservations[0].Start < startTime)
                        break;
                }

                //set next node
                node = NodeBackpointerLastStopId[node];

                //stop condition
                if (reservations[0].Start < startTime)
                    break;
            }

            //rounding differences
            for (int i = reservations.Count - 2; i >= 0; i--)
            {
                if (reservations[i].Node == reservations[i + 1].Node && Math.Abs(reservations[i].End - reservations[i + 1].Start) < 0.001)
                {
                    reservations[i].End = reservations[i + 1].End;
                    reservations.RemoveAt(i + 1);
                }
            }

        }

        /// <summary>
        /// Diagnostic: dump the final search path (start → GoalNode) with per-step time/energy.
        /// Backtrack via NodeBackpointerId from GoalNode; print in forward order.
        /// Zero overhead when logger disabled (early exit).
        /// </summary>
        public void DumpPathDiagnostic(int agentId, double currentTime, string tag)
        {
            if (!PathDiagnosticLogger.Enabled || GoalNode < 0) return;

            // Backtrack from GoalNode → start (node 0)
            var seq = new List<int>();
            int cur = GoalNode;
            int guard = 0;
            while (cur >= 0 && guard++ < 1000000)
            {
                seq.Add(cur);
                if (cur == 0) break;
                cur = NodeBackpointerId[cur];
            }
            seq.Reverse();

            var sb = new StringBuilder();
            double totalT = NodeTime[GoalNode];
            double totalE = NodeEnergy[GoalNode];
            sb.AppendLine(
                $"{tag}|agent={agentId}|currentTime={currentTime:F3}" +
                $"|start={_agent.NextNode}|goal={_agent.DestinationNode}" +
                $"|totalTime={totalT:F3}|totalEnergy={totalE:F3}|steps={seq.Count}");

            for (int i = 0; i < seq.Count; i++)
            {
                int n = seq[i];
                int n2d = NodeTo2D(n);
                double t = NodeTime[n];
                double e = NodeEnergy[n];
                double dt = i == 0 ? 0.0 : t - NodeTime[seq[i - 1]];
                double de = i == 0 ? 0.0 : e - NodeEnergy[seq[i - 1]];
                int lastStop = NodeBackpointerLastStopId[n];
                short angle = GetLastStopAngleAfterTurn(n);

                // Classify action
                string act;
                if (n == 0) act = "START";
                else if (NodeBackpointerEdge[n] == null) act = "WAIT"; // wait successor has null edge
                else if (lastStop == n) act = "TURN+MOVE"; // turned at n then moved
                else act = "MOVE";

                sb.AppendLine(
                    $"  step={i} node2d={n2d} t={t:F3} e={e:F3} dt={dt:F3} de={de:F3}" +
                    $" act={act} angle={angle} lastStop={lastStop}");
            }

            PathDiagnosticLogger.WriteLine(sb.ToString());
        }
    }
}
