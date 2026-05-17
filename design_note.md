# Engineering Design Note: MAPFUU-Inspired Congestion-Aware Travel-Time Estimation for M1G / OJOOPT in RAWSim-O

**Author:** Aesop / AI research assistant
**Status:** Draft for review
**Spec source:** `spec.md`
**Companion papers:** MAPFUU (Street et al., AAMAS 2020); M1G / OJOOPT (`online.pdf`)
**Scope:** A *cost estimator*, not a path planner.

---

## 1. Executive Summary

RAWSim-O drives an online RMFS controller (M1G / OJOOPT) that, at every replan epoch, jointly decides Order Assignment, Pod Selection, and Task Allocation. The routing terms inside its MILP objective use **static graph distance** (`EstimateBotPodDistance`, `EstimatePodStationDistance` in `M1GManager.cs:117, 134`), so the optimiser cannot tell that one bot's "shortest" pickup will arrive late because the bot in front is parked in a single-lane aisle, or that another bot must wait three reservations at the station fan-in.

MAPFUU (AAMAS 2020) offers a principled way to incorporate routing-layer congestion into upper-level decisions, but it does so with machinery that is *not* directly compatible with RAWSim-O: edge-keyed probabilistic reservation tables, phase-type duration distributions, continuous-time Markov chains, Poisson-binomial composition of bot positions, and per-robot MDP planning. RAWSim-O instead exposes a node-keyed deterministic reservation table over a 15-second window (`WHCAnMethod.cs:23`, `ReservationTable.cs`), a directed graph (`Graph.cs:45`), and a short-horizon WHCA\* planner that never publishes a full bot-to-pod or pod-to-station trajectory.

This note proposes a deliberately narrow adaptation: a **congestion-aware two-leg travel-time estimator** that (i) keeps WHCA\* as the only path planner, (ii) reads the existing reservation table plus an offline empirical edge-duration table, and (iii) replaces the cost coefficients inside the two M1G distance-estimator call sites — *without altering the objective structure (`w1`, `w2`, `w3` retained per user direction).* The estimator is queried, not coupled into the planning loop; it has no side effects on reservations and cannot deadlock the planner the way the earlier `PPCostFeedbackEstimator` did.

Expected contributions: a MAPFUU-inspired but RAWSim-O-faithful estimator; an empirical `μ̂(edge, congestion-band)` table built from instrumented runs; an offline validation framework comparing the static distance baseline (TP=327.5, PO=3.047, OD=23.18, RD=15 183 on `small + m1g_time + seed=0`) against the new estimator before any production swap.

---

## 2. Problem Definition

### 2.1 The online OA–PS–TA decision

RAWSim-O's `M1GManager` solves a Gurobi MILP every replan epoch over the variables `xps[p,s]` (pod-to-station), `yos[o,s]` (order-to-station), `yaos[o,s]` (order-acceptance), `yrp[r,p]` (bot-to-pod), `us[s]` (unused capacity), and `dops[o,p,s]` (linking) — see `M1GManager.cs:303-399`. The current objective at `M1GManager.cs:563-570` is

```
min  w1 · ( Σ xps · EstimatePodStationDistance + Σ yrp · EstimateBotPodDistance )
   + w2 · Σ yos
   + w3 · Σ us
```

with `w1=1`, `w2=-40`, `w3=1000`. The two routing terms are the only places in the objective where a path-cost estimate is consumed.

### 2.2 The cost estimates currently used

* `EstimatePodStationDistance(Pod, OutputStation)` (M1GManager.cs:134) reads from `DistanceSet[station][pod]` (`OrderManager.cs:70`, populated at `OrderManager.cs:89-99` from `Distances.CalculateShortestPathPodSafe1`). This is a precomputed **pod-safe** shortest distance — not pure Manhattan, but still a static graph distance with no traffic information and no time semantics.
* `EstimateBotPodDistance(Bot, Pod)` (M1GManager.cs:117) calls `Distances.CalculateShortestPath` (`Distances.cs:23` → `MetaInfoManager.ShortestPathManager.GetShortestPath(..., emulatePodCarrying=false)`), again a static graph distance.

Neither call considers (a) when the trip would actually start, (b) which other bots are reserving nodes along the way, (c) whether the pod-to-station leg starts at `t0 + T_robot_to_pod + T_lift` rather than `t0`, (d) station-queue pressure, or (e) WHCA\*-induced waiting delays.

### 2.3 Why the obvious "fixes" don't work

* **Plug in WHCA\* output directly.** WHCA\*'s reservation window is `LengthOfAWindow = 15.0` s (`WHCAnMethod.cs:23`). A bot-to-pod trip in `small + m1g_time` averages tens of seconds; a pod-to-station leg, often longer. Treating the 15-second window cost as the full leg cost is a categorical type error.
* **Run a CBS / WHCA\* dry-run for each candidate.** Mutates `_reservationTable` (`WHCAnMethod.cs:152, 229`) and changes RRA\* state (`WHCAnMethod.cs:124, 164`). This is exactly what the rolled-back `PPCostFeedbackEstimator` attempted, with the result documented in `project_prt_implementation_state.md` (memory leaks, deadlock at t≈6981 s, M1G decisions dropping from ~850 to 192).
* **Full simulation rollout per candidate.** For 10 bots × 60 pods × 2 stations = 1200 candidates per epoch, this is computationally hopeless at decision time.
* **Replace the M1G objective with raw ETA minimisation.** Violates the thesis methodology and the user's explicit direction (`w1/w2/w3` are preserved). It also damages pod value, order-served reward, and unused-capacity penalty, none of which are about routing.

### 2.4 What we actually need

A function

```
EstimateTwoLegTime(robot r, pod p, station s, currentTime t0)
    → (T_rp, T_ps)
```

that returns a *plausible* estimate of the robot-to-pod and pod-to-station leg durations conditioned on currently observable traffic, evaluated as cheaply as `O(|path|)` per query and with **zero side effects** on the WHCA\* state. This estimate is then injected as the coefficient of routing-sensitive M1G variables. The estimator is not in the planning loop; it provides decision-time information only.

---

## 3. MAPFUU Original Method

### 3.1 Problem setting

MAPFUU (Street et al., AAMAS 2020, §1) addresses multi-robot navigation under uncertainty: every robot has a known start and goal, and the question is how to plan routes / policies so that congestion (the slowdown one robot causes to another sharing a region) is taken into account. The output is, per robot, a policy in an MDP whose transition probabilities are obtained from a shared structure called the Probabilistic Reservation Table (PRT). MAPFUU is **not** an OA / PS / TA system; it has no orders, no pods, no stations, no inventory state.

