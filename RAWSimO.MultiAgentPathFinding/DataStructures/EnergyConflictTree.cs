using RAWSimO.MultiAgentPathFinding.Elements;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.MultiAgentPathFinding.DataStructures
{
    /// <summary>
    /// Energy-aware conflict tree for ECBS.
    /// Identical to ConflictTree except SolutionCost accumulates energy (Joules)
    /// instead of time spans. CBS is unaffected — it still uses ConflictTree.
    /// </summary>
    public class EnergyConflictTree
    {
        public Node Root { get; set; }

        public int Size
        {
            get
            {
                var size = 0;
                var stack = new Stack<Node>();
                stack.Push(Root);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    size++;
                    foreach (var child in current.Children)
                        if (child != null)
                            stack.Push(child);
                }
                return size;
            }
        }

        public EnergyConflictTree(int rootChildrenCount = 2)
        {
            Root = new Node(-1, null, null, rootChildrenCount);
        }

        #region Node
        public class Node
        {
            public Node Parent;
            public Node[] Children;
            public ReservationTable.Interval IntervalConstraint;

            private Dictionary<int, Path> _solution;
            private Dictionary<int, List<ReservationTable.Interval>> _reservation;

            /// <summary>
            /// Per-agent energy cost in Joules. Used to compute SolutionCost.
            /// </summary>
            private Dictionary<int, double> _agentEnergy;

            /// <summary>
            /// Sum of individual agent energy costs (Joules). High-level ECBS
            /// selects the node with lowest SolutionCost → least total energy.
            /// </summary>
            public double SolutionCost;

            public bool SolutionValid;
            public int AgentId;
            public int Depth;

            public Node(int agentId, ReservationTable.Interval constraint, Node parent, int childrenCount = 2)
            {
                Children = new Node[childrenCount];
                AgentId = agentId;
                IntervalConstraint = constraint;
                _solution = new Dictionary<int, Path>();
                _reservation = new Dictionary<int, List<ReservationTable.Interval>>();
                _agentEnergy = new Dictionary<int, double>();

                if (parent != null)
                {
                    parent._addChild(this);
                    Parent = parent;
                    Depth = parent.Depth + 1;
                }
                else
                {
                    Depth = 0;
                }

                Debug.Assert(validate());
            }

            private void _addChild(Node child)
            {
                for (int i = 0; i < Children.Length; i++)
                {
                    if (Children[i] == null)
                    {
                        Children[i] = child;
                        break;
                    }
                }
            }

            /// <summary>
            /// Original time-based overload — not used by ECBS, kept for API compatibility.
            /// </summary>
            public void setSolution(int agentId, Path path, List<ReservationTable.Interval> intervals)
            {
                setSolution(agentId, path, intervals, 0.0);
            }

            /// <summary>
            /// Energy-aware overload. SolutionCost = sum of agent energy costs (J).
            /// </summary>
            public void setSolution(int agentId, Path path, List<ReservationTable.Interval> intervals, double energyCostJ)
            {
                _solution[agentId] = path;
                _reservation[agentId] = intervals;
                _agentEnergy[agentId] = energyCostJ;

                if (Parent == null)
                {
                    // Root: sum over all agents already stored
                    SolutionCost = 0.0;
                    foreach (var key in _agentEnergy.Keys)
                        SolutionCost += _agentEnergy[key];
                }
                else
                {
                    // Child: inherit parent cost, subtract old agent cost, add new
                    double oldCost = Parent._getEnergyCostOf(agentId);
                    SolutionCost = Parent.SolutionCost - oldCost + energyCostJ;
                }
            }

            private double _getEnergyCostOf(int agentId)
            {
                var node = this;
                while (node != null)
                {
                    if (node._agentEnergy.ContainsKey(agentId))
                        return node._agentEnergy[agentId];
                    node = node.Parent;
                }
                return 0.0;
            }

            public Path getSolution(int agentId)
            {
                var currentNode = this;
                while (currentNode != null)
                {
                    if (currentNode._solution.ContainsKey(agentId))
                        return currentNode._solution[agentId];
                    Debug.Assert(currentNode.AgentId != agentId);
                    currentNode = currentNode.Parent;
                }
                return new Path();
            }

            public List<ReservationTable.Interval> getReservation(int agentId)
            {
                var currentNode = this;
                while (currentNode != null)
                {
                    if (currentNode._reservation.ContainsKey(agentId))
                        return currentNode._reservation[agentId];
                    Debug.Assert(currentNode.AgentId != agentId);
                    currentNode = currentNode.Parent;
                }
                return null;
            }

            public IEnumerable<EnergyConflictTree.Node> getConstraints(int agentId)
            {
                return new ConstraintCollector(this, agentId);
            }

            public string printStack()
            {
                var b = new StringBuilder();
                var current = this;
                do
                {
                    if (current.IntervalConstraint != null)
                        b.Append("Agent ").Append(current.AgentId)
                         .Append(": (").Append(current.IntervalConstraint.Node).Append(") ")
                         .Append(current.IntervalConstraint.Start).Append(" - ")
                         .Append(current.IntervalConstraint.End).Append(Environment.NewLine);
                    current = current.Parent;
                } while (current != null);
                return b.ToString();
            }

            public bool validate()
            {
                if (AgentId == -1) return true;

                var maxNode = 0;
                foreach (var reservation in getConstraints(AgentId))
                    maxNode = Math.Max(maxNode, reservation.IntervalConstraint.Node);

                var table = new ReservationTable(new Graph(maxNode + 1));
                try
                {
                    foreach (var reservation in getConstraints(AgentId))
                        table.Add(reservation.IntervalConstraint);
                }
                catch (DisjointIntervalTree.IntervalIntersectionException)
                {
                    return false;
                }
                return true;
            }

            #region Enumerable
            public class ConstraintCollector : IEnumerable<EnergyConflictTree.Node>
            {
                private Node _startNode;
                private int _agentId;

                public ConstraintCollector(Node conflictNode, int agentId)
                {
                    _startNode = conflictNode;
                    _agentId = agentId;
                }

                public IEnumerator<EnergyConflictTree.Node> GetEnumerator()
                    => new ConstraintEnumerator(_startNode, _agentId);

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

                public class ConstraintEnumerator : IEnumerator<EnergyConflictTree.Node>
                {
                    private Node _startNode;
                    private Node _currentNode;
                    private int _agentId;
                    private bool _firstCall;

                    public ConstraintEnumerator(Node conflictNode, int agentId)
                    {
                        _currentNode = conflictNode;
                        _startNode = conflictNode;
                        _agentId = agentId;
                        _firstCall = true;
                    }

                    public EnergyConflictTree.Node Current => _currentNode;
                    object IEnumerator.Current => _currentNode;
                    public void Dispose() { }

                    public bool MoveNext()
                    {
                        if (_currentNode == null) return false;
                        if (_firstCall) _firstCall = false;
                        else _currentNode = _currentNode.Parent;

                        while (_currentNode != null && _currentNode.AgentId != _agentId)
                            _currentNode = _currentNode.Parent;

                        return _currentNode != null;
                    }

                    public void Reset()
                    {
                        _currentNode = _startNode;
                        _firstCall = true;
                    }
                }
            }
            #endregion
        }
        #endregion
    }
}
