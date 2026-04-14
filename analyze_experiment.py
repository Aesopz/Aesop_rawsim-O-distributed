"""
Aggregate CBS vs ECBS experiment results (30 seeds each).
Outputs: console table + results_summary.csv
"""
import os, re, csv, sys
from pathlib import Path
from collections import defaultdict

BASE = Path(__file__).parent / "output_exp"

METRICS = [
    "StatOverallDistanceTraveled",
    "StatEnergyTotalKJ",
    "StatEnergyE1AccelKJ",
    "StatEnergyE2DecelKJ",
    "StatEnergyE3CruiseKJ",
    "StatEnergyE4RotationKJ",
    "StatTurningCount",
    "StatStopAndGoCount",
    "StatLoadedDistanceM",
    "StatLoadedTurningCount",
    "StatWaitTimeSec",
    "StatOverallOrdersHandled",
]

def parse_stats(path):
    vals = {}
    text = path.read_text(encoding="utf-8", errors="ignore")
    for line in text.splitlines():
        for m in METRICS:
            if line.strip().startswith(m + ":"):
                raw = line.split(":", 1)[1].strip()
                try:
                    vals[m] = float(raw)
                except ValueError:
                    vals[m] = raw
    return vals

def collect(algo):
    rows = []
    algo_dir = BASE / algo
    if not algo_dir.exists():
        return rows
    for seed in range(1, 31):
        seed_dir = algo_dir / f"seed_{seed}"
        # statistics.txt is inside a sub-folder named after the run
        candidates = list(seed_dir.glob("*/statistics.txt")) if seed_dir.exists() else []
        if not candidates:
            # fallback: direct path
            direct = seed_dir / "statistics.txt"
            if direct.exists():
                candidates = [direct]
        if candidates:
            d = parse_stats(candidates[0])
            d["seed"] = seed
            rows.append(d)
    return rows

def mean(lst): return sum(lst)/len(lst) if lst else 0
def std(lst):
    if len(lst) < 2: return 0
    m = mean(lst)
    return (sum((x-m)**2 for x in lst)/(len(lst)-1))**0.5

def main():
    cbs  = collect("cbs")
    ecbs = collect("ecbs")
    print(f"CBS: {len(cbs)} seeds  |  ECBS: {len(ecbs)} seeds")

    if not cbs or not ecbs:
        print("No data yet.")
        return

    header = f"{'Metric':<30} {'CBS mean':>12} {'CBS sd':>10} {'ECBS mean':>12} {'ECBS sd':>10} {'delta%':>9}"
    print("\n" + header)
    print("="*90)

    csv_rows = []
    for m in METRICS:
        cv = [r[m] for r in cbs  if m in r and isinstance(r[m], float)]
        ev = [r[m] for r in ecbs if m in r and isinstance(r[m], float)]
        if not cv or not ev:
            continue
        cm, cs = mean(cv), std(cv)
        em, es = mean(ev), std(ev)
        d = (em - cm) / cm * 100 if cm != 0 else 0
        print(f"  {m:<28} {cm:>12.2f} {cs:>10.2f} {em:>12.2f} {es:>10.2f} {d:>+8.2f}%")
        csv_rows.append([m, cm, cs, em, es, d])

    # Write CSV
    out = Path(__file__).parent / "results_summary.csv"
    with open(out, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["metric","cbs_mean","cbs_sd","ecbs_mean","ecbs_sd","delta_pct"])
        w.writerows(csv_rows)
    print(f"\nSaved: {out}")

if __name__ == "__main__":
    main()
