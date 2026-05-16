"""
n=10 paired statistical validation: Phase E v2 (probabilistic cost-aware) vs distance baseline.

Reads kpi_report.csv from both arms, computes paired Wilcoxon signed-rank on per-seed deltas.
"""
import os
import statistics as stats_mod
from scipy.stats import wilcoxon

KPIS = [
    ("TP",  "orders_per_hour",     "up"),
    ("PO",  "system_order_pile_on","up"),
    ("OD",  "order_distance_m",    "down"),
    ("RD",  "total_distance_m",    "down"),
]

def kpi(seed_dir, name_subdir):
    path = os.path.join(seed_dir, name_subdir, "kpi_report.csv")
    out = {}
    with open(path) as f:
        for line in f:
            parts = line.strip().split(",")
            if len(parts) < 5: continue
            if parts[0] == "L1" and parts[1] in {k[1] for k in KPIS}:
                out[parts[1]] = float(parts[4])
    return out

def main():
    import sys
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 10
    seeds = list(range(n))
    base = []
    prob = []
    for s in seeds:
        base.append(kpi(f"out_stageA_s{s}" if s < 5 else f"out_baseline_s{s}",
                         f"benchmark_ss_layout-small_sett-m1g-time-{s}"))
        prob.append(kpi(f"out_phaseE_v2_s{s}",
                         f"benchmark_ss_layout-small_sett-m1g-time-prob-{s}"))

    print(f"{'KPI':<4} {'baseline mean':>14} {'prob mean':>11} {'delta%':>8} {'wins/n':>8} {'p-value':>9}  (n={n})")
    print("-" * 60)
    for name, key, direction in KPIS:
        b = [r[key] for r in base]
        p = [r[key] for r in prob]
        delta = [pi - bi for bi, pi in zip(b, p)]
        favorable = sum((d < 0 if direction == "down" else d > 0) for d in delta)
        wins_label = f"{favorable}/{n}"
        bm, pm = stats_mod.mean(b), stats_mod.mean(p)
        pct = 100 * (pm - bm) / bm
        try:
            stat, pval = wilcoxon(delta, alternative=("greater" if direction == "up" else "less"))
        except ValueError as e:
            pval = float("nan")
        print(f"{name:<4} {bm:>14.3f} {pm:>11.3f} {pct:>+7.2f}% {wins_label:>8} {pval:>9.4f}")
    print()
    print(f"Wilcoxon n={n} mathematical p-floor (one-sided): {1/2**n:.5f}")
    print()
    print("--- Per-seed delta (prob - baseline, want OD/RD < 0) ---")
    print(f"{'seed':>4} {'TP_delta':>10} {'PO_delta':>10} {'OD_delta':>10} {'RD_delta':>10}")
    for i, s in enumerate(seeds):
        td = prob[i]["orders_per_hour"] - base[i]["orders_per_hour"]
        pd_ = prob[i]["system_order_pile_on"] - base[i]["system_order_pile_on"]
        od = prob[i]["order_distance_m"] - base[i]["order_distance_m"]
        rd = prob[i]["total_distance_m"] - base[i]["total_distance_m"]
        print(f"{s:>4} {td:>+10.2f} {pd_:>+10.4f} {od:>+10.4f} {rd:>+10.2f}")

if __name__ == "__main__":
    main()
