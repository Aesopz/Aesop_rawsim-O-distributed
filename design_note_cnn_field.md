# CNN-CF：學習式壅塞場預測作為 BAED Regime B/C 補完

**Author:** Aesop / AI research assistant
**Status:** Draft v1 — 延伸 `design_note_baed.md` 與 `design_note_baed_hadgs.md`
**定位:** BAED Regime B/C 的學習式替代；長期 NN cost oracle 的具體實作

---

## 0. 與既有設計文件的關係

| 文件 | 方法定位 | 在本研究中的角色 |
|---|---|---|
| `design_note.md` | MAPFUU-inspired density residual estimator | A2 ablation 對照組 |
| `design_note_baed.md` | WCHA\* committed reservation → analytical EntryDelay | 主線方法，**Regime A** (≤15s) 主訊號來源 |
| `design_note_baed_hadgs.md` | BAED 在 HADGS + large layout 的部署 | 實驗平台 |
| `design_note_cnn_field.md`（本文件） | CNN 預測 node-level EntryDelay field，補完 **Regime B/C** | 學習式延伸 / hybrid 方法 |

**核心關係**：BAED 已驗證 Regime A 內 EntryDelay 訊號可用，但 Regime B (>15s) 退化為 RRA\* 投影、Regime C 退化為純 distance。第二段 path（pod→station）因 `tCursor + T_lift` 多半超出 15s window，整段都是 Regime C → 第二段 BAED ≈ B0 baseline。本研究訓練 CNN 直接學「未來 T_future 秒內任一 node 的 EntryDelay 分佈」，把第二段 BAED 從 distance 救回成 cost-aware。

---

## 1. Executive Summary

把同一個量 `EntryDelay(v, t_arr)` 改用學習式估計：

```
D̂_θ : (S_t, x, y, τ) → 預測 EntryDelay   (τ ∈ [0, T_future])
S_t  = 倉庫當前狀態（bot 位置/heading/speed/carrying、pod、station queue、order backlog、committed reservation 投影）
```

整合方式：

```
EntryDelay(v, t_arr) = {
    FirstFreeStart(v, t_arr) − t_arr             if Regime A   (BAED 原樣，硬訊號)
    D̂_θ(S_{t_now}, v.x, v.y, t_arr − t_now)      if Regime B/C (CNN 替代 fallback)
}
```

→ BAED 公式、M1G 整合接點、`v_ref` 換算全部不動，只替換 `FirstFreeStart` 的 fallback 分支。

**論文 narrative**：BAED 證明 analytical blocking-aware cost 在 Regime A 有效；CNN-CF 把同一概念外推到 Regime B/C，並以 ablation 驗證學習式對 hybrid 系統的邊際貢獻。

---

## 2. 方法核心

### 2.1 為什麼是 CNN 而非 GNN

| 面向 | CNN (ConvLSTM / 3D-CNN) | GNN (ST-GCN / GraphWaveNet) |
|---|---|---|
| 倉庫 layout | 規則 aisle grid → spatial inductive bias 完全適用 | 需手動建 node/edge graph |
| 動力學編碼 | channel 編碼 heading/speed/carrying，receptive field 涵蓋加速/轉彎 (≤2 cells) | 同樣靠 node feature |
| SingleLane 方向性 | direction channel hint | 天然 |
| 訓練成熟度 | crowd flow / traffic heatmap 文獻豐富 | ST-GNN 較新、訓練重 |
| 60 bots × large inference | 一次 forward 出整場 | 多輪 message passing |

**v1 選 CNN**；GNN 列為 ablation E2，若 CNN 在 HighwayHallway 方向性上 underperform 再升級。

### 2.2 輸入表示：multi-channel grid tensor

倉庫 `1-6-12-60-0.89-large.xlayo` 的 waypoint grid 解析度為 `H × W`。輸入 `X_t ∈ R^{C × H × W × L}`（L = history frames，預設 L = 5，每 frame 間隔 1s）：

