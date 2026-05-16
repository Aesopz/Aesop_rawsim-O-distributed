"""
Phase B.3: per-leg accuracy validation.

Compares two SegmentTime estimators against actual recorded SegmentTime via
leave-one-seed-out cross-validation:

  baseline  : T_hat = a + b * DistanceM        (fit on train seeds)
  congestion: T_hat = μ̂(FromNode, ToNode, CarryingPod, DensityBand)
              with shrinkage to type prior at strength k.

Metrics per estimator: MAE, RMSE, bias, Spearman ρ, conditional MAE by density.
"""
from __future__ import annotations
import argparse
import os
import numpy as np
import pandas as pd
from scipy import stats

from load_data import load_seeds
from build_table import density_band


# Option B re-train: prefer the Gaussian SoftBotDensity column when the logger emitted it.
# Falls back to the integer LocalBotDensity for older Stage A captures.
def _pick_density_column(df: pd.DataFrame) -> pd.Series:
    if "SoftBotDensity" in df.columns:
        return df["SoftBotDensity"].astype(float)
    return df["LocalBotDensity"].astype(int)


def fit_baseline(df_train: pd.DataFrame) -> tuple[float, float]:
    """Linear fit: SegmentTime = a + b * DistanceM."""
    x = df_train["DistanceM"].astype(float).values
    y = df_train["SegmentTime"].astype(float).values
    b, a = np.polyfit(x, y, 1)
    return float(a), float(b)


def fit_congestion_table(df_train: pd.DataFrame, k: int = 5):
    """Hybrid model: T_predict = (a + b*Dist) + residual_table[edge, carrying, density].

    The kinematic baseline a + b*Dist is fit on the training fold; the table only captures
    the *deviation* from that baseline (turn overhead, congestion wait, edge-specific bias).
    Residuals have much smaller magnitude than the raw time so shrinkage with a modest k
    keeps both per-edge specificity and stability for low-count cells.
    """
    df_train = df_train.copy()
    df_train["DensityBand"] = density_band(_pick_density_column(df_train))
    a, b = fit_baseline(df_train)
    df_train["Residual"] = df_train["SegmentTime"].astype(float) - (a + b * df_train["DistanceM"].astype(float))

    def _p90(x):
        import numpy as _np
        return float(_np.percentile(x, 90)) if len(x) > 0 else 0.0
    edge_table = (
        df_train.groupby(["FromNode", "ToNode", "CarryingPod", "DensityBand"])["Residual"]
        .agg(count="count", mean="mean", std="std", p90=_p90).reset_index()
    )
    edge_table["std"] = edge_table["std"].fillna(0.0)
    type_prior = (
        df_train.groupby(["EdgeType", "CarryingPod", "DensityBand"])["Residual"]
        .agg(count="count", mean="mean", std="std", p90=_p90).reset_index()
        .rename(columns={"mean": "type_residual", "std": "type_std", "p90": "type_p90"})
    )
    type_prior["type_std"] = type_prior["type_std"].fillna(0.0)
    global_prior = (
        df_train.groupby(["CarryingPod", "DensityBand"])["Residual"]
        .agg(count="count", mean="mean", std="std", p90=_p90).reset_index()
        .rename(columns={"mean": "global_residual", "std": "global_std", "p90": "global_p90"})
    )
    global_prior["global_std"] = global_prior["global_std"].fillna(0.0)
    edge_to_type = (
        df_train.groupby(["FromNode", "ToNode"])["EdgeType"]
        .agg(lambda s: s.mode().iat[0]).reset_index()
    )
    edge_table = edge_table.merge(edge_to_type, on=["FromNode", "ToNode"], how="left")
    edge_table = edge_table.merge(type_prior[["EdgeType", "CarryingPod", "DensityBand", "type_residual"]],
                                  on=["EdgeType", "CarryingPod", "DensityBand"], how="left")
    edge_table = edge_table.merge(global_prior[["CarryingPod", "DensityBand", "global_residual"]],
                                  on=["CarryingPod", "DensityBand"], how="left")
    edge_table["type_residual"] = edge_table["type_residual"].fillna(edge_table["global_residual"])
    edge_table["residual_shrunk"] = (
        edge_table["count"] / (edge_table["count"] + k) * edge_table["mean"]
        + k / (edge_table["count"] + k) * edge_table["type_residual"]
    )
    return (a, b), edge_table, type_prior, global_prior


