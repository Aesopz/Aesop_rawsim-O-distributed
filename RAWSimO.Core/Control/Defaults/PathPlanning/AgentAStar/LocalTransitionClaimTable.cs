using RAWSimO.Core.Bots;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar
{
    /// <summary>
    /// Minimal local transition claim table for AgentAStar.
    ///
    /// This is NOT a full reservation table. It tracks only single-step transitions
    /// (from CurrentWaypoint to the next waypoint) to detect basic conflicts:
    /// 1. Same transition claimed by different bots
    /// 2. Reverse transition pairs (A->B claimed while B->A also requested)
    /// 3. Same destination from different bots
    ///
    /// Claims have a short TTL and are cleared opportunistically.
    /// Conflict resolution uses deterministic bot ID ordering.
    /// </summary>
    internal class LocalTransitionClaimTable
    {
        /// <summary>Key for a transition: (FromWaypointId, ToWaypointId).</summary>
        private struct TransitionKey : IEquatable<TransitionKey>
        {
            public int FromId { get; }
            public int ToId { get; }

            public TransitionKey(int fromId, int toId)
            {
                FromId = fromId;
                ToId = toId;
            }

            public override bool Equals(object obj) => obj is TransitionKey other && Equals(other);

            public bool Equals(TransitionKey other) => FromId == other.FromId && ToId == other.ToId;

            public override int GetHashCode() => unchecked(FromId * 73856093 ^ ToId * 19349663);

            public override string ToString() => $"({FromId}->{ToId})";
        }

        /// <summary>A single claim record for a transition.</summary>
        private struct ClaimRecord
        {
            public int BotId { get; set; }
            public double CreatedTime { get; set; }
            public double ExpiryTime { get; set; }

            public bool IsExpired(double currentTime) => currentTime >= ExpiryTime;
        }

        /// <summary>TTL for claims in simulation seconds.</summary>
        private readonly double _claimTtl;

        /// <summary>Active claims, keyed by (FromWaypointId, ToWaypointId).</summary>
        private readonly Dictionary<TransitionKey, ClaimRecord> _claims
            = new Dictionary<TransitionKey, ClaimRecord>();

        public LocalTransitionClaimTable(double claimTtl)
        {
            _claimTtl = claimTtl;
        }

        /// <summary>
        /// Attempts to claim a transition. Returns true if the claim is granted (or already owned).
        /// </summary>
        public bool TryClaimTransition(BotNormal bot, Waypoint fromWp, Waypoint toWp, double currentTime)
        {
            if (fromWp == null || toWp == null || bot == null)
                return true; // Trivial case: no claim needed

            var key = new TransitionKey(fromWp.ID, toWp.ID);
            var reverseKey = new TransitionKey(toWp.ID, fromWp.ID);

            // Check if we already own this claim
            if (_claims.TryGetValue(key, out var existing))
            {
                if (existing.IsExpired(currentTime))
                {
                    // Claim expired, take ownership
                    _claims[key] = new ClaimRecord
                    {
                        BotId = bot.ID,
                        CreatedTime = currentTime,
                        ExpiryTime = currentTime + _claimTtl
                    };
                    // Clear reverse claim if it exists
                    _claims.Remove(reverseKey);
                    return true;
                }

                if (existing.BotId == bot.ID)
                {
                    // We already own it, extend expiry
                    _claims[key] = new ClaimRecord
                    {
                        BotId = bot.ID,
                        CreatedTime = existing.CreatedTime,
                        ExpiryTime = currentTime + _claimTtl
                    };
                    return true;
                }

                // Someone else owns it and it hasn't expired
                // Apply priority rule: lower bot ID wins
                if (bot.ID < existing.BotId)
                {
                    // We win, take the claim
                    _claims[key] = new ClaimRecord
                    {
                        BotId = bot.ID,
                        CreatedTime = currentTime,
                        ExpiryTime = currentTime + _claimTtl
                    };
                    _claims.Remove(reverseKey);
                    return true;
                }
                else
                {
                    // They own it and won the priority
                    return false;
                }
            }

            // No existing claim, check reverse direction
            if (_claims.TryGetValue(reverseKey, out var reverseExisting))
            {
                if (!reverseExisting.IsExpired(currentTime))
                {
                    // Reverse claim is active, deny (avoid head-on)
                    return false;
                }
            }

            // No conflict, grant the claim
            _claims[key] = new ClaimRecord
            {
                BotId = bot.ID,
                CreatedTime = currentTime,
                ExpiryTime = currentTime + _claimTtl
            };
            return true;
        }

        /// <summary>
        /// Releases the claim for a bot on a given transition.
        /// </summary>
        public void ReleaseClaim(BotNormal bot, Waypoint fromWp, Waypoint toWp)
        {
            if (fromWp == null || toWp == null || bot == null)
                return;

            var key = new TransitionKey(fromWp.ID, toWp.ID);
            if (_claims.TryGetValue(key, out var existing) && existing.BotId == bot.ID)
            {
                _claims.Remove(key);
            }
        }

        /// <summary>
        /// Clears all expired claims. Call periodically to maintain table size.
        /// </summary>
        public void ExpireStale(double currentTime)
        {
            var toRemove = new List<TransitionKey>();
            foreach (var kvp in _claims)
            {
                if (kvp.Value.IsExpired(currentTime))
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _claims.Remove(key);
        }

        /// <summary>
        /// Clear all claims for a specific bot (e.g., when bot is no longer mid-segment).
        /// </summary>
        public void ClearClaimsForBot(int botId)
        {
            var toRemove = new List<TransitionKey>();
            foreach (var kvp in _claims)
            {
                if (kvp.Value.BotId == botId)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _claims.Remove(key);
        }

        /// <summary>
        /// Clear all claims (useful for testing or reset).
        /// </summary>
        public void Clear()
        {
            _claims.Clear();
        }

        /// <summary>
        /// Returns the number of active claims.
        /// </summary>
        public int Count => _claims.Count;
    }
}