| Channel | 內容 | 編碼 |
|---|---|---|
| 0 | bot occupancy | 0/1 |
| 1 | bot heading (sin) | [-1, 1] |
| 2 | bot heading (cos) | [-1, 1] |
| 3 | bot speed | 0–1 normalized |
| 4 | bot carrying flag | 0/1 |
| 5 | committed reservation footprint (next 15s) | 0–1，∑ occupancy over window |
| 6 | pod occupancy | 0/1 |
| 7 | station queue length | 0–1 normalized |
| 8 | station inbound pod count | 0–1 normalized |
| 9 | order backlog density (per station catchment) | 0–1 normalized |
| 10 | edge direction (one-hot 4 方向之合成) | 0/1 |
| 11 | obstacle / non-waypoint mask | 0/1 |

→ `C = 12`，small ≈ 22×22、large ≈ 60×60，tensor 大小完全可接受。

### 2.3 輸出表示：Node-level Expected EntryDelay Field

```
Ŷ_t ∈ R^{H × W × T_future}     T_future = 30 frames (s)
Ŷ_t[x, y, τ] = E[ EntryDelay(node(x,y), t_now + τ) | S_t ]
```

→ M1G candidate 計算時對 path 上每個 (v, t_arr − t_now) 點 sample 即可：

```
EntryDelay(v, t_arr) = Ŷ_t[v.x, v.y, clip(t_arr − t_now, 0, T_future − 1)]
```

`τ > T_future` 仍 fallback 為 0（純 distance），但 T_future = 30s 已涵蓋第二段 path 絕大多數 edges（large + 60 bots 平均 pod→station 路徑 ~25s）。

### 2.4 為什麼 EntryDelay field 比其他表示精準

| 表示 | M1G 對齊 | Ground truth | 精度 | 選 |
|---|---|---|---|---|
| (a) Density heatmap (bot occupancy prob.) | 弱（要再轉成 delay） | 易 | 中 | ✗ |
| **(c) EntryDelay field** | **✓ 與 BAED 同型** | **✓ 從 traversal log 統計** | **高** | **✓ v1** |
| (b) Edge traversal time | 中（path-level） | 易 | 高 | ✗（需 GNN）|
| (d) End-to-end T̂(bot, pod, station) | 弱（黑盒） | 易 | 中 | ✗ |

選 (c) 的關鍵理由：與 BAED 數學同型，整合接點不變，ablation 公平（B5 vs C2 差別只在 Regime B/C 是否啟用 CNN）。

### 2.5 網路架構（v1）

ConvLSTM encoder + 3D-Conv decoder（U-Net 風）：

```
Input  X ∈ R^{B × C × L × H × W}    (C=12, L=5, H×W=60×60 for large)

Encoder:
  ConvLSTM2D(in=12, out=64, k=3) × 2 layers   # 萃取 history temporal feature
  Conv2D(64→128, k=3, stride=2) + ReLU
  Conv2D(128→128, k=3) + ReLU
  Conv2D(128→256, k=3, stride=2) + ReLU

Decoder (predict T_future frames):
  ConvTranspose2D(256→128, k=3, stride=2)
  ConvTranspose2D(128→64, k=3, stride=2)
  Conv3D(64 → T_future, k=(1,3,3))            # 出 30 frames future field
  ReLU                                          # EntryDelay ≥ 0

Output Ŷ ∈ R^{B × T_future × H × W}
```

參數量約 3–5M，single GPU 訓得動，inference < 20 ms / epoch（large）。

---

## 3. 訓練資料生成

### 3.1 模擬蒐集 pipeline（spec §資料蒐集）

每 1 秒 snapshot 一次，存 `(X_t, future_actual_EntryDelay[t..t+T_future])` pair。

**Ground truth 萃取**：

從 RAWSim-O `traversal_log.csv` 反推 actual EntryDelay：對每筆 segment `(bot, from_v, to_v, t_request_enter, t_actual_enter)`，
```
actualEntryDelay(to_v, t_request_enter) = t_actual_enter − t_request_enter
```

匯整成 dense field：對所有 (v, t) 中**沒有 bot 嘗試進入**的點，label = 0（自由）。

### 3.2 蒐集設定 sweep

避免單一壅塞水準過擬合：

| 變因 | sweep 範圍 | 目的 |
|---|---|---|
| Bot count | {30, 45, 60} | 涵蓋低/中/高壅塞 |
| Order arrival rate | baseline ±30% | 涵蓋淡旺峰 |
| Seed | 5 個 | 隨機性 |
| Layout | large + medium | 拓樸多樣性 |
| Cost policy | baseline HADGS + BAED-only (B5) | 涵蓋兩種派工分佈 |

