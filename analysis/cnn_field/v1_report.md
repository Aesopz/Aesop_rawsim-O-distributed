# V1 Signal Audit — Round 1 Report

**Date:** 2026-05-16
**Scope:** 既有 `feature/congestion-aware-cost` worktree 內 60+ traversal_log（small + M1G + 10 bots）
**Gate (preset):** G1 ≥5% all wait>0 / G2 ≥5% Regime BC wait>0 / G3 ≥0.5s Regime BC mean

---

## 0. TL;DR

- 全域 gate **FAIL**（G1/G2/G3 都不過）—— 但這是 expected：small + 10 bots 擁擠度不足，與 `design_note_baed.md §9.4` 的預警一致
- **目標子群「Leg2 (pod→station) + Regime BC」訊號強**：8h 跑顯示 20–31% wait>0、0.5–1.1s mean，**但 sample size 太小**（每 run 2–14 segments）
- 結論：**V1.1 inconclusive** — 不是 NO-GO，而是 small data 不足以判定，需要在 HADGS + large + 60 bots 上重做

---

## 1. 跨 dataset 結果總表

| Dataset prefix | runs | all wait>0 | regimeBC wait>0 | regimeBC mean | **Leg2+BC wait>0** | **Leg2+BC mean** | Leg2+BC n |
|---|---:|---:|---:|---:|---:|---:|---:|
| out_baseline_s     | 10 | 2.51% | 2.94% | 0.083s | 7.92% | 0.258s | 3 |
| out_phaseC_s       |  5 | 2.69% | 3.53% | 0.095s | 10.00% | 0.200s | 2 |
| out_phaseE_v2_s    | 15 | 2.47% | 2.92% | 0.082s | **21.78%** | **0.993s** | 3 |
| out_phaseD_s       |  5 | 2.88% | 4.14% | 0.116s | 2.78% | 0.056s | 4 |
| out_base8h_s       |  5 | 2.47% | 3.01% | 0.089s | **31.24%** | **1.132s** | 13 |
| out_joint8h_s      |  5 | 2.45% | 3.03% | 0.089s | 13.55% | 0.426s | 12 |
| out_phaseC_recal_s |  5 | 2.63% | 3.61% | 0.097s | 15.71% | 0.505s | 3 |
| out_recal8h_s      |  5 | 2.53% | 3.12% | 0.090s | **30.66%** | **0.895s** | 14 |

所有 dataset G1/G2/G3 都 FAIL；但 Leg2+BC 在 8h 長跑與 phaseE prob runs 內 wait>0 比例 > 20%、mean > 0.5s（已達 G3 等價門檻），只是 n 過小。

## 2. 為何 small + 10 bots 全域訊號弱

1. **大多 leg 全程 < 15s** → 自然進 Regime A，無法進 BC
2. **10 bots / 12 stations** 派工分散，aisle 利用率低，等待 event rare
3. **node-exclusive + WHCA\* 15s 視窗** → 多半衝突在 Regime A 內被消化

→ 全域 wait>0 ≈ 2.5% 是 small 的 floor，不會因為換 cost policy 大幅變動（baseline 2.51% vs cong recal 2.63% 幾乎沒差）。

## 3. 為何 Leg2 + Regime BC 子群訊號強但 n 小

- Leg2 (pod→station) 因負載較重 + station entrance 排隊，平均 segment_time > Leg1
- 只要 leg2 cumulative time 超過 15s（多半因為遇到 station queue），剩下的 segments 就進 Regime BC，且**這些 segments 高機率本身就是在排隊**
- 所以「進 BC = 已在排隊」是 selection effect，使該子群 wait>0 比例 > 20%

→ 這恰好是 CNN-CF 要救的場景（BAED Regime A 看不到 station queue 形成過程），但 small 上樣本不夠。

## 4. 8h 跑 vs 7200s 跑的對比

| Metric | 7200s baseline | 8h baseline | delta |
|---|---:|---:|---|
| all wait>0          | 2.51% | 2.47% | flat |
| max wait            | 11.4s | 19.2s | +68% |
| Leg2+BC n / run     | 3     | 13    | +333% |
| Leg2+BC wait>0      | 7.9%  | 31.2% | +295% |
| Leg2+BC mean        | 0.26s | 1.13s | +335% |

→ 長跑放大了極端等待事件（更多 leg 進到 BC，且 BC 內等待更重）。但全域比例沒變。

## 5. Gate Pass 預測：HADGS + large + 60 bots

從 BAED memory ([project_hadgs_e15b41a_breakage]) 與 spec 推測：
- 60 bots / 12 stations + SingleLane HighwayHallway → 走廊持續排隊
- 平均 pod→station 路徑 ~25s（design_note_cnn_field §2.3 假設）→ Leg2 多半進 BC
- 預期 Leg2+BC n 從 ~13/run（small 8h）→ **數千/run（large）**

若 large 全域 wait>0 ≥ 5%（**對應 BAED 主線假設**），V1 PASS。

## 6. 下一步：V1.2 — Port TraversalLogger + HADGS+large baseline

### 6.1 工作項

1. 從 `feature/congestion-aware-cost` cherry-pick TraversalLogger commit (552a59e + 3cfba14) 到 main (leg-test)，或開新 worktree 並 cherry-pick
2. Build x64 Release
3. 跑 1 個 HADGS + large + 7200s + seed=0
4. `python v1_signal_audit.py <hadgs_large_run>` 重新做 audit

### 6.2 退出條件

- HADGS+large 跑出 wait>0 ≥ 5% 且 Regime BC mean ≥ 0.5s → V1 PASS → 進 V2 Oracle 實驗
- 仍 FAIL → 整套 CNN-CF 在 RAWSim-O 無訊號可學，回頭考慮 GNN edge-level 或放棄此方向

### 6.3 預估成本

- TraversalLogger port：~30 分鐘（兩個 commit cherry-pick）
- Build：~5 分鐘
- HADGS + large 7200s seed=0：依 memory ~2–8 小時
- Audit：< 1 分鐘

---

## 7. 對 design_note_cnn_field.md §3.2 蒐集策略的影響

無論 V1.2 結果如何，shoul 確認以下：

| §3.2 假設 | V1.1 已驗證 | 待 V1.2 驗證 |
|---|---|---|
| Leg2 + Regime BC 訊號強 | ✓ 子群比例 20–31% | n 是否 ≥ 1000/run |
| 8h 跑放大訊號 | ✓ +335% mean | large 8h 是否進一步放大 |
| sweep bot count 提升訊號 | — | 30 vs 45 vs 60 訊號比較 |

若 V1.2 PASS，§3.2 的 9-setting sweep 可確認；若 60 bots 都不夠強，整套方法 dead。
