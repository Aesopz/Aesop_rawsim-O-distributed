# Phase C Integration Report

**Setup:** `small + benchmark_ss_layout + m1g_time_cong` (M1G with `UseCongestionAwareCost=true`),
5 seeds × 7200 s. Compared against the matched-seed Stage A baseline (same logger configuration,
distance-based M1G).

**Estimator loaded:** 3 810 EDGE rows, 22 TYPE rows, 6 GLOBAL rows. Kinematic a=2.0493, b=0.6673.

## Per-seed deltas

| KPI | seed=0 | seed=1 | seed=2 | seed=3 | seed=4 |
|---|---:|---:|---:|---:|---:|
| TP (orders/h) | −2.44% | −1.75% | −0.61% | **+2.08%** | **+1.90%** |
| PO            | −2.44% | −1.75% | −2.39% | **+5.95%** | −1.79% |
| OD (m/order)  | +5.04% | **−1.86%** | +0.44% | **−5.06%** | +0.49% |
| RD (m)        | +2.47% | **−3.58%** | −0.17% | **−3.09%** | +2.40% |

(bold = move in the desirable direction)

## Aggregate

| KPI | want | baseline | cong | diff | diff% | better/5 | Wilcoxon p | Verdict |
|---|---|---:|---:|---:|---:|---:|---:|---|
| TP | ↑ | 319.80 | 319.20 | −0.60   | −0.19% | 2/5 | 1.000 | NON-DEGRAD |
| PO | ↑ |   2.967 |   2.951 |  −0.016 | −0.55% | 1/5 | 0.625 | NON-DEGRAD |
| OD | ↓ |  24.053 |  23.987 |  −0.066 | −0.28% | 2/5 | 1.000 | NON-DEGRAD |
| RD | ↓ |15376.85 |15311.68 | −65.17  | −0.42% | 3/5 | 0.625 | NON-DEGRAD |

**Exit gate (design_note.md §11.5): NON-DEGRADATION → PASSED on all four KPIs.**

No statistically significant improvement (Wilcoxon p ≥ 0.625 on every KPI), and the smallest p attainable with n = 5 paired observations is 1/32 ≈ 0.0625, so the gate cannot be answered with this sample size.

## Interpretation

The per-segment estimator achieves a 44.88% MAE reduction on segment-time prediction (Phase B), yet the in-sim KPI shift is essentially noise. The two facts are not contradictory: M1G decisions depend on **relative** cost differences between candidate `(r, p, s)` triples, not absolute time accuracy. Two effects neutralise most of the per-segment improvement:

1. **Distance is already a very strong ordering signal.** Most candidate triples have widely different leg lengths; the cheapest candidate by distance is usually the cheapest by congestion-aware time too. The per-edge residual (±0.2–0.5 s) is small compared with leg-level distance differences (~5–15 m → ~5–10 s of segment time).

2. **"Density now" is a weak proxy for "density when the bot will reach the edge."** The current C# estimator reads `LocalBotDensity` at the bot's current waypoint and reuses it for every edge of the planned path. For long legs this is a constant across distant edges that should actually see different densities. A truer estimate requires forward-projecting other bots' positions (the full MAPFUU PRT story).

## What this proves

- The end-to-end Phase C plumbing works: model file is loaded, both estimator hooks fire (`EstimateBotPodDistance` and `EstimatePodStationDistance`), M1G decisions change, and the simulator stays stable (no deadlocks, no crashes, no infinite loops — contrast with the PPCostFeedbackEstimator failure preserved in commit history as 8381bc5).
- The cost replacement is safe: every KPI remains within ±0.6% of baseline on the matched-seed test. This is the non-degradation contract that the design note demanded as a prerequisite for any future PRT extension.
- A naive deterministic per-edge residual is **insufficient** to flip M1G's decision boundary on this small layout with 10 bots. This is the central negative result of the deterministic PRT track and motivates the next two directions in the thesis plan: (i) probabilistic PRT with per-edge density forecasting and (ii) larger-scale layouts where congestion is a stronger source of variance.

## Suggested follow-up experiments

1. **Larger AMR counts.** Re-run on medium and large layouts with 30+ bots where the static distance baseline is known to underperform. The hypothesis: as congestion variance grows, the per-edge residual carries more decision-relevant signal.
2. **Density forecasting.** Replace `LocalBotDensity_at_current_t` with a forward-projection: for each edge `e` on the path, estimate density at the predicted arrival time at `e`'s `from` node, using either (a) other bots' remaining `Path` enumerations or (b) the deterministic PRT trajectory injection plan from `design_note.md §6`.
3. **Weight calibration.** The kinematic coefficients (a=2.05, b=0.67) and the relative scale between routing terms and `w2/w3` were not retuned for the time-units swap. A cost on the order of 30 s per leg may sit differently in the objective than a cost on the order of 10 m. Sweep `w1` ∈ {0.5, 1, 2, 4} and check whether any setting unlocks the improvement signal.
4. **Per-edge residual ablation.** Run two extra Phase C variants: (a) global-only residual (no per-edge, no edge-type), (b) edge-type-only (no per-edge) — to confirm that the per-edge resolution is what carries any small improvement at all.
5. **Wilcoxon power.** 5 seeds is the floor for paired non-parametric tests. Re-run with 20+ seeds before claiming any signed direction; the current data does not justify either "improves" or "degrades" claims at α=0.05.

## Files produced

| File | Contents |
|---|---|
| `analysis/output/congestion_cost_model.csv` | Self-describing model exported for the C# estimator |
| `Material/Instances/CoreBenchmark/congestion_cost_model.csv` | Same file, deployed for runtime loading |
| `Material/Instances/CoreBenchmark/m1g_time_cong.xconf` | Controller config with `UseCongestionAwareCost=true` |
| `RAWSimO.Core/Metrics/CongestionAwareCostEstimator.cs` | C# port, ~280 lines, Dijkstra + per-edge residual lookup |
| `out_phaseC_s{0..4}/` | 5-seed Phase C outputs |
