# BAED on HADGS：大規模 layout 部署計畫

**Author:** Aesop / AI research assistant
**Status:** Draft — 與 `design_note_baed.md`（M1G + small layout 版本）並行
**Spec source:** `spec.md`（2026-05-14 修正版，方法論部分仍適用）
**Pivot 動機:** M1G 在 large layout 求解時間爆炸無法線上跑；同時 small (10 bots) 擁擠度不足，BAED 訊號被 distance 主導。改 HADGS（heuristic + 輕量 MILP）作上層派工 + large layout (60 bots) 解決兩個問題。
**Layout / Setting:**
- Layout `Material/Instances/CoreBenchmark/1-6-12-60-0.89-large.xlayo` — 60 bots, 12 output stations + 6 input + 6 replenishment, SingleLane HighwayHallway, O-station capacity 6
- Setting `Material/Instances/CoreBenchmark/large_7200_300.xsett` — 7200s sim, 5000 warmup orders, 300 evaluated orders, InventoryLevelDriven order generation
- Control `Material/Instances/CoreBenchmark/hadgs.xconf`（HADGS + BalancedTA + WHCA\* window=30s）

---

## 0. 與 `design_note_baed.md` 的關係

`design_note_baed.md` 的 §2（BAED 形式定義）、§3（演算法）、§4（candidate-level validation）、§7（成功失敗判準）、§9（批判性風險分析）**全部沿用**。本文件**只記差異**——換 manager、換 layout 後新增 / 修改的部分。

| 軸 | M1G + small（舊文件） | HADGS + large（本文件） |
|---|---|---|
| 上層派工 | M1G MILP（OA+PS+TA joint） | HADGS heuristic + 輕量 bipartite MILP |
| Layout | small (10 bots, 2 stations) | large (60 bots, 12 stations) |
| Fleet 擁擠度 | 低（util ≈ 0.9999 但 SNR 低） | 高（congestion 訊號顯著浮上來） |
| Cost hook 數 | 2 | **6** |
| Baseline | M1G distance | HADGS distance |
| Wall-clock / seed | ~5–10 min | **~2–8 h（須安排）** |

---

## 1. HADGS 的 6 個 BAED hook 點

`RAWSimO.Core/Control/Defaults/OrderBatching/HADGSManager.cs`：

```
L44:  private double EstimateBotPodDistance(Bot bot, Pod pod)
L62:  private double EstimatePodStationDistance(Pod pod, OutputStation station)
```

兩個 function 內部呼叫 `Distances.CalculateShortestPath` / `DistanceSet[...]` / Manhattan fallback——介面完全等同於 `M1GManager.cs:117/134`。

所有使用點（grep `EstimateBotPodDistance|EstimatePodStationDistance` in HADGSManager）：

| 行 | 角色 | 對 BAED 替換的敏感度 |
|---|---|---|
| L182 | `_inboundPodsPerStation[s].OrderBy(EstimatePodStationDistance)` — inbound pod 排序 | 中 |
| L194 | `Pods.Sum(EstimatePodStationDistance)` — station 的總 inbound distance（station-level metric） | 中 |
| **L272** | **`−(40·completeable) + Σ(EstimateBotPodDistance + EstimatePodStationDistance)` — 核心 (r,p,s) joint scoring** | **高（主要決策來源）** |
| L480 | 同 L182（chosenStation 變數） | 中 |
| L535 | `Ra.OrderBy(EstimateBotPodDistance).FirstOrDefault()` — 找最近 robot fallback | 低 |
| **L641** | **Gurobi MILP objective: `min Σ yrp · EstimateBotPodDistance`** | **高（HADGS 內部 bipartite assignment）** |

### 1.1 替換策略

只改 L44 / L62 兩個 function body：

