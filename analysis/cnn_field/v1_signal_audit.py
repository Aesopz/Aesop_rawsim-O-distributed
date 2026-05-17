"""V1 Signal Audit — CNN Congestion Field 可行性檢驗第一階

目標：在投入 CNN 訓練前，確認 WaitBeforeEdge (= EntryDelay) 在資料中有足夠強度。
特別檢驗 Regime B/C（離 leg start > 15s 的 segments）內是否仍有顯著訊號——
這是 BAED 失效區，也是 CNN-CF 要補完的目標。

退出條件（Gate）:
  G1: 全域 WaitBeforeEdge > 0 比例 >= 5%
  G2: Regime B/C 內 WaitBeforeEdge > 0 比例 >= 5%
  G3: Regime B/C 內 mean WaitBeforeEdge >= 0.5s
任一未達 -> V1 FAIL -> 整套 CNN-CF 訊號不足，停。

Usage:
    python v1_signal_audit.py <runs_root_dir> [--regime-cutoff 15.0]

預設讀 feature/congestion-aware-cost worktree 內 out_stageA_s* / out_baseline_s* 等。
"""
from __future__ import annotations

import argparse
import glob
import os
import sys
from dataclasses import dataclass

import numpy as np
import pandas as pd

REGIME_CUTOFF_DEFAULT = 15.0  # BAED Regime A 視窗 (s)


@dataclass
class AuditResult:
    run_label: str
    n_segments: int
    n_legs: int
    wait_gt0_frac_all: float
    wait_mean_all: float
    wait_p95_all: float
    wait_max_all: float
    # leg-type breakdown
    wait_gt0_frac_extract: float   # robot -> pod (carrying=False)
    wait_gt0_frac_insert: float    # pod -> station (carrying=True)
    wait_mean_extract: float
    wait_mean_insert: float
    # regime A vs B/C
    n_regimeA: int
    n_regimeBC: int
    wait_gt0_frac_regimeA: float
    wait_gt0_frac_regimeBC: float
    wait_mean_regimeA: float
    wait_mean_regimeBC: float
    # leg2-only B/C (BAED 第二段失效區，最關鍵)
    n_leg2_regimeBC: int
    wait_gt0_frac_leg2_regimeBC: float
    wait_mean_leg2_regimeBC: float


def load_traversal(csv_path: str) -> pd.DataFrame:
    df = pd.read_csv(csv_path, sep=";")
    df = df.rename(columns={"#TimeStamp": "TimeStamp"})
    needed = {"BotId", "ReadyTime", "LeaveTime", "ArriveTime",
              "WaitBeforeEdge", "LegType", "CarryingPod"}
    missing = needed - set(df.columns)
    if missing:
        raise ValueError(f"Missing columns in {csv_path}: {missing}")
    return df


def assign_leg_start(df: pd.DataFrame) -> pd.DataFrame:
    """為每個 (bot, leg) 求 leg start time = 該 leg 內最早的 ReadyTime。

    一個 leg = 連續同 LegType 的 segments；當 LegType 切換或 bot 不同，視為新 leg。
    """
    df = df.sort_values(["BotId", "TimeStamp"]).reset_index(drop=True)
    leg_id = np.zeros(len(df), dtype=np.int64)
    prev_bot, prev_leg = None, None
    cur = -1
    for i, row in enumerate(df.itertuples(index=False)):
        bot = row.BotId
        leg = row.LegType
        if bot != prev_bot or leg != prev_leg:
            cur += 1
            prev_bot, prev_leg = bot, leg
        leg_id[i] = cur
    df["LegId"] = leg_id
    leg_start = df.groupby("LegId")["ReadyTime"].transform("min")
    df["TimeFromLegStart"] = df["ReadyTime"] - leg_start
    return df


