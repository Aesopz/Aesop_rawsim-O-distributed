using RAWSimO.MultiAgentPathFinding.DataStructures;
using RAWSimO.MultiAgentPathFinding.Physic;
using System;
using System.Collections.Generic;

namespace RAWSimO.MultiAgentPathFinding.Elements
{
    /// <summary>
    /// The Agent has a start node and a destination node
    /// </summary>
    public class Agent
    {
        /// <summary>
        /// The identifier
        /// </summary>
        public int ID;

        /// <summary>
        /// next node
        /// </summary>
        public int NextNode;

        /// <summary>
        /// passed nodes to next node
        /// </summary>
        public List<ReservationTable.Interval> ReservationsToNextNode;

        /// <summary>
        /// Time of arrival at next node
        /// </summary>
        public double ArrivalTimeAtNextNode;

        /// <summary>
        /// Start orientation.
        /// </summary>
        public double OrientationAtNextNode;

        /// <summary>
        /// Destination node.
        /// </summary>
        public int DestinationNode;

        /// <summary>
        /// The final destination of the agent. This might differ from the destination node in case the bot is send to an area managed by a queue.
        /// </summary>
        public int FinalDestinationNode;

        /// <summary>
        /// Agent has a fixed position.
        /// </summary>
        public bool FixedPosition;

        /// <summary>
        /// Agent is resting.
        /// </summary>
        public bool Resting;

        /// <summary>
        /// Agent can go throw obstacles.
        /// </summary>
        public bool CanGoThroughObstacles;

        /// <summary>
        /// Agent has a physics class.
        /// </summary>
        public Physics Physics;

        /// <summary>
        /// The calculated path of the agent
        /// </summary>
        public Path Path;

        /// <summary>
        /// The agent requests a re optimization
        /// </summary>
        public bool RequestReoptimization;

        /// <summary>
        /// Indicates whether the bot is currently queueing.
        /// </summary>
        public bool Queueing;

        /// <summary>
        /// Energy-related current state of the robot.
        /// </summary>
        public EnergyState CurrentEnergyState = new EnergyState();

        /// <summary>
        /// Scalar protection score (PS) for VO-LWHCAn\*. 0 for non-VO-LWHCAn\* methods.
        /// Populated by VoLwhcaStarPathManager from value, load, urgency, and aging components.
        /// Higher PS means "this agent should be protected from yielding" in priority sort.
        /// </summary>
        public double ProtectionScore = 0.0;

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        public override string ToString()
        {
            return "Agent" + this.ID;
        }

        public class EnergyState //aesop energy CBS
        {
            /// <summary>
            /// Whether the robot is currently carrying a pod.
            /// </summary>
            public bool CarryingPod;

            /// <summary>
            /// Robot self weight in kg.
            /// </summary>
            public double RobotWeight;

            /// <summary>
            /// Current payload weight in kg.
            /// </summary>
            public double PayloadWeight;

            /// <summary>
            /// Total current mass used by the energy model.
            /// </summary>
            public double TotalWeight => RobotWeight + PayloadWeight;
        }

        #region Debug fields

        /// <summary>
        /// The destination object of the bot.
        /// </summary>
        public object DestinationNodeObject;

        /// <summary>
        /// The next node object of the bot.
        /// </summary>
        public object NextNodeObject;

        #endregion
    }
}