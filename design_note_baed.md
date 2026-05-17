# BAED：WCHA\* 感知之阻塞感知等效距離

**Author:** Aesop / AI research assistant
**Status:** Draft — 取代 `design_note.md`（舊設計降為 ablation A2 對照組）
**Spec source:** `spec.md`（2026-05-14 修正版）
**Companion paper（ablation only）:** MAPFUU (Street et al., AAMAS 2020)

---

## 0. 與舊設計文件的關係

| 文件 | 方法定位 | 在本研究中的角色 |
|---|---|---|
| `design_note.md` | MAPFUU-inspired edge traversal time uncertainty + 經驗 (edge, band) → μ̂ 表 | **降級為 ablation A2 對照組**；保留作為「density residual estimator」的代表 |
| `design_note_baed.md`（本文件） | WCHA\* committed reservation 推導之 node-entry blocking delay + station modifier，輸出 distance-like cost | **本研究主線方法** |

舊文件 §3–§4 對 RAWSim-O 既有碼（`WHCAnMethod.cs`、`ReservationTable.cs`、`M1GManager.cs:117/134/563`）的解構在本文件中**繼續引用**；本文不重複這些基礎事實，只記新方法所需的差異。

---

## 1. Executive Summary

舊方法把壅塞建模為「edge 內部通行時間變慢」(`T(edge) = baseline + residual(carrying, density)`)。這在 MAPFUU 的 bidirectional, multi-occupant edge 模型下成立，但在 RAWSim-O 的 **node-exclusive, one-way SingleLane** 模型下與物理語意不一致：兩台 bot 不會在同一格繞行彼此，而是後者在前者尚未離開 next node 前必須停車等待。

BAED 把同一份 WCHA\* committed reservation 資訊改用另一種讀法：

```
T(u → v) = MoveTime(u, v, carrying) + EntryDelay(v, t_arrival, carrying, blocking)
BAED     = physicalDistance + v_ref · totalEntryDelay + stationModifier
```

並沿 robot→pod→station 兩段路徑以 `tCursor` 串接累積。輸出為 **distance-like cost**，直接替換 `M1GManager.EstimateBotPodDistance` / `EstimatePodStationDistance` 的回傳值，不動 MILP 結構與 `w1/w2/w3`。

驗證上採 spec §七 的順序：**先 candidate-level decision-quality 比對**，BAED ranking 必須比 static distance 更貼近 WCHA\* 實際執行成本，才進 KPI A/B。

---

## 2. 方法論修正核心

### 2.1 物理語意差異（一句話版本）

| | MAPFUU | RAWSim-O / WCHA\* |
|---|---|---|
| Node 容量 | multi-occupant edge | **node-exclusive** |
| 壅塞表現 | edge traversal time 的隨機分佈 | **next node entry 前的確定性等待** |
| Reservation 結構 | per-robot route CTMC（PRT） | **node-keyed disjoint-interval tree**（已是時空保留資訊本身） |
| 不確定性來源 | 多 bot 同 edge 互擾 | 主要是 reservation 衝突 → 嚴格等待（已半確定）|

→ 舊方法把 WCHA\* 已經確定的「等待」當成「未知 edge 通行隨機性」去擬合，是雙重損失：(a) 丟掉已知的時間資訊，(b) 拿一個錯誤的物理模型去回擬。

### 2.2 BAED 的形式定義

對 directed edge `(u → v)`：

```
MoveTime(u, v, carrying)
    = Physics.getTimeNeededToMove(speed_in, Edge.Distance, carrying)
      + Physics.getTimeNeededToTurn(prevAngle, Edge.Angle)
```

`arrivalAttemptTime = tCursor + MoveTime`

```
EntryDelay(v, t_arr, carrying, B)
    = max(0, FirstFreeStart(v, t_arr) − t_arr)
      + downstreamMergePenalty(v, t_arr)
      + stationEntrancePenalty(v, t_arr)
```

其中 `FirstFreeStart(v, t_arr)` 是 node v 的 `DisjointIntervalTree` 在 `[t_arr, ∞)` 內第一個 free interval 的起點。若 `t_arr` 本身已 free，`EntryDelay` 退化為 merge / station 修正項（多半為 0）。

於是：

```
T(u → v) = MoveTime + EntryDelay
tCursor  ← tCursor + T(u → v)
```

兩段加總後得到 `t_robot_arrive_pod`、`t_pod_arrive_station`。

### 2.3 從 seconds 轉成 distance-like cost