→ 9 settings × 5 seeds × 2 cost policy = 90 runs × 7200s × 1 snapshot/s ≈ **650k snapshots**

實際儲存體積：60×60×12×4byte ≈ 170KB / snapshot × 650k ≈ 110 GB（可接受，外接 SSD 存）。

### 3.3 Train/val/test split

| Split | 切法 | 規模 |
|---|---|---|
| Train | seeds 0–2 全部 settings | ~390k |
| Val | seed 3 全部 settings | ~130k |
| Test | seed 4 全部 settings | ~130k |

**重要**：依 seed 切而非 random shuffle，避免時間相關洩漏（同一 run 內 t 與 t+1 snapshot 高度相關）。

### 3.4 DAgger-style iterative training

policy-consistent 要求：訓練資料的派工策略需與部署一致，否則 distribution shift。

```
Iter 0: 用 baseline HADGS (B0) 蒐集 D_0 → 訓 CNN_0
Iter 1: 用 HADGS + CNN_0 (C1) 蒐集 D_1 → 用 D_0 ∪ D_1 訓 CNN_1
Iter 2: 用 HADGS + CNN_1 蒐集 D_2 → 訓 CNN_2
停止條件: KPI delta 連續兩輪 < 1% 或 D̂ 預測 MAE 收斂
```

至少跑 2 輪；第 1 輪資料常已足夠（CNN 不會根本改變 HADGS 排序），但要實證驗證。

---

## 4. Candidate-level Decision Quality Validation（沿用 BAED §4 流程）

**這是進 KPI 實驗前的唯一 gating criterion。**

### 4.1 Shadow estimator 擴充

baseline run 內同時計算五種 score：

| Score | 定義 |
|---|---|
| `c_dist` | 原始 static distance |
| `c_baed` | BAED only (Regime A + C，C 退化 distance) |
| `c_baed_full` | BAED + delay cap + station modifier |
| `c_baed_cnn` | BAED Regime A + CNN-CF Regime B/C |
| `c_baed_cnn_full` | 上式 + delay cap + station modifier |

### 4.2 評估指標

| 指標 | 成功門檻 |
|---|---|
| Spearman ρ(c_baed_cnn, c_real) > ρ(c_baed, c_real) | CNN-CF 對 Regime B/C 有實質貢獻 |
| Top-1 regret(c_baed_cnn) < regret(c_baed) | 排序提升足以翻轉 M1G/HADGS 決策 |
| 第二段 path（pod→station）的 ρ 改善幅度 | 主目標——這段是 BAED 失效區 |

**Gating**：若 `ρ(c_baed_cnn) − ρ(c_baed) < 0.05`，**禁止進入 §5 KPI 實驗**，回頭檢查：(a) CNN 預測 MAE 是否夠低、(b) Regime B/C 內 EntryDelay 訊號是否本來就稀疏、(c) DAgger 是否需要再一輪。

---

## 5. KPI 實驗矩陣

延伸 BAED §5 的 B0–B5，加入 C 系列：

| ID | 描述 | 對照 |
|---|---|---|
| B0 | Original distance | absolute baseline |
| B1 | A2 density residual estimator | narrative 對照 |
| B5 | BAED + cap + station modifier | BAED 主線最終版 |
| **C1** | **CNN-CF only**（無 BAED Regime A）| 學習式單獨是否夠用 |
| **C2** | **BAED A + CNN-CF B/C**（hybrid） | **論文主打** |
| C3 | C2 + station modifier | 完整 stack |
| E1 | C3 + DAgger iter 2 | DAgger 邊際 |
| E2 | GNN 版（若 CNN 在 HighwayHallway 失效）| 架構 ablation |

KPI 同 BAED §5.1（TP / PO / OD / RD + decision time + station idle + queue），全部由 `kpi_report.csv` 直接抽。

### 5.1 進入長跑的條件

```
C2 vs B5: TP ≥ B5 AND PO ≥ B5 AND (OD < B5 OR RD < B5)
C2 vs B0: 同 BAED 主線標準
```

通過 → 5 seeds × 28800s，最終 → 15 seeds × 7200s paired Wilcoxon。

---

## 6. Ablation 設計

