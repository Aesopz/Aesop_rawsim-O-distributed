#!/usr/bin/env python3
"""
CBS-time vs CBS-energy 10-seed comparison analysis.
Reads statistics.txt + pathfinding.csv from each run directory.

Usage:
    python analyze_cbs_comparison.py <cbs_base_dir> <ecbs_base_dir> [--seeds 0,1,2,...,9]

Example:
    python analyze_cbs_comparison.py output_compare/10seed-cbs output_compare/10seed-ecbs
"""

import os
import sys
import re
import math
import argparse
from pathlib import Path
from collections import defaultdict


# ─── Metrics to extract from statistics.txt ─────────────────────────────────
# Key: field name in statistics.txt → display name
STAT_FIELDS = {
    # Throughput
    "StatOverallOrdersHandled": ("Orders", int),
    # Energy (kJ)
    "StatEnergyTotalKJ": ("E_mech_kJ", float),
    "StatEnergyE1AccelKJ": ("E1_accel_kJ", float),
    "StatEnergyE2DecelKJ": ("E2_decel_kJ", float),
    "StatEnergyE3CruiseKJ": ("E3_cruise_kJ", float),
    "StatEnergyE4RotationKJ": ("E4_rotation_kJ", float),
    "StatEnergyE5LiftLowerKJ": ("E5_liftlower_kJ", float),
    "StatEnergyPerOrderKJ": ("E_mech_per_order_kJ", float),
    "StatEnergyIdleKJ": ("E_idle_kJ", float),
    "StatEnergyTotalWithIdleKJ": ("E_total_with_idle_kJ", float),
    "StatEnergyPerOrderWithIdleKJ": ("E_total_per_order_kJ", float),
    # Motion behavior
    "StatTurningCount": ("Turns_total", int),
    "StatStopAndGoCount": ("StopGo_total", int),
    "StatLoadedTurningCount": ("Turns_loaded", int),
    "StatLoadedStopAndGoCount": ("StopGo_loaded", int),
    "StatEmptyTurningCount": ("Turns_empty", int),
    "StatEmptyStopAndGoCount": ("StopGo_empty", int),
    "StatDistanceTraveledRizqiM": ("Distance_m", float),
    "StatLoadedDistanceM": ("Distance_loaded_m", float),
    "StatWaitTimeSec": ("WaitTime_s", float),
    # Energy split by load state (kJ)
    "StatMoveEnergyEmptyKJ": ("MoveE_empty_kJ", float),
    "StatTurnEnergyEmptyKJ": ("TurnE_empty_kJ", float),
    "StatMoveEnergyLoadedKJ": ("MoveE_loaded_kJ", float),
    "StatTurnEnergyLoadedKJ": ("TurnE_loaded_kJ", float),
    # Path planning timing
    "StatTimingPathPlanningAverage": ("PP_avg_s", float),
    "StatTimingPathPlanningOverall": ("PP_total_s", float),
    "StatTimingPathPlanningCount": ("PP_count", int),
    # Timeout
    "StatPathPlanningTimeouts": ("PP_timeouts", int),
}


