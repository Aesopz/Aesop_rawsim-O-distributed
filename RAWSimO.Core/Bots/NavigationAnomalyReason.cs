namespace RAWSimO.Core.Bots
{
    /// <summary>
    /// Enumeration of reasons for navigation anomalies (movement failures or stalls).
    /// Used to categorize failures and guide recovery strategies.
    /// </summary>
    public enum NavigationAnomalyReason
    {
        /// <summary>No anomaly detected.</summary>
        None = 0,

        /// <summary>MoveBotOverride returned false, indicating the move may have been partially applied but is unsafe/invalid.</summary>
        MoveOverrideReportedInvalid = 1,

        /// <summary>setNextWaypoint() failed because RegisterNextWaypoint denied the transition.</summary>
        TransitionRegistrationDenied = 2,

        /// <summary>A path replan was deferred because the bot was mid-segment.</summary>
        ReplanDeferredBecauseMidSegment = 3,

        /// <summary>No path is currently available for the bot.</summary>
        NoPathAvailable = 4,

        /// <summary>The bot is suspected to be in a stall (no progress for N consecutive ticks).</summary>
        SuspectedStall = 5
    }
}