### 3.2 Topological map (MAPFUU §3, Def. 3.1)

The environment is `T = ⟨V, E, ρ⟩` where `V` is a finite set of nodes, `E ⊆ V × V` is a set of *bidirectional* edges, and

```
ρ : E × ℕ → Dist(ℝ_≥0)
```

assigns each `(edge, number-of-extra-robots-on-it)` pair a probability distribution over the duration for an additional robot to traverse that edge. Congestion modifies the duration *distribution*, not the geometric distance.

### 3.3 Congestion bands (MAPFUU §3, congestion modelling)

Instead of one duration distribution per integer count of co-occupants, MAPFUU groups counts into congestion bands

```
c0 = [0, 0]
c1 = [1, 3]
c2 = [4, 5]
c3 = [6, n−1]
```

This is purely a state-space reduction to keep PTD fitting and CTMC construction tractable.

### 3.4 Phase-Type Distribution (PTD)

For every (edge, band) pair, MAPFUU fits a phase-type distribution `ρ_C(e, c_j) ∈ Dist(ℝ_≥0)`. A PTD is the distribution of the absorption time of a finite-state CTMC; it can approximate any non-negative distribution arbitrarily well while remaining tractable for further CTMC composition. Intuitively, the same edge has mean duration ~5 s with `c_0`, ~8 s with `c_1`, ~15 s with `c_2`, etc.

### 3.5 Probabilistic Reservation Table — the structure (MAPFUU §4)

The PRT does **not** store `(edge, time-window, robot id)` tuples. It stores, per robot, a **route CTMC**:

```
PRT(r_i) = Q_i
```

where `Q_i` is the continuous-time Markov chain induced by robot `i`'s policy convolved with the per-edge PTDs along the realised path. Because each edge's traversal duration is a random variable, the robot's location at any future time `t` is itself a random variable; the CTMC encodes precisely the transient distribution over which edge robot `i` occupies at time `t`.

### 3.6 Route CTMC transient probability

For a robot whose route is `e_1 → e_2 → e_3` with durations `T_1, T_2, T_3` (independent random variables), the probability of being on each edge at time `t` is

```
Pr(robot on e_1 at time t) = Pr(0 ≤ t < T_1)
Pr(robot on e_2 at time t) = Pr(T_1 ≤ t < T_1 + T_2)
Pr(robot on e_3 at time t) = Pr(T_1 + T_2 ≤ t < T_1 + T_2 + T_3)
```

CTMC transient analysis returns these probabilities efficiently.

### 3.7 The `cong` query (MAPFUU §4)

Given a robot `r_i`, an edge `e`, a congestion band index `j`, and a time `t`, the PRT answers

```
cong(r_i, e, j, t) = Pr(robot i meets congestion band c_j when traversing e at time t)
```

The computation has three steps:

1. **Per-robot transient query.** For each *other* robot `h` already in the PRT, compute `Pr_h(e @ t)` from `Q_h`.
2. **Poisson-binomial composition.** Treat each `1[h on e at t]` as an independent Bernoulli with probability `Pr_h(e @ t)`. Let `Q = Σ_h X_h`. Then `Pr(Q = q | e @ t)` is given by the Poisson-binomial distribution.
3. **Band aggregation.** `Pr(c_j | e @ t) = Σ_{q ∈ c_j} Pr(Q = q | e @ t)`.

### 3.8 Single-robot MDP / Stochastic Shortest Path

For robot `r_i`, MAPFUU builds an MDP with

* state `s = (v, t)` where `v ∈ V` and `t` is the expected arrival time at `v`;
* action `a = e` choosing an outgoing edge;
* expected edge cost

```
cost((v, t), e) = Σ_j cong(r_i, e, j, t) · E[ρ_C(e, c_j)]
```

* transition `(v, t) →_{e} (v', t + cost)` (a discretisation of the underlying continuous-time process).

The SSP is solved with LRTDP to obtain a policy minimising expected time-to-goal.

### 3.9 Sequential / priority planning (MAPFUU §5)

MAPFUU does not solve a joint MMDP. Robots are ordered (priorities fixed exogenously — finding the optimal order is NP-hard, see MAPFUU §2). Robot 1 plans against an empty PRT, its policy is converted into a route CTMC and inserted; robot 2 plans against the PRT containing robot 1; and so on. The output is a best-response policy per robot under the assumption that earlier robots' policies are fixed.

### 3.10 Minimum requirements MAPFUU imposes

A clean list of what MAPFUU needs in order to run at all, useful as the falsification target for the gap analysis:

| # | Requirement | Failure mode if missing |
|---|---|---|
| R1 | Per-robot start–goal pair is known and stable | Cannot define the MDP |
| R2 | The whole policy of each robot is materialised (not just a 15 s window) | CTMC has nothing to integrate |
| R3 | Edge-keyed duration distributions `ρ_C(e, c_j)` exist for each band | `cong` query reduces to distance |
| R4 | Robots act asynchronously with continuous stochastic action durations | Synchronous MMDP collapses |
| R5 | A fixed priority order is supplied | Joint optimum is intractable |
| R6 | No mid-route re-assignment | PRT entries become stale and PTDs lose meaning |

---

## 4. RAWSim-O Current Implementation Assumptions

Each claim below is grounded in the codebase.

### 4.1 WHCA\* plans only a 15-second window

`WHCAnMethod.cs:23` sets `LengthOfAWindow = 15.0` (seconds). The planner is `WHCAnStarMethod` (`WHCAnMethod.cs:17`). Each agent's `SpaceTimeAStar` instance is constructed with horizon `currentTime + LengthOfAWindow` (`WHCAnMethod.cs:168`). Beyond this point the planner does *not* produce a time-attached trajectory.

`[CODE INSPECTION REQUIRED]` — confirm whether `LengthOfAWindow` is reset from an XConf parameter at runtime; default in code is 15 s but the simulator may override. This affects how far into the future the deterministic occupancy signal extends.

### 4.2 Reservation table is node-keyed and agent-anonymous

`ReservationTable` (`ReservationTable.cs:13`) stores `DisjointIntervalTree[]` indexed by node id (`ReservationTable.cs:28, 58`). The `Interval` struct (`ReservationTable.cs:611-650`) is `(Node, Start, End)` — no edge id, no agent id. WHCAnMethod constructs the table with `_storeAgentIds = false` (`WHCAnMethod.cs:67`); per-agent identity is only preserved in `_calculatedReservations` (`WHCAnMethod.cs:48`), a `Dictionary<int, List<Interval>>` keyed by agent id, maintained in parallel.