```
BAED(r, p, s)
    = Σ_{e ∈ path_rp} Edge.Distance(e)
    + Σ_{e ∈ path_ps} Edge.Distance(e)
    + v_ref · ( totalEntryDelay_rp + totalEntryDelay_ps )
    + stationModifier(s)
```

`v_ref` 取 bot 空載巡航速度（`Physics.MaxVelocity`，small layout 約 1.5 m/s）。這樣 BAED 與舊 `EstimateBotPodDistance` 同單位（公尺），M1G 不需重新校準 `w1`。

### 2.4 Station Modifier（spec §五）

```
AdjustedCost(r, p, s)
    = BAED(r, p, s)
    − η · StationStarvationRisk(s)
    + γ · StationOverloadRisk(s)
```

* `StationStarvationRisk(s)` = `max(0, S_min − InboundPodCount(s))` × `bayWidth`
* `StationOverloadRisk(s)` = `max(0, InboundPodCount(s) − S_max)` × `bayWidth` + `α · QueueOccupancyRatio(s)` × `bayWidth`
* `η, γ` 都以 `bayWidth`（layout 一個 bay 的物理長度）為單位，避免壓過 BAED 主體
* 預設 `S_min = stationCapacity − 1`、`S_max = stationCapacity`、`η = γ = 0.5 · bayWidth`、`α = 1.0`，後續以 ablation A5 / A6 微調

---

## 3. 演算法流程（pseudocode）

```
on every M1G decision epoch t0:
    # 1. 分類 agents
    external = bots with active WCHA* path or non-null destination
    internal = candidate bots for this epoch

    # 2. rolling blocking field 是免費的：直接讀
    # WCHAnMethod._reservationTable + _calculatedReservations
    # 不複製、不快取、不寫入

    for each candidate (r, p, s):
        path_rp = Distances.CalculateShortestPath(r.CurrentWaypoint, p.Waypoint)
        path_ps = Distances.CalculateShortestPathPodSafe1(p.Waypoint, s.Waypoint)

        if path_rp is None or path_ps is None:
            cost(r,p,s) = StaticDistanceFallback(r,p,s)
            continue

        # 3. robot → pod (carrying=false)
        tCursor = t0
        sumMove_rp, sumDelay_rp = 0, 0
        for edge (u, v) in path_rp:
            tMove  = MoveTime(u, v, carrying=false, prevAngle)
            tArr   = tCursor + tMove
            tDelay = min( EntryDelay(v, tArr, carrying=false), DelayCap )
            tCursor   = tArr + tDelay
            sumMove_rp  += tMove
            sumDelay_rp += tDelay
        tRobotArrivePod = tCursor

        # 4. lift / pickup
        tCursor = tRobotArrivePod + T_lift

        # 5. pod → station (carrying=true)
        sumMove_ps, sumDelay_ps = 0, 0
        for edge (u, v) in path_ps:
            tMove  = MoveTime(u, v, carrying=true, prevAngle)
            tArr   = tCursor + tMove
            tDelay = min( EntryDelay(v, tArr, carrying=true), DelayCap )
            tCursor   = tArr + tDelay
            sumMove_ps  += tMove
            sumDelay_ps += tDelay
        tPodArriveStation = tCursor

        # 6. distance-like cost
        physDist = sum(Edge.Distance for e in path_rp + path_ps)
        BAED     = physDist + v_ref * (sumDelay_rp + sumDelay_ps)
        cost(r,p,s) = BAED
                   - eta   * StationStarvationRisk(s)
                   + gamma * StationOverloadRisk(s)

    # 7. M1G 解 MILP，objective 結構不動，僅替換 cost coefficient
    # 8. WCHA* 按 M1G 結果產生實際路徑
    # 9. log: predicted vs actual (for §4 candidate-level validation)
```

關鍵實作不變量：

* **read-only**：禁止任何分支（包含 dry-run）寫 `_reservationTable` / `_calculatedReservations` / `Graph.NodeInfo`（這是舊 PPCostFeedbackEstimator 死鎖的根因，見 MEMORY `project_prt_implementation_state`）
* **bounded compute**：每 query `O(|path|)`，每 M1G epoch 整體約 `R × P × S × |path|` 次 `DisjointIntervalTree` lookup；small layout ~36k 次，落在毫秒級
* **deterministic**：固定 reservation + table snapshot 必得相同結果
* **DelayCap**：單邊 entry delay 上限（預設 `LengthOfAWindow = 15s`），避免單一極端 reservation 主導整條路徑

### 3.1 EntryDelay 的三段時間 regime

