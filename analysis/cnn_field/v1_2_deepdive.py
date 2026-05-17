"""V1.2 deep-dive: 為什麼 HADGS+large 訊號比預期弱？

從 4.25% global wait>0 開始，深挖：
1. 非零 wait 的分佈（給 CNN learn signal 的 actual richness）
2. 總 wait 時間 / 總 segment 時間（cost-impact 上界）
3. 空間 / 時間 / leg-stage 的 wait 分佈
4. 與 BAED 主線假設（Regime A 已抓走主訊號）的對比
5. Per-station / per-edge hot-spot 是否存在
"""
from __future__ import annotations

import pandas as pd
import numpy as np

CSV = (r"C:\Users\Aesop\Desktop\EE-RAWSim-O_PP\.worktrees\feature\cnn-field-v1"
       r"\out_v12_hadgs_large_s0\1-6-12-60-0.89-large-large_7200_300-hadgs-0\traversal_log.csv")

df = pd.read_csv(CSV, sep=";").rename(columns={"#TimeStamp": "TimeStamp"})
print(f"Total segments: {len(df)}, legs: {df.groupby(['BotId', 'LegType']).ngroups}")

wait = df["WaitBeforeEdge"].astype(float)
seg = df["SegmentTime"].astype(float)
move = df["MoveTime"].astype(float)
turn = df["TurnTime"].astype(float)

print("\n=== Q1: Non-zero wait distribution ===")
nz = wait[wait > 0]
print(f"  N non-zero wait events  : {len(nz)} ({len(nz)/len(df):.2%})")
print(f"  Mean (non-zero only)    : {nz.mean():.3f}s")
print(f"  Median                  : {nz.median():.3f}s")
print(f"  p25 / p75 / p95 / max   : {nz.quantile(0.25):.2f}/{nz.quantile(0.75):.2f}"
      f"/{nz.quantile(0.95):.2f}/{nz.max():.2f}s")
print(f"  Distribution buckets    :")
for lo, hi in [(0, 0.5), (0.5, 1), (1, 2), (2, 5), (5, 10), (10, 50)]:
    mask = (nz >= lo) & (nz < hi)
    print(f"    [{lo:5.1f}, {hi:5.1f}): {mask.sum():5d} ({mask.sum()/len(nz):.2%})")

print("\n=== Q2: Wait time impact on total route cost ===")
total_wait = wait.sum()
total_move = move.sum()
total_seg = seg.sum()
print(f"  Total wait time      : {total_wait:.0f}s")
print(f"  Total move time      : {total_move:.0f}s")
print(f"  Total turn time      : {turn.sum():.0f}s")
print(f"  Total segment time   : {total_seg:.0f}s")
print(f"  Wait / Segment       : {total_wait/total_seg:.2%}  <-- Cost-impact CEILING for CNN")
print(f"  Wait / Move          : {total_wait/total_move:.2%}")
print()
print(f"  Per-leg avg wait     : {df.groupby(['BotId'])['WaitBeforeEdge'].sum().mean():.2f}s/bot")
print(f"  Per-leg avg seg      : {df.groupby(['BotId'])['SegmentTime'].sum().mean():.2f}s/bot")

print("\n=== Q3: Wait per LegType ===")
for lt, g in df.groupby("LegType"):
    w = g["WaitBeforeEdge"].astype(float)
    s = g["SegmentTime"].astype(float)
    nzg = (w > 0).sum()
    print(f"  {lt:25s}: n={len(g):6d} | wait>0 {nzg:5d} ({nzg/len(g):.2%}) | "
          f"sum_wait={w.sum():.0f}s | sum_seg={s.sum():.0f}s | "
          f"wait/seg={w.sum()/s.sum():.2%}")

print("\n=== Q4: Spatial hot-spots (top 20 ToNode by total wait) ===")
hot = df.groupby("ToNode")["WaitBeforeEdge"].agg(["sum", "count", "max"]).sort_values("sum", ascending=False).head(20)
hot["frac_with_wait"] = df.groupby("ToNode").apply(
    lambda g: (g["WaitBeforeEdge"] > 0).sum() / len(g), include_groups=False
).reindex(hot.index)
print(hot.to_string())

print("\n=== Q5: Temporal pattern (wait per 600s window) ===")
df["TimeBin"] = (df["ReadyTime"] // 600).astype(int)
tw = df.groupby("TimeBin").agg(
    n=("WaitBeforeEdge", "count"),
    n_wait=("WaitBeforeEdge", lambda x: (x > 0).sum()),
    sum_wait=("WaitBeforeEdge", "sum"),
)
tw["frac"] = tw["n_wait"] / tw["n"]
print(tw.to_string())

print("\n=== Q6: Leg-level (group consecutive same-type segments per bot) ===")
df_sorted = df.sort_values(["BotId", "TimeStamp"]).reset_index(drop=True)
leg_id = ((df_sorted["BotId"] != df_sorted["BotId"].shift()) |
          (df_sorted["LegType"] != df_sorted["LegType"].shift())).cumsum()
df_sorted["LegId"] = leg_id
leg_stats = df_sorted.groupby(["LegId", "LegType"]).agg(
    seg_count=("WaitBeforeEdge", "count"),
    leg_wait=("WaitBeforeEdge", "sum"),
    leg_segtime=("SegmentTime", "sum"),
    leg_dist=("DistanceM", "sum"),
).reset_index()
print(f"  Total legs           : {len(leg_stats)}")
print(f"  Legs with wait>0     : {(leg_stats['leg_wait']>0).sum()} "
      f"({(leg_stats['leg_wait']>0).mean():.2%})")
print(f"  Avg leg duration     : {leg_stats['leg_segtime'].mean():.1f}s")
print(f"  Legs > 30s (Regime BC entry possible): {(leg_stats['leg_segtime']>30).sum()} "
      f"({(leg_stats['leg_segtime']>30).mean():.2%})")
print(f"  Among those, % with wait>0: "
      f"{((leg_stats['leg_segtime']>30) & (leg_stats['leg_wait']>0)).sum() / max(1, (leg_stats['leg_segtime']>30).sum()):.2%}")
print(f"  Mean leg duration by LegType:")
print(leg_stats.groupby("LegType")["leg_segtime"].describe()[["mean", "50%", "75%", "max"]].to_string())

print("\n=== Q7: Signal strength assessment ===")
total_wait_pct = total_wait / total_seg
ceiling = total_wait_pct  # max possible runtime reduction if wait eliminated
print(f"  Wait/Segment ratio    : {total_wait_pct:.2%}  (upper-bound runtime reduction)")
if total_wait_pct < 0.02:
    print(f"  --> Even ORACLE can only save {total_wait_pct:.1%} runtime. CNN-CF dead.")
elif total_wait_pct < 0.05:
    print(f"  --> Oracle ceiling {total_wait_pct:.1%}. CNN-CF likely marginal.")
else:
    print(f"  --> Oracle ceiling {total_wait_pct:.1%}. CNN-CF has room.")