Edges are therefore *implicit*: an edge `(u, v)` is "occupied" by agent `a` over time interval `I` iff `_calculatedReservations[a]` contains an interval at `u` ending at `I.start` and an interval at `v` starting at `I.start` (modulo turn / wait tolerance). The PRT-style edge-keyed query MAPFUU performs natively must be reconstructed by the estimator.

### 4.3 Edges are directed under SingleLane

`Graph` (`Graph.cs:13`) carries `Edges[][]` (forward, `Graph.cs:45`) and `BackwardEdges[][]` (`Graph.cs:73`). The `Edge` struct (`Edge.cs:13-44`) has `From`, `To`, `Distance` (m), `Angle` (deg), `FromNodeInfo`, `ToNodeInfo`. There is no edge id; the natural key in any data table is the ordered pair `(From, To)`. Under `SingleLane=true` (used in the layouts under study), one-way aisles are guaranteed by the layout, so head-on conflicts on a single edge are absent and need not be modelled.

### 4.4 Time model: kinematic with explicit turn cost

`Physics.getTimeNeededToMove` (`Physics.cs:143, 193`) computes movement time from current speed, distance, and the bot's acceleration / deceleration / top-speed parameters; `Physics.getTimeNeededToTurn` (`Physics.cs:399`) computes the rotation cost from heading change. Neither function takes a load flag, but the bot's `Physics` instance is constructed differently for loaded vs unloaded states (relevant for pod-carrying segments). `Distances.CalculateShortestTimePath` (`Distances.cs:56`) wraps `MetaInfoManager.TimeEfficientPathManager`, which integrates these kinematic costs into a static "time-shortest path"; it is the natural baseline against which our congestion-aware estimate is compared (still ignores other bots' interference, but corrects for turn / acceleration effects).

### 4.5 Beyond the WHCA\* window: only the RRA\* abstract path is available

`ReverseResumableAStar` (`WHCAnMethod.cs:124, 164`) is constructed per agent and supplies the heuristic abstract path to `agent.DestinationNode`. It produces an ordered node list via `getPathAsNodeList(...)`. Critically, this list has **no time attached** — it is a sequence of nodes only. To use it as a "tail projection" beyond the window, our estimator must apply nominal `Physics` durations and the empirical duration table.

After the planning window WHCAnMethod also appends `[lastNode, lastTime, +∞)` (`WHCAnMethod.cs:233-238`), reserving the agent's final node indefinitely. This is the *only* persisted post-window signal, and it pollutes per-node occupancy counts beyond `t = lastTime` (see §13).

### 4.6 Bot waypoint events

`BotNormal.cs:1244` performs `CurrentWaypoint = NextWaypoint;` at the moment a bot completes a segment, immediately followed by `_arrivedAtWaypointThisTick = true;` (line 1247). This is the natural hook for logging `ArriveTime`. The corresponding "leave waypoint" event is the next `Update` tick after `NextWaypoint` is set; `[CODE INSPECTION REQUIRED]` to confirm the exact line where `NextWaypoint` is assigned on segment start (likely in the path-iteration block earlier in `BotNormal.Update`).

`OnReachedWaypoint(Waypoint)` (`BotNormal.cs:2247`) is only invoked under `RealWorldIntegrationEventDriven` mode and is therefore **unreliable** for offline-mode logging. Use the inline path-iteration hook instead.

### 4.7 M1G hook points and current cost semantics

* `EstimateBotPodDistance(Bot, Pod)` (`M1GManager.cs:117-132`) — uses `Distances.CalculateShortestPath` (graph distance, not pod-safe) then Manhattan fallback.
* `EstimatePodStationDistance(Pod, OutputStation)` (`M1GManager.cs:134-151`) — uses precomputed `DistanceSet[station][pod]` (pod-safe shortest path), then `CalculateShortestPathPodSafe1`, then Manhattan.
* Objective (`M1GManager.cs:563-570`): both estimates enter as coefficients on `xps` (pod-to-station) and `yrp` (bot-to-pod) variables, weighted by `w1`. The terms `w2 · Σ yos` and `w3 · Σ us` are independent of routing.

**These are the only two places that need to be touched to swap in a congestion-aware estimator.** Anything beyond them is out of scope.

### 4.8 No native per-edge traversal log

A grep across `RAWSimO.Core/Statistics` shows footprint and KPI logs but no per-bot-per-edge traversal record. The new estimator depends on building such a log; this is the dominant code surface of the proposed work (see §9).

---

## 5. Gap Analysis

| Aspect | MAPFUU contract | RAWSim-O reality | Implication for the adapted method |
|---|---|---|---|
| **Problem type** | Start–goal navigation under uncertainty | Online OA + PS + TA decision (M1G `M1GManager.cs:303-570`) | Need two-leg cost (bot→pod + pod→station), not one-leg policy |
| **Task structure** | Single start→goal per robot | Composite `r → p → (lift) → s` with future `t_pickup` | **Added:** lift-time anchor and forward-time congestion query at `t_pickup` |
| **Path planner** | Stochastic MDP / SSP solved with LRTDP | Deterministic WHCA\* (`WHCAnMethod.cs:90-258`) | **Dropped:** policy synthesis. WHCA\* keeps planning; estimator only scores |
| **Reservation structure** | PRT = per-robot route CTMC, edge-keyed | Node-keyed disjoint-interval tree (`ReservationTable.cs:28`), agent id only via parallel dictionary | **Approximated:** edge occupancy reconstructed from consecutive node intervals + `_calculatedReservations` lookup |
| **Duration model** | PTD per edge × band, composed in CTMC | Deterministic kinematic via `Physics` (`Physics.cs:143-399`) | **Replaced:** PTD by empirical mean per (edge, band); CTMC by sample-mean lookup |
| **Stochasticity source** | Edge ρ_C(e, c_j) distributions | None at the planner; only emergent from multi-bot interaction in the simulator | **Dropped:** intrinsic edge randomness. Variance enters only through congestion-band conditioning |
| **Congestion modelling** | Edge robot count → band → PTD | One-way aisles (`SingleLane=true`), downstream node blocking, merge pressure, station queue | **Added:** queue / merge / one-way features alongside the band concept |
| **Coupling with assignment** | Not modelled (no OA / PS / TA) | M1G assigns at every replan epoch | **Added:** the estimator must be query-only and consume identifiers (`Bot.ID`, `Pod.ID`, `Station.ID`) |
| **Long-horizon availability** | Inserted policy queryable at arbitrary future `t` | Only 15 s WHCA\* window + RRA\* untimed tail + `[lastNode, +∞)` terminal block | **Added:** tri-regime congestion forecast `C(e, t)` — in-window / RRA\*-projected / historical-prior |
| **Re-planning frequency** | Plan once per robot | Every M1G epoch (and WHCA\* every clocking step) | **Implication:** estimator caches must be invalidated per epoch, not held longer |
| **Priority / sequencing** | Fixed exogenous priority | M1G decides assignments jointly via MILP; WHCA\* uses a sort by remaining distance (`WHCAnMethod.cs:102`) | **Dropped:** MAPFUU's priority-planning assumption. M1G's MILP order replaces it |
| **Bidirectional edges?** | Yes (MAPFUU §3.1) | No under SingleLane (`Graph.cs:45` + layout) | **Simplification:** edge key is the ordered pair `(From, To)` |

The "Implication" column collapses to three actions across the table: **drop** the stochastic policy machinery (CTMC, PTD, MDP, Poisson-binomial), **approximate** edge occupancy via the node-keyed reservation table, **add** the missing pieces that MAPFUU did not need (station queue features, future-time query for the second leg, M1G-facing query API).

---

## 6. Proposed Adapted Method

A **congestion-aware travel-time estimator for M1G**. The complete contract:

> Given a candidate triple `(bot r, pod p, station s)` and the current simulator time `t0`, return `(T_rp, T_ps)`: the estimated robot-to-pod and pod-to-station leg durations, conditioned on currently observable WHCA\* reservations and an offline empirical edge-duration table. Side effects: none.

### 6.1 What we keep from MAPFUU

* **Edge × congestion-band duration table.** Equivalent to MAPFUU's `ρ_C(e, c_j)` but reduced from a phase-type distribution to a sample mean (and optional p50, p90 for diagnostics).
* **Per-query congestion classification.** At each edge along the candidate path, we classify the current congestion into one of the bands and look up the conditional mean duration.
* **Two-leg query that respects future time.** The pod-to-station leg's congestion is read at `t_pickup = t0 + T_rp + T_lift`, not at `t0`. This is the structural feature that distinguishes the estimator from a static lookup.
* **Sequential / chained evaluation.** Each edge is added to the running clock before the next edge's congestion is sampled, mirroring MAPFUU's transient propagation.

### 6.2 What we drop or replace

| MAPFUU element | Drop / replace with | Reason |
|---|---|---|
| Phase-type distribution `ρ_C` | Sample mean `μ̂(e, c)` (plus p50, p90 for diagnostics) | Estimator does not need the full distribution; M1G consumes a scalar cost. PTD fitting requires hundreds of samples per (edge, band), unrealistic in early experiments |
| Route CTMC `Q_i` | None — not constructed | Without PTD there is no distribution to integrate |
| Poisson-binomial composition | Deterministic count of overlapping reservations at `(e, t)` within the WHCA\* window | The reservations are deterministic intervals — Bernoulli probabilities collapse to 0/1 |
| MDP / SSP / LRTDP | None — not solved | We are not planning. WHCA\* still plans |
| Per-robot policy materialised | RRA\* abstract node list + `Physics` projection | Best we can do without a real future trajectory |
| Priority sequential planning | Replaced by M1G's MILP joint decision | Out of scope |

### 6.3 The estimator's standing contract

* **Read-only** with respect to `WHCAnMethod._reservationTable`, `WHCAnMethod.rraStars`, `Graph`, and every bot's `Path` / `CurrentWaypoint`. The estimator must never temporarily modify `IsLocked` / `IsObstacle` on graph nodes (the failure mode of the rolled-back `PPCostFeedbackEstimator`).
* **Stateless across queries** except for the offline-built `EdgeDurationTable` and the lazily refreshed `CongestionMap` snapshot per M1G epoch.
* **Deterministic** given a fixed reservation state and table. No internal RNG.
* **Bounded compute**: O(|path|) per leg query; two-leg query is O(|path_rp| + |path_ps|).
* **Graceful fallback**: if the candidate path cannot be obtained, return `Distances.CalculateShortestTimePath`-based estimate so M1G can still solve.

---

## 7. Algorithms / Pseudocode

### 7.1 Two-leg estimator (top-level)

```
function EstimateTwoLegTime(robot r, pod p, station s, time t0):
    path_rp = Graph.shortestPath(r.currentNode, p.waypoint.node)        # static, pod-safe-aware
    path_ps = Graph.shortestPath(p.waypoint.node, s.waypoint.node)

    if path_rp is empty or path_ps is empty:
        return FallbackToStaticTime(r, p, s)

    T_rp = WalkPath(path_rp, t0, carryingPod=false)
    t_pickup = t0 + T_rp + T_lift
    T_ps = WalkPath(path_ps, t_pickup, carryingPod=true)

    return (T_rp, T_ps)
```

### 7.2 Per-leg walk with three-regime congestion lookup

```
function WalkPath(path, t_start, carryingPod):
    t = t_start
    for each directed edge e = (u, v) in path:
        c = C(e, t)                                  # congestion band at time t
        μ = LookupDuration(e, c, mode=carryingPod)   # with shrinkage fallback
        # Optional: add turn time relative to previous edge angle for first-class kinematics
        Δturn = Physics.getTimeNeededToTurn(prevAngle, e.Angle)
        t = t + μ + Δturn
        prevAngle = e.Angle
    return t - t_start
```

* `T_lift` is the `PodTransferTime` constant from the layout (`[CODE INSPECTION REQUIRED]` — confirm exact symbol; the small-layout value is 2.2 s).
* If `LookupDuration` returns the table's "no data" sentinel, fall back through edge-type → global per the shrinkage rule in §10.

### 7.3 Three-regime congestion forecast `C(e, t)`

```
function C(edge e, time t):
    t_now = Instance.Controller.CurrentTime
    if t ≤ t_now + LengthOfAWindow:
        # Regime A: deterministic from reservation table
        k = CountReservationsOnEdge(e, t)
    elif t ≤ t_now + RRA_horizon:
        # Regime B: project from each agent's RRA* abstract node path with nominal physics
        k = CountProjectedAgentsOnEdge(e, t)
    else:
        # Regime C: historical prior
        return HistoricalEdgeBand(e)

    return BandIndex(k)        # spec §2.3 default thresholds
```

```
function CountReservationsOnEdge(edge e = (u, v), time t):
    # An agent occupies edge (u, v) over interval I iff
    #   _calculatedReservations[a] has an interval at u ending at I.start
    #   AND an interval at v starting at I.start (within tolerance)
    count = 0
    for each agent_id a in _calculatedReservations:
        intervals = _calculatedReservations[a]
        if any consecutive pair (i_u, i_v) in intervals satisfies
              i_u.Node == u and i_v.Node == v
              and i_u.End ≤ t ≤ i_v.Start                       # transit moment
              and (i_u.End − tolerance) ≤ t ≤ (i_v.Start + tolerance):
            count += 1
    return count
```

`[CODE INSPECTION REQUIRED]` — confirm the precise semantics of consecutive `Interval`s in `_calculatedReservations[a]`. From the WHCA\* construction in `WHCAnMethod.cs:222-238` it appears each `Interval` is the node-occupancy span; thus the edge-traversal interval lies *between* the End of the predecessor node's interval and the Start of the successor's. This is also consistent with `ESpaceTimeAStar.GetPathAndReservations` (`ESpaceTimeAStar.cs:894-940`) which iterates node-by-node, attaching wait time and turn time to each node visit.

```
function CountProjectedAgentsOnEdge(edge e, time t):
    count = 0
    for each agent a:
        if a has an RRA* state:
            nodes = rraStars[a.ID].getPathAsNodeList(a.NextNode)
            project nominal arrival times along nodes
              starting from a.NextNode at currentTime,
              using Physics.getTimeNeededToMove with empty-load defaults
            if projected (u, v) traversal interval contains t:
                count += 1
    return count
```

### 7.4 Static path source

`path_rp` and `path_ps` come from a Dijkstra over `Graph`, using `Edge.Distance` as the edge weight. We do *not* call WHCA\* dry-runs; the estimator must not write to any reservation table. For pod-to-station, use the pod-safe edge filter consistent with `Distances.CalculateShortestPathPodSafe1` to avoid pretending the carrying bot will traverse a storage cell.

---

## 8. Data Structures

### 8.1 EdgeDurationTable (persistent, offline-built)

```
EdgeDurationTable :  Map<(int From, int To, BandIndex c, Mode m), DurationStat>
DurationStat       :  struct { double mean; double p50; double p90; int count }
Mode               :  enum { Empty, Loaded }            # optional; absent in v1
BandIndex          :  enum { B0=[0,0], B1=[1,3], B2=[4,5], B3=[6,n−1] }
```

* **Build cadence:** offline, after each instrumented run. Re-built from CSV by an external Python script (no in-sim cost).
* **Refresh policy:** static during a simulation. A new table is loaded at sim start and never mutated.
* **Lifecycle:** loaded by `M1GManager` (or an auxiliary `CostEstimatorService`) at simulation start; lives until shutdown.

### 8.2 EdgeTypePrior (persistent, offline-built)

```
EdgeTypePrior      :  Map<(EdgeType t, BandIndex c, Mode m), DurationStat>
EdgeType           :  enum { Aisle, Intersection, StationApproach, StationQueue, PodStorageAccess, Other }
```

* Same lifecycle as `EdgeDurationTable`. Used as the back-off when a per-edge sample count is below the smoothing threshold (§10).
* `[CODE INSPECTION REQUIRED]` — confirm whether `Waypoint.IsBottleneck` or similar flags exist on `Waypoint` (`Waypoints/Waypoint.cs:21`) and on `OutputStation`'s `IQueuesOwner` queue waypoints (`OutputStation.cs:22`, `PathManager.cs:656`). If absent, edge types must be derived geometrically (in-degree / out-degree of the underlying nodes from `Graph.Edges` / `Graph.BackwardEdges`).

### 8.3 CongestionMap (transient, per-M1G-epoch)

```
CongestionMap     :  Map<(int From, int To, double TimeBucket), BandIndex>
TimeBucket width  :  1.0 s (configurable)
```

* Computed lazily on first query for an edge within an epoch, cached for the rest of that epoch's M1G solve.
* Cleared at the next M1G replan tick.

### 8.4 RuntimeContext (transient, per-query)

```
RuntimeContext :
    Graph             graph
    ReservationTable  reservationTable          # read-only handle
    Dict<int,List<Interval>>  calculatedReservations   # read-only handle
    Dict<int,RRA*>    rraStars                  # read-only handle
    Physics           defaultPhysicsEmpty
    Physics           defaultPhysicsLoaded
    double            t_now
    EdgeDurationTable edgeTable
    EdgeTypePrior     typePrior
    CongestionMap     cmap (epoch-scoped)
```

---

## 9. Logging Requirements

Two logs are added at simulator startup; both write CSV files alongside the existing `kpi_report.csv`.

### 9.1 Traversal Log (one row per edge crossing)

Trigger: every tick where `_arrivedAtWaypointThisTick == true` in `BotNormal.Update` (`BotNormal.cs:1247`). The row describes the *just-completed* edge `(CurrentWaypoint.previous, CurrentWaypoint)`.

Columns:

```
SimId, Seed, BotId, FromWaypoint, ToWaypoint, EdgeFrom, EdgeTo,
ReadyTime, LeaveTime, ArriveTime, WaitBeforeEdge, MoveTime, SegmentTime,
CarryingPod, PodId, LegType, StationId, Planner, CurrentTime
```

Semantics:
* `ReadyTime` — when the bot's controller emitted the path step that targets `ToWaypoint`. **Source TBD** — `[CODE INSPECTION REQUIRED]` — likely accessible in the WHCA\* `Path.NextAction` queueing point (`BotNormal` reads `Path.NextAction`). The natural definition is "time at which `NextWaypoint` was first assigned" rather than "when the bot was first idle at `FromWaypoint`".
* `LeaveTime` — when the bot starts physical motion toward `ToWaypoint` (after wait + turn).
* `ArriveTime` — set at `BotNormal.cs:1244`.
* `WaitBeforeEdge = LeaveTime − ReadyTime`.
* `MoveTime = ArriveTime − LeaveTime`.
* `SegmentTime = ArriveTime − ReadyTime`. **Use `SegmentTime` as the regression target**, per spec §5.2.
* `CarryingPod ∈ {0, 1}` derived from `Bot.Pod != null`.
* `LegType ∈ {RobotToPod, PodToStation, Return, Idle}` — inferred from the bot's task state machine (`BotNormal`'s task class — `[CODE INSPECTION REQUIRED]`).
* `StationId` filled when `LegType ∈ {PodToStation, Return}`.

