using RAWSimO.Core.IO;
using RAWSimO.MultiAgentPathFinding.Methods;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RAWSimO.Core.Configurations
{
    #region Path planning configurations

    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class SimplePathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.Simple; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppS" +
                (SimpleWaitingEnabled ? "t" : "f") +
                (SimpleWaitingExtendedEnabled ? "t" : "f") +
                (SimpleWaitingD2Enabled ? "t" : "f") +
                (PingPongWaitingEnabled ? "t" : "f");
        }
        /// <summary>
        /// Enables or disables bots simply waiting for the next waypoint to be clear before advancing.
        /// </summary>
        public bool SimpleWaitingEnabled = true;

        /// <summary>
        /// Enables or disables bots blocking the waypoint they just left.
        /// </summary>
        public bool SimpleWaitingExtendedEnabled = true;

        /// <summary>
        /// Enables or disables waiting for bots approaching the after next waypoint.
        /// </summary>
        public bool SimpleWaitingD2Enabled = true;

        /// <summary>
        /// Enables or disables random waiting after a ping pong (bot moving back and forth between two waypoints) occurred.
        /// </summary>
        public bool PingPongWaitingEnabled = true;

        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            SimpleWaitingEnabled = bool.Parse(args[2]);
            SimpleWaitingExtendedEnabled = bool.Parse(args[3]);
            SimpleWaitingD2Enabled = bool.Parse(args[4]);
            PingPongWaitingEnabled = bool.Parse(args[5]);
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class DummyPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.Dummy; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppD"; }
    };
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class WHCAvStarPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.WHCAvStar; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppWHCAV" + LengthOfAWindow.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER) + (AbortAtFirstConflict ? "t" : "f") + (UseDeadlockHandler ? "t" : "f"); }
        /// <summary>
        /// The length of a time space search window
        /// </summary>
        public double LengthOfAWindow = 30.0;
        /// <summary>
        /// Abort the search at first conflict
        /// </summary>
        public bool AbortAtFirstConflict = true;
        /// <summary>
        /// Indicates whether the method uses a deadlock handler.
        /// </summary>
        public bool UseDeadlockHandler = true;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            LengthOfAWindow = double.Parse(args[2], new CultureInfo("en"));
            AbortAtFirstConflict = bool.Parse(args[3]);
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (LengthOfAWindow <= 0)
            {
                errorMessage = "Problem with path planning configuration: LengthOfAWindow has to be > 0.";
                return false;
            }

            errorMessage = "";
            return true;

        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class WHCAnStarPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.WHCAnStar; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppWHCAN" + LengthOfAWindow.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER) + (UseBias ? "t" : "f") + (UseDeadlockHandler ? "t" : "f"); }
        /// <summary>
        /// The length of a time space search window
        /// </summary>
        public double LengthOfAWindow = 30.0;
        /// <summary>
        /// Indicates whether the method uses the biased cost pathfinding algorithm
        /// </summary>
        public bool UseBias = false;
        /// <summary>
        /// Indicates whether the method uses a deadlock handler.
        /// </summary>
        public bool UseDeadlockHandler = true;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            LengthOfAWindow = double.Parse(args[2], new CultureInfo("en"));
            UseBias = bool.Parse(args[3]);
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (LengthOfAWindow <= 0)
            {
                errorMessage = "Problem with path planning configuration: LengthOfAWindow has to be > 0.";
                return false;
            }

            errorMessage = "";
            return true;

        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class FARPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.FAR; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppFAR" +
                (evadingStrategy == FARMethod.EvadingStrategy.EvadeByRerouting ? "P" : evadingStrategy == FARMethod.EvadingStrategy.EvadeToNextNode ? "A" : "?") +
                MaximumNumberOfBreakingManeuverTries.ToString() +
                (NoBackEvading ? "t" : "f") +
                (UseDeadlockHandler ? "t" : "f");
        }
        /// <summary>
        /// The maximum number of breaking maneuver tries
        /// </summary>
        public int MaximumNumberOfBreakingManeuverTries = 2;
        /// <summary>
        /// The evading strategy
        /// </summary>
        public FARMethod.EvadingStrategy evadingStrategy;
        /// <summary>
        /// No evading to a node the bot already evaded from
        /// </summary>
        public bool NoBackEvading = true;
        /// <summary>
        /// Indicates whether the method uses a deadlock handler.
        /// </summary>
        public bool UseDeadlockHandler = true;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            MaximumNumberOfBreakingManeuverTries = int.Parse(args[2]);
            evadingStrategy = (FARMethod.EvadingStrategy)Enum.Parse(typeof(FARMethod.EvadingStrategy), (args[3]));
            NoBackEvading = bool.Parse(args[4]);
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (MaximumNumberOfBreakingManeuverTries < 0)
            {
                errorMessage = "Problem with path planning configuration: MaximumNumberOfBreakingManeuverTries has to be >= 0.";
                return false;
            }

            errorMessage = "";
            return true;

        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class CBSPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.CBS; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "ppCBS";
            switch (SearchMethod)
            {
                case CBSMethod.CBSSearchMethod.BestFirst: name += "O"; break;
                case CBSMethod.CBSSearchMethod.DepthFirst: name += "D"; break;
                case CBSMethod.CBSSearchMethod.BreathFirst: name += "B"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            return name;
        }
        /// <summary>
        /// The search method
        /// </summary>
        public CBSMethod.CBSSearchMethod SearchMethod = CBSMethod.CBSSearchMethod.BestFirst;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            SearchMethod = (CBSMethod.CBSSearchMethod)Enum.Parse(typeof(CBSMethod.CBSSearchMethod), (args[2]));
        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class BCPPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.BCP; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppBCP" + BiasedCostAmount.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER); }
        /// <summary>
        /// amount of biased cost in seconds
        /// </summary>
        public double BiasedCostAmount = 1;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            BiasedCostAmount = double.Parse(args[2], new CultureInfo("en"));
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (BiasedCostAmount < 0)       //<= 0 my be wise as well
            {
                errorMessage = "Problem with path planning configuration: BiasedCostAmount has to be >= 0.";
                return false;
            }
            errorMessage = "";
            return true;

        }

    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class PASPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.PAS; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppPAS" + MaxPriorities.ToString() + LengthOfAWindow.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER); }
        /// <summary>
        /// The maximum number of priorities
        /// </summary>
        public int MaxPriorities = 2;
        /// <summary>
        /// The length of a time space search window
        /// </summary>
        public double LengthOfAWindow = 30.0;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            MaxPriorities = int.Parse(args[2]);
            LengthOfAWindow = double.Parse(args[3], new CultureInfo("en"));
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (MaxPriorities < 0)      //<= 0 my be wise as well
            {
                errorMessage = "Problem with path planning configuration: MaxPriorities has to be >= 0.";
                return false;
            }
            if (LengthOfAWindow <= 0)
            {
                errorMessage = "Problem with path planning configuration: LengthOfAWindow has to be > 0";
                return false;
            }

            errorMessage = "";
            return true;

        }
    }
    /// <summary>
    /// The configuration for the corresponding method.
    /// </summary>
    public class ODIDPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.OD_ID; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName() { if (!string.IsNullOrWhiteSpace(Name)) return Name; return "ppODID" + LengthOfAWindow.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER) + (UseFinalReservations ? "t" : "f") + MaxNodeCountPerAgent.ToString(); }
        /// <summary>
        /// The length of a time space search window
        /// </summary>
        public double LengthOfAWindow = 30.0;
        /// <summary>
        /// Use Final Reservations
        /// </summary>
        public bool UseFinalReservations = false;
        /// <summary>
        /// Use Final Reservations
        /// </summary>
        public int MaxNodeCountPerAgent = 100;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            LengthOfAWindow = double.Parse(args[2], new CultureInfo("en"));
            MaxNodeCountPerAgent = int.Parse(args[3]);
            UseFinalReservations = bool.Parse(args[4]);
        }
        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        /// <param name="errorMessage">A message describing the error if the configuration is not valid.</param>
        /// <returns>Indicates whether the path planning configuration is valid.</returns>
        public override bool AttributesAreValid(out String errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
            {
                return false;
            }
            if (MaxNodeCountPerAgent < 0)       //<= 0 may be wise as well
            {
                errorMessage = "Problem with path planning configuration: MaxPriorities has to be >= 0.";
                return false;
            }
            if (LengthOfAWindow <= 0)
            {
                errorMessage = "LengthOfAWindow has to be > 0";
                return false;
            }

            errorMessage = "";
            return true;

        }
    }

    /// <summary>
    /// Configuration for decentralized per-agent A* path planning.
    /// Each bot independently runs SpaceAStar to plan its own path.
    /// No collision avoidance at this layer — conflict resolution is delegated to
    /// Plan 2 (Gym controller) and Plan 3 (World Model).
    /// </summary>
    public class DecentralAStarPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        public override PathPlanningMethodType GetMethodType() => PathPlanningMethodType.AgentAStar;

        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppAgentAStar" + ReplanInterval.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER);
        }

        /// <summary>
        /// How often (in simulation seconds) each bot checks whether it needs to replan its path.
        /// A bot always replans immediately if RequestReoptimization is true or Path is null.
        /// </summary>
        public double ReplanInterval = 0.5;

        /// <summary>
        /// Threshold in milliseconds for logging slow A* calls (monitoring only).
        /// Search() is blocking — this cannot interrupt an in-progress search.
        /// </summary>
        public double RuntimeLimitPerAgentMs = 10.0;

        #region Stage B — Local Transition Claims

        /// <summary>
        /// [Stage B] Enable local transition claim gate for waypoint transitions.
        /// When enabled, a minimal claim table tracks single-step transitions to detect conflicts.
        /// Default false for backward compatibility.
        /// </summary>
        public bool EnableLocalTransitionClaims = false;

        /// <summary>
        /// [Stage B] TTL (time-to-live) in seconds for transition claims.
        /// Default 2.0 seconds.
        /// </summary>
        public double LocalTransitionClaimTtl = 2.0;

        #endregion

        #region Stage C — Navigation Anomaly Handling

        /// <summary>
        /// [Stage C] Enable no-progress watchdog to detect and recover from stalls.
        /// When enabled, a bot that fails to progress for N consecutive ticks will
        /// trigger soft recovery (replan request).
        /// Default false for backward compatibility.
        /// </summary>
        public bool EnableNoProgressWatchdog = false;

        /// <summary>
        /// [Stage C] Threshold (number of consecutive ticks) before marking a bot as stalled.
        /// Default 20 ticks.
        /// </summary>
        public int NoProgressTickThreshold = 20;

        #endregion

        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        public override bool AttributesAreValid(out string errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
                return false;
            if (ReplanInterval <= 0)
            {
                errorMessage = "Problem with AgentAStar configuration: ReplanInterval must be > 0.";
                return false;
            }
            if (RuntimeLimitPerAgentMs <= 0)
            {
                errorMessage = "Problem with AgentAStar configuration: RuntimeLimitPerAgentMs must be > 0.";
                return false;
            }
            if (EnableLocalTransitionClaims && LocalTransitionClaimTtl <= 0)
            {
                errorMessage = "Problem with AgentAStar configuration: LocalTransitionClaimTtl must be > 0 when enabled.";
                return false;
            }
            if (EnableNoProgressWatchdog && NoProgressTickThreshold <= 0)
            {
                errorMessage = "Problem with AgentAStar configuration: NoProgressTickThreshold must be > 0 when enabled.";
                return false;
            }
            errorMessage = "";
            return true;
        }
    }

    /// <summary>
    /// Configuration for the JunctionArbitration path planner. Inherits the per-bot A*
    /// behaviour of <see cref="DecentralAStarPathPlanningConfiguration"/> and adds a
    /// reservation table + total-order priority arbitration at the RegisterNextWaypoint
    /// gate, plus a wait-for-graph cycle breaker.
    /// </summary>
    public class JunctionArbitrationPathPlanningConfiguration : DecentralAStarPathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        public override PathPlanningMethodType GetMethodType() => PathPlanningMethodType.JunctionArbitration;

        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppJunctionArb" + ReplanInterval.ToString(IOConstants.EXPORT_FORMAT_SHORTER, IOConstants.FORMATTER);
        }

        /// <summary>How many waypoints ahead to reserve when planning the next move.</summary>
        public int LookaheadK = 3;

        /// <summary>
        /// How often (in simulation seconds) the path manager wakes itself up to retry
        /// every blocked bot.  Without this, a bot whose RegisterNextWaypoint failed is
        /// pinned by BotMove's hardcoded 1-second sleep, even after the physical cause
        /// of the block has cleared.  Setting this drives PathManager.GetNextEventTime
        /// so the simulation timeline does not skip the retry window.
        /// </summary>
        public double BlockedRetryIntervalSec = 0.1;

        /// <summary>Per-second priority boost while a bot is blocked (anti-starvation).</summary>
        public double StarvationBoostPerSec = 1.0;

        /// <summary>How long a bot may remain blocked before forced replan kicks in.</summary>
        public double DeadlockTimeoutSec = 5.0;

        /// <summary>How long a forcibly-replanned bot blacklists the blocked waypoint.</summary>
        public double ForcedReplanBlacklistSec = 8.0;

        /// <summary>Run Tarjan SCC on the wait-for graph each tick to break livelocks.</summary>
        public bool EnableWaitForGraphCycleCheck = true;

        /// <summary>
        /// Checks whether the path planning configuration is valid.
        /// </summary>
        public override bool AttributesAreValid(out string errorMessage)
        {
            if (!base.AttributesAreValid(out errorMessage))
                return false;
            if (LookaheadK < 1)
            {
                errorMessage = "Problem with JunctionArbitration configuration: LookaheadK must be >= 1.";
                return false;
            }
            if (BlockedRetryIntervalSec <= 0.0)
            {
                errorMessage = "Problem with JunctionArbitration configuration: BlockedRetryIntervalSec must be > 0.";
                return false;
            }
            errorMessage = "";
            return true;
        }
    }

    #endregion
}