```csharp
private double EstimateBotPodDistance(Bot bot, Pod pod) {
    if (Instance.ControllerConfig.UseBAED && _baed != null)
        return _baed.BotPodDistance(bot, pod);   // 等效距離 (m)
    // ... 原有 fallback 邏輯保留
}
```

L272、L641 等 5 個 call site **自動繼承**——不需改 scoring 公式或 MILP 結構。

### 1.2 L272 的 `−40 · completeable` 需要重校（Phase C 路線3 教訓）

HADGS L272 寫死 `−40`，這在 distance 單位下校準。BAED 改變了 cost magnitude（加 `v_ref · totalEntryDelay`，可能讓 cost 從 ~50m 變成 ~70–100m 等效距離）。`−40` 的相對權重會被稀釋。

**處理方式**：第一版**不動 `−40`**，先跑 B2/B3，看 `completeable` 在 ranking 中的佔比是否仍合理。若 ranking 變得幾乎只看距離（completeable 失語），再做尺度補正：

```
scaleFactor = mean(BAED) / mean(distance)        # 從 baseline run 量
−40_new     = −40 × scaleFactor
```

把這個校準視為**新的 ablation 軸**（A6.cal vs A6）而非預設行為，避免一次動太多變數。

### 1.3 L641 MILP build / solve time 風險

HADGS 內部 MILP 是 `R × P` bipartite（60 × candidate pods），比 M1G 的 `R × P × S` 三元組小一個數量級。BAED 不改變變數個數，只改 coefficient 計算成本。

**風險**：BAED 對每個 `(r, p)` 都要 `O(|path_rp|)` 的 `DisjointIntervalTree` lookup。60 bots × ~30 candidate pods × ~50 edges/path ≈ 90k lookups/epoch。預期落在 100ms–1s 量級，可接受。**必須量測** `Cost matrix build time`（spec §八）。

---

## 2. Large layout 帶來的方法論收益（vs small）

### 2.1 擁擠度提升讓 BAED 訊號浮現

`design_note_baed.md §9.3` 列的失敗模式 #1（「fraction of edges with EntryDelay > 0 < 5%」）與 #2（「fraction of path_ps edges in Regime C ≈ 100%」），主因都是 small 太鬆。large layout 預期會明顯改善：

| 指標 | small 預期 | large 預期 | 來源 |
|---|---|---|---|
| Fraction(EntryDelay > 0) | < 5% | **20–40%** | 60 bots / 24-aisle grid 的 reservation 衝突頻率 |
| Avg WaitBeforeNode | ~0 | **0.5–2s** | SingleLane + AislesTwoDirectional=false 形成的 funnel |
| Regime A coverage(path_rp) | ~60% | **40%** | path 變長，超出 15s window 比例上升 |
| Regime A coverage(path_ps) | ~5% | **15%** | 同上但 baseline 更低 |

→ Regime B（RRA\* 投影）**可能變成必要而非可選**。第一版仍只做 A + C；若 §3 candidate-level validation 顯示第二段 BAED ≈ 純 distance，必須補上 B。

### 2.2 Station modifier 真正能發揮

small layout 只有 2 個 output stations，兩個都很容易滿載 → starvation/overload 訊號弱。large 有 12 個 station 分東/西側，且 O-station capacity = 6，starvation/overload 同時存在的機率高。**spec §五 的 station modifier 在 large 才會真正貢獻 PO**。

### 2.3 Robot util 不會貼 1.0

`large_7200_300.xsett` 5000 warmup + 300 evaluated 模式，加上 InventoryLevelDriven 的 order stop/restart，預期 robot util 落在 0.85–0.95，**留有派工選擇空間** → cost feedback 才有東西可優化。

---

## 3. Validation / KPI 矩陣調整

### 3.1 Baseline 換成 HADGS distance（不可跨比 M1G）

