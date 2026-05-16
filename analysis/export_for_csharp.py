"""
Export the Phase B model in a format the C# estimator can load.

Emits a single self-describing CSV: ``congestion_cost_model.csv`` with sections.

Header (key=value comment lines):
    # kinematic_a = 1.9510
    # kinematic_b = 0.7780
    # shrinkage_k = 2
    # density_bands = 0,1,2
    # carrying_values = 0,1
    # built_at = 2026-05-13T...

Body rows (semicolon-separated):
    KIND;FROM;TO;EDGE_TYPE;CARRYING;DENSITY;RESIDUAL;COUNT;STD;P90

KIND ∈ {EDGE, TYPE, GLOBAL}. FROM/TO are integers (-1 for non-EDGE rows).
EDGE_TYPE is the categorical label (empty for GLOBAL rows).
RESIDUAL is the (shrunk for EDGE rows, raw for TYPE/GLOBAL) mean residual.
STD is the empirical sample std of (SegmentTime - kinematic_baseline) in that cell
(NaN-safe: 0.0 when only one sample).
P90 is the 90th percentile of the same.
Step E (distributional estimator): C# side can use (mean, std) for risk-aware
cost (mean + α·std) and (mean, p90) for tail-aware planning.
"""
from __future__ import annotations
import argparse
import datetime
import os
import pandas as pd
import numpy as np

from load_data import load_seeds
from build_table import density_band
from validate import fit_baseline, fit_congestion_table


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", nargs="+", default=[f"../out_stageA_s{s}" for s in range(5)])
    ap.add_argument("--out", default="output/congestion_cost_model.csv")
    ap.add_argument("-k", type=int, default=2)
    args = ap.parse_args()

    df = load_seeds(args.seeds)
    print(f"loaded {len(df)} merged rows from {len(args.seeds)} seeds")

    # Fit on the whole dataset (not LOSO) — this is the production model.
    a, b = fit_baseline(df)
    (a2, b2), edge_table, type_prior, global_prior = fit_congestion_table(df, k=args.k)
    # Sanity: a/b from fit_baseline and fit_congestion_table use same data, should match.
    assert abs(a - a2) < 1e-6 and abs(b - b2) < 1e-6, "baseline coeff mismatch"

    os.makedirs(os.path.dirname(args.out), exist_ok=True)

    with open(args.out, "w", encoding="utf-8", newline="") as f:
        f.write(f"# kinematic_a = {a:.6f}\n")
        f.write(f"# kinematic_b = {b:.6f}\n")
        f.write(f"# shrinkage_k = {args.k}\n")
        f.write(f"# density_bands = 0,1,2\n")
        f.write(f"# carrying_values = 0,1\n")
        f.write(f"# built_at = {datetime.datetime.utcnow().isoformat()}Z\n")
        f.write(f"# train_rows = {len(df)}\n")
        f.write(f"# seeds = {','.join(str(s) for s in sorted(df['Seed'].unique()))}\n")
        f.write("KIND;FROM;TO;EDGE_TYPE;CARRYING;DENSITY;RESIDUAL;COUNT;STD;P90\n")

        # EDGE rows
        for _, r in edge_table.iterrows():
            f.write(f"EDGE;{int(r['FromNode'])};{int(r['ToNode'])};{r['EdgeType']};"
                    f"{int(r['CarryingPod'])};{int(r['DensityBand'])};"
                    f"{float(r['residual_shrunk']):.6f};{int(r['count'])};"
                    f"{float(r['std']):.6f};{float(r['p90']):.6f}\n")

        # TYPE rows
        for _, r in type_prior.iterrows():
            f.write(f"TYPE;-1;-1;{r['EdgeType']};{int(r['CarryingPod'])};"
                    f"{int(r['DensityBand'])};{float(r['type_residual']):.6f};{int(r['count'])};"
                    f"{float(r['type_std']):.6f};{float(r['type_p90']):.6f}\n")

        # GLOBAL rows
        for _, r in global_prior.iterrows():
            f.write(f"GLOBAL;-1;-1;;{int(r['CarryingPod'])};{int(r['DensityBand'])};"
                    f"{float(r['global_residual']):.6f};{int(r['count'])};"
                    f"{float(r['global_std']):.6f};{float(r['global_p90']):.6f}\n")

    rows_edge = len(edge_table); rows_type = len(type_prior); rows_global = len(global_prior)
    print(f"wrote {args.out}")
    print(f"  kinematic: a={a:.4f}, b={b:.4f}")
    print(f"  EDGE rows: {rows_edge}")
    print(f"  TYPE rows: {rows_type}")
    print(f"  GLOBAL rows: {rows_global}")


if __name__ == "__main__":
    main()
