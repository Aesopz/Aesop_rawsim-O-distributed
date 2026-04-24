# 已實作模組總覽

> 更新日期：2026-04-07
> 分支：aesop
> 論文：Energy-Aware Phase-Asymmetric Routing for Multi-AGV Coordination in Unidirectional RMFS

---

## 1. JunctionArbitration 模組

### 目的
去中心化 A* + 交叉口仲裁，作為 MAPPO 訓練的 baseline 與比較組。
保證 collision-free + deadlock-free，仲裁狀態曝露給 GymServer 供 Python 觀測。

### 核心設計原則
- A* 規劃層不變（純幾何，去中心化）
- 在 `RegisterNextWaypoint()` 加入四層閘道
- `Update()` 每 tick 以 live 動態資訊執行死鎖解除 + 重規劃

### 四層仲裁閘道（RegisterNextWaypoint）

| 層 | 名稱 | 說明 |
|---|---|---|
| 0 | Queue lock | base PathManager 既有佇列鎖 |
| 1 | Hard occupancy | live `CurrentWaypoint`/`NextWaypoint`/同 tick claim |
| 2 | Swap check | head-on 雙向衝突偵測 |
| 3 | K-step zone overlap | K = ceil(v_max²/(2·a_decel)/1.0m)，live 計算，無 stale cache；優先級比較 |
| 4 | Physical distance | waypointEnd 中心距任何其他 bot 實體位置/NextWaypoint < (botR+0.35+0.05)m 即拒絕 |

### 優先級（高→低）
1. Tier 2：載貨且目標為 OutputStation（delivery）
2. Tier 1：載貨但目標不是 OutputStation（returning pod）
3. Tier 0：空車
4. Tiebreaker 1：垂直方向移動 > 水平
5. Tiebreaker 2：bot ID 較小者優先

### 死鎖解除
- 5 秒 timeout（`DeadlockTimeoutSec`）觸發
- 比較 stuck bot vs blocker 的 DOF（free outgoing waypoints）
- DOF 多的 → 強制 reroute；相等則隨機
- Reroute 時將目標 wp 暫設 `IsObstacle=true` 迫使 A* 繞道

### Update() 每 tick 流程
1. Flush `_nextClaim`（same-tick claim 表）
2. 死鎖解除（掃描 `_blocked` 逾時 bot）
3. `RunQueueManagement` + `UpdateLocksAndObstacles`（繼承自 base）
4. 每 bot 個別 A* replan，注入 occupancy locks（所有其他 bot 的 CurrentWp/NextWp 設 IsLocked）

### 新增檔案

| 檔案 | 說明 |
|---|---|
| `RAWSimO.Core/Control/Defaults/PathPlanning/JunctionArbitration/JunctionArbitrationPathManager.cs` | 主類，繼承 AgentAStarPathManager |
| `RAWSimO.Core/Configurations/MethodConfigurationsPP.cs` | `JunctionArbitrationPathPlanningConfiguration` 設定類 |
| `Material/Instances/CoreBenchmark/aesop-junction.xconf` | 使用 JunctionArbitration 的 xconf |

> 注：`BotArbitrationState.cs`, `BotLookahead.cs`, `ArbitrationReservationTable.cs`, `WaitForGraph.cs`, `PriorityComparer.cs` 為舊設計殘留，目前不使用。

### 修改檔案

| 檔案 | 修改點 |
|---|---|
| `AgentAStarPathManager.cs` | `_planners` private→internal；抽出 `RunQueueManagement()` protected helper |
| `PathManager.cs` | `UpdateLocksAndObstacles()` private→internal |
| `BotAStarPlanner.cs` | `CanGoThroughObstacles = CanTunnel && bot.Pod == null` |
| `MethodConfiguration.cs` | `PathPlanningMethodType` 加 `JunctionArbitration` 枚舉值 |
| `Controller.cs` | switch 加 `case JunctionArbitration → new JunctionArbitrationPathManager(...)` |
| `InstanceCreation.cs` | bot 類型 switch 加 `JunctionArbitration` 走 BotNormal 分支 |
| `BotCrashHandler.cs` | `_isDecentralizedMode` 包含 `JunctionArbitration` |
| `GymSimulationHost.cs` | infos 加入 `arbitration_stopped`, `arbitration_stop_duration`, `arbitration_blocked_at_wp`, `wait_for_graph_cycle_count`, `total_arbitration_stops` |

### 設定欄位（aesop-junction.xconf）