| ID | 設定 | 要證明的事 |
|---|---|---|
| A0 | distance | absolute baseline |
| A_baed | BAED only | analytical 部分的價值 |
| **A_cnn** | **CNN-CF only** | **學習式單獨價值** |
| **A_hybrid** | **BAED A + CNN B/C** | **hybrid 邊際貢獻**（核心對照）|
| A_horizon | T_future ∈ {10, 30, 60} | 預測時域敏感度 |
| A_channels | 去掉 reservation channel | committed reservation 對學習是否關鍵 |
| A_layout | 跨 layout 泛化（train large → test medium） | distribution shift robustness |
| A_botcount | train 30 bots → test 60 bots | 規模泛化 |

關鍵對照：

* **A_hybrid vs A_baed**：CNN 對 Regime B/C 補完是否真有用
* **A_cnn vs A_baed**：學習式能否單獨擊敗 analytical（若是，BAED 可裁掉）
* **A_layout / A_botcount**：泛化能力——這是論文回應「為何不用解析式就好」的關鍵實證

---

## 7. 成功 / 失敗判準

### 7.1 最低成功

1. Candidate-level：`ρ(c_baed_cnn) > ρ(c_baed)`，且第二段 path 改善顯著
2. C2 vs B5：OD/RD 至少一項顯著下降，TP/PO 不退化
3. 跨 layout/bot count 泛化誤差 < 30%

### 7.2 理想成功

C3 vs B5: TP↑ PO↑ OD↓ RD↓，且 inference latency < 20% of M1G decision time

### 7.3 失敗條件

1. CNN 預測 MAE 收斂後仍高於 actual EntryDelay 的 std（學不到訊號）
2. C2 vs B5 在 candidate-level 顯著贏，但 system-level 沒贏（排序差異被 M1G/HADGS feasibility 吃掉）
3. 跨 bot count 泛化 MAE 增加 > 2×（過擬合特定 fleet size）
4. DAgger 第 2 輪 KPI 退化（policy shift 反向）

---

## 8. 最小可行實作

| 檔案 | 動作 |
|---|---|
| `RAWSimO.Core/Statistics/SnapshotLogger.cs` | 新增；每秒輸出 `snapshot_t.npz`（X_t multi-channel tensor） |
| `RAWSimO.Core/Statistics/TraversalLogger.cs` | 已存在；確認 `t_request_enter` / `t_actual_enter` 雙欄完整 |
| `analysis/cnn_field/dataset.py` | 新增；snapshot + traversal log → (X, Y) pairs |
| `analysis/cnn_field/train.py` | 新增；ConvLSTM + U-Net training |
| `analysis/cnn_field/export.py` | 新增；PyTorch → ONNX 匯出 |
| `RAWSimO.Core/Metrics/CnnCongestionFieldEstimator.cs` | 新增；ONNX Runtime C# binding，每 M1G epoch 一次 forward |
| `RAWSimO.Core/Metrics/BAEDEstimator.cs` | 改 Regime B/C 分支，呼叫 CNN-CF |
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 加 `UseCnnField`、`CnnModelPath`、`CnnFutureHorizon` |
| `RAWSimO.Core/Statistics/DecisionRankingLogger.cs` | 已存在；加 `c_baed_cnn` / `c_baed_cnn_full` 兩欄 |

**ONNX Runtime C# 整合**：CNN 部署用 ONNX 而非重寫 inference，避免 C# 端 manual conv 實作。Nuget package `Microsoft.ML.OnnxRuntime`。

刻意不在最小版做的：
* GNN 架構（E2，留 ablation）
* Online fine-tuning（每 run 內更新權重）
* Probabilistic output（predict mean + var）

---

## 9. 關鍵批判：CNN-CF 能合理改善 TP/PO/OD/RD 嗎？

### 9.1 為什麼「應該」會贏 BAED-only (B5)

* BAED 第二段 path 多半進 Regime C → 等於用 distance。CNN-CF 把第二段 cost 從 distance 救回成 delay-aware → **第二段排序準度應有明顯提升**
* 大 fleet (60 bots) + large layout → 壅塞訊號豐富，CNN 學得到 spatial pattern（aisle bottleneck、station queue 形成的 hotspot）
* HADGS 的 candidate 數比 M1G 多（不過 R×P×S 三元組），CNN 排序差異更容易翻轉

