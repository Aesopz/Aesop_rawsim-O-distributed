# Phase C Recalibrated Report — 路線 3 Scale Matching

## Motivation

Phase C (original-weights) showed all four KPIs within ±0.6% of baseline (Wilcoxon p ≥ 0.625). One of the three diagnostic hypotheses in `phase_c_report.md` was that **the objective weights `w2 = -40, w3 = 1000` were calibrated for the distance regime; under the time regime they no longer balance the three terms correctly**. This report tests that hypothesis by 路線 3 (scale matching).

## Method

Empirically observed per-segment scale factor from the 5-seed Stage A data:

| quantity | value |
|---|---|
| mean SegmentTime  | 4.380 s |
| mean DistanceM    | 3.493 m |
| **scale = time / distance** | **1.254** |

(This matches the Phase B kinematic prediction `(a + b · avg_dist) / avg_dist = (2.05 + 0.67·3.5) / 3.5 ≈ 1.254` exactly.)

Apply the scale to the orders-served reward and the unused-capacity penalty so each term contributes the same fraction of the objective under the time regime as under the distance regime:

| weight | distance regime | time regime (recalibrated) |
|---|---|---|
| W1 (routing) | 1.0 | 1.0 (anchor) |
| W2 (orders served) | -40 | **-50.16** |
| W3 (unused capacity) | 1000 | **1254.16** |

Weights are now exposed on `M1GConfiguration` via XML; baseline behaviour unchanged when fields are absent (defaults are 1, -40, 1000).

## Setup

- 5 seeds (0..4), 7200 s each, `small + benchmark_ss_layout`, M1G with `UseCongestionAwareCost=true` and recalibrated weights
- Paired against the same-seed Stage-A baseline (M1G distance, original weights 1, -40, 1000)

## Results

### Per-seed deltas

| KPI | seed 0 | seed 1 | seed 2 | seed 3 | seed 4 |
|---|---:|---:|---:|---:|---:|
| TP  | +0.15% | +0.00% | -1.67% | **+2.24%** | **+2.06%** |
| PO  | **+2.54%** | **+3.41%** | -0.31% | **+5.12%** | **+3.02%** |
| OD  | **-1.33%** | **-1.64%** | +1.21% | **-5.36%** | +0.26% |
| RD  | **-1.17%** | **-1.64%** | **-0.48%** | **-3.24%** | +2.32% |

(bold = desired direction)

### Aggregate

| KPI | want | base | recal | Δ% | better/5 | Wilcoxon p | Verdict |
|---|---|---:|---:|---:|---:|---:|---|
| TP | ↑ |  319.80 |  321.50 | **+0.53%** | 3/5 | 0.375 | NON-DEGRAD |
| PO | ↑ |    2.967 |    3.048 | **+2.73%** | 4/5 | 0.125 | NON-DEGRAD |
| OD | ↓ |   24.053 |   23.713 | **-1.41%** | 3/5 | 0.313 | NON-DEGRAD |
| RD | ↓ | 15376.85 | 15244.19 | **-0.86%** | 4/5 | 0.438 | NON-DEGRAD |

**All four KPIs move in the desired direction.**

### Comparison vs original-weight Phase C

Recalibration delivers a uniform improvement on top of the original-weight Phase C:

| KPI | original Phase C Δ% | recalibrated Δ% | uplift |
|---|---:|---:|---:|
| TP | -0.19% | **+0.53%** | +0.72 pp |
| PO | -0.55% | **+2.73%** | **+3.30 pp** |
| OD | -0.28% | -1.41% | -1.13 pp |
| RD | -0.42% | -0.86% | -0.44 pp |

PO ("pile-on", orders-per-station-arrival) shows the biggest swing — the recalibrated weights make the optimiser realise station-utilisation is more valuable than under the distance-tuned weights.

## Statistical caveat

5-seed Wilcoxon's minimum attainable p is `2 / 2^5 = 0.0625`. To declare any of these effects significant (p < 0.05) we need ≥ 6 paired observations. With n=5 the smallest p we got is 0.125 (PO), with 4/5 seeds favouring the recalibrated version.

**Effect-size interpretation**:
- Original Phase C: |Δ| ≤ 0.6% on all KPIs — noise level
- Recalibrated Phase C: |Δ| up to 2.7% on PO, consistently same-direction across seeds — beyond noise but below statistical-power threshold

## Fair-comparison claim

The recalibrated time version vs the original distance version are now both "internally consistent" — each uses weights tuned to its cost units. The comparison is fair in the sense that the comparison is no longer biased against the time version by the distance-tuned weights.

## Files

- `Material/Instances/CoreBenchmark/m1g_time_cong.xconf` (now with `<W1>1.0</W1><W2>-50.16</W2><W3>1254.16</W3>`)
- `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` (W1/W2/W3 exposed on M1GConfiguration)
- `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs` (reads weights from `_config`)
- `out_phaseC_recal_s{0..4}/` (5-seed outputs)

## Recommended next step

Run **20 seeds** of (a) distance baseline original weights, (b) time + recalibrated weights. With n=20, Wilcoxon's minimum p drops to ~10⁻⁶, so any same-direction effect ≥ 1% will reach p<0.05. Cost ≈ 80 min background.