### 9.2 Congestion Feature Log (one row per edge entry)

Trigger: same as 9.1, but the row is filled at `ReadyTime` (or the closest sampling point before motion start) so that features describe the state the bot faced when committing to the edge.

Columns:

```
SimId, Seed, BotId, EdgeFrom, EdgeTo, ReadyTime,
EdgeReservationCount_H,         # number of distinct agents whose reservations overlap (EdgeFrom, EdgeTo) within [ReadyTime, ReadyTime+H]
DownstreamNodeOccupancy,        # 0/1 — is ToWaypoint reserved by another agent in [ReadyTime, ReadyTime+H]
MergePressure,                  # number of in-edges of EdgeFrom with reservations overlapping [ReadyTime, ReadyTime+H]
StationQueuePressure,           # if EdgeTo is near a station: queue length on the station's IQueuesOwner queue
LocalBotDensity,                # bots within R metres of EdgeFrom
IsStationEntrance,              # bool — EdgeTo equals a station's QueueWaypoint
IsMergeEdge,                    # bool — EdgeFrom has in-degree ≥ 2
IsBottleneckEdge,               # bool — heuristic, e.g. SingleLane corridor segment
EdgeType                        # categorical (see §8.2)
```

`H` — feature horizon, default 30 s (twice the WHCA\* window) — configurable.