| ID | 設定 | 改自舊 ID |
|---|---|---|
| B0\* | HADGS distance（hadgs.xconf 原樣） | 對應舊 B0 (M1G distance) |
| B1\* | HADGS + 舊 density residual estimator（A2 移植） | 對應舊 B1 |
| B2\* | HADGS + BAED only | B2 |
| B3\* | HADGS + BAED + delay cap | B3 |
| B4\* | HADGS + BAED + station modifier | B4 |
| B5\* | HADGS + BAED + delay cap + station modifier | B5 |
| B5.cal | B5 + L272 `−40` 重校 | 新增（HADGS 限定） |

KPI 與成功條件沿用 `design_note_baed.md §5`，但**「M1G decision time」改為「HADGS scoring time + L641 MILP time」分兩欄記錄**。

### 3.2 Wall-clock 預算與 seed 安排

| 規模 | 單 seed wall-clock（估計） | 5 seeds | 15 seeds |
|---|---|---|---|
| small + m1g_time（舊） | 5–10 min | < 1 h | < 3 h |
| **large + HADGS（新）** | **2–8 h** | **10–40 h** | **30–120 h** |

→ 第一輪只跑 **3 seeds × 7200s × {B0\*, B2\*, B5\*}** = 9 runs，先確認 BAED 在 large 是否真有 candidate-level ranking 提升。通過 gate 再擴 5 seeds + 全 ablation。

### 3.3 Candidate-level validation 的 large 版本

`design_note_baed.md §4` 的 4 種 score（c_dist / c_old / c_baed / c_baed_full）+ Spearman / Kendall / top-1 regret 全部沿用。**top-k 從 10 提高到 30**：

- HADGS 每 epoch 候選 (r, p, s) 數量比 M1G 更多（60 bots × 大量 pods）
- 排序差異在 top-30 內才能展示 BAED 真正翻轉 ranking 的能力

Gating（與 M1G 版相同）：BAED 在 Spearman 與 top-1 regret 同時擊敗 distance 才進 KPI 實驗。

---

## 4. 風險與既有不確定性

### 4.1 沿用 `design_note_baed.md §9.3` 的失敗模式表

但對應到 large 後幾項改動：

| 失敗模式 | small 風險 | large 風險變化 |
|---|---|---|
| EntryDelay 訊號太弱 | 高 | **降為中**——擁擠度上升 |
| 第二段全進 Regime C | 高 | **降為中**——但 path_ps 變長仍可能觸發 |
| Station modifier 灌某 station | 低 | **升為中**——12 站可能造成東/西不平衡 |
| DelayCap 過大/過小 | 中 | **升為高**——擁擠度高，極端 reservation 更常見 |
| 長跑 compounding 反轉 | 中 | **升為高**——60 bots 互擾鏈條長 |
| B1 已偷學到等待 | 中（thesis 核心風險） | **持平**——needs explicit A3\* vs A2\* 比 |

### 4.2 HADGS 特有的新風險

1. **L272 hard-coded −40 與 BAED 尺度錯配** — 已於 §1.2 處理
2. **HADGS 重排頻率 vs M1G epoch 不同** — HADGS 在 `BotReallocationTimeout=30s`、PodSelection 是事件驅動。BAED query 的觸發時機更密集，需要確認 cache invalidation 邏輯（每次 scoring 都重算 EntryDelay 還是有 epoch-level cache）
3. **L535 fallback `OrderBy(EstimateBotPodDistance)` 可能在 BAED 下變慢** — 對 Ra 全集排序，每個 element 都要算一次 BAED。large 下需驗證不會變成 hot path
4. **`5000 warmup orders` 是否被計入 KPI** — 需確認 `kpi_report.csv` 是否排除 warmup 段；若沒，所有比較會被 warmup 主導
5. **HADGS 在 large 的 baseline 數值未知** — `design_note_baed.md` 的 small baseline (TP=327.5 等) 完全不適用。第一步必須先跑 B0\* 5 seeds 拿 baseline

---

## 5. 最小可行實作範圍

對 `design_note_baed.md §8` 的修改：

