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
    /// Configuration for VO-LWHCAn\* (Value- and Load-aware WHCAn*).
    /// Inherits WHCAn\*-shaped params plus protection-score weights and yielding knobs.
    /// </summary>
    public class VoLwhcaStarPathPlanningConfiguration : PathPlanningConfiguration
    {
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.VoLwhcaStar; }
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppVOLWHCAN";
        }

        // WHCAn-shaped
        public double LengthOfAWindow = 15.0;
        public bool UseBias = false;
        public bool UseDeadlockHandler = true;

        // VO-LWHCAn\* yielding
        public double DelayStartCoef = 2.0;
        public double HighPsBufferSec = 2.0;
        public double HighPsProtectRatio = 0.3;

        // D-VO-LWHCAn* rescue parameters
        public double DistanceBucketSize = 5.0;
        public double FailFallbackHorizonSec = 15.0;

        // Protection Score weights (sum normalized implicitly via w_A = 0.05 aging)
        public double W_L = 0.25;
        public double W_S = 0.25;
        public double W_V = 0.20;
        public double W_U = 0.15;
        public double W_C = 0.10;
        public double W_A = 0.05;

        public override void Parse(string[] args)
        {
            base.Parse(args);
            LengthOfAWindow = double.Parse(args[2], new CultureInfo("en"));
            UseBias = bool.Parse(args[3]);
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
    /// The configuration for the ECBS method.
    /// </summary>
    public class ECBSPathPlanningConfiguration : PathPlanningConfiguration
    {
        /// <summary>
        /// Returns the type of the corresponding method this configuration belongs to.
        /// </summary>
        /// <returns>The type of the method.</returns>
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.ECBS; }
        /// <summary>
        /// Returns a name identifying the method.
        /// </summary>
        /// <returns>The name of the method.</returns>
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            string name = "ppECBS";
            switch (SearchMethod)
            {
                case ECBSMethod.ECBSSearchMethod.BestFirst: name += "O"; break;
                case ECBSMethod.ECBSSearchMethod.DepthFirst: name += "D"; break;
                case ECBSMethod.ECBSSearchMethod.BreathFirst: name += "B"; break;
                default: throw new ArgumentException("Unexpected argument!");
            }
            return name;
        }
        /// <summary>
        /// The search method
        /// </summary>
        public ECBSMethod.ECBSSearchMethod SearchMethod = ECBSMethod.ECBSSearchMethod.BestFirst;
        /// <summary>
        /// Parses the specified arguments.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Parse(string[] args)
        {
            base.Parse(args);
            SearchMethod = (ECBSMethod.ECBSSearchMethod)Enum.Parse(typeof(ECBSMethod.ECBSSearchMethod), (args[2]));
        }
    }

    /// <summary>
    /// Configuration for TBEM (Time-Budgeted Energy Minimization).
    /// Mirrors ECBS config but adds TimeBudgetSlack (λ).
    /// </summary>
    public class TBEMPathPlanningConfiguration : PathPlanningConfiguration
    {
        public override PathPlanningMethodType GetMethodType() { return PathPlanningMethodType.TBEM; }
        public override string GetMethodName()
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name;
            return "ppTBEM";
        }

        /// <summary>Search ordering strategy for the TBEM high-level CBS.</summary>
        public TBEMMethod.TBEMSearchMethod SearchMethod = TBEMMethod.TBEMSearchMethod.BestFirst;

        /// <summary>
        /// Time-budget slack λ. Paths with arrival time ≤ (1+λ)·t_CBS are accepted,
        /// where t_CBS comes from RRA* (time-optimal assuming no other agents).
        /// In congested multi-agent scenarios real execution time is often 2-3× t_CBS
        /// due to waits, so λ=1.0 (default, 100% slack = 2× budget) is the practical floor.
        /// λ=0 → reduces to CBS-time (but triggers unsolvable fallback in congestion);
        /// λ=10 → almost free ECBS-like wait but bounded.
        /// </summary>
        public double TimeBudgetSlack = 1.0;

        public override void Parse(string[] args)
        {
            base.Parse(args);
            SearchMethod = (TBEMMethod.TBEMSearchMethod)Enum.Parse(typeof(TBEMMethod.TBEMSearchMethod), (args[2]));
            if (args.Length > 3)
                TimeBudgetSlack = double.Parse(args[3]);
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

    #endregion
}