def parse_statistics(filepath):
    """Parse a statistics.txt file and return a dict of extracted metrics."""
    result = {}
    with open(filepath, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            for field, (name, typ) in STAT_FIELDS.items():
                if line.startswith(field + ":"):
                    val_str = line.split(":", 1)[1].strip()
                    try:
                        result[name] = typ(val_str)
                    except ValueError:
                        result[name] = float("nan")
    return result


def parse_pathfinding_csv(filepath):
    """Parse pathfinding.csv and return per-replan timing stats."""
    runtimes = []
    with open(filepath, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("#"):
                continue
            parts = line.strip().split(";")
            if len(parts) >= 2:
                try:
                    runtimes.append(float(parts[1]))
                except ValueError:
                    pass
    if not runtimes:
        return {"PP_max_s": 0.0, "PP_median_s": 0.0, "PP_replans": 0}
    runtimes.sort()
    n = len(runtimes)
    median = runtimes[n // 2] if n % 2 == 1 else (runtimes[n // 2 - 1] + runtimes[n // 2]) / 2
    return {
        "PP_max_s": max(runtimes),
        "PP_median_s": median,
        "PP_replans": n,
    }


def find_run_dir(base_dir, seed):
    """Find the single run sub-directory under base_dir for a given seed.
    Convention: <base_dir>/<instance>-<setting>-<config>-<method>-<seed>/
    """
    base = Path(base_dir)
    candidates = list(base.glob(f"*-{seed}"))
    if not candidates:
        # Try subdirectories matching the seed
        candidates = [d for d in base.iterdir() if d.is_dir() and d.name.endswith(f"-{seed}")]
    if len(candidates) == 1:
        return candidates[0]
    if len(candidates) > 1:
        # Prefer the one most recently modified
        return max(candidates, key=lambda p: p.stat().st_mtime)
    return None


def collect_data(base_dir, seeds):
    """Collect metrics for all seeds from a base directory."""
    all_data = {}
    for seed in seeds:
        run_dir = find_run_dir(base_dir, seed)
        if run_dir is None:
            print(f"  WARNING: No run directory found for seed {seed} in {base_dir}")
            continue
        stat_file = run_dir / "statistics.txt"
        pf_file = run_dir / "pathfinding.csv"
        if not stat_file.exists():
            print(f"  WARNING: {stat_file} not found")
            continue
        data = parse_statistics(str(stat_file))
        if pf_file.exists():
            data.update(parse_pathfinding_csv(str(pf_file)))
        all_data[seed] = data
    return all_data


def mean_std(values):
    """Compute mean and sample std."""
    n = len(values)
    if n == 0:
        return float("nan"), float("nan")
    m = sum(values) / n
    if n == 1:
        return m, 0.0
    var = sum((v - m) ** 2 for v in values) / (n - 1)
    return m, math.sqrt(var)


def print_comparison(cbs_data, ecbs_data, seeds):
    """Print per-seed and aggregate comparison."""
    # Determine all metric keys
    all_keys = []
    for d in list(cbs_data.values()) + list(ecbs_data.values()):
        for k in d:
            if k not in all_keys:
                all_keys.append(k)

    # ─── Per-seed table ──────────────────────────────────────────────────
    print("=" * 120)
    print("PER-SEED COMPARISON: CBS-time vs CBS-energy (ECBS)")
    print("=" * 120)

    for seed in seeds:
        if seed not in cbs_data or seed not in ecbs_data:
            continue
        c = cbs_data[seed]
        e = ecbs_data[seed]
        print(f"\n--- Seed {seed} ---")
        print(f"  {'Metric':<30s} {'CBS-time':>14s} {'CBS-energy':>14s} {'Diff':>10s} {'Diff%':>8s}")
        print(f"  {'-'*30} {'-'*14} {'-'*14} {'-'*10} {'-'*8}")
        for key in all_keys:
            cv = c.get(key)
            ev = e.get(key)
            if cv is None and ev is None:
                continue
            cv = cv if cv is not None else float("nan")
            ev = ev if ev is not None else float("nan")
            diff = ev - cv if isinstance(cv, (int, float)) and isinstance(ev, (int, float)) else float("nan")
            if isinstance(cv, int) and isinstance(ev, int):
                pct = f"{diff/cv*100:+.2f}%" if cv != 0 else "N/A"
                print(f"  {key:<30s} {cv:>14d} {ev:>14d} {diff:>+10d} {pct:>8s}")
            else:
                pct = f"{diff/cv*100:+.2f}%" if cv != 0 and not math.isnan(cv) else "N/A"
                print(f"  {key:<30s} {cv:>14.2f} {ev:>14.2f} {diff:>+10.2f} {pct:>8s}")

    # ─── Aggregate table ─────────────────────────────────────────────────
    print("\n" + "=" * 120)
    print(f"AGGREGATE (n={len(cbs_data)} seeds): mean +/- std")
    print("=" * 120)
    print(f"  {'Metric':<30s} {'CBS mean':>12s} {'CBS std':>10s} {'ECBS mean':>12s} {'ECBS std':>10s} {'Diff%':>8s}")
    print(f"  {'-'*30} {'-'*12} {'-'*10} {'-'*12} {'-'*10} {'-'*8}")

    for key in all_keys:
        c_vals = [cbs_data[s][key] for s in seeds if s in cbs_data and key in cbs_data[s]]
        e_vals = [ecbs_data[s][key] for s in seeds if s in ecbs_data and key in ecbs_data[s]]
        if not c_vals and not e_vals:
            continue
        cm, cs = mean_std(c_vals)
        em, es = mean_std(e_vals)
        pct = f"{(em-cm)/cm*100:+.2f}%" if cm != 0 and not math.isnan(cm) else "N/A"
        print(f"  {key:<30s} {cm:>12.2f} {cs:>10.2f} {em:>12.2f} {es:>10.2f} {pct:>8s}")

    # ──�� Timeout analysis ────────────────────────────────────────────────
    print("\n" + "=" * 120)
    print("TIMEOUT ANALYSIS")
    print("=" * 120)
    cbs_to = [cbs_data[s].get("PP_timeouts", 0) for s in seeds if s in cbs_data]
    ecbs_to = [ecbs_data[s].get("PP_timeouts", 0) for s in seeds if s in ecbs_data]
    print(f"  CBS-time  timeouts per seed: {cbs_to}")
    print(f"  CBS-energy timeouts per seed: {ecbs_to}")
    cm_to, cs_to = mean_std(cbs_to)
    em_to, es_to = mean_std(ecbs_to)
    print(f"  CBS-time  mean={cm_to:.1f} +/- {cs_to:.1f}")
    print(f"  CBS-energy mean={em_to:.1f} +/- {es_to:.1f}")

    # Correlate timeouts with energy difference
    print("\n  Seed-level correlation (timeout count vs energy difference):")
    print(f"  {'Seed':>6s} {'CBS_TO':>8s} {'ECBS_TO':>8s} {'E_mech_diff%':>14s} {'E_total_diff%':>14s}")
    for seed in seeds:
        if seed not in cbs_data or seed not in ecbs_data:
            continue
        c = cbs_data[seed]
        e = ecbs_data[seed]
        cto = c.get("PP_timeouts", 0)
        eto = e.get("PP_timeouts", 0)
        emech_d = (e.get("E_mech_kJ", 0) - c.get("E_mech_kJ", 0)) / c.get("E_mech_kJ", 1) * 100
        etot_d = (e.get("E_total_with_idle_kJ", 0) - c.get("E_total_with_idle_kJ", 0)) / c.get("E_total_with_idle_kJ", 1) * 100
        print(f"  {seed:>6d} {cto:>8d} {eto:>8d} {emech_d:>+14.2f}% {etot_d:>+14.2f}%")

    # ─── Summary ─────────────────────────────────────────────────────────
    print("\n" + "=" * 120)
    print("SUMMARY")
    print("=" * 120)
    cm_orders = mean_std([cbs_data[s]["Orders"] for s in seeds if s in cbs_data and "Orders" in cbs_data[s]])[0]
    em_orders = mean_std([ecbs_data[s]["Orders"] for s in seeds if s in ecbs_data and "Orders" in ecbs_data[s]])[0]
    cm_emech = mean_std([cbs_data[s]["E_mech_kJ"] for s in seeds if s in cbs_data and "E_mech_kJ" in cbs_data[s]])[0]
    em_emech = mean_std([ecbs_data[s]["E_mech_kJ"] for s in seeds if s in ecbs_data and "E_mech_kJ" in ecbs_data[s]])[0]
    cm_etot = mean_std([cbs_data[s]["E_total_with_idle_kJ"] for s in seeds if s in cbs_data and "E_total_with_idle_kJ" in cbs_data[s]])[0]
    em_etot = mean_std([ecbs_data[s]["E_total_with_idle_kJ"] for s in seeds if s in ecbs_data and "E_total_with_idle_kJ" in ecbs_data[s]])[0]

    print(f"  Throughput:       CBS={cm_orders:.0f} orders  ECBS={em_orders:.0f} orders  ({(em_orders-cm_orders)/cm_orders*100:+.2f}%)")
    print(f"  E_mech (kJ):      CBS={cm_emech:.1f}  ECBS={em_emech:.1f}  ({(em_emech-cm_emech)/cm_emech*100:+.2f}%)")
    print(f"  E_total+idle (kJ): CBS={cm_etot:.1f}  ECBS={em_etot:.1f}  ({(em_etot-cm_etot)/cm_etot*100:+.2f}%)")
    print(f"  Avg timeouts:     CBS={cm_to:.1f}  ECBS={em_to:.1f}")
    if em_to > cm_to * 2:
        print("  ** ECBS has significantly more timeouts — results may be dominated by suboptimal fallback solutions **")
    elif em_to > 0 or cm_to > 0:
        print("  ** Some timeouts detected — check per-seed correlation above **")
    else:
        print("  No timeouts — results reflect conflict-free optimal solutions")


def main():
    parser = argparse.ArgumentParser(description="CBS-time vs CBS-energy comparison")
    parser.add_argument("cbs_dir", help="Base directory for CBS-time runs")
    parser.add_argument("ecbs_dir", help="Base directory for CBS-energy runs")
    parser.add_argument("--seeds", default="0,1,2,3,4,5,6,7,8,9",
                        help="Comma-separated seed list (default: 0-9)")
    args = parser.parse_args()

    seeds = [int(s) for s in args.seeds.split(",")]

    print(f"CBS-time  dir: {args.cbs_dir}")
    print(f"CBS-energy dir: {args.ecbs_dir}")
    print(f"Seeds: {seeds}\n")

    print("Collecting CBS-time data...")
    cbs_data = collect_data(args.cbs_dir, seeds)
    print(f"  Found {len(cbs_data)} seeds\n")

    print("Collecting CBS-energy data...")
    ecbs_data = collect_data(args.ecbs_dir, seeds)
    print(f"  Found {len(ecbs_data)} seeds\n")

    print_comparison(cbs_data, ecbs_data, seeds)


if __name__ == "__main__":
    main()
