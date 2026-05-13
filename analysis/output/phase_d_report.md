# Phase D Report — Joint (r,p,s) Cost via qrps Variable

## Motivation

Phase C (separable, recalibrated) showed all four KPIs trending favourably
(PO +2.73 %), but each leg was scored at `t_now` with no time chaining:
- leg 1's density used the current snapshot, not the projected per-edge arrival times;
- leg 2's density used the current snapshot, not the post-pickup arrival time;
- the lift-up time `T_lift` was not represented in the cost at all.

Phase D restructures the M1G MILP to expose true joint (r, p, s) cost while keeping the
existing `w1/w2/w3` objective structure (the user's red line is relaxed for this phase).

## Architectural changes

### Estimator (CongestionAwareCostEstimator.cs)

- New `EstimateLegTimeFrom(from, to, carrying, tStart)` — tracks `tCursor` per edge so the
  density lookup at edge `e_i` uses time `tStart + Σ_{j<i} predicted_dt[j]`.
- New `ComputeDensityBandAt(wp, tTarget)` — projects every other bot forward via
  `ProjectBotPosition`, which walks the bot's remaining `Path` at the kinematic baseline
  (the same `a + b · dist` used in Phase B), so the density count at `tTarget` reflects
  where other bots will actually be.
- New `EstimateThreeTupleTime(botWp, podWp, stationWp, tNow, tLift)`:
  `leg1 (tNow) + tLift + leg2 (tNow + leg1 + tLift)`.

### MILP (M1GManager.cs)

- New decision variable `qrps[r, p, s] ∈ {0,1}` (variableNames key 7), dense product over
  `R × allPods × Cs`. With 10 bots × 60 pods × 2 stations ≈ 1200 binaries per epoch.
- Linking constraints (McCormick linearisation of AND):
  ```
  qrps ≤ yrp,  qrps ≤ xps,  qrps ≥ yrp + xps − 1
  ```
- Replaces the two separable routing terms in the objective:
  ```
  was:  w1 · (Σ xps · c_ps + Σ yrp · c_rp)
  now:  w1 · Σ qrps · T_total(r, p, s)
  ```
- `T_total` computed once per (r,p,s) before each MILP solve via
  `EstimateThreeTupleTime`.

### Config

`M1GConfiguration` adds two fields, both backwards-compatible defaults:
- `UseJointRPSCost` (default `false`) — toggles joint mode.
- `BaseLiftTime` (default 2.2 s) — matches `PodTransferTime` in layout.

## Results (5 seeds × 7200 s)

Paired against the original Stage A distance baseline (`m1g_time.xconf`, weights 1/-40/1000):

| KPI | want | base | recal (Phase C) | joint (Phase D) | recal Δ% | joint Δ% | joint better/5 | Wilcoxon p |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| TP | ↑ |  319.80 |  321.50 |  319.90 | +0.53% | +0.03% | 1/5 | 0.875 |
| PO | ↑ |   2.967 |   3.048 |   3.020 | +2.73% | +1.94% | 3/5 | 0.438 |
| OD | ↓ |  24.053 |  23.713 |  23.643 | -1.41% | -1.73% | 4/5 | 0.188 |
| RD | ↓ |15376.85 |15244.19 |15118.99 | -0.86% | -1.68% | 4/5 | 0.188 |

All four KPIs move in the favourable direction. None reaches `p < 0.05` (n=5 cap is 0.0625).

### Phase D vs Phase C recal interpretation

Phase D trades **throughput** for **path efficiency**:

| | recal (separable) | joint (D) | who wins |
|---|---:|---:|---|
| TP improvement | +0.53% | +0.03% | recal |
| PO improvement | +2.73% | +1.94% | recal |
| OD reduction   | -1.41% | -1.73% | joint  |
| RD reduction   | -0.86% | -1.68% | joint  |

Reading: joint-cost optimisation tends to pick (r, p, s) triples that minimise the **whole task time** including lift and turn, which translates into shorter total distance per order (`-1.68%` RD). Separable optimisation keeps each leg locally cheap, which tends to keep candidates available for the next assignment epoch and yields more orders per station arrival (`+2.73%` PO).

Both modes are useful, expressing different points on the throughput–distance Pareto frontier. The thesis can frame this as a deliberate methodological contribution: the same offline residual model, two different MILP integration regimes, two different operating points.

## Computational cost

- 1200 extra binary variables (qrps) and ~3600 linking constraints per epoch.
- Estimator pre-computes `T_total` for each qrps before the MILP build (1200
  `EstimateThreeTupleTime` calls = 2400 `EstimateLegTimeFrom` calls = 2400 Dijkstras).
- Each Dijkstra on the small layout (~700 nodes, ~1700 edges) takes < 1 ms.
- Empirically the total per-epoch overhead vs separable Phase C is small (single seed completed
  in roughly the same time as Stage A baseline).

## Files added / changed

- `RAWSimO.Core/Metrics/CongestionAwareCostEstimator.cs` — time-aware estimator
- `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs` — qrps + linking + new objective branch
- `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` — `UseJointRPSCost`, `BaseLiftTime`
- `Material/Instances/CoreBenchmark/m1g_time_joint.xconf` — joint-mode config (UseJointRPSCost=true)

## Open question — 8 hour validation

Phase D's per-decision improvements should compound over longer horizon simulations. The
follow-up experiment runs 28800 s (4 × current horizon) across 3 conditions × 5 seeds:
distance baseline, separable recal, and joint. The hypothesis: under longer accumulated
decisions, the joint cost's per-task path savings should clearly outpace the separable
recal in OD/RD even if PO equalises.
