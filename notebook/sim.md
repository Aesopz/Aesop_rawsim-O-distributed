# RAWSim-O 路由與交通控制模擬架構文件

## 目錄

1. [概觀：三層架構](#1-概觀三層架構)
2. [Waypoint 概念與地圖結構](#2-waypoint-概念與地圖結構)
3. [路徑規劃層：AgentAStar](#3-路徑規劃層agendastar)
4. [交通控制層：TrafficArbiter](#4-交通控制層trafficarbiter)
5. [物理移動層：BotNormal._updateMoveTC](#5-物理移動層botnormal_updatemovetc)
6. [Waypoint 佔用鎖（歷史遺留）](#6-waypoint-佔用鎖歷史遺留)
7. [每 Tick 完整執行流程](#7-每-tick-完整執行流程)
8. [已知問題與設計限制](#8-已知問題與設計限制)
9. [相關檔案索引](#9-相關檔案索引)
10. [如何運行模擬與視覺化](#10-如何運行模擬與視覺化)

---

## 1. 概觀：三層架構

```
┌─────────────────────────────────────────────────────────────┐
│  Layer 1：路徑規劃（AgentAStar）                              │
│  每 bot 獨立跑 A*，算出一條 waypoint 序列（Path）               │
│  頻率：每 ReplanInterval 秒 或 RequestReoptimization 時        │
│  輸出：bot.Path = [WP_a, WP_b, WP_c, ...]                   │
├─────────────────────────────────────────────────────────────┤
│  Layer 2：交通控制（TrafficArbiter）                           │
│  每 tick 對每個 bot 計算 speedCap（速度上限）                    │
│  依據：附近 bot 的位置、速度、到達時間估計、Phase 優先級           │
│  輸出：speedCap ∈ [0, MaxSpeed]                              │
├─────────────────────────────────────────────────────────────┤
│  Layer 3：物理移動（BotNormal._updateMoveTC）                  │
│  每 tick 根據 speedCap 做物理積分（加速/等速/減速）               │
│  碰撞最終防線：Tier.MoveBot() 用 QuadTree 檢查 bot 圓形包圍盒   │
│  輸出：bot 的 (X, Y) 座標更新                                  │
└─────────────────────────────────────────────────────────────┘
```

**設計原則**：Layer 1 管「走哪條路」，Layer 2 管「走多快」，Layer 3 管「物理上能不能走」。三層解耦。

---

## 2. Waypoint 概念與地圖結構

### 2.1 什麼是 Waypoint

Waypoint 是地圖上的**節點**，所有 bot 移動都是沿著 waypoint 之間的有向邊（edge）進行的。bot 在任何時刻都處於某條邊上（從 `CurrentWaypoint` 到 `NextWaypoint`）或停在某個 waypoint 上。

**核心屬性**（`RAWSimO.Core/Waypoints/Waypoint.cs`）：
- `X, Y`：座標（公尺）
- `Paths`：該 waypoint 連向的所有鄰居 waypoint（有向邊）
- `Pod`：此位置是否有 pod（Pod 儲存格）
- `OutputStation / InputStation`：此位置是否是揀貨站/補貨站
- `Tier`：所在樓層

### 2.2 HighwayHallway 佈局

我們的地圖（`aesop.xlayo`）使用 `HighwayHallway` 佈局：

```
                    ← ← ← ← ← ← ←   (北 highway，西行)
                    ↓               ↑
  [Input]  ← ← ← ← ← ← ← ← ← → → → → → → → → [Output]
  [Station]         ↓  Pod  Pod  ↑                  [Station]
                    ↓  Pod  Pod  ↑
                    ↓  Pod  Pod  ↑
                    ↓  Pod  Pod  ↑
                    ↓  (走道)     ↑
                    ↓  Pod  Pod  ↑
                    ↓  Pod  Pod  ↑
  [Input]  ← ← ← ← ← ← ← ← ← → → → → → → → → [Output]
  [Station]         ↓               ↑              [Station]
                    → → → → → → →   (南 highway，東行)
```

- **Highway**（外圍環形道路）：單向、反時針（`CounterClockwiseRingwayDirection=true`）
- **Aisle**（走道，水平）：連接 pod 儲存格，`SingleLane=true`（單向）
- **Vertical aisle**（垂直走道）：連接 highway 與 aisle
- **Pod 儲存格**：waypoint 之間有鏈狀邊（pod-to-pod），用於 bot 穿越 pod 區域

### 2.3 有向圖（Graph）

`PathManager.GenerateGraph()` 從 waypoint 網路建立 `Graph` 物件：
- 每個 `Waypoint` 對映一個 `int nodeId`（透過 `BiDictionary<Waypoint, int> _waypointIds`）
- `Graph.Edges[nodeId]` = 該節點的所有出邊（有向）
- `Graph.NodeInfo[nodeId].IsObstacle`：是否為障礙物（動態設定）

**重要**：圖的邊是**有向的**。在 `SingleLane=true` 的佈局中，bot 只能沿箭頭方向走。但 pod 儲存格的鏈狀邊可能允許雙向（取決於 InstanceGenerator），這是 head-on 碰撞的來源之一。

### 2.4 Bot 的位置狀態

```
                  CurrentWaypoint                NextWaypoint
                        ●───────────────────────────●
                        ↑                           ↑
                   bot 出發點                     bot 目的地
                        |← _distanceOnSegment →|
                        |←───── _segmentLength ─────→|
```

| 欄位 | 型別 | 說明 |
|------|------|------|
| `CurrentWaypoint` | Waypoint | bot 最後完成 snap 的 waypoint |
| `NextWaypoint` | Waypoint | bot 當前正在前往的 waypoint（null = 停在原地） |
| `_distanceOnSegment` | double | 已走的距離（公尺） |
| `_segmentLength` | double | 這條邊的總長度（公尺） |
| `_currentSpeed` | double | 當前瞬時速度（m/s） |
| `DestinationWaypoint` | Waypoint | 最終目的地（任務指定，不是 NextWaypoint） |

---

## 3. 路徑規劃層：AgentAStar

### 3.1 架構

```
AgentAStarPathManager : PathManager
├── _planners: Dictionary<BotNormal, BotAStarPlanner>   （每 bot 一個）
├── _graph: Graph                                       （共用導航圖）
├── _waypointIds: BiDictionary<Waypoint, int>            （共用映射）
│
├── Update(lastTime, currentTime)
│   ├── _lastCallTimeStamp = MaxValue/2   ← 阻止 base 呼叫 _reoptimize()
│   ├── base.Update()                     ← 只跑 queue management
│   └── foreach planner → TryReplan()     ← 分散式規劃
│
└── RegisterNextWaypoint(...)             ← override: 只檢查 queue lock
```

**檔案**：`RAWSimO.Core/Control/Defaults/PathPlanning/AgentAStar/AgentAStarPathManager.cs`

### 3.2 BotAStarPlanner.TryReplan()

**檔案**：`RAWSimO.Core/Control/Defaults/PathPlanning/AgentAStar/BotAStarPlanner.cs`

每個 bot 有一個 `BotAStarPlanner` 實例。`TryReplan()` 在以下情況被呼叫：

1. `ReplanInterval` 時間到
2. `RequestReoptimization = true`（如 head-on reroute 觸發）
3. `Path == null`（尚未規劃）

#### 執行步驟

```
TryReplan(currentTime)
│
├── 1. 判斷是否需要規劃（interval / reopt / 無路徑）
├── 2. 跳過 queue 中的 bot
├── 3. 解析目的地（ResolveQueueDestination）
├── 4. 決定起點：NextWaypoint ?? CurrentWaypoint
├── 5. 標記 pod 格為障礙物：
│       foreach podWp in GetPodPositions()
│           graph.NodeInfo[nodeId].IsObstacle = true
│       agent.CanGoThroughObstacles = CanTunnel && bot.Pod == null
│       ↑ 載貨 bot 繞開 pod 格；空車穿越
├── 6. 建立 SpaceAStar 實例（每次新建）
├── 7. aStar.Search()（一次性阻塞搜尋）
├── 8. 還原 IsObstacle 標誌
└── 9. 若找到路徑 → bot.Path = 新路徑
```

#### Pod 障礙物機制（與 FAR 等相同）

這是模仿原始 `PathManager.UpdateLocksAndObstacles()` 的機制：
- `WaypointGraph.GetPodPositions()` 回傳所有目前有 pod 的 waypoint
- 這些 waypoint 在 A* 搜尋期間被標為 `IsObstacle = true`
- `agent.CanGoThroughObstacles`：
  - `true`（空車 + CanTunnel=true）：可穿越 pod 格
  - `false`（載貨車）：必須繞開 pod 格，走 highway
- **目的地節點永遠不標障礙**（bot 需要到達該 pod 進行取/放貨）
- 搜尋結束後**立即還原**所有 IsObstacle 標誌（因為 NodeInfo 是共用的）

#### Stagger 機制

建構時每個 bot 被分配一個初始延遲（`stagger`），均勻分佈在一個 `ReplanInterval` 內。防止所有 bot 在同一個 tick 同時跑 A*（造成 CPU 峰值）。

### 3.3 RequestReoptimization 旗標

| 設定者 | 情境 |
|--------|------|
| `BotMove.Act()` | `Path == null` 或 `Path.Count == 0` 時 |
| `TrafficArbiter` | Head-on 空車衝突時，較低優先權的 bot 被觸發 reroute |
| `setNextWaypoint()` 失敗 | `RequestReoptimizationAfterFailingOfNextWaypointReservation` 為 true 時 |

TryReplan() 會在每次嘗試規劃時**重置此旗標**（line 63）。

---

## 4. 交通控制層：TrafficArbiter

**檔案**：`RAWSimO.Core/Control/TrafficArbiter.cs`

### 4.1 核心 API

```csharp
public double ComputeSpeedCap(BotNormal bot, double currentTime)
```

每個 tick、每個 bot 呼叫一次（從 `_updateMoveTC`）。回傳值：

| 回傳值 | 意義 |
|--------|------|
| `MaxSpeed` (1.5) | 無衝突，全速前進 |
| `0 < cap < MaxSpeed` | 有衝突，限速以延後到達時間 |
| `0` | 完全停止 |

### 4.2 感應半徑

```csharp
double selfBrakeDist = bot.Physics.getDistanceToStop(bot._currentSpeed);
double detectionR = TrafficDetectionRadius + selfBrakeDist;
```

- `TrafficDetectionRadius`：設定值 = 4.5m（固定基礎半徑）
- `selfBrakeDist`：v²/(2a)，當前速度下的煞車距離
- **動態擴展**：速度越高，感應半徑越大。目的是讓第一個衝突解決後、重新加速時，能看到第二個衝突

### 4.3 三種衝突類型判定

#### Type 1：Head-on（對向同邊）

```
偵測條件：nb.CurrentWaypoint == bot.NextWaypoint
        && nb.NextWaypoint == bot.CurrentWaypoint
```

```
bot: u ──→ v       nb: v ──→ u
         ×（對撞）
```

**處理邏輯**：

1. FCFS 檢查：若兩方到達時間差距夠大（> `TrafficWaypointTraverseTime`），各走各的
2. 若到達時間重疊（Overlap zone）：
   - **兩台都是空車**（`bot.Pod == null && nb.Pod == null`）：
     - 比較各自到 `DestinationWaypoint` 的直線距離
     - 距離較遠者 yield + 觸發 `RequestReoptimization = true`（reroute）
   - **其他情況**：使用 `HasLowerPriority()` 判斷
3. yield 方：speedCap 降低或歸零

#### Type 2：Shared waypoint（同目標）

```
偵測條件：nb.NextWaypoint == bot.NextWaypoint
```

```
bot: a ──→ c ←── b :nb
       （匯聚同一 waypoint）
```

**處理邏輯**：

1. FCFS：若 `myArrival + margin < nbArrival` → 我先到，不煞車
2. 若重疊 → `HasLowerPriority()` 判斷 → yield 方透過 `BinarySearchSpeedCap` 計算需降多少速

#### Type 3：Occupied waypoint（鄰居佔據我的目標）

```
偵測條件：nb.CurrentWaypoint == bot.NextWaypoint
        && nb.NextWaypoint == null（停在那裡）
```

```
bot: a ──→ c     nb: [停在 c]
```

**處理邏輯**：

- 若 `nb.NextWaypoint != null`（正在離開）→ 視為「driving away」，不衝突
- 若 `nb.NextWaypoint == null`（停著不動）：
  - 若 bot 距離 < 0.15m → `speedCap = 0`（太近，必須停）
  - 若距離 > 煞車距離 + 0.3m → 繼續走（還有空間煞車）
  - 否則 → `speedCap = 0`

### 4.4 優先權規則：HasLowerPriority()

```
1. Phase 優先（數值越高越優先）：
   B_Deliver (3) > C_Return (2) > A_Fetch (1) > Idle (0)

2. 垂直運動優先（N/S 方向）> 水平運動（E/W 方向）
   判斷：|sin(Orientation)| > 0.7 視為垂直

3. Tie-break：ID 較小者優先
```

### 4.5 BinarySearchSpeedCap（動量感知版）

```csharp
private double BinarySearchSpeedCap(BotNormal bot, double distance, double targetTime)
```

二分搜尋一個 `speedCap`，使得 bot 恰好在 `targetTime` 秒後走完 `distance` 公尺。

**動量感知**：使用 `v0 = bot._currentSpeed`（當前實際速度），而非 v0=0。這透過 `TravelTimeFromCurrentSpeed()` 處理 `v0 > speedCap` 的情況（先減速再等速），繞過 `Physics.getTimeNeededToMove` 在 v0 > MaxSpeed 時的符號錯誤。

#### TravelTimeFromCurrentSpeed 的三種情境

```
情境 1：v0 ≤ speedCap
  → 正常加速至 speedCap，等速，減速停止
  → 委託 Physics.ComputeSegmentDuration(v0, distance, speedCap)

情境 2：v0 > speedCap 且煞車距離 < distance
  → Phase 1：減速 v0 → speedCap（kinematics: t=(v0-cap)/a）
  → Phase 2：以 speedCap 走完剩餘距離
  → 總時間 = Phase 1 + Phase 2

情境 3：v0 > speedCap 且煞車距離 ≥ distance
  → 整段都在減速，用二次方程解 t
  → d = v0·t - 0.5·a·t²  →  t = (v0 - √(v0²-2ad)) / a
```

### 4.6 SpeedCap 恢復速率限制（Option 3）

在 `BotNormal._updateMoveTC` 中，speedCap 被額外限制：

```csharp
double maxRecovery = Physics.Acceleration * deltaT;
speedCap = Math.Min(speedCap, _prevSpeedCap + maxRecovery);
_prevSpeedCap = speedCap;
```

- speedCap 每個 tick 最多增加 `a_max × Δt`
- 衝突解除後不會瞬間跳回 MaxSpeed，而是**漸進恢復**
- 恢復期間，若出現第二個衝突，bot 的速度仍低，有足夠距離減速

---

## 5. 物理移動層：BotNormal._updateMoveTC

**檔案**：`RAWSimO.Core/Bots/BotNormal.cs`（line ~935）

### 5.1 兩種移動模式的切換

```csharp
if (NextWaypoint != null)
{
    if (Instance.SettingConfig.TrafficControlEnabled)
        _updateMoveTC(lastTime, currentTime);   // TC-ON：per-tick 積分
    else
        _updateMove(currentTime);                // TC-OFF：原始百分比插值
}
```

| | `_updateMove`（TC-OFF） | `_updateMoveTC`（TC-ON） |
|---|---|---|
| 速度控制 | 無（全速走完一條邊） | TrafficArbiter speedCap |
| 位置計算 | 百分比插值 `travelPercentage` | 距離積分 `_distanceOnSegment` |
| 碰撞處理 | `MoveBotOverride`（強制移動） | `MoveBot`（嚴格檢查，失敗則停止） |
| 能耗計算 | 整條邊一次算 | 每 tick 增量累積 |

### 5.2 _updateMoveTC 完整流程

```
_updateMoveTC(lastTime, currentTime)
│
├── 1. 計算有效 deltaT（扣除等待和旋轉時間）
│       driveStart = _waitUntil + _rotateDuration
│       deltaT = currentTime - max(lastTime, driveStart)
│       if deltaT <= 0 → return（還在等/轉）
│
├── 2. 呼叫 TrafficArbiter.ComputeSpeedCap()
│       → 回傳 speedCap
│
├── 3. Option 3 恢復速率限制
│       speedCap = min(speedCap, _prevSpeedCap + a_max × deltaT)
│       _prevSpeedCap = speedCap
│
├── 4. Snap zone 判定（remaining < 0.05m）
│   │
│   ├── if speedCap <= 0 → 停止，return（不嘗試 snap）
│   │
│   └── 嘗試 Tier.MoveBot(NextWaypoint.X, Y)
│       ├── 成功 → ReleaseWaypoint(CurrentWP)
│       │         CurrentWP = NextWP, NextWP = null
│       │         _distanceOnSegment = 0
│       └── 失敗 → 碰撞偵測，停止，下 tick 重試
│
├── 5. speedCap == 0 → 減速至停止
│       newSpeed = max(0, _currentSpeed - decel × dt)
│       distTraveled = v·dt - 0.5·a·dt²
│
├── 6. speedCap > 0 → Physics.IntegrateStep()
│       IntegrateStep(_currentSpeed, deltaT, remaining, speedCap,
│                     out distTraveled, out newSpeed)
│
├── 7. 更新 _currentSpeed, _distanceOnSegment
│       位置插值：frac = _distanceOnSegment / _segmentLength
│       xNew = CurrentWP.X × (1-frac) + NextWP.X × frac
│
├── 8. 能耗累計：_accumulateTickEnergy(vBefore, vAfter, dist)
│
├── 9. Full arrival 判定（_distanceOnSegment ≥ _segmentLength - 0.01m）
│   │
│   └── 嘗試 Tier.MoveBot(NextWaypoint.X, Y)
│       ├── 成功 → snap（同 Step 4）
│       └── 失敗 → 碰撞偵測，保留進度，下 tick 重試
│
└── 10. 中間移動：Tier.MoveBot(xNew, yNew)
        ├── 成功 → 位置更新
        └── 失敗 → 碰撞偵測，回退 _distanceOnSegment，停止
```

### 5.3 Snap 機制

bot 的位置是連續的（任意 X,Y），但邏輯位置是離散的（CurrentWaypoint / NextWaypoint）。**Snap** 是從連續位置跳到精確 waypoint 座標的動作：

```
Snap 前：bot 在 (X, Y) ≈ NextWaypoint 附近
         CurrentWaypoint = WP_old
         NextWaypoint = WP_new

Snap 後：bot 在 (NextWaypoint.X, NextWaypoint.Y) 精確位置
         CurrentWaypoint = WP_new
         NextWaypoint = null
```

兩個 snap 門檻：
- **Snap zone**（line ~963）：`remaining < 0.05m`（tick 開始時已經很近）
- **Full arrival**（line ~1046）：`_distanceOnSegment ≥ _segmentLength - 0.01m`（走到了）

### 5.4 物理碰撞檢查：Tier.MoveBot()

**檔案**：`RAWSimO.Core/Elements/Tier.cs`（line 212）

```csharp
public bool MoveBot(Bot bot, double x, double y)
{
    // 1. 邊界檢查：bot 不能超出地圖
    if (x - bot.Radius < 0 || ...) return false;

    // 2. QuadTree 碰撞檢查：是否與其他 bot 重疊
    if (BotQuadTree.IsValidMove(bot, x, y))
    {
        // 3. 若載貨，還要檢查 pod 是否與其他 pod 重疊
        if (bot.Pod == null) { move; return true; }
        if (PodQuadTree.IsValidMove(bot.Pod, x, y)) { move both; return true; }
    }
    return false;  // 移動被拒絕
}
```

- `BotQuadTree.IsValidMove`：檢查 bot 的圓形包圍盒（radius=0.35m）是否與其他 bot 重疊
- `PodQuadTree.IsValidMove`：載貨時額外檢查 pod 的圓形包圍盒（radius=0.45m）
- **這是碰撞防止的最後一道防線**

#### MoveBot vs MoveBotOverride

| | `MoveBot` | `MoveBotOverride` |
|---|---|---|
| 碰撞時行為 | **拒絕移動**（return false） | **強制移動**（移動但回報碰撞） |
| 使用者 | `_updateMoveTC`（TC-ON） | `_updateMove`（TC-OFF） |
| 碰撞統計 | 不計入（因為沒移動） | 計入碰撞計數 |

---

## 6. Waypoint 佔用鎖（歷史遺留）

**檔案**：`RAWSimO.Core/Control/Controller.cs`（line 187-211）

### 6.1 API

```csharp
// Controller.cs 中的字典和方法
Dictionary<Waypoint, BotNormal> _waypointOccupancy;

bool ClaimWaypoint(Waypoint wp, BotNormal bot)
// → 若 wp 無人佔用：佔用並回傳 true
// → 若已被 bot 自己佔用：回傳 true
// → 若被其他 bot 佔用：回傳 false

void ReleaseWaypoint(Waypoint wp, BotNormal bot)
// → 若 wp 被 bot 佔用：移除佔用
// → 否則：no-op
```

### 6.2 目前使用狀態

**目前 ClaimWaypoint 不在任何關鍵路徑上被使用**。經過多次疊代後：

- ~~初始化時 Claim~~：已移除（造成 ring deadlock）
- ~~RegisterNextWaypoint 時 Claim~~：已移除（造成 registration deadlock）
- ~~Snap zone Claim~~：已移除（造成 snap zone deadlock）

ReleaseWaypoint 仍在以下位置被呼叫（作為防禦性清理）：
- `_updateMoveTC` snap 成功時（line ~983）
- `_updateMove` 到達時（line ~911）
- `_arriveAtNextWaypointSnap()`（line ~1138）

這些呼叫目前是 no-op（字典中沒有對應的 entry 因為沒有 Claim），但保留以備未來啟用。

### 6.3 為何佔用鎖被棄用

| 方案 | 結果 | 原因 |
|------|------|------|
| 初始化 Claim | Ring deadlock | N 台 bot 在環形道路上互等，無人能出發 |
| Registration Claim | Circular intent deadlock | A 等 B 的 WP，B 等 C 的 WP，C 等 A |
| Departure Release | 過早釋放 | TrafficArbiter 誤判「driving away」放行，bot 尚未物理離開 |
| Snap-time Claim | Snap zone deadlock | 兩台 bot 各持 CurrentWP 的 claim，都進不去對方的 WP |

**結論**：在分散式 A*（無全域 reservation table）架構中，waypoint 佔用鎖無法避免循環鎖死。碰撞防止改由 TrafficArbiter（速度控制）+ MoveBot（物理檢查）負責。

---

## 7. 每 Tick 完整執行流程

```
Controller.Update(lastTime, currentTime)
│
├── PathManager.Update(lastTime, currentTime)
│   │
│   ├── [AgentAStarPathManager]
│   │   ├── _lastCallTimeStamp = MaxValue/2  ← 阻止 _reoptimize()
│   │   ├── base.Update()                    ← queue management only
│   │   └── foreach planner → TryReplan(currentTime)
│   │       └── 若需要 → 跑 A*，更新 bot.Path
│   │
│   └── [base PathManager.Update]
│       ├── queue management（QueueManager.Update）
│       └── reservation table maintenance（我們跳過）
│
├── BotManager.Update()
│   └── 分配任務給閒置 bot
│
├── foreach Bot → bot.Update(lastTime, currentTime)
│   │
│   ├── StateQueue 狀態機驅動
│   │   └── BotMove.Act(bot, lastTime, currentTime)
│   │       │
│   │       ├── bot 已在目的地？→ 完成任務，dequeue
│   │       ├── bot.NextWaypoint != null？→ return（正在移動中）
│   │       ├── bot.Path 為空？→ RequestReoptimization = true
│   │       └── 從 Path 取下一個 waypoint → setNextWaypoint()
│   │           ├── 成功 → NextWaypoint = wp, 初始化 segment
│   │           └── 失敗 → _waitUntil = currentTime + 0.05
│   │
│   └── _updateState(lastTime, currentTime)
│       ├── 等待中？→ return
│       ├── 旋轉中？→ _updateRotation()
│       └── 移動中（NextWaypoint != null）？
│           ├── TC-ON → _updateMoveTC(lastTime, currentTime)
│           │   ├── speedCap = ComputeSpeedCap()
│           │   ├── speedCap = min(speedCap, _prevSpeedCap + recovery)
│           │   ├── Physics.IntegrateStep() or 減速
│           │   └── MoveBot() 更新位置
│           └── TC-OFF → _updateMove(currentTime)
│               └── 百分比插值 + MoveBotOverride()
│
├── 碰撞統計更新
└── 能耗統計更新
```

### 7.1 setNextWaypoint() 的詳細流程

```
setNextWaypoint(waypoint, currentTime)
│
├── 前提：bot.GetSpeed() == 0（必須靜止）
├── 計算旋轉時間 rotateDuration
├── 計算等待時間 waitUntil
│
├── PathManager.RegisterNextWaypoint(...)
│   │
│   └── [AgentAStarPathManager override]
│       ├── 檢查 IsQueueWaypointLocked → false 則拒絕
│       └── return true（不做 Claim）
│
├── 成功：
│   ├── NextWaypoint = waypoint
│   ├── _currentSpeed = 0
│   ├── _distanceOnSegment = 0
│   ├── _segmentLength = CurrentWP.GetDistance(NextWP)
│   └── 能耗計算（E4 旋轉 / E1-E3 depending on TC mode）
│
└── 失敗：
    ├── NextWaypoint = null
    ├── RequestReoptimization = true
    └── ConflictStopPending = true（E7 標記）
```

---

## 8. 已知問題與設計限制

### 8.1 Head-on 碰撞（空車在雙向邊上）

**根因**：Pod 儲存格的鏈狀邊可能允許雙向通行（InstanceGenerator 的行為），兩台空車可能從兩端進入同一條走道。

**目前處理**：TrafficArbiter 偵測 head-on → 距離較遠者 yield + 觸發 reroute。但 reroute 可能找到**同一條路徑**（無替代路線時），導致反覆觸發。

### 8.2 連續衝突反應延遲

**根因**：bot 解決第一個衝突後恢復速度，在高速狀態下遇到第二個衝突時煞車距離不足。

**目前處理**：
1. 感應半徑動態擴展（+ 煞車距離）
2. BinarySearchSpeedCap 使用 v0=currentSpeed（動量感知）
3. _prevSpeedCap 恢復速率限制（a_max × Δt/tick）

### 8.3 MoveBot 失敗後的恢復

若 `MoveBot` 在 snap zone 或 full arrival 返回 false：
- bot 停在原地（_currentSpeed = 0）
- NextWaypoint 保留（不釋放）
- 下一個 tick 再嘗試

**風險**：若對方也卡在 snap zone，雙方可能永遠互等。目前依賴 TrafficArbiter 的 speedCap=0 來**預防**兩台 bot 同時進入 snap zone，而非事後修復。

### 8.4 TC-OFF 模式

TC-OFF 使用 `_updateMove`（原始邏輯）+ `MoveBotOverride`（強制移動）。碰撞被計入統計但不防止。此模式下碰撞數量顯著較高（~893 vs TC-ON ~18）。

---

## 9. 相關檔案索引

| 檔案 | 層 | 說明 |
|------|---|------|
| `RAWSimO.Core/Bots/BotNormal.cs` | L3 | bot 移動邏輯：`_updateMoveTC`, `_updateMove`, `setNextWaypoint`, `_accumulateTickEnergy`, `_prevSpeedCap` |
| `RAWSimO.Core/Control/TrafficArbiter.cs` | L2 | 交通仲裁：`ComputeSpeedCap`, `BinarySearchSpeedCap`, `TravelTimeFromCurrentSpeed`, head-on reroute |
| `RAWSimO.Core/Control/Defaults/PathPlanning/AgentAStar/AgentAStarPathManager.cs` | L1 | 分散式 A* 管理器：`Update`, `RegisterNextWaypoint` |
| `RAWSimO.Core/Control/Defaults/PathPlanning/AgentAStar/BotAStarPlanner.cs` | L1 | per-bot A* 規劃：`TryReplan`, pod obstacle, CanTunnel |
| `RAWSimO.Core/Control/Controller.cs` | infra | Controller 初始化, `ClaimWaypoint`/`ReleaseWaypoint`, `TrafficArbiter` property |
| `RAWSimO.Core/Control/PathManager.cs` | L1 base | 基底類別：`RegisterNextWaypoint` (virtual), `GenerateGraph`, queue management |
| `RAWSimO.Core/Elements/Tier.cs` | L3 | 碰撞檢查：`MoveBot`, `MoveBotOverride`, `GetBotsWithinDistance` (QuadTree) |
| `RAWSimO.Core/Waypoints/Waypoint.cs` | data | Waypoint 結構：座標, 鄰居, Pod, Station |
| `RAWSimO.MultiAgentPathFinding/Physics/Physics.cs` | physics | 物理引擎：`IntegrateStep`, `getTimeNeededToMove`, `ComputeSegmentDuration`, `getDistanceToStop` |
| `RAWSimO.Core/Configurations/SettingConfiguration.cs` | config | TC 開關 (`TrafficControlEnabled`), 感應半徑 (`TrafficDetectionRadius=4.5`), 安全邊際 (`TrafficWaypointTraverseTime=0.2`) |

### 設定檔

| 檔案 | 用途 |
|------|------|
| `Material/Instances/CoreBenchmark/aesop-test100.xlayo` | 測試地圖（45 bots） |
| `Material/Instances/CoreBenchmark/aesop-tc-on.xsett` | TC 開啟設定 |
| `Material/Instances/CoreBenchmark/aesop-tc-off.xsett` | TC 關閉設定 |
| `Material/Instances/CoreBenchmark/aesop-astar.xconf` | AgentAStar 路徑規劃 |

---

## 10. 如何運行模擬與視覺化

### 10.1 編譯

```bash
dotnet build RAWSimO.sln -c Release
```

### 10.2 CLI 模式（無頭，跑統計）

```bash
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \
  Material/Instances/CoreBenchmark/aesop-test100.xlayo \
  Material/Instances/CoreBenchmark/aesop-tc-on.xsett \
  Material/Instances/CoreBenchmark/aesop-astar.xconf \
  output/test_run/ 42
```

輸出目錄結構：
```
output/test_run/aesop-tc_on-aesop-42/
├── statistics.txt      ← collisions, orders completed, energy
├── performance.csv     ← 吞吐量時序
├── footprint.csv       ← bot 軌跡
├── pathfinding.csv     ← A* 統計
└── output.log          ← 完整日誌
```

### 10.3 視覺化模式（WPF GUI, Windows only）

```bash
dotnet run --project RAWSimO.Visualization/RAWSimO.Visualization.csproj
```

1. File → Open Layout → 選 `.xlayo`
2. File → Open Setting → 選 `.xsett`
3. File → Open Config → 選 `.xconf`
4. 按 **Generate**
5. 按 **▶ Start**，調整 Speed Multiplier 觀察

### 10.4 觀察重點

| 現象 | 可能原因 |
|------|---------|
| 兩台 bot 重疊穿透 | TC-OFF 模式，或 TrafficArbiter 未偵測到衝突 |
| bot 長時間停止不動 | speedCap = 0 持續（前方佔據），或 Path 為 null |
| 載貨 bot 走 pod 區域 | A* 未標記 pod 障礙（`IsObstacle` 機制故障） |
| bot 在走道口來回抖動 | head-on reroute 反覆觸發（A* 找到同一條路） |
| bot 群集塞車 | 多台 bot 同時前往同一 station，自然排隊（非 bug） |
