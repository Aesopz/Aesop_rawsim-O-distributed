using RAWSimO.MultiAgentPathFinding.Algorithms.AStar;
using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Elements;
using RAWSimO.MultiAgentPathFinding.Physic;
using RAWSimO.MultiAgentPathFinding.Toolbox;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace RAWSimO.MultiAgentPathFinding.Methods
{

    /// <summary>
    /// Energy-aware Conflict-based Search (ECBS), based on CBS (Sharon 2015)
    /// </summary>
    public class ECBSMethod : PathFinder
    {
        /// <summary>
        /// The search method for node selection
        /// </summary>
        public ECBSSearchMethod SearchMethod = ECBSSearchMethod.BestFirst;

        /// <summary>
        /// The reservation table for finding a way through constraints
        /// </summary>
        private ReservationTable _reservationTable;

        /// <summary>
        /// The reservation table for collision detection
        /// </summary>
        ReservationTable _agentReservationTable;

        /// <summary>
        /// Lambda Express for node selection
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns></returns>
        delegate double NodeSelectionExpression(EnergyConflictTree.Node node);

        /// <summary>
        /// The deadlock handler
        /// </summary>
        private DeadlockHandler _deadlockHandler;

        /// <summary>
        /// Initializes a new instance of the <see cref="ECBSMethod"/> class.
        /// </summary>
        /// <param name="graph">graph</param>
        /// <param name="seed">The seed to use for the randomizer.</param>
        /// <param name="logger">The logger to use.</param>
        public ECBSMethod(Graph graph, int seed, PathPlanningCommunicator logger)
            : base(graph, seed, logger)
        {
            if (graph.BackwardEdges == null)
                graph.GenerateBackwardEgdes();
            _reservationTable = new ReservationTable(graph);
            _agentReservationTable = new ReservationTable(graph, false, true, false);
            _deadlockHandler = new DeadlockHandler(graph, seed);
        }

        /// <summary>
        /// Find the path for all the agents.
        /// </summary>
        /// <param name="currentTime">The current Time.</param>
        /// <param name="agents">agents</param>
        // Static counter: limits diagnostic dumps to first N FindPaths() calls.
        // Enabled when PathDiagnosticLogger is active via RMFS_PATH_DIAG_FILE env var.
        private static int _diagFindPathsCount = 0;

        /// <summary>
        /// Cumulative number of FindPaths() invocations that hit the high-level timeout
        /// (i.e. returned a fallback best-LB node instead of a conflict-free optimum).
        /// Exposed so experiments can gauge how often ECBS was forced into suboptimal fallback.
        /// </summary>
        public int StatHighLevelTimeouts { get; private set; } = 0;

        /// <summary>
        /// Cumulative number of times the deadlock handler mutated an agent path via RandomHop
        /// after high-level search completed. These mutations break the correspondence between
        /// bestNode.SolutionCost and the actually executed path, so they should be tracked.
        /// </summary>
        public int StatRandomHopMutations { get; private set; } = 0;

        /// <summary>
        /// When true, suppress the post-search RandomHop deadlock mutation so that the executed
        /// path exactly matches the high-level bestNode solution. Useful for validating the
        /// algorithm itself (CT SolutionCost == executed energy). Default: false (keep safety net).
        /// Default: true (disabled). Set env var RMFS_ECBS_ENABLE_RANDOMHOP=1 to re-enable.
        /// </summary>
        public bool DisableRandomHopMutation { get; set; } =
            Environment.GetEnvironmentVariable("RMFS_ECBS_ENABLE_RANDOMHOP") != "1";

        public override void FindPaths(double currentTime, List<Agent> agents)
        {
            Stopwatch.Restart();

            // Diagnostic: only dump root-level Solve on first N FindPaths() calls
            bool shouldDiag = PathDiagnosticLogger.Enabled &&
                              Interlocked.Increment(ref _diagFindPathsCount) <= PathDiagnosticLogger.MaxCalls;
            if (shouldDiag)
                PathDiagnosticLogger.WriteLine(
                    $"=== ECBS FindPaths #{_diagFindPathsCount} currentTime={currentTime:F3} agents={agents.Count} ===");

            //initialization data structures
            var conflictTree = new EnergyConflictTree();
            var Open = new FibonacciHeap<double, EnergyConflictTree.Node>();
            var solvable = true;
            var generatedNodes = 0;
            EnergyConflictTree.Node bestNode = null;

            //deadlock handling
            _deadlockHandler.LengthOfAWaitStep = LengthOfAWaitStep;
            _deadlockHandler.MaximumWaitTime = 30;
            _deadlockHandler.Update(agents, currentTime);

            //simply blocked
            foreach (var agent in agents.Where(a => a.FixedPosition))
                Graph.NodeInfo[agent.NextNode].IsLocked = true;

            // TODO this only works as long as a possible solution is guaranteed - maybe instead ignore paths to plan for agents with no possible solution and hope that it clears by others moving on?
            //first node initialization
            List<Agent> unsolvableAgents = null;
            foreach (var agent in agents.Where(a => !a.FixedPosition))
            {
                bool agentSolved = Solve(conflictTree.Root, currentTime, agent, shouldDiag);
                if (!agentSolved)
                {
                    if (unsolvableAgents == null)
                        unsolvableAgents = new List<Agent>() { agent };
                    else
                        unsolvableAgents.Add(agent);
                }
                solvable = solvable && agentSolved;
            }

            //node selection strategy (Queue will pick the node with minimum value
            NodeSelectionExpression nodeObjectiveSelector = node =>
            {
                switch (SearchMethod)
                {
                    case ECBSSearchMethod.BestFirst:
                        return node.SolutionCost;
                    case ECBSSearchMethod.BreathFirst:
                        return node.Depth;
                    case ECBSSearchMethod.DepthFirst:
                        return (-1) * node.Depth;
                    default:
                        return 0;
                }
            };

            //Enqueue first node
            if (solvable)
                Open.Enqueue(nodeObjectiveSelector(conflictTree.Root), conflictTree.Root);
            else
                Communicator.LogDefault("WARNING! Aborting ECBS - could not obtain an initial solution for the following agents: " +
                    string.Join(",", unsolvableAgents.Select(a => "Agent" + a.ID.ToString() + "(" + a.NextNode.ToString() + "->" + a.DestinationNode.ToString() + ")")));
            bestNode = conflictTree.Root;

            //search loop
            EnergyConflictTree.Node p = conflictTree.Root;
            bool timedOut = false;
            while (Open.Count > 0)
            {

                //local variables
                int agentId1;
                int agentId2;
                ReservationTable.Interval interval;

                //pop out best node
                p = Open.Dequeue().Value;

                //check the path
                var hasNoConflicts = ValidatePath(p, agents, out agentId1, out agentId2, out interval);

                //has no conflicts? => optimal: this is the lowest SolutionCost conflict-free node
                if (hasNoConflicts)
                {
                    bestNode = p;
                    break;
                }

                // time up? => return the best-LB fallback (current pop has lowest SolutionCost among remaining)
                if (Stopwatch.ElapsedMilliseconds / 1000.0 > RuntimeLimitPerAgent * agents.Count * 0.9 || Stopwatch.ElapsedMilliseconds / 1000.0 > RunTimeLimitOverall)
                {
                    // NOTE: timeout fallback `bestNode = p` only guarantees p is minimum SolutionCost
                    // under BestFirst search order. DepthFirst/BreadthFirst do NOT dequeue by cost.
                    if (SearchMethod != ECBSSearchMethod.BestFirst)
                        Communicator.LogDefault("WARNING: ECBS timeout fallback assumes BestFirst search order; current mode=" + SearchMethod);
                    Communicator.SignalTimeout();
                    // FIX: previously bestNode was tracked by `interval.Start > bestTime`, which
                    // selected the node whose first conflict occurred latest -- NOT the lowest-energy
                    // feasible solution. Under Best-First by SolutionCost, the node currently popped
                    // (p) is the minimum-cost candidate remaining in Open. Use it as the fallback.
                    bestNode = p;
                    timedOut = true;
                    StatHighLevelTimeouts++;
                    break;
                }

                //append child 1
                var node1 = new EnergyConflictTree.Node(agentId1, interval, p);
                solvable = Solve(node1, currentTime, agents.First(a => a.ID == agentId1));
                if (solvable)
                    Open.Enqueue(nodeObjectiveSelector(node1), node1);

                //append child 2
                var node2 = new EnergyConflictTree.Node(agentId2, interval, p);
                solvable = Solve(node2, currentTime, agents.First(a => a.ID == agentId2));
                if (solvable)
                    Open.Enqueue(nodeObjectiveSelector(node2), node2);

                generatedNodes += 2;

            }

            // Emit a brief search summary so experiments can track timeout frequency.
            // Keep the message short to avoid log bloat on frequent FindPaths() calls.
            if (timedOut)
                Communicator.LogDefault($"ECBS timeout #{StatHighLevelTimeouts} fallback=bestLB SolutionCost={bestNode.SolutionCost:F2} generated={generatedNodes}");

            //return the solution => suboptimal (may still contain conflicts if timed out)
            int hopCountThisCall = 0;
            foreach (var agent in agents)
            {
                agent.Path = bestNode.getSolution(agent.ID);
                if (_deadlockHandler.IsInDeadlock(agent, currentTime))
                {
                    // RandomHop mutates agent.Path post-search, which decouples the executed
                    // trajectory from bestNode.SolutionCost. We count every mutation so the
                    // experimenter can see how much the deadlock handler distorts comparisons.
                    StatRandomHopMutations++;
                    hopCountThisCall++;
                    if (!DisableRandomHopMutation)
                        _deadlockHandler.RandomHop(agent);
                }
            }
            if (hopCountThisCall > 0)
                Communicator.LogDefault($"ECBS RandomHop mutated {hopCountThisCall}/{agents.Count} agent paths" +
                    (DisableRandomHopMutation ? " [SUPPRESSED]" : "") +
                    $" (cumulative={StatRandomHopMutations})");
        }

        private bool ValidatePath(EnergyConflictTree.Node node, List<Agent> agents, out int agentId1, out int agentId2, out ReservationTable.Interval interval)
        {
            //clear
            _agentReservationTable.Clear();

            //add next hop reservations
            foreach (var agent in agents.Where(a => !a.FixedPosition))
                _agentReservationTable.Add(agent.ReservationsToNextNode, agent.ID);

            //get all reservations sorted
            var reservations = new FibonacciHeap<double, Tuple<Agent, ReservationTable.Interval>>();
            foreach (var agent in agents.Where(a => !a.FixedPosition))
                foreach (var reservation in node.getReservation(agent.ID))
                    reservations.Enqueue(reservation.Start, Tuple.Create(agent, reservation));

            //check all reservations
            while (reservations.Count > 0)
            {
                var reservation = reservations.Dequeue().Value;

                int collideWithAgentId;
                var intersectionFree = _agentReservationTable.IntersectionFree(reservation.Item2, out collideWithAgentId);
                if (!intersectionFree)
                {
                    agentId1 = collideWithAgentId;
                    agentId2 = reservation.Item1.ID;
                    interval = _agentReservationTable.GetOverlappingInterval(reservation.Item2);
                    if (interval.End - interval.Start > ReservationTable.TOLERANCE)
                        return false;
                }
                else
                {
                    _agentReservationTable.Add(reservation.Item2, reservation.Item1.ID);
                }
            }

            agentId1 = -1;
            agentId2 = -1;
            interval = null;
            return true;
        }


        /// <summary>
        /// Solves the specified node.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="currentTime">The current time.</param>
        /// <param name="agent">The agent.</param>
        /// <param name="obstacleNodes">The obstacle nodes.</param>
        /// <param name="lockedNodes">The locked nodes.</param>
        /// <returns></returns>
        private bool Solve(EnergyConflictTree.Node node, double currentTime, Agent agent, bool diagDump = false)
        {
            //clear reservation table
            _reservationTable.Clear();

            //add constraints (agentId = -1 => Intervals from the tree)
            foreach (var constraint in node.getConstraints(agent.ID))
                _reservationTable.Add(constraint.IntervalConstraint);

            //drive to next node must be possible - otherwise it is not a valid node
            foreach (var reservation in agent.ReservationsToNextNode)
                if (!_reservationTable.IntersectionFree(reservation))
                    return false;

            //We can use WHCA Star here in a low level approach.
            //Window = Infinitively long
            var rraStar = new ReverseResumableAStar(Graph, agent, agent.Physics, agent.DestinationNode);
            var aStar = new ESpaceTimeAStar(Graph, LengthOfAWaitStep, double.PositiveInfinity, _reservationTable, agent, rraStar);

            // Low-level timeout: 10% of per-agent high-level budget.
            // Prevents a single low-level A* from monopolizing the entire time budget.
            if (RuntimeLimitPerAgent > 0)
                aStar.SearchTimeoutMilliseconds = (long)(RuntimeLimitPerAgent * 1000 * 0.1);

            //execute
            var found = aStar.Search();

            //+ WHCA* Nodes
            List<ReservationTable.Interval> reservations;
            Path path = new Path();
            if (found)
            {
                //add all WHCA Nodes
                aStar.GetPathAndReservations(ref path, out reservations);

#if DEBUG
                foreach (var reservation in reservations)
                    Debug.Assert(_reservationTable.IntersectionFree(reservation));
#endif

                //add the next node again
                if (path.Count == 0 || path.NextAction.Node != agent.NextNode || path.NextAction.StopAtNode == false)
                    path.AddFirst(agent.NextNode, true, 0);

                double energyCost = aStar.NodeEnergy[aStar.GoalNode];
                node.setSolution(agent.ID, path, reservations, energyCost);

                // Diagnostic: dump this agent's ECBS path (root-level only, controlled by caller)
                if (diagDump)
                    aStar.DumpPathDiagnostic(agent.ID, currentTime, "ECBS");

                //found
                return true;

            }
            else
            {
                //not found
                return false;
            }


        }

        /// <summary>
        /// Strategy for Node Selection
        /// </summary>
        [Serializable]
        public enum ECBSSearchMethod
        {
            BestFirst,
            DepthFirst,
            BreathFirst
        }

    }
}