### 9.3 Where the rows are emitted

A small `TraversalLogger` component subscribes to the existing `Statistics` plumbing (the same code path used by `DataPoint.cs` `FootPrintEntry`). It writes via `StreamWriter` to `traversal_log.csv` and `congestion_features.csv` in the output directory of each run. No new file format; rows are appended.

---

## 10. Offline Table Construction

After a logging run produces `traversal_log.csv` and `congestion_features.csv`, an external Python script builds the tables.

### 10.1 Build pipeline

```
1.  Join traversal_log.csv with congestion_features.csv on (BotId, EdgeFrom, EdgeTo, ReadyTime).
2.  Derive ConcurrentBotsOnEdge from EdgeReservationCount_H using the band thresholds (§2.3).
3.  Group by (EdgeFrom, EdgeTo, Band, Mode) and compute count, mean, p50, p90 of SegmentTime.
4.  Group by (EdgeType, Band, Mode) similarly to obtain the back-off table.
5.  Group globally (Band, Mode) to obtain the deepest back-off.
6.  Apply shrinkage smoothing (§10.2).
7.  Emit edge_duration_table.csv and edge_type_prior.csv.
```

### 10.2 Shrinkage smoothing

For each `(e, c, m)` with sample count `n` and per-edge mean `μ_edge`, define