| Regime | t_arr 範圍 | 來源 | 行為 |
|---|---|---|---|
| A | `t_arr ≤ t_now + LengthOfAWindow` (15s) | WCHA\* committed reservation | `FirstFreeStart(v, t_arr) − t_arr` 直接查 `DisjointIntervalTree` |
| B | `t_arr ≤ t_now + RRA_horizon` | RRA\* abstract node list（無時間） | 以 nominal Physics 投影 external agents 抵 v 的時間 → 估 entry delay |
| C | `t_arr > RRA_horizon` | 無資訊 | `EntryDelay = 0`；落回純 physical distance |

Regime A 是主訊號；B 只在第二段（pod→station，因為 `tCursor` 已加上 robot→pod + T_lift，常超出 15s window）才會大量觸發；C 是 fallback。

---

## 4. Candidate-level Decision Quality Validation（spec §七）

**這是進 KPI 實驗前的唯一 gating criterion。**

### 4.1 收集流程

在 baseline run（B0, original distance）執行下啟用一個 **shadow estimator logger**：

每個 M1G epoch 對 top-k candidates（k = 10）同時計算四種 score：

| Score | 定義 |
|---|---|
| `c_dist` | 原始 static distance |
| `c_old` | 舊 MAPFUU-inspired residual estimator（從 `feature/congestion-aware-cost` worktree 移植成 read-only library） |
| `c_baed` | BAED（不含 station modifier） |
| `c_baed_full` | BAED + delay cap + station modifier |

`c_real`（actual cost）取以下其一：

* **首選**：M1G 實際選中的 candidate 之 WCHA\* 執行完後的真實 `(tRobotArrivePod, tPodArriveStation, totalDistance)`
* **退而求其次**：對未被選中的 candidate 做 post-hoc replay validation（單獨 dry-run WCHA\* 在保留當下 snapshot 的副本上）。第一階段不做 replay，只記被選中那一條的真實成本，spec §七 接受此簡化。

### 4.2 評估指標

對每個 epoch（top-k 內部）計算：

| 指標 | 定義 | 成功門檻 |
|---|---|---|
| Spearman ρ | rank correlation(c_*, c_real) | `ρ(c_baed_full) > ρ(c_dist)` 顯著為正 |
| Kendall τ | 同上 | 同上 |
| Top-1 regret | `c_real(arg min c_*) − min c_real` | `regret(c_baed_full) < regret(c_dist)` |
| Selected actual cost | `c_real` of M1G's choice | `c_baed_full ≤ c_dist`（成對檢定） |

聚合：跑 5 seeds × 1800s（短跑），每個 seed 收集 ~200 epochs，跨 seed 算 paired Wilcoxon。

**Gating**：若 BAED 沒在 Spearman 與 top-1 regret 同時擊敗 `c_dist`，**禁止進入 §5 的 KPI 實驗**，回頭調整 EntryDelay 公式 / DelayCap / Regime B 投影。

---

## 5. KPI 實驗矩陣（spec §八）

### 5.1 第一輪：5 seeds × 7200s

| ID | 描述 |
|---|---|
| B0 | Original M1G distance（baseline，等同 main 1009517） |
| B1 | 舊 residual density estimator（從舊 worktree 移植，等同 A2） |
| B2 | BAED only（無 cap、無 modifier） |
| B3 | BAED + delay cap |
| B4 | BAED + station modifier |
| B5 | BAED + delay cap + station modifier（最終版本） |

KPI（全部已在 `kpi_report.csv`）：

| 名稱 | 方向 | 來源 column |
|---|---|---|
| TP（throughput） | ↑ | `orders_per_hour` |
| PO（pile-on） | ↑ | `system_order_pile_on` |
| OD（order distance） | ↓ | `order_distance_m` |
| RD（robot distance） | ↓ | `total_distance_m` |
| M1G decision time | ↓ / 持平 | new log |
| Cost matrix build time | ↓ / 持平 | new log |
| Station idle time | ↓ | new log |
| Station queue length（avg） | ↓ / 持平 | new log |
| Avg waiting before node entry | ↓ | new log |

### 5.2 進入長跑與最終驗證的條件

```
B5 vs B0: TP ≥ baseline AND PO > baseline AND OD < baseline AND RD < baseline
B5 vs B1: OD/RD ≥ B1 AND TP/PO ≥ B1（不可退化）
```

若通過 → 5 seeds × 28800s 檢查長期 compounding。
最後最佳版本 → **15 seeds × 7200s paired**，跑 Wilcoxon signed-rank one-sided，報 mean/median delta、win count、effect size。

