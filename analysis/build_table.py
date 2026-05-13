"""
Phase B.1: build the empirical edge duration table from traversal + congestion logs.

Outputs (in analysis/output/):
- edge_duration_table.csv   per (FromNode, ToNode, CarryingPod, DensityBand)
- edge_type_prior.csv       per (EdgeType, CarryingPod, DensityBand)
- global_prior.csv          per (CarryingPod, DensityBand)
- edge_duration_smoothed.csv  same as edge_duration_table.csv but with shrinkage applied

Shrinkage rule:
    μ̂(e,b,m) = (n/(n+k)) μ_edge(e,b,m) + (k/(n+k)) μ_type(type(e),b,m)
where k = 20 by default.
"""
from __future__ import annotations
import os
import argparse
import numpy as np
import pandas as pd

from load_data import load_seeds


def density_band(d: pd.Series) -> pd.Series:
    """Bucket LocalBotDensity into three bands: 0, 1, >=2."""
    return d.clip(0, 2)


def build_tables(df: pd.DataFrame, output_dir: str, k: int = 20) -> None:
    os.makedirs(output_dir, exist_ok=True)
    df = df.copy()
    df["DensityBand"] = density_band(df["LocalBotDensity"])
    df["SegmentTime"] = df["SegmentTime"].astype(float)

    edge_keys = ["FromNode", "ToNode", "CarryingPod", "DensityBand"]
    type_keys = ["EdgeType", "CarryingPod", "DensityBand"]
    global_keys = ["CarryingPod", "DensityBand"]

    def agg(group_cols):
        g = (
            df.groupby(group_cols)["SegmentTime"]
            .agg(count="count", mean="mean", p50=lambda x: float(np.percentile(x, 50)),
                 p90=lambda x: float(np.percentile(x, 90)), std="std")
            .reset_index()
        )
        return g

    edge_table = agg(edge_keys)
    type_prior = agg(type_keys)
    global_prior = agg(global_keys)

    # Attach edge_type to edge_table to feed shrinkage.
    edge_to_type = (
        df.groupby(["FromNode", "ToNode"])["EdgeType"]
        .agg(lambda s: s.mode().iat[0])
        .reset_index()
    )
    edge_table = edge_table.merge(edge_to_type, on=["FromNode", "ToNode"], how="left")

    # Shrinkage with type-prior fallback.
    type_prior_lookup = type_prior.set_index(["EdgeType", "CarryingPod", "DensityBand"])["mean"]
    edge_table = edge_table.merge(
        type_prior_lookup.rename("type_mean").reset_index(),
        on=["EdgeType", "CarryingPod", "DensityBand"], how="left",
    )
    global_lookup = global_prior.set_index(["CarryingPod", "DensityBand"])["mean"]
    edge_table = edge_table.merge(
        global_lookup.rename("global_mean").reset_index(),
        on=["CarryingPod", "DensityBand"], how="left",
    )
    edge_table["type_mean"] = edge_table["type_mean"].fillna(edge_table["global_mean"])
    edge_table["mu_shrunk"] = (
        edge_table["count"] / (edge_table["count"] + k) * edge_table["mean"]
        + k / (edge_table["count"] + k) * edge_table["type_mean"]
    )

    edge_table.to_csv(os.path.join(output_dir, "edge_duration_table.csv"), index=False, sep=";")
    type_prior.to_csv(os.path.join(output_dir, "edge_type_prior.csv"), index=False, sep=";")
    global_prior.to_csv(os.path.join(output_dir, "global_prior.csv"), index=False, sep=";")

    # Coverage report.
    total_cells = len(edge_table)
    sparse_cells = (edge_table["count"] < k).sum()
    print(f"edge_duration_table rows: {total_cells}")
    print(f"sparse cells (count < k={k}): {sparse_cells} ({100*sparse_cells/total_cells:.1f}%)")
    print("--- type prior preview ---")
    print(type_prior.head(15).to_string())
    print("--- global prior preview ---")
    print(global_prior.to_string())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", nargs="+", default=[f"out_stageA_s{s}" for s in range(5)])
    ap.add_argument("--out", default="analysis/output")
    ap.add_argument("-k", type=int, default=20, help="shrinkage strength")
    args = ap.parse_args()

    df = load_seeds(args.seeds)
    print(f"loaded {len(df)} merged rows from {len(args.seeds)} seeds")
    build_tables(df, args.out, k=args.k)


if __name__ == "__main__":
    main()