```
μ̂(e, c, m) = (n / (n + k)) · μ_edge(e, c, m)
           + (k / (n + k)) · μ_type(type(e), c, m)
```

with default `k = 20`. Same rule cascades to back off from edge-type to global if the type cell is itself sparse.

### 10.3 Band collapse on small layouts

With `n_bots ≤ 6` (the `small` layout), bands `c_2 = [4, 5]` and `c_3 = [6, n−1]` collapse — `c_3` is empty by definition. The build script must detect this and either (i) merge `c_2` and `c_3` into a "high congestion" composite, or (ii) drop `c_3` entirely. Recorded as a Risk in §13.

### 10.4 Sanity outputs from the build

* Per-edge fit plot: empirical `(c, mean)` vs. shrunk `μ̂` (line plot).
* Per-edge-type heatmap: bands × edge-types → mean duration, sample count.
* Coverage report: percentage of edges with `count ≥ k`, fraction of `(e, c)` cells fully relying on back-off.

---

## 11. M1G Integration Plan

Phased to honour the previous failure's lessons: instrument and validate before touching the objective coefficient feed.

### 11.1 Phase A — Logging only (1–3 d, no behavioural change)

Add `TraversalLogger` and `CongestionFeatureLogger` per §9. Run `small + m1g_time + seed ∈ {0, 1, 2, 3, 4}` for 7200 s each. **KPIs must match the verified baseline (TP=327.5, PO=3.047, OD=23.18, RD=15 183 on seed=0)**; any drift indicates the logger introduced a side effect and must be fixed before continuing.

### 11.2 Phase B — Offline estimator validation (2–3 d, still no M1G change)

Build the tables per §10. Implement `EstimateTwoLegTime` as a standalone C# library or, more pragmatically for the first pass, as a Python reference that consumes the same CSVs. Validation:

| Metric | Definition | Baseline to beat |
|---|---|---|
| MAE per leg | mean(|T̂ − T_real|) | static distance / nominal speed → time |
| RMSE per leg | √(mean((T̂ − T_real)²)) | same |
| Spearman ρ | rank correlation on T̂ vs T_real | same |
| Top-1 candidate agreement | fraction of (r, p, s) triples where static and congestion-aware estimators pick the same best candidate | reference only |
| Error per band | MAE conditional on observed congestion band | identify where the estimator is worst |

Pass criterion: MAE on `T_robot_to_pod` improves vs `Distances.CalculateShortestTimePath`-based baseline by **at least 15%** on the small layout. If not met, iterate on band thresholds, smoothing `k`, or feature definitions before moving to Phase C.

### 11.3 Phase C — M1G integration (surgical, 1 d)

Add a new boolean config flag `UseCongestionAwareCost` to `M1GConfiguration`. Default off. When on, the two estimator bodies in `M1GManager.cs:117-151` swap their internals as follows.

**Structural consequence to flag now.** The two-leg estimate is a function of `(r, p, s)`, but the current MILP cost feed uses the *separable* coefficients `EstimateBotPodDistance(r, p)` on `yrp[r,p]` and `EstimatePodStationDistance(p, s)` on `xps[p,s]`. To inject a non-separable cost without restructuring the MILP, the cleanest path is:

* Keep `yrp` and `xps` with their existing static-distance coefficients (or zero, depending on calibration), *and*
* Add a new cost term on the existing `dops[o, p, s]` linking variable (`M1GManager.cs:345-389`):

```
+ w_pp · Σ dops[o, p, s] · EstimateTwoLegTimeAggregate(p, s)
```