---

## 6. Ablation 設計（spec §九）

| ID | 設定 | 目的（要證明的事） |
|---|---|---|
| A0 | Original distance | baseline |
| A1 | Kinematic baseline only（純 `Physics` 不含 entry delay） | physical movement correction 是否有效 |
| A2 | 舊 residual density estimator | density-residual narrative vs blocking-aware narrative |
| A3 | BAED entry-delay only | blocking-aware 語意本身的價值 |
| A4 | A3 + delay cap | 避免單邊極端 reservation 主導 |
| A5 | A3 + station modifier | station starvation/overload 處理是否能拉 TP/PO |
| A6 | A3 + delay cap + station modifier | 最終 stack |

關鍵對照：

* **A1 vs A0**：純動力學修正本身有沒有用（若 A1 已贏，整套 BAED 的邊際貢獻就被高估了）
* **A3 vs A2**：blocking-aware 是否比 density-residual 更貼近 RAWSim-O 物理（**這是論文最核心的對照**）
* **A5 vs A3**：station modifier 是不是 TP/PO 提升的真正來源
* **A6 vs A5**：delay cap 是否避免長跑 compounding 反轉

---

## 7. 成功 / 失敗判準（spec §十）

### 7.1 最低成功（throughput-preserving efficiency improvement）

1. Candidate-level：BAED 的 ranking 比 static distance 更接近 actual WCHA\* cost
2. System-level：OD ↓ 且 RD ↓
3. TP / PO 不退化

### 7.2 理想成功

TP ↑、PO ↑、OD ↓、RD ↓，且 decision time / cost matrix build time overhead 可接受（spec 沒給絕對閾值；建議 decision time ≤ 1.5× B0、build time ≤ 2× B0）。

### 7.3 失敗條件（任一觸發即視為方法失敗，回頭重新設計）

1. BAED ranking 不比 distance ranking 更接近 actual cost（candidate-level 沒贏）
2. OD/RD 下降但 TP/PO 顯著退化
3. TP/PO 上升但 OD/RD 顯著退化
4. 只有某一組超參（`η, γ, DelayCap, S_min, S_max, v_ref`）有效，敏感度過高
5. Decision time overhead 過大，失去 online decision 可行性（> 2× B0）

---

## 8. 最小可行實作（spec §十三第 7 項）

範圍鎖定在以下檔案，**不動 MILP 結構、不動 WCHA\*、不動 ReservationTable**：

| 檔案 | 動作 |
|---|---|
| `RAWSimO.Core/Metrics/BAEDEstimator.cs` | 新增；read-only access to `WHCAnMethod` 暴露的 reservation snapshot |
| `RAWSimO.Core/Control/Defaults/OrderBatching/M1GManager.cs:117, 134` | 改 `EstimateBotPodDistance` / `EstimatePodStationDistance` 兩點，根據 config flag 切換 baseline / BAED |
| `RAWSimO.Core/Configurations/MethodConfigurationsOB.cs` | 加 `UseBAED`、`UseDelayCap`、`UseStationModifier`、`DelayCap`、`Eta`、`Gamma` 等 flag |
| `RAWSimO.Core/Statistics/DecisionRankingLogger.cs` | 新增；shadow 計算 c_dist / c_old / c_baed / c_baed_full / c_real，輸出 `decision_ranking.csv` |
| `RAWSimO.MultiAgentPathFinding/Methods/WHCAnMethod.cs` | **只加 public read-only accessor**（snapshot 介面），絕不改 reservation 寫入路徑 |

刻意不在最小版做的：

* 舊 estimator（A2）的 C# 移植 → 留 ablation 跑時再做
* Regime B（RRA\* 投影）→ 第一版只做 Regime A + C
* qrps joint MILP / Poisson-binomial / NN oracle → spec §四已封存

---

## 9. 關鍵批判：此方法是否能合理改善 TP / PO / OD / RD？

> **Spec §十三 的必答題：「請優先回答：此方法是否能更合理地改善 TP / PO / OD / RD？若不能，請指出最可能失敗的原因與需要觀察的診斷指標。」**

### 9.1 為什麼「應該」會贏 baseline（B0）