### 9.2 為什麼「可能會輸」

| 失敗模式 | 因 | 診斷指標 |
|---|---|---|
| **Regime B/C 內 EntryDelay 訊號稀疏** | 未來 30s 內絕大多數 node 仍 free | `fraction of Ŷ > threshold` < 5% → 訊號不足 |
| **CNN 學到 mean-revert pattern**，無法區分 candidate | training MAE 收斂但 Spearman 沒提升 | training loss 低、validation Spearman vs ranking 沒漲 |
| **Distribution shift after DAgger iter 1** | CNN 把流量推到別處，新 hotspot 沒見過 | iter 2 訓練 MAE 高於 iter 1 |
| **ONNX inference latency 過大** | large × 60×60×30 frames 每 epoch | M1G/HADGS decision time > 1.5× B0 |
| **泛化失敗** | train 60 bots → test 30 bots 過擬合 | A_botcount ablation MAE 增加 > 2× |

### 9.3 與 BAED 主線最大風險的對比

BAED 主線最大風險是「narrative 對、邊際小」。CNN-CF 的最大風險是相反：「邊際大但泛化差」。兩個風險可以互補——若 CNN-CF 跨 setting 泛化不過關，可退守 hybrid (BAED + CNN-CF 只在訓練分佈內啟用)。

### 9.4 整體判斷

* **第二段 path 排序改善機率高**（low risk）：因為 BAED 在那段本來就是 distance，CNN 只要學到任何訊號都贏
* **system-level KPI 改善機率中等**（medium risk）：取決於排序改善是否大到翻轉 HADGS bipartite MILP 的決策
* **跨 layout/bot count 泛化機率低-中**（main risk）：CNN 慣常的泛化弱點；需 ablation 驗證後決定是否限制部署範圍

---

## 10. 開放問題

| ID | 問題 | 影響章節 |
|---|---|---|
| Q1 | T_future = 30s 足夠？large + 60 bots 第二段 path 平均多久 | §2.3, §5 |
| Q2 | DAgger 收斂幾輪？預算 2 輪是否夠 | §3.4 |
| Q3 | ONNX Runtime C# 在 Windows + .NET Framework 的相容性與 latency | §8 |
| Q4 | snapshot 1s 間隔是否過密？降到 2s 是否影響精度 | §3.1 |
| Q5 | reservation channel (5) 是否會 leak Regime A 訊號 → 評估 CNN 對 Regime B/C 貢獻時不公平 | §2.2, §6 |
| Q6 | small layout 是否值得也跑一次（M1G 路線 sanity check）| §5 |
| Q7 | 是否要做 probabilistic output（predict mean + var）支援風險敏感 cost | §2.5, §8 |

---

## 11. 實作 Phase 規劃

| Phase | 範圍 | 退出條件 |
|---|---|---|
| **D1** | SnapshotLogger + TraversalLogger 驗證；跑 baseline HADGS B0 蒐集 D_0（large + 3 bot counts × 5 seeds） | 110 GB 資料完整、可重建 (X, Y) pairs |
| **D2** | ConvLSTM 訓練 + val Spearman gating | val Spearman ≥ 0.7、Top-1 regret 低於 distance baseline |
| **D3** | ONNX 匯出 + C# 整合 + Shadow Logger 驗證 | `ρ(c_baed_cnn) − ρ(c_baed) ≥ 0.05` candidate-level |
| **D4** | 5 seeds × 7200s C2 vs B5 KPI 對比 | TP/PO 不退化、OD/RD 至少一項顯著下降 |
| **D5** | DAgger iter 1 蒐集 D_1 + 再訓 | iter 1 model 不退化 |
| **D6** | Ablation E1 (DAgger iter 2)、A_horizon、A_layout、A_botcount | 完整 ablation table |
| **D7** | 15 seeds × 7200s paired Wilcoxon final run | 論文最終結果表 |

預估時程：D1–D4 約 4 週，D5–D7 約 3–4 週，total 7–8 週，落在三個月口試窗內。

---

*End of design note. 配套 memory：[[project_baed_method]] / [[project_thesis_method]]。短期主線仍為 BAED；CNN-CF 進入後 [[project_thesis_method]] 的長期 NN cost oracle 段落需更新為「已實作為 CNN-CF」。*