where the aggregation collapses the `r` dimension via either (i) a min over candidate bots, (ii) a mean over candidate bots, or (iii) decision-time choice of the assigned bot from the current `yrp` LP relaxation. The user has explicitly forbidden adding `w4 / w5`, so `w_pp` is realised by *re-purposing* the existing `w1` weight on the routing terms, not as a new objective term.

`[DESIGN DECISION REQUIRED]` — which aggregation to choose. Recommendation: option (ii), mean across candidate bots, because (i) is non-convex w.r.t. assignment and (iii) requires a two-pass MILP solve. The mean is the cheapest convex relaxation and preserves the MILP shape.

### 11.4 What we do NOT touch in Phase C

* `w2 · Σ yos` (orders served reward)
* `w3 · Σ us` (unused capacity penalty)
* `dops` linking constraints (`M1GManager.cs:608-628`)
* `xps / yos / yaos / yrp / us` variable definitions
* All other Order / Pod / Station managers
* WHCA\* and the reservation table

### 11.5 Phase C validation (1 d)

Re-run `small + m1g_time` across seeds 0–4 with `UseCongestionAwareCost = false` and `= true`. The **non-degradation criterion**: for each KPI in `{TP, PO, OD, RD}`, the proportion of seeds where the congestion-aware run is worse must be ≤ 50% (Wilcoxon p > 0.05 in either direction is acceptable for non-degradation). Improvement on at least one KPI with p < 0.05 is the success criterion for promoting the flag to default on medium layouts.

---

## 12. Validation Plan

Three nested loops, each gated by the prior.

### 12.1 Loop A — per-edge regression sanity

For each `(edge, band, mode)` cell with `count ≥ 5`, plot `predicted μ̂` against the empirical sample mean; compute R² per edge type. Outliers > 3σ are flagged for inspection before Loop B.

### 12.2 Loop B — per-leg accuracy

For each `(estimate, real)` pair from the held-out portion of the traversal log:

| Metric | Target |
|---|---|
| MAE (RobotToPod) | ≤ 0.85 × MAE(static-time) |
| MAE (PodToStation) | ≤ 0.85 × MAE(static-time) |
| Spearman ρ | ≥ 0.6 |
| Bias | |mean(T̂ − T_real)| ≤ 0.5 s |
| Conditional error per band | error grows monotonically with band index but with bounded slope |

### 12.3 Loop C — per-decision impact (offline, no live swap)

Replay the first 100 M1G epochs of a baseline run. At each epoch, compute the candidate scoring under (a) static distance and (b) congestion-aware estimate, **without changing the assignment that actually executed**. Report:

* Top-1 agreement — fraction of epochs where both estimators rank the same `(r, p, s)` first.
* Spearman ρ over the full ranking.
* Distribution of rank flips per epoch.

A useful side metric: among the cases where the rankings disagree, what is the empirical `T_real` of each estimator's preferred candidate? If congestion-aware preferred candidates execute faster on average, that is direct evidence the swap will help.

### 12.4 Loop D — KPI A/B test under live swap (Phase C)

Per §11.5.

### 12.5 Reporting bundle

Per run, emit `validation_report.md` with:
* baseline KPI snapshot,
* per-leg accuracy tables and plots,
* per-decision agreement statistics,
* the Wilcoxon test results,
* sample-count coverage map.

---

## 13. Risks and Limitations

### 13.1 Inherent (spec §9)

1. The congestion table is an estimate, not a guarantee of future cost.
2. WHCA\* exposes only a 15 s reservation window; everything beyond is projected.
3. The pod-to-station leg starts at a *future* `t_pickup`; intervening reservations can change.
4. Unknown future paths of bots that have not yet committed are invisible.
5. Per-edge samples are sparse on cold edges.
6. High-congestion samples are imbalanced; small-layout runs may have no `c_3` data at all.
7. Pure ETA minimisation hurts pod value, energy, and order-completion incentives — hence the spec's insistence on keeping ETA as a coefficient on routing terms only.
8. Estimated-fastest routes may increase distance / energy if `w1` is not recalibrated.
9. The estimator might steer M1G away from congested but high-value pods.
10. Production swap requires statistical validation before promotion.

### 13.2 RAWSim-O-specific

11. **`[lastNode, +∞)` reservation pollutes counts.** WHCAnMethod appends an infinite reservation at every agent's last-known node (`WHCAnMethod.cs:233-238`). When counting `EdgeReservationCount_H` we must exclude or clamp these tails, otherwise idle bots inflate every adjacent edge's congestion band.
12. **The current "distance" baseline is not pure distance.** `EstimatePodStationDistance` reads from `DistanceSet`, which is built from `CalculateShortestPathPodSafe1` (`OrderManager.cs:97`). Calling the baseline "distance" in the paper without qualification is imprecise; the design note and any future paper must specify "pod-safe shortest path".
13. **Two-leg compute scales as R × P × S × |path|.** On `small + m1g_time` with 10 bots × 60 candidate pods × 2 stations × ~30 edges per leg, the per-epoch cost is on the order of 36 000 lookups plus path computation. Path Dijkstra dominates; either pre-compute pairwise shortest paths once per simulation (analogous to `DistanceSet`) or memoise within an epoch.
14. **Past failure precedent.** `PPCostFeedbackEstimator` (commit 8381bc5, dropped) attempted a similar swap but mutated graph state to drive `SpaceTimeAStar` and deadlocked the planner. Our design forbids any in-place mutation of `_reservationTable`, `_calculatedReservations`, `Graph.NodeInfo`, or RRA\* state.
15. **LengthOfAWindow may be overridden.** Hard-coded default is 15 s but configurable — log the realised value alongside the traversal log so the table consumer can re-derive the in-window regime threshold.
16. **`OnReachedWaypoint` is event-driven-mode only.** Logger must use the inline path-iteration hook (`BotNormal.cs:1244-1247`), not the event API.

---

## 14. Open Questions

(Each item must be resolved during implementation; this section accumulates the unresolved code-inspection items and design choices for the implementation plan to address.)