* **OD / RD**：B0 的 static distance 完全忽略 reservation，會把當下擁塞區的 bot/pod 配給彼此，造成多餘繞行；BAED 的 EntryDelay 直接讀 deterministic 等待時間，**幾乎不可能比 B0 更差**——除非 entry delay 的方向與真實 OD/RD 反相關，而這在 node-exclusive 模型下沒有合理機制。
* **TP / PO**：靠 station modifier。BAED 自己只會降 OD/RD，可能讓某些 station 餓肚子（spec §五的隱憂）；`η` 對 starvation 的鼓勵與 `γ` 對 overload 的懲罰共同把 inbound pod 分散到使用率均衡的 station。

### 9.2 為什麼「可能會輸」 B1（舊 residual density estimator）

舊 estimator 已經用了部分相同資料（reservation footprint → density），若它已能間接捕捉到等待語意（即使 narrative 錯，數值上可能也是負相關於 entry delay），則 BAED 相對 B1 的邊際貢獻**可能很小**。**這是論文最大的風險**：narrative 改正了、KPI 沒改太多，paper 故事好說但實證不漂亮。

### 9.3 最可能的失敗模式與診斷指標

| 失敗模式 | 直接因 | 診斷指標 |
|---|---|---|
| **EntryDelay 訊號太弱** — 15s window 內大部分 node 都 free，`FirstFreeStart − t_arr` 多半為 0 | 系統其實沒這麼擠（small layout，10 bots） | `fraction of edges with EntryDelay > 0` < 5% → 訊號不足 |
| **Regime A 訊號集中在第一段、第二段全是 C** | `t_robot_arrive_pod + T_lift > t_now + 15s`，pod→station 路徑都進 Regime C 等於用純 distance | `fraction of path_ps edges in Regime C` 接近 100% → 第二段 BAED 等於 B0 |
| **Station modifier 把流量灌到較遠 station** | `η` 過大壓過 BAED 的 distance 部分 | `Σ xps[p,s] · physDistance` 比 B0 顯著上升 |
| **DelayCap 過小** → cap 把 BAED 變回 distance；**過大** → 單一極端 reservation 主導 | 預設 `DelayCap = LengthOfAWindow` 可能不對 | `fraction of EntryDelay = DelayCap` > 30%（過大）或 = 0（過小）|
| **長跑 compounding 反轉** | BAED 推大家避開暫時擁塞區，把擁塞推到別處；下一 epoch 換邊擠 | 28800s vs 7200s 的 KPI delta 變號 |
| **舊 B1 已偷學到等待訊號** | 真實 narrative 是 blocking，但 density residual 與 blocking 高度相關 | A3 vs A2 的 Spearman ρ 差距 < 0.05 → 邊際貢獻不顯著 |

### 9.4 我的整體判斷（不打太滿）

* **OD / RD 改善的機率高**（low risk）：因為 BAED 在物理語意上嚴格不會比 B0 更繞，最壞情況退化為 B0 + 一個極小常數。
* **TP / PO 改善的機率中等**（medium risk）：完全靠 station modifier 拉，而 modifier 是新加的維度，需要超參調試；若 layout 本身 station 不會餓（small 兩個 station 都很容易飽），這項貢獻會看不出來。
* **相對 B1 顯著贏的機率偏低**（main risk to thesis story）：narrative 對、邊際小。建議在 candidate-level validation 看 **A3 vs A2 的 Spearman delta** 是否 ≥ 0.10 作為早期 go/no-go 訊號；若 < 0.05，需要找更難的 layout（medium / large）或更高 fleet size 才能讓 blocking 比 density 更具區分力。

---

## 10. 開放問題（implementation 時必須回答）

| ID | 問題 | 影響章節 |
|---|---|---|
| Q1 | `v_ref` 取空載 max velocity 還是 average effective speed？ | §2.3 |
| Q2 | `T_lift` 是否含 unload 時間？M1G 兩段估算的 anchor 對齊點需明確 | §3 |
| Q3 | Regime B 是否值得做？若不做，第二段 BAED 訊號是否仍夠強 | §3.1, §9.3 |
| Q4 | `bayWidth` 在 small layout 是多少？station modifier 量級需校準 | §2.4 |
| Q5 | `_calculatedReservations` 在 epoch 間 snapshot 的成本（淺拷貝是否安全） | §3 |
| Q6 | candidate-level validation 是否需要 replay actual cost for non-selected candidates？第一版不做的代價 | §4.1 |
| Q7 | M1G internal `MipGap` / `TimeLimit` 是否需隨 cost magnitude 重調？BAED + modifier 後 objective 數值範圍會改變 | §5 |

---

*End of design note. 舊文件 `design_note.md` 保留為 A2 ablation 對照組與歷史紀錄。*