def predict_congestion(model_pieces, df_test: pd.DataFrame) -> np.ndarray:
    (a, b), edge_table, type_prior, global_prior = model_pieces
    df_test = df_test.copy()
    df_test["DensityBand"] = density_band(_pick_density_column(df_test))
    et = edge_table[["FromNode", "ToNode", "CarryingPod", "DensityBand", "residual_shrunk"]]
    out = df_test.merge(et, on=["FromNode", "ToNode", "CarryingPod", "DensityBand"], how="left")
    out = out.merge(type_prior[["EdgeType", "CarryingPod", "DensityBand", "type_residual"]],
                    on=["EdgeType", "CarryingPod", "DensityBand"], how="left")
    out = out.merge(global_prior[["CarryingPod", "DensityBand", "global_residual"]],
                    on=["CarryingPod", "DensityBand"], how="left")
    residual = out["residual_shrunk"].fillna(out["type_residual"]).fillna(out["global_residual"]).fillna(0.0)
    base = a + b * df_test["DistanceM"].astype(float).values
    return base + residual.values


def metrics(y_true: np.ndarray, y_pred: np.ndarray, label: str) -> dict:
    err = y_pred - y_true
    rho, _ = stats.spearmanr(y_pred, y_true)
    return {
        "estimator": label,
        "n": len(y_true),
        "MAE": float(np.mean(np.abs(err))),
        "RMSE": float(np.sqrt(np.mean(err ** 2))),
        "bias": float(np.mean(err)),
        "spearman": float(rho),
    }


def per_density_mae(y_true, y_pred, density):
    out = []
    for b in sorted(set(density)):
        mask = density == b
        out.append({"DensityBand": int(b), "n": int(mask.sum()),
                    "MAE": float(np.mean(np.abs(y_pred[mask] - y_true[mask])))})
    return out


def loso_cv(df: pd.DataFrame, k: int = 20) -> tuple[pd.DataFrame, pd.DataFrame, list[dict]]:
    """Leave-one-seed-out cross-validation."""
    seeds = sorted(df["Seed"].unique())
    rows_overall = []
    rows_dens = []
    seedwise = []
    for held in seeds:
        train = df[df["Seed"] != held]
        test = df[df["Seed"] == held]
        y = test["SegmentTime"].astype(float).values

        # Baseline.
        a, b = fit_baseline(train)
        y_base = a + b * test["DistanceM"].astype(float).values
        m_base = metrics(y, y_base, "baseline_linear")
        m_base["held_seed"] = held

        # Congestion table.
        model = fit_congestion_table(train, k=k)
        y_cong = predict_congestion(model, test)
        m_cong = metrics(y, y_cong, "congestion_table")
        m_cong["held_seed"] = held

        rows_overall.append(m_base)
        rows_overall.append(m_cong)
        seedwise.append({"held": held, "base_MAE": m_base["MAE"], "cong_MAE": m_cong["MAE"],
                         "improve_%": 100 * (m_base["MAE"] - m_cong["MAE"]) / m_base["MAE"]})

        d = density_band(_pick_density_column(test)).values
        for stat in per_density_mae(y, y_base, d):
            stat.update({"estimator": "baseline_linear", "held_seed": held}); rows_dens.append(stat)
        for stat in per_density_mae(y, y_cong, d):
            stat.update({"estimator": "congestion_table", "held_seed": held}); rows_dens.append(stat)

    return pd.DataFrame(rows_overall), pd.DataFrame(rows_dens), seedwise


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", nargs="+", default=[f"out_stageA_s{s}" for s in range(5)])
    ap.add_argument("--out", default="analysis/output")
    ap.add_argument("-k", type=int, default=20)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    df = load_seeds(args.seeds)
    print(f"loaded {len(df)} merged rows from {len(args.seeds)} seeds")

    overall, by_density, seedwise = loso_cv(df, k=args.k)
    overall.to_csv(os.path.join(args.out, "validation_overall.csv"), index=False, sep=";")
    by_density.to_csv(os.path.join(args.out, "validation_by_density.csv"), index=False, sep=";")

    # Print summary.
    print("\n=== Per-seed MAE comparison (Leave-One-Seed-Out) ===")
    sw = pd.DataFrame(seedwise)
    print(sw.to_string(index=False))
    print(f"\nmean improve %: {sw['improve_%'].mean():.2f}%")
    print(f"\n=== Mean metrics across folds ===")
    print(overall.groupby("estimator")[["MAE", "RMSE", "bias", "spearman"]].mean().to_string())
    print(f"\n=== MAE by density band (mean across folds) ===")
    print(by_density.groupby(["estimator", "DensityBand"])[["MAE", "n"]].mean().to_string())


if __name__ == "__main__":
    main()
