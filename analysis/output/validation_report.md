# Phase B Validation Report

**Data:** 19 473 merged segments across 5 seeds (`small + m1g_time + seed ∈ {0..4}`, 7200 s each).
**Cross-validation:** Leave-one-seed-out (LOSO). Train on 4 seeds, evaluate on the 5th. Average over the 5 folds.

## Estimator forms

```
baseline_linear:  T_hat = a + b · DistanceM        (2-parameter linear fit on train fold)

congestion_table: T_hat = (a + b · DistanceM)
                        + residual_table[FromNode, ToNode, CarryingPod, DensityBand]
                  with shrinkage k = 2 to type_prior → global_prior
                  DensityBand = clip(LocalBotDensity, 0, 2)
```

The congestion table models the **residual** from the kinematic baseline (turn overhead, congestion wait, edge-specific bias), not the full segment time. This avoids the per-edge cell sparsity problem that destroyed distance information in the earlier "predict absolute SegmentTime" variant.

## Headline results (LOSO mean ± std)

| Metric                | baseline_linear | congestion_table | Improvement |
|-----------------------|-----------------|------------------|-------------|
| MAE (s)               | 0.401           | 0.221            | **−44.88%** |
| RMSE (s)              | 0.632           | 0.538            | −14.88%     |
| Bias (s)              | +0.00004        | −0.013           | both clean  |
| Spearman ρ            | 0.887           | 0.921            | +0.034      |
| Per-seed MAE range    | 0.397–0.406     | 0.217–0.228      | tight       |

**Phase B exit gate (design_note.md §11.2): MAE improvement ≥ 15% → PASSED at 44.88%.**

## Per-seed consistency

| Held seed | base MAE | cong MAE | improve % |
|---|---|---|---|
| 0 | 0.397 | 0.217 | 45.47 |
| 1 | 0.400 | 0.220 | 45.02 |
| 2 | 0.406 | 0.224 | 44.84 |
| 3 | 0.404 | 0.228 | 43.56 |
| 4 | 0.399 | 0.217 | 45.53 |

All 5 folds show consistent 43.6–45.5% improvement; no fold-level anomalies.

## MAE by DensityBand

| DensityBand                | n     | baseline MAE | congestion MAE | reduction |
|----------------------------|-------|--------------|----------------|-----------|
| 0 (no other bots ≤3m)      | 2 246 | 0.351        | 0.200          | −43.0%    |
| 1 (1 other bot ≤3m)        | 1 143 | 0.477        | 0.298          | −37.5%    |
| 2 (≥2 other bots ≤3m)      |   505 | 0.455        | 0.142          | **−68.8%** |

The biggest accuracy improvement happens at high density — exactly the regime where the static distance estimator is worst. This is the structural evidence the cost feedback is worth propagating to M1G.

## Key design takeaways

1. **The kinematic baseline is strong.** SegmentTime is dominated by DistanceM. Any congestion-aware estimator that does not condition on DistanceM (or use an edge-key proxy that encodes it) will lose to a 2-parameter linear fit.
2. **Modelling the residual** (`T − kinematic`) is the correct factorisation. Residuals are small in magnitude (~0.5 s), so even noisy per-cell estimates carry useful signal.
3. **Light shrinkage (k = 2) wins.** With ~4 samples per cell, k = 20 over-pulls toward the type prior; k = 2 keeps per-edge specificity. Improvement vs baseline at k ∈ {2, 5, 10, 20}: 44.9% / 41.5% / 38.2% / 34.4%.
4. **Three-band density bucket suffices.** Higher band granularity gives no improvement on this dataset.
5. **DensityBand 2 captures the high-variance regime.** Reduction is 69% there vs 43% at zero density.

## Loop C (per-decision impact) — deferred

Loop C of design_note.md §12 requires the M1G candidate `(r, p, s)` triples evaluated at each decision epoch. These are not currently logged. Implementing Loop C requires either:
- adding a `m1g_decisions.csv` log in `M1GManager.cs` capturing each candidate's static cost vs congestion-aware cost, or
- replaying decisions offline using a recorded snapshot of bot / pod / station / reservation state per epoch.

Recommendation: defer until Phase C and observe whether the in-sim KPI sweep already settles the question.

## Caveats and follow-ups

- **Single layout, single bot count.** Only `small` layout with 10 bots tested. The good Phase B numbers do not guarantee transfer to medium / large layouts; re-run Phase B on each before promoting Phase C settings.
- **Empty bots show inverted density effect** (mean SegmentTime decreases with density in global prior). Likely confound: empty bots travel shorter aisle paths near stations, and high-density samples are concentrated there. Conditioning on per-edge captures this; the unconditioned global prior is misleading.
- **WaitBeforeEdge is rare (median 0, mean 0.07 s) but heavy-tailed** (max 16 s). The bulk of segment-time variance comes from edge length and turn count, not from queue wait. Stage C should not over-weight wait-driven cost terms.
- **DistanceM 1 m segments are 49% of all segments** with mean 2.75 s and std 0.77 s — most of the absolute MAE is spent here. Improvements on longer segments are larger in relative terms.

## Files produced

| File | Contents |
|---|---|
| `analysis/output/edge_duration_table.csv` | Per (From, To, Carrying, Density) cells with count / mean / p50 / p90 / std / shrunk mean |
| `analysis/output/edge_type_prior.csv` | (EdgeType, Carrying, Density) prior |
| `analysis/output/global_prior.csv` | (Carrying, Density) global fallback |
| `analysis/output/validation_overall.csv` | Per-fold metrics for both estimators |
| `analysis/output/validation_by_density.csv` | Per-fold per-density MAE |

## Recommendation

Promote to Phase C with the following parameters:

- Use the residual model: `T_hat = (a + b · DistanceM) + edge_residual_table[FromNode, ToNode, CarryingPod, DensityBand]`
- Shrinkage strength `k = 2`
- Three-band density bucket `band = min(LocalBotDensity, 2)`
- Fallback chain: per-edge residual → edge-type residual → global residual → 0

Phase C task: port the predictor to C# (`RAWSimO.Core/Metrics/CongestionAwareCostEstimator.cs`) and swap behind the `UseCongestionAwareCost` config flag in `M1GManager.cs:117-151`. Then re-run the multi-seed sweep against the verified baseline (TP/PO/OD/RD reference in `project_prt_implementation_state.md`).