| 檔案 | 動作（HADGS 版本） |
|---|---|
| `RAWSimO.Core/Metrics/BAEDEstimator.cs` | 新增（同 M1G 版） |
| **`RAWSimO.Core/Control/Defaults/OrderBatching/HADGSManager.cs:44, 62`** | **改 `EstimateBotPodDistance` / `EstimatePodStationDistance` 兩 function body**（取代 M1GManager 的兩 hook） |
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 加 `UseBAED / UseDelayCap / UseStationModifier / DelayCap / Eta / Gamma`——`HADGSConfiguration` 與 `M1GConfiguration` 共用同一組 BAED config（建議拉到 base class 或 shared static） |
| `RAWSimO.Core/Statistics/DecisionRankingLogger.cs` | 新增；hook 進 HADGSManager scoring loop 內，記 top-30 候選 |
| `RAWSimO.MultiAgentPathFinding/Methods/WHCAnMethod.cs` | 只加 public read-only snapshot accessor（同 M1G 版禁止 mutation） |
| `Material/Instances/CoreBenchmark/hadgs_baed.xconf` | 新增；複製 `hadgs.xconf` + 開 BAED flags |

刻意不做：
- M1G 路線（保留 `design_note_baed.md` 不動，small 路線封存）
- A2 舊 estimator 的 HADGS 移植（除非 candidate-level validation 結果支持） — 因為 A2 與 BAED 在 large 比較需要它，但可延後到第一輪 candidate-level 通過後再做

---

## 6. 起手三步（具體 actionable）

1. **驗證 baseline 可重現**：拉 `hadgs.xconf + 1-6-12-60-0.89-large.xlayo + large_7200_300.xsett`，seed=0 跑一輪 7200s，記錄 TP/PO/OD/RD/walltime/HADGS scoring time/L641 MILP time。建立 large baseline reference。
2. **跑 3 seeds × 7200s baseline**：seed ∈ {0,1,2}，建立 paired 比較的對照組。確認 KPI 變異（CV）量級，估計需要多少 seeds 才能在 BAED 開啟後拒絕 null。
3. **實作 BAED minimal version + 跑 candidate-level validation**：在 baseline run 上 attach `DecisionRankingLogger`（B0\* 模式不開 BAED），只 shadow 計算 c_baed / c_baed_full vs c_dist 的 Spearman / Kendall / top-1 regret。這一步**不動 HADGS 決策**，純記錄。決定是否進入 §3 的 KPI 實驗。

---

## 7. 對 spec.md 的偏離 / 補充

| spec 條目 | 偏離 | 理由 |
|---|---|---|
| §一「M1G/OJOOPT 決策」 | 改為 HADGS scoring | M1G 在 large 不可行 |
| §五 station modifier | 沿用，但 small / 2 stations 改為 large / 12 stations | 設計不變，量級需重校 |
| §六「M1G decision epoch」 | 改為 HADGS replan / `BotReallocationTimeout` 觸發 | HADGS 重排頻率不同 |
| §八 KPI 比較 baseline | 改為 HADGS distance baseline | 不可跨 manager 比 |
| §九 ablation | 全表加 `*` 標示 HADGS variant，A2 移植優先級下降 | 大樣本確認 narrative 比比 small 重要 |
| §十三 最小實作 | 新增 L272 `−40` 重校為獨立 ablation 軸 | HADGS 特有 |

**主要研究敘述不變**：本研究將 RAWSim-O 上層派工的 distance-based cost 修正為 WCHA\* committed reservation 推導之 blocking-aware effective distance，並透過 station modifier 處理 throughput / utilization trade-off。M1G 與 HADGS 為兩種上層 manager 的對照——M1G 展示方法在 MILP 結構下的可行性（small），HADGS 展示方法在 large layout / 高擁擠度下的實證效益。

---

*End of HADGS + Large layout design note.*