def audit_one_run(csv_path: str, regime_cutoff: float, label: str) -> AuditResult:
    df = load_traversal(csv_path)
    df = assign_leg_start(df)

    wait = df["WaitBeforeEdge"].astype(float)
    extract = df["LegType"] == "ExtractRobotToPod"
    insert = df["LegType"] == "InsertRobotToPod"

    regimeA = df["TimeFromLegStart"] <= regime_cutoff
    regimeBC = ~regimeA

    return AuditResult(
        run_label=label,
        n_segments=len(df),
        n_legs=int(df["LegId"].nunique()),
        wait_gt0_frac_all=float((wait > 0).mean()),
        wait_mean_all=float(wait.mean()),
        wait_p95_all=float(wait.quantile(0.95)),
        wait_max_all=float(wait.max()),
        wait_gt0_frac_extract=float((wait[extract] > 0).mean()) if extract.any() else 0.0,
        wait_gt0_frac_insert=float((wait[insert] > 0).mean()) if insert.any() else 0.0,
        wait_mean_extract=float(wait[extract].mean()) if extract.any() else 0.0,
        wait_mean_insert=float(wait[insert].mean()) if insert.any() else 0.0,
        n_regimeA=int(regimeA.sum()),
        n_regimeBC=int(regimeBC.sum()),
        wait_gt0_frac_regimeA=float((wait[regimeA] > 0).mean()) if regimeA.any() else 0.0,
        wait_gt0_frac_regimeBC=float((wait[regimeBC] > 0).mean()) if regimeBC.any() else 0.0,
        wait_mean_regimeA=float(wait[regimeA].mean()) if regimeA.any() else 0.0,
        wait_mean_regimeBC=float(wait[regimeBC].mean()) if regimeBC.any() else 0.0,
        n_leg2_regimeBC=int((insert & regimeBC).sum()),
        wait_gt0_frac_leg2_regimeBC=float((wait[insert & regimeBC] > 0).mean())
            if (insert & regimeBC).any() else 0.0,
        wait_mean_leg2_regimeBC=float(wait[insert & regimeBC].mean())
            if (insert & regimeBC).any() else 0.0,
    )


def find_traversal_logs(root: str) -> list[tuple[str, str]]:
    pattern = os.path.join(root, "**", "traversal_log.csv")
    paths = sorted(glob.glob(pattern, recursive=True))
    out = []
    for p in paths:
        rel = os.path.relpath(p, root)
        label = rel.split(os.sep)[0]
        out.append((label, p))
    return out


def aggregate(results: list[AuditResult]) -> dict:
    if not results:
        return {}
    fields = [f for f in AuditResult.__dataclass_fields__ if f != "run_label"]
    agg = {}
    for f in fields:
        vals = [getattr(r, f) for r in results]
        agg[f"{f}__mean"] = float(np.mean(vals))
        agg[f"{f}__std"] = float(np.std(vals))
        agg[f"{f}__min"] = float(np.min(vals))
        agg[f"{f}__max"] = float(np.max(vals))
    return agg


def gate_check(agg: dict) -> tuple[bool, list[str]]:
    """Return (pass, list of reasons)."""
    reasons = []
    ok = True

    if agg.get("wait_gt0_frac_all__mean", 0) < 0.05:
        ok = False
        reasons.append(
            f"G1 FAIL: all-segments wait>0 frac = {agg['wait_gt0_frac_all__mean']:.3%} < 5%"
        )
    else:
        reasons.append(
            f"G1 PASS: all-segments wait>0 frac = {agg['wait_gt0_frac_all__mean']:.3%}"
        )

    if agg.get("wait_gt0_frac_regimeBC__mean", 0) < 0.05:
        ok = False
        reasons.append(
            f"G2 FAIL: Regime B/C wait>0 frac = {agg['wait_gt0_frac_regimeBC__mean']:.3%} < 5%"
        )
    else:
        reasons.append(
            f"G2 PASS: Regime B/C wait>0 frac = {agg['wait_gt0_frac_regimeBC__mean']:.3%}"
        )

    if agg.get("wait_mean_regimeBC__mean", 0) < 0.5:
        ok = False
        reasons.append(
            f"G3 FAIL: Regime B/C mean wait = {agg['wait_mean_regimeBC__mean']:.3f}s < 0.5s"
        )
    else:
        reasons.append(
            f"G3 PASS: Regime B/C mean wait = {agg['wait_mean_regimeBC__mean']:.3f}s"
        )
    return ok, reasons