```xml
<LookaheadK>3</LookaheadK>
<SafetyBufferMeters>2.0</SafetyBufferMeters>
<StarvationBoostPerSec>1.0</StarvationBoostPerSec>
<DeadlockTimeoutSec>5.0</DeadlockTimeoutSec>
<ForcedReplanBlacklistSec>8.0</ForcedReplanBlacklistSec>
<EnableWaitForGraphCycleCheck>true</EnableWaitForGraphCycleCheck>
<CanTunnel>true</CanTunnel>
```

---

## 2. Energy Consumption 模組（Rizqi Model）

### 目的
以 Rizqi 論文的物理能耗公式量化每台 AGV 的能耗，作為 MAPPO reward 的能耗分項。

### 實作位置
- `RAWSimO.Core/Bots/EnergyConsumption.cs` — E1–E7 計算函數
- `RAWSimO.Core/Bots/BotNormal.cs` — hooks（加速、減速、勻速、提舉 events）

### 能耗分類

| 項目 | 說明 | 備註 |
|---|---|---|
| E1 | 加速能耗 = m·v·Δv / (2·η) | η=0.15（Rizqi 論文） |
| E2 | 減速能耗 | 永遠為 0（被動煞車，d·η < g·μr） |
| E3 | 勻速行駛能耗 | f(速度, 距離, 載重) |
| E4 | 空載靜止維持 | 待機功耗 |
| E5 | 載貨靜止維持 | 待機功耗 + 貨重 |
| E6 | 提舉能耗 | 升降 pod 時 |
| E7 | 降放能耗 | 降下 pod 時 |

---

## 3. GymServer 擴充

### 目的
將模擬狀態透過 TCP 伺服器傳給 Python MAPPO 訓練端。

### 主要檔案
- `RAWSimO.GymServer/GymSimulationHost.cs`
- `RAWSimO.GymServer/GymPathManager.cs`（繼承 AgentAStarPathManager）

### infos 欄位（每 bot）

| 欄位 | 說明 |
|---|---|
| `action_mask` | bool[5]，哪些方向可移動 |
| `collision_count` | 當前同格 bot 數 |
| `arbitration_stopped` | 是否被仲裁層停住 |
| `arbitration_stop_duration` | 停住累計秒數 |
| `arbitration_blocked_at_wp` | 被擋的目標 waypoint ID |
| `energy_e1`–`energy_e7` | 各能耗分項 |

### infos `__global__`

| 欄位 | 說明 |
|---|---|
| `wait_for_graph_cycle_count` | 本 step 偵測到的循環死鎖次數（= LastCycleCount） |
| `total_arbitration_stops` | 累計仲裁停止次數 |

---

## 4. 地圖設定

| 檔案 | 說明 |
|---|---|
| `Material/Instances/CoreBenchmark/aesop.xlayo` | 地圖佈局（單向走廊 + highway） |
| `Material/Instances/CoreBenchmark/aesop.xsett` | 模擬參數 |
| `Material/Instances/CoreBenchmark/aesop.xconf` | 使用 AgentAStar 的 baseline 設定 |
| `Material/Instances/CoreBenchmark/aesop-junction.xconf` | 使用 JunctionArbitration 的實驗設定 |

---

## 5. 已知問題與風險

| 問題 | 現狀 |
|---|---|
| `MoveBotOverride`（Tier.cs:255）永遠移動，即使 IsValidMove=false | **根本穿透來源**；屬於物理引擎層，需修改 Tier.cs 才能完全解決 |
| QuadTree IsValidMove 用舊位置搜尋（QuadNode.cs:149 用 c.X 而非 x） | 可能漏偵測跨象限碰撞 |
| `setNextWaypoint` 在 `_updateMove` 之前執行，CurrentWaypoint 可能 stale | 仲裁閘道的 hard occupancy check 已用 live NextWaypoint 補充 |
| Graph mutate/restore 為單線程前提 | 若未來並行化需改設計 |

---

## 6. 驗收測試指令

```bash
# 編譯
dotnet build RAWSimO.sln -c Release

# Baseline（AgentAStar）
dotnet run --project RAWSimO.CLI -- \
  Material/Instances/CoreBenchmark/aesop.xlayo \
  Material/Instances/CoreBenchmark/aesop.xsett \
  Material/Instances/CoreBenchmark/aesop.xconf \
  output/baseline 42

# 實驗組（JunctionArbitration）
dotnet run --project RAWSimO.CLI -- \
  Material/Instances/CoreBenchmark/aesop.xlayo \
  Material/Instances/CoreBenchmark/aesop.xsett \
  Material/Instances/CoreBenchmark/aesop-junction.xconf \
  output/junction-smoke 42
```

**驗收標準**：
- `output/junction-smoke/.../performance.csv` collision count == 0
- 無 bot 永久卡死（throughput > 0）
- `controllog.txt` cycle break 事件 < 1 per minute