| ID | Question | Affected sections | Resolution path |
|---|---|---|---|
| Q1 | Exact shape of consecutive `Interval`s in `_calculatedReservations[a]` — is edge transit always inferred from `prev.End → next.Start`? | §4.2, §7.3 | Trace `ESpaceTimeAStar.GetPathAndReservations` (`ESpaceTimeAStar.cs:894-940`) end-to-end |
| Q2 | Does any XConf override `WHCAnMethod.LengthOfAWindow`? | §4.1, §13.15 | Grep `LengthOfAWindow` setter; inspect `WHCAnStarPathManager` |
| Q3 | Where is `NextWaypoint` first assigned per segment (defines `ReadyTime`)? | §4.6, §9.1 | Trace `BotNormal.Update` task-iteration block |
| Q4 | Does `Waypoint`/`Edge` carry intrinsic edge-type flags, or must they be derived? | §8.2 | Inspect `Waypoints/Waypoint.cs`, `NodeInfo` |
| Q5 | Time-bucket width for `CongestionMap` (default 1 s)? | §8.3 | Choose by per-leg accuracy in Loop B |
| Q6 | Band cutoffs for small layout — merge or drop `c_3`? | §10.3 | Choose during Phase B |
| Q7 | Aggregation for two-leg cost over candidate bots (mean / min / LP-weighted)? | §11.3 | Compare in Loop C |
| Q8 | Whether to split `Mode = {Empty, Loaded}` from day 1 or after enough samples? | §8.1 | Defer to Phase A's data; log both regardless |
| Q9 | Sample threshold `k` in shrinkage? | §10.2 | Sweep `k ∈ {5, 10, 20, 50}` in Loop A |
| Q10 | How to handle PRT-style query for pod-carrying bots that take wider turns? | §7.2 | If signal is weak in Loop B, skip; otherwise add `mode` to the table key |

---

## 15. Recommended Next Steps

Phased, with explicit checkpoints to prevent the next implementation from over-reaching.

### 15.1 Stage A — Instrumentation (1–3 d, no behavioural change)

A.1 Implement `TraversalLogger` + `CongestionFeatureLogger` per §9.
A.2 Run `small + m1g_time + seed ∈ {0..4}` × 7200 s; verify baseline KPIs unchanged.
A.3 Spot-check the first 100 rows of each CSV for sanity (negative times, missing PodId during pickup, etc.).

**Exit gate:** KPIs match the verified baseline to within numerical noise; ≥ 95% of rows pass the row-level sanity checks.

### 15.2 Stage B — Offline table + validation (2–3 d)

B.1 Python script: build `edge_duration_table.csv`, `edge_type_prior.csv`.
B.2 Implement `EstimateTwoLegTime` in Python first (faster iteration), consuming the same CSVs.
B.3 Run Loops A, B, C of §12.

**Exit gate:** per-leg MAE improvement ≥ 15% vs static-time baseline AND top-1 ranking agreement ≥ 70%.

### 15.3 Stage C — In-sim integration (1 d)

C.1 Port `EstimateTwoLegTime` to C# (`RAWSimO.Core/Metrics/CongestionAwareCostEstimator.cs`).
C.2 Add `UseCongestionAwareCost` flag to `M1GConfiguration`; wire the two estimator bodies in `M1GManager.cs:117-151`; inject the aggregated two-leg cost into the `dops` coefficient as described in §11.3.
C.3 Run `small + m1g_time + seed ∈ {0..4}` with the flag both off and on; produce side-by-side KPI table.

**Exit gate:** non-degradation criterion of §11.5 satisfied.

### 15.4 Stage D — Scale-up (deferred)

Only after Stage C passes: extend to medium / large layouts, sweep AMR counts, and prepare the ablation table for the thesis. This stage is intentionally not specified in detail here; it depends on whether Stage C indicates the estimator helps or merely doesn't hurt.

### 15.5 Worktree strategy (deferred until Stage A starts)

To be chosen at Stage A kickoff: new `feature/congestion-aware-cost` worktree (clean history) vs. reuse `feature/pp-cost-feedback` (already reset to `1009517`). Recommendation pending Stage A: a new branch keeps the historical PPCostFeedbackEstimator failure visible in `origin/joint` for the thesis discussion without confusing the new line of work.

### 15.6 Out of scope (recorded for transparency)

* Probabilistic upgrade (PTD / CTMC / Poisson-binomial) — future direction; estimator interface designed so that swapping the lookup body is enough.
* Replacing `Distances.CalculateShortestPathPodSafe1` cache — the existing cache stays; we only change how its output is consumed inside M1G.
* NN cost oracle (long-term thesis track) — referenced for thesis context only; not implemented under this design note.
* Multi-tier (multi-floor) extensions and elevator costs (`ElevatorEdge` in `Edge.cs:49`) — single-tier focus suffices for the spec.

---

## Appendix A — File / Line Index Cited

```
RAWSimO.MultiAgentPathFinding/
  Methods/WHCAnMethod.cs        17, 23, 48, 67, 90, 102, 124, 152, 164, 168, 222, 226, 229, 233-238, 258
  DataStructures/ReservationTable.cs   13, 28, 58, 600, 611-650
  DataStructures/DisjointIntervallTree.cs  (referenced; not cited line-by-line)
  Elements/Graph.cs             13, 45, 73, 134
  Elements/Edge.cs              13, 18, 23, 28, 34, 39, 43, 49
  Physics/Physics.cs            123, 132, 143, 193, 399
  Algorithms/AStar/ESpaceTimeAStar.cs  882, 894-940
  Algorithms/AStar/ReverseResumableAStar.cs (referenced)

RAWSimO.Core/
  Control/Defaults/OrderBatching/M1GManager.cs  55, 117, 134, 303-399, 345-389, 563-570, 608-628
  Control/OrderManager.cs       70, 89-99, 115-149
  Metrics/Distances.cs          23, 34, 45, 56, 67, 78, 89
  Management/MetaInformationManager.cs 91, 187, 202, 445, 583
  Bots/BotNormal.cs             1244, 1247, 2247
  Elements/Bot.cs               26, 468
  Elements/OutputStation.cs     22
  Waypoints/Waypoint.cs         21
  Control/PathManager.cs        656
```

## Appendix B — KPI Reference

| KPI | Formula | `kpi_report.csv` column | Baseline (`small + m1g_time + seed=0`, clean `1009517`, 7200 s) |
|---|---|---|---|
| TP — Throughput | orders completed / sim hours | `orders_per_hour` | 327.5 orders/h |
| PO — Pile-on | orders completed / output-station arrivals | `system_order_pile_on` | 3.047 |
| OD — Order Distance | total distance / orders completed | `order_distance_m` | 23.18 m/order |
| RD — Robot Distance | total distance | `total_distance_m` | 15 183 m |

Any congestion-aware run must report all four against this baseline (per-seed and across seeds 0–4 when available).

---

*End of design note.*