def print_report(results: list[AuditResult], agg: dict, gate_ok: bool, gate_msgs: list[str],
                 regime_cutoff: float) -> None:
    print("=" * 72)
    print(f"V1 Signal Audit Report  (Regime cutoff = {regime_cutoff}s)")
    print("=" * 72)
    print(f"\nN runs: {len(results)}")
    for r in results:
        print(f"  - {r.run_label}: {r.n_segments} segments, {r.n_legs} legs")

    print("\n--- Aggregate (mean across runs) ---")
    print(f"  Segments total                    : {agg['n_segments__mean']:.0f}")
    print(f"  Legs total                        : {agg['n_legs__mean']:.0f}")
    print()
    print(f"  Wait>0 frac (all)                 : {agg['wait_gt0_frac_all__mean']:.3%}"
          f"  ±{agg['wait_gt0_frac_all__std']:.3%}")
    print(f"  Wait mean   (all)                 : {agg['wait_mean_all__mean']:.3f}s"
          f"  ±{agg['wait_mean_all__std']:.3f}")
    print(f"  Wait p95    (all)                 : {agg['wait_p95_all__mean']:.3f}s")
    print(f"  Wait max    (all)                 : {agg['wait_max_all__mean']:.3f}s")
    print()
    print(f"  Wait>0 frac (Extract: r->p)       : {agg['wait_gt0_frac_extract__mean']:.3%}")
    print(f"  Wait>0 frac (Insert : p->s)       : {agg['wait_gt0_frac_insert__mean']:.3%}")
    print(f"  Wait mean   (Extract)             : {agg['wait_mean_extract__mean']:.3f}s")
    print(f"  Wait mean   (Insert)              : {agg['wait_mean_insert__mean']:.3f}s")
    print()
    print(f"  Regime A  segments (avg/run)      : {agg['n_regimeA__mean']:.0f}")
    print(f"  Regime BC segments (avg/run)      : {agg['n_regimeBC__mean']:.0f}")
    print(f"  Wait>0 frac (Regime A)            : {agg['wait_gt0_frac_regimeA__mean']:.3%}")
    print(f"  Wait>0 frac (Regime BC)           : {agg['wait_gt0_frac_regimeBC__mean']:.3%}")
    print(f"  Wait mean   (Regime A)            : {agg['wait_mean_regimeA__mean']:.3f}s")
    print(f"  Wait mean   (Regime BC)           : {agg['wait_mean_regimeBC__mean']:.3f}s")
    print()
    print(f"  Leg2 (p->s) + Regime BC segments  : {agg['n_leg2_regimeBC__mean']:.0f}  "
          f"(BAED fail zone, CNN-CF target)")
    print(f"  Wait>0 frac (Leg2 + Regime BC)    : {agg['wait_gt0_frac_leg2_regimeBC__mean']:.3%}")
    print(f"  Wait mean   (Leg2 + Regime BC)    : {agg['wait_mean_leg2_regimeBC__mean']:.3f}s")

    print("\n--- Gate Check ---")
    for m in gate_msgs:
        print(f"  {m}")
    print(f"\n  V1 OVERALL: {'PASS -> proceed to V2' if gate_ok else 'FAIL -> need higher congestion data'}")
    print("=" * 72)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?",
                    default=r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\.worktrees\feature\congestion-aware-cost",
                    help="Root dir to glob traversal_log.csv from")
    ap.add_argument("--regime-cutoff", type=float, default=REGIME_CUTOFF_DEFAULT,
                    help="Regime A window in seconds (default: 15)")
    ap.add_argument("--filter", default="out_baseline_s",
                    help="Only include runs whose top-level dir starts with this prefix "
                         "(default: out_baseline_s = baseline M1G runs only)")
    ap.add_argument("--csv-out", default=None, help="Optional per-run CSV output path")
    args = ap.parse_args()

    all_paths = find_traversal_logs(args.root)
    paths = [(l, p) for l, p in all_paths if l.startswith(args.filter)]
    if not paths:
        print(f"ERROR: no traversal_log.csv matching filter={args.filter!r} under {args.root}",
              file=sys.stderr)
        sys.exit(2)

    print(f"Found {len(paths)} runs (filter={args.filter!r}):")
    for l, p in paths:
        print(f"  {l}")
    print()

    results = []
    for label, p in paths:
        try:
            results.append(audit_one_run(p, args.regime_cutoff, label))
        except Exception as e:
            print(f"  [skip] {label}: {e}", file=sys.stderr)

    if not results:
        print("ERROR: no runs produced valid audit results", file=sys.stderr)
        sys.exit(2)

    agg = aggregate(results)
    gate_ok, gate_msgs = gate_check(agg)
    print_report(results, agg, gate_ok, gate_msgs, args.regime_cutoff)

    if args.csv_out:
        df = pd.DataFrame([r.__dict__ for r in results])
        df.to_csv(args.csv_out, index=False)
        print(f"\nPer-run CSV written: {args.csv_out}")

    sys.exit(0 if gate_ok else 1)


if __name__ == "__main__":
    main()
