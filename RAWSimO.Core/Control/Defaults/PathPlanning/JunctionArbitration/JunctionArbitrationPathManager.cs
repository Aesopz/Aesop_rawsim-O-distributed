using RAWSimO.Core.Bots;
using RAWSimO.Core.Configurations;
using RAWSimO.Core.Control.Defaults.PathPlanning.AgentAStar;
using RAWSimO.Core.Waypoints;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWSimO.Core.Control.Defaults.PathPlanning.JunctionArbitration
{
    /// <summary>
    /// Decentralized A* + K-step lookahead priority arbitration.
    ///
    /// == Per-tick information flow ==
    ///   GetNextEventTime():
    ///     • _blocked.Count == 0  →  PositiveInfinity (passive, like base PathManager).
    ///     • _blocked.Count >  0  →  currentTime + BlockedRetryIntervalSec.
    ///       This forces the simulation to wake the path manager every retry interval
    ///       so blocked bots are re-validated even when BotMove has parked them under
    ///       the hardcoded 1-second sleep.
    ///
    ///   Update() runs once per tick:
    ///     1. Flush same-tick claims.
    ///     2. RetryBlockedBots — re-run the no-side-effect gate for every bot in
    ///        _blocked using LIVE state.  Anyone who passes is removed from _blocked
    ///        and woken via WaitUntil(currentTime).
    ///     3. Deadlock resolution (DeadlockTimeoutSec, DOF comparison).
    ///     4. RunQueueManagement + UpdateLocksAndObstacles.
    ///     5. Per-bot A* replan.  Before each replan, ALL other bots' CurrentWaypoint
    ///        and NextWaypoint are temporarily marked IsLocked = true in the graph so
    ///        SpaceAStar naturally routes around physically-occupied nodes.
    ///        K (safety zone depth) is computed per-bot from actual braking physics:
    ///        K = ceil( v_max² / (2 · a_decel) / waypoint_spacing ).
    ///
    ///   RegisterNextWaypoint() is called by the simulation when a bot is stationary
    ///   and ready to commit to its next waypoint.  It defers all 4 checks to the
    ///   side-effect-free <see cref="CanPassGateNoSideEffect"/> helper, so the same
    ///   logic is used by RetryBlockedBots.
    ///
    /// == Data freshness invariants ==
    ///   • All gate checks read live bot.CurrentWaypoint / NextWaypoint / Path.
    ///   • _nextClaim is per-tick (cleared at the start of every Update).
    ///   • _blocked stores ONLY a Since timestamp; the target waypoint is derived
    ///     live from bot.Path.Actions.First() so a replan automatically refreshes it.
    ///   • ComputeBotZoneLive treats blocked bots as occupying ONLY their
    ///     CurrentWaypoint, so a stale Path on a frozen bot cannot poison the zone
    ///     calculations of other bots (the chain-freeze root cause).
    ///
    /// == Priority order (highest wins) ==
    ///   Tier 2 — loaded, DestinationWaypoint.OutputStation != null  (delivery)
    ///   Tier 1 — loaded, destination is NOT output station          (returning pod)
    ///   Tier 0 — empty bot
    ///   Tiebreaker 1: vertical direction > horizontal
    ///   Tiebreaker 2: lower bot ID
    ///
    /// == Deadlock resolution ==
    ///   Stuck bot and direct blocker — DOF (free outgoing waypoints) comparison.
    ///   Bot with more freedom reroutes; one bot per event; target wp made into
    ///   temporary obstacle during A* so the detour is found immediately.
    /// </summary>
    public sealed class JunctionArbitrationPathManager : AgentAStarPathManager
    {
        private readonly JunctionArbitrationPathPlanningConfiguration _arbConfig;
        private readonly Instance _sim;
        private readonly Random   _rng;

        // Same-tick claim: set when RegisterNextWaypoint returns true, cleared in Update().
        // Prevents a second bot from claiming the same next waypoint within the same tick
        // before bot.NextWaypoint has been written by the physics model.
        private readonly Dictionary<BotNormal, Waypoint> _nextClaim
            = new Dictionary<BotNormal, Waypoint>();

        // Per-bot block record: ONLY first-block-time is stored here.
        // The target waypoint is always derived live from bot.Path.Actions.First()
        // so a subsequent replan automatically refreshes it (no stale TargetWp cache).
        private readonly Dictionary<BotNormal, double> _blocked
            = new Dictionary<BotNormal, double>();

        // One-shot reroute target: consumed in per-bot A* replan inside Update().
        private readonly Dictionary<BotNormal, Waypoint> _rerouteTarget
            = new Dictionary<BotNormal, Waypoint>();

        public long TotalArbitrationStops { get; private set; }
        public int  LastRerouteCount      { get; private set; }
        public int  LastCycleCount        => LastRerouteCount;  // GymServer compat
        private double _lastObservedTime;

        public bool TryGetArbitrationStop(
            BotNormal bot, out bool stopped, out double duration, out int blockedAtId)
        {
            stopped = false; duration = 0.0; blockedAtId = -1;
            if (!_blocked.TryGetValue(bot, out double since)) return false;
            stopped     = true;
            duration    = Math.Max(0.0, _lastObservedTime - since);
            blockedAtId = GetBlockedTarget(bot)?.ID ?? -1;
            return true;
        }

        public JunctionArbitrationPathManager(
            Instance instance,
            JunctionArbitrationPathPlanningConfiguration config)
            : base(instance, config)
        {
            _arbConfig = config;
            _sim       = instance;
            _rng       = new Random(instance.SettingConfig.Seed);
        }

        // ═══════════════════════════ event time ═══════════════════════════════════

        /// <summary>
        /// Drives the simulation timeline when blocked bots are waiting for retry.
        ///
        /// Without this override the path manager is a passive listener
        /// (<c>PositiveInfinity</c>) and the simulation jumps to the next bot event,
        /// which is typically <c>BotNormal._waitUntil = currentTime + 1</c> set by
        /// <c>BotMove</c>.  That means a bot whose only obstacle cleared 0.1 s after
        /// being blocked still waits the full second before retrying — exactly the
        /// "wait until eventually replanned" symptom.
        ///
        /// By returning <c>currentTime + BlockedRetryIntervalSec</c> while
        /// <c>_blocked</c> is non-empty, the path manager forces the controller to
        /// call <c>Update()</c> at the next retry slot, where
        /// <c>RetryBlockedBots</c> re-validates every blocked bot against the live
        /// world state and wakes any that can now pass.
        /// </summary>
        public override double GetNextEventTime(double currentTime)
        {
            if (_blocked.Count == 0) return double.PositiveInfinity;
            return currentTime + _arbConfig.BlockedRetryIntervalSec;
        }

        // ═══════════════════════════ arbitration gate ════════════════════════════
        // Called by the simulation engine each time a bot is stationary and ready to
        // commit to its next waypoint.  bot.GetSpeed() == 0 at this point.

        public override bool RegisterNextWaypoint(
            BotNormal bot, double currentTime,
            double blockCurrentWaypointUntil, double rotationDuration,
            Waypoint waypointStart, Waypoint waypointEnd)
        {
            if (waypointEnd == null) return true;

            // 0. Queue-slot lock (unchanged from base PathManager).
            if (IsQueueWaypointLocked(waypointEnd, bot))
                return false;

            // Steps 1–4 are delegated to the side-effect-free helper so the same
            // gate logic is used by RetryBlockedBots.
            if (!CanPassGateNoSideEffect(bot, waypointStart, waypointEnd))
            {
                RecordBlocked(bot, currentTime);
                return false;
            }

            // Approved.
            _nextClaim[bot] = waypointEnd;
            _blocked.Remove(bot);
            return true;
        }

        /// <summary>
        /// Pure predicate version of the arbitration gate (steps 1–4 from
        /// <see cref="RegisterNextWaypoint"/>).  Reads only live bot state — no
        /// caches, no side effects (no <c>RecordBlocked</c>, no <c>_nextClaim</c>
        /// mutation, no <c>_blocked</c> mutation).
        /// </summary>
        private bool CanPassGateNoSideEffect(
            BotNormal bot, Waypoint waypointStart, Waypoint waypointEnd)
        {
            if (waypointEnd == null) return true;

            // 1. Hard occupancy: target is physically occupied or claimed this tick.
            //    Uses live bot.CurrentWaypoint and bot.NextWaypoint — always current.
            foreach (var other in _planners.Keys)
            {
                if (other == bot) continue;
                if (other.CurrentWaypoint == waypointEnd
                 || other.NextWaypoint    == waypointEnd
                 || (_nextClaim.TryGetValue(other, out var cl) && cl == waypointEnd))
                    return false;
            }

            // 2. Swap conflict: another bot at waypointEnd heading toward our start.
            //    Uses live data — prevents head-on passes on single-lane corridors.
            foreach (var other in _planners.Keys)
            {
                if (other == bot) continue;
                if (other.CurrentWaypoint == waypointEnd)
                {
                    bool headingBack = other.NextWaypoint == waypointStart
                        || (_nextClaim.TryGetValue(other, out var cl2) && cl2 == waypointStart);
                    if (headingBack) return false;
                }
            }

            // 3. K-step safety-zone overlap — computed LIVE, no stale cache.
            //    K is derived from this bot's actual braking physics so the zone matches
            //    the real stopping distance.  Zone_bot = [waypointEnd .. +K-1 steps].
            int K      = ComputeK(bot);
            var myZone = BuildZone(bot, waypointEnd, K);

            foreach (var other in _planners.Keys)
            {
                if (other == bot) continue;

                // Compute the other bot's zone LIVE from its current physical state.
                // ComputeBotZoneLive shrinks the zone of bots in _blocked to a single
                // cell so a stale Path on a frozen bot cannot create false overlaps.
                var otherZone = ComputeBotZoneLive(other, K);
                if (!myZone.Overlaps(otherZone)) continue;

                // Zones overlap.  A moving bot's commitment cannot be revoked.
                if (other.NextWaypoint != null)
                    return false;

                // Both stationary (or claiming this tick): priority comparison.
                if (Compare(bot, waypointStart, waypointEnd, other) < 0)
                    return false;
                // This bot wins: other will be blocked when it later calls this gate.
            }

            // 4. Physical distance + trajectory check.
            //    safe_distance = bot.Radius + other.Radius + 0.05 m margin.
            //
            //    otherDest = other.NextWaypoint  (physics-committed, always authoritative)
            //              ?? _nextClaim[other]  (same-tick claim, NextWaypoint not yet written)
            //
            //    Edge case fixed: when a bot was just approved this tick (claim set but
            //    NextWaypoint still null), we still use its claimed destination for both
            //    the point check (4b) and the trajectory cross check (4c).
            //    This prevents two bots getting approved in the same tick to adjacent
            //    waypoints whose travel segments physically cross (turning + replanning).
            double safeR   = bot.Radius + 0.35 + 0.05;   // 0.35 = max other bot radius
            double safeRSq = safeR * safeR;
            double tx = waypointEnd.X, ty = waypointEnd.Y;
            foreach (var other in _planners.Keys)
            {
                if (other == bot) continue;

                // 4a. vs other's current physical position (catches mid-segment approach).
                double dx = tx - other.X, dy = ty - other.Y;
                if (dx * dx + dy * dy < safeRSq)
                    return false;

                // Resolve other's committed/claimed destination.
                // NextWaypoint takes priority; fall back to same-tick _nextClaim.
                Waypoint otherDest = other.NextWaypoint;
                if (otherDest == null)
                    _nextClaim.TryGetValue(other, out otherDest);

                if (otherDest == null) continue;   // other is fully stationary, no destination

                // 4b. vs other's destination centre.
                dx = tx - otherDest.X; dy = ty - otherDest.Y;
                if (dx * dx + dy * dy < safeRSq)
                    return false;

                // 4c. Trajectory cross-conflict:
                //     segment A (waypointStart → waypointEnd)  vs
                //     segment B (other.CurrentWaypoint → otherDest).
                //     Catches crossing paths at intersections, pod-storage aisles, and
                //     any case where both bots get approved in the same tick to
                //     geometrically intersecting moves (turning, post-replan first step).
                if (waypointStart != null && other.CurrentWaypoint != null
                    && other.CurrentWaypoint != otherDest)  // skip zero-length segment
                {
                    double minDistSq = SegMinDistSq(
                        waypointStart.X, waypointStart.Y, tx, ty,
                        other.CurrentWaypoint.X, other.CurrentWaypoint.Y,
                        otherDest.X, otherDest.Y);
                    if (minDistSq < safeRSq)
                        return false;
                }
            }

            return true;
        }

        // ═══════════════════════════════ update ══════════════════════════════════

        public override void Update(double lastTime, double currentTime)
        {
            _lastObservedTime = currentTime;

            // 1. Flush per-tick claims from the previous tick.
            _nextClaim.Clear();

            // 2. Re-validate every bot in _blocked against the LIVE world state.
            //    This is the core fix for the "wait 1 second after the obstacle has
            //    cleared" symptom.  Bots whose blocking condition has resolved are
            //    removed from _blocked and woken via WaitUntil(currentTime), which
            //    overrides BotMove's hardcoded 1-second sleep.
            RetryBlockedBots(currentTime);

            // 3. Deadlock resolution.
            //    A "true" deadlock requires the blocker to also be stationary and stuck.
            //    If the blocker is still moving (NextWaypoint != null), it will clear on
            //    its own — reset the stuck-timer and do NOT reroute prematurely.
            //    Only escalate when the blocker is also genuinely stopped and in _blocked,
            //    OR the bot has been stuck for 2× the timeout (emergency escape).
            LastRerouteCount = 0;
            foreach (var kv in _blocked.ToList())
            {
                var stuckBot       = kv.Key;
                double stuckSince  = kv.Value;
                Waypoint targetWp  = GetBlockedTarget(stuckBot);   // LIVE — derived from bot.Path
                if (targetWp == null) continue;
                double stuckDuration = currentTime - stuckSince;

                // Find who is occupying the target waypoint right now.
                BotNormal blocker = FindBlocker(targetWp, stuckBot);

                // Blocker is actively moving (NextWaypoint committed) → will clear soon.
                // Reset stuck-timer to avoid rerouting just because of slow travel / turning.
                if (blocker != null && blocker.NextWaypoint != null)
                {
                    _blocked[stuckBot] = currentTime;
                    continue;
                }

                // Categorise why the blocker is stationary:
                //
                //   (A) Blocker also stuck in _blocked    → mutual deadlock → resolve at 1× timeout
                //   (B) Blocker has a path but no NextWp  → turning/rotating, will commit soon
                //                                         → allow 2× timeout grace period
                //   (C) Blocker truly idle (no path, no NextWp, not blocked)
                //                                         → resolve at 1× timeout (idle bots
                //                                            never self-resolve, waiting longer
                //                                            only wastes throughput)
                bool blockerAlsoStuck  = blocker != null && _blocked.ContainsKey(blocker);
                bool blockerAboutToMove = blocker != null
                    && blocker.NextWaypoint == null
                    && blocker.Path != null
                    && blocker.Path.Actions.Any();
                double effectiveTimeout = blockerAboutToMove
                    ? _arbConfig.DeadlockTimeoutSec * 2.0   // (B) turning grace
                    : _arbConfig.DeadlockTimeoutSec;         // (A) / (C) normal

                if (stuckDuration < effectiveTimeout)
                    continue;

                ResolveDeadlock(stuckBot, targetWp, currentTime);
            }

            // 4. Queue management + pod-obstacle / queue-lock flags (base PathManager).
            RunQueueManagement(lastTime, currentTime);
            UpdateLocksAndObstacles();

            // 5. Per-bot A* replan.
            //    Key policy:
            //      • Bots that are arbitration-blocked (in _blocked) and have NOT been
            //        explicitly asked to reoptimize are SKIPPED here.
            //        They keep their existing path and retry RegisterNextWaypoint
            //        every retry interval via RetryBlockedBots — no new A* needed.
            //        As soon as the blocking condition clears (zone free, distance safe)
            //        RetryBlockedBots removes them from _blocked and wakes them.
            //      • Bots with RequestReoptimization=true (set by deadlock resolution or
            //        task allocation) are always replanned, even if currently blocked.
            //      • Non-blocked bots: replan normally with occupancy locks so A* routes
            //        around physically-occupied waypoints.
            foreach (var kv in _planners)
            {
                var bot     = kv.Key;
                var planner = kv.Value;

                // Arbitration-blocked + no explicit replan request → skip replan,
                // let RetryBlockedBots resume it on the next retry slot.
                // Exception: if the bot somehow lost its path (null/empty), allow one
                // replan so it has a route to resume on.
                bool hasPath = bot.Path != null && bot.Path.Actions.Any();
                if (_blocked.ContainsKey(bot) && !bot.RequestReoptimization && hasPath)
                    continue;

                // Gather saves: reroute-obstacle + occupancy locks for all other bots.
                var saved = new List<KeyValuePair<int, bool>>();
                InjectRerouteObstacle(bot, saved);
                InjectOccupancyLocks(bot, saved);

                try   { planner.TryReplan(currentTime); }
                finally { RestoreGraph(saved); }
            }
        }

        /// <summary>
        /// Re-runs the no-side-effect arbitration gate for every bot in <c>_blocked</c>
        /// using LIVE state.  Anyone who passes is removed from <c>_blocked</c> and
        /// woken via <see cref="BotNormal.WaitUntil"/> so the next simulation tick
        /// retries <c>setNextWaypoint</c> immediately, bypassing BotMove's hardcoded
        /// 1-second sleep.
        ///
        /// This is the per-tick refresh that prevents stale block records from
        /// chain-freezing the warehouse.
        /// </summary>
        private void RetryBlockedBots(double currentTime)
        {
            foreach (var bot in _blocked.Keys.ToList())
            {
                // Defensive: a bot that started moving (e.g. via reroute) is no
                // longer arbitration-blocked.  Drop the stale record.
                if (bot.NextWaypoint != null)
                {
                    _blocked.Remove(bot);
                    continue;
                }

                Waypoint startWp  = bot.CurrentWaypoint;
                Waypoint targetWp = GetBlockedTarget(bot);
                if (startWp == null || targetWp == null) continue;

                // Queue-lock check parallels RegisterNextWaypoint step 0.
                if (IsQueueWaypointLocked(targetWp, bot)) continue;

                if (CanPassGateNoSideEffect(bot, startWp, targetWp))
                {
                    _blocked.Remove(bot);
                    // Override BotMove's hardcoded +1s sleep so the bot retries
                    // setNextWaypoint on the very next simulation tick.
                    // Safe because blocked bots are stationary (GetSpeed() == 0).
                    if (bot.GetSpeed() == 0.0)
                        bot.WaitUntil(currentTime);
                }
            }
        }

        // ═══════════════════════════ priority helpers ═════════════════════════════

        private int Compare(BotNormal a, Waypoint aFrom, Waypoint aTo, BotNormal b)
        {
            int ta = GetTier(a), tb = GetTier(b);
            if (ta != tb) return ta.CompareTo(tb);

            int da = GetDirScore(aFrom, aTo);
            int db = GetDirScoreForBot(b);
            if (da != db) return da.CompareTo(db);

            return b.ID.CompareTo(a.ID);  // lower ID wins
        }

        private static int GetTier(BotNormal bot)
        {
            if (bot.Pod == null) return 0;
            return bot.DestinationWaypoint?.OutputStation != null ? 2 : 1;
        }

        private static int GetDirScore(Waypoint from, Waypoint to)
        {
            if (from == null || to == null) return 0;
            return Math.Abs(to.Y - from.Y) > Math.Abs(to.X - from.X) ? 1 : 0;
        }

        private int GetDirScoreForBot(BotNormal bot)
        {
            Waypoint to = bot.NextWaypoint;
            if (to == null && bot.Path != null)
            {
                var first = bot.Path.Actions.FirstOrDefault();
                if (first != null) to = _waypointIds[first.Node];
            }
            return GetDirScore(bot.CurrentWaypoint, to);
        }

        // ═══════════════════════════ zone helpers ═════════════════════════════════

        /// <summary>
        /// K = ceil(v_max² / (2·a_decel) / waypoint_spacing).
        /// Falls back to LookaheadK from config if physics unavailable.
        /// waypoint_spacing is conservatively assumed to be 1.0 m.
        /// </summary>
        private int ComputeK(BotNormal bot)
        {
            var phy = bot.Physics;
            if (phy == null || phy.Deceleration <= 0.0)
                return Math.Max(1, _arbConfig.LookaheadK);
            double brakingDist = phy.MaxSpeed * phy.MaxSpeed / (2.0 * phy.Deceleration);
            // Divide by assumed 1.0 m spacing; at least 1 waypoint.
            return Math.Max(1, (int)Math.Ceiling(brakingDist));
        }

        /// <summary>
        /// Builds a zone of up to K waypoints starting at firstWp along bot's A* plan.
        /// </summary>
        private HashSet<Waypoint> BuildZone(BotNormal bot, Waypoint firstWp, int K)
        {
            var zone = new HashSet<Waypoint>();
            if (firstWp == null) return zone;
            zone.Add(firstWp);
            foreach (var wp in WalkPathAhead(bot, firstWp, K - 1))
                zone.Add(wp);
            return zone;
        }

        /// <summary>
        /// Computes another bot's current safety zone using LIVE physical state.
        ///
        /// Critical invariant for the chain-freeze fix:
        ///   A bot in <c>_blocked</c> is physically frozen at its CurrentWaypoint
        ///   until it passes the gate again.  Its <c>Path</c> still points to where
        ///   it WANTED to go, but it cannot actually reach those cells.  Treating
        ///   that stale Path as "imminently occupied" makes other bots think they
        ///   need to wait for cells the frozen bot cannot reach — the root cause of
        ///   the empty-bot pod-aisle freeze cascade.  We therefore collapse the zone
        ///   of every blocked bot to a single cell (its CurrentWaypoint).
        /// </summary>
        private HashSet<Waypoint> ComputeBotZoneLive(BotNormal bot, int K)
        {
            // Frozen bot: only the cell it physically occupies counts.
            if (_blocked.ContainsKey(bot))
            {
                var z = new HashSet<Waypoint>();
                if (bot.CurrentWaypoint != null) z.Add(bot.CurrentWaypoint);
                return z;
            }

            // Moving: zone starts at the committed next waypoint (live).
            if (bot.NextWaypoint != null)
                return BuildZone(bot, bot.NextWaypoint, K);

            // Stationary: use first waypoint in current A* plan.
            if (bot.Path != null)
            {
                var first = bot.Path.Actions.FirstOrDefault();
                if (first != null)
                {
                    Waypoint startWp = _waypointIds[first.Node];
                    if (startWp != null)
                        return BuildZone(bot, startWp, K);
                }
            }
            return new HashSet<Waypoint>();
        }

        /// <summary>
        /// Yields up to K waypoints that follow 'from' in bot's current A* path.
        /// </summary>
        private IEnumerable<Waypoint> WalkPathAhead(BotNormal bot, Waypoint from, int K)
        {
            if (bot.Path == null || K <= 0) yield break;
            bool found = false;
            int  count = 0;
            foreach (var action in bot.Path.Actions)
            {
                Waypoint w = _waypointIds[action.Node];
                if (w == null) continue;
                if (!found) { if (w == from) found = true; continue; }
                yield return w;
                if (++count >= K) yield break;
            }
        }

        // ═══════════════════════ deadlock resolution ══════════════════════════════

        /// <summary>
        /// Records that a bot failed the gate.  Stores ONLY the timestamp; the
        /// target waypoint is always derived live from <c>bot.Path</c> via
        /// <see cref="GetBlockedTarget"/> so a replan automatically refreshes it.
        /// </summary>
        private void RecordBlocked(BotNormal bot, double currentTime)
        {
            if (!_blocked.ContainsKey(bot))
            {
                _blocked[bot] = currentTime;
                TotalArbitrationStops++;
            }
        }

        /// <summary>
        /// Returns the waypoint a blocked bot is currently trying to enter, derived
        /// LIVE from its A* plan.  No cached state — automatically tracks replans.
        /// </summary>
        private Waypoint GetBlockedTarget(BotNormal bot)
        {
            // Defensive: a bot might have a committed NextWaypoint while still in
            // _blocked for one tick after RetryBlockedBots wakes it.
            if (bot.NextWaypoint != null) return bot.NextWaypoint;
            var first = bot.Path?.Actions.FirstOrDefault();
            if (first == null) return null;
            return _waypointIds[first.Node];
        }

        private void ResolveDeadlock(BotNormal stuckBot, Waypoint targetWp, double currentTime)
        {
            BotNormal blocker = FindBlocker(targetWp, stuckBot);
            BotNormal toReroute;

            if (blocker == null)
            {
                toReroute = stuckBot;
            }
            else
            {
                int dofStuck   = CountDOF(stuckBot);
                int dofBlocker = CountDOF(blocker);
                if      (dofStuck   > dofBlocker) toReroute = stuckBot;
                else if (dofBlocker > dofStuck)   toReroute = blocker;
                else toReroute = _rng.NextDouble() < 0.5 ? stuckBot : blocker;
            }

            _blocked.Remove(stuckBot);
            if (blocker != null) _blocked.Remove(blocker);
            _rerouteTarget[toReroute] = targetWp;
            toReroute.RequestReoptimization = true;
            LastRerouteCount++;
        }

        private BotNormal FindBlocker(Waypoint wp, BotNormal exclude)
        {
            if (wp == null) return null;
            foreach (var other in _planners.Keys)
            {
                if (other == exclude) continue;
                if (other.CurrentWaypoint == wp || other.NextWaypoint == wp) return other;
                if (_nextClaim.TryGetValue(other, out var c) && c == wp)    return other;
            }
            return null;
        }

        private int CountDOF(BotNormal bot)
        {
            if (bot?.CurrentWaypoint == null) return 0;
            int count = 0;
            foreach (var wp in bot.CurrentWaypoint.Paths)
            {
                if (wp == null) continue;
                if (_waypointIds.ValuesFirst.Contains(wp))
                {
                    int nId = _waypointIds[wp];
                    if (ExposedGraph.NodeInfo[nId].IsObstacle) continue;
                }
                bool occupied = false;
                foreach (var other in _planners.Keys)
                {
                    if (other == bot) continue;
                    if (other.CurrentWaypoint == wp || other.NextWaypoint == wp)
                    { occupied = true; break; }
                }
                if (!occupied) count++;
            }
            return count;
        }

        // ═══════════════════════ graph injection helpers ══════════════════════════

        /// <summary>
        /// Appends reroute-obstacle save/restore entry for this bot (if scheduled).
        /// Marks the obstacle waypoint as IsObstacle = true so SpaceAStar avoids it.
        /// </summary>
        private void InjectRerouteObstacle(BotNormal bot, List<KeyValuePair<int, bool>> saved)
        {
            if (!_rerouteTarget.TryGetValue(bot, out var wp) || wp == null) return;
            _rerouteTarget.Remove(bot);
            if (!_waypointIds.ValuesFirst.Contains(wp)) return;
            int id = _waypointIds[wp];
            var ni = ExposedGraph.NodeInfo[id];
            saved.Add(new KeyValuePair<int, bool>(id, ni.IsObstacle));
            ni.IsObstacle = true;
        }

        /// <summary>
        /// Marks every OTHER bot's CurrentWaypoint and NextWaypoint as IsLocked = true
        /// in the A* graph.  This gives SpaceAStar real-time occupancy awareness:
        /// it will route around physically-taken nodes rather than planning through them.
        ///
        /// Only bot's OWN position is left unlocked (a bot must be able to plan FROM
        /// its current location).
        /// </summary>
        private void InjectOccupancyLocks(BotNormal bot, List<KeyValuePair<int, bool>> saved)
        {
            foreach (var other in _planners.Keys)
            {
                if (other == bot) continue;
                LockWaypointForBot(other.CurrentWaypoint, saved);
                LockWaypointForBot(other.NextWaypoint,    saved);
            }
        }

        private void LockWaypointForBot(Waypoint wp, List<KeyValuePair<int, bool>> saved)
        {
            if (wp == null) return;
            if (!_waypointIds.ValuesFirst.Contains(wp)) return;
            int id = _waypointIds[wp];
            var ni = ExposedGraph.NodeInfo[id];
            if (ni.IsLocked) return;   // already locked — don't double-save
            saved.Add(new KeyValuePair<int, bool>(~id, ni.IsLocked));  // ~id encodes IsLocked
            ni.IsLocked = true;
        }

        /// <summary>
        /// Restores all IsObstacle and IsLocked flags saved by InjectRerouteObstacle /
        /// InjectOccupancyLocks.  Positive key = obstacle entry; bitwise-NOT key = lock entry.
        /// </summary>
        private void RestoreGraph(List<KeyValuePair<int, bool>> saved)
        {
            foreach (var kv in saved)
            {
                if (kv.Key >= 0)
                    ExposedGraph.NodeInfo[kv.Key].IsObstacle = kv.Value;
                else
                    ExposedGraph.NodeInfo[~kv.Key].IsLocked = kv.Value;
            }
        }

        // ═══════════════════════ geometry helpers ════════════════════════════════

        /// <summary>
        /// Returns the squared minimum distance between line segment P1→P2 and Q1→Q2.
        /// Used for trajectory cross-conflict detection (check 4c).
        /// Based on the parametric closest-point-on-segment formula.
        /// </summary>
        private static double SegMinDistSq(
            double p1x, double p1y, double p2x, double p2y,
            double q1x, double q1y, double q2x, double q2y)
        {
            double ux = p2x - p1x, uy = p2y - p1y;   // segment P direction
            double vx = q2x - q1x, vy = q2y - q1y;   // segment Q direction
            double wx = p1x - q1x, wy = p1y - q1y;

            double a = ux*ux + uy*uy;  // |u|²
            double b = ux*vx + uy*vy;  // u·v
            double c = vx*vx + vy*vy;  // |v|²
            double d = ux*wx + uy*wy;  // u·w
            double e = vx*wx + vy*wy;  // v·w
            double D = a*c - b*b;      // denominator

            double sc, tc;

            const double eps = 1e-10;
            if (D < eps)
            {
                // Segments nearly parallel — fix sc=0, find closest tc.
                sc = 0.0;
                tc = (c > eps) ? (e / c) : 0.0;
            }
            else
            {
                sc = (b*e - c*d) / D;
                tc = (a*e - b*d) / D;
            }

            sc = Math.Max(0.0, Math.Min(1.0, sc));
            tc = Math.Max(0.0, Math.Min(1.0, tc));

            // Closest points on each segment.
            double cpx = p1x + sc*ux - (q1x + tc*vx);
            double cpy = p1y + sc*uy - (q1y + tc*vy);
            return cpx*cpx + cpy*cpy;
        }
    }
}
