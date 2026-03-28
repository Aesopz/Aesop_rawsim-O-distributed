# Energy Consumption Module — 計算流程技術文件

**版本**：2026-03-29
**基礎論文**：Rizqi et al. AGV energy model
**供後續人工檢驗使用**

---

## 1. 架構概覽

```
xinst / xlayo 檔案
    └── <EnergyParameters> 區塊
            │  (由 DTOInstance.Submit() 讀取)
            ▼
    EnergyConsumption.Configure()   ← 全域靜態參數初始化（一次）
            │
            ▼
    BotNormal 狀態機（每個 tick / 事件）
        ├── setNextWaypoint()  → E1 + E2 + E3 + E4
        ├── BotPickupPod       → E5a
        ├── BotSetdownPod      → E5b
        └── DequeueState()     → E6（BotGetItems / BotPutItems 退出時）
            │
            ▼
    BotNormal.Stat* 欄位（per-bot 累計）
            │
            ▼
    InstanceStatistics.StatOverall* 屬性（全隊加總）
            │
            ▼
    controllog.txt 輸出（StatEnergy*KJ 等指標）
```

---

## 2. 物理參數來源與預設值

| 參數 | 靜態欄位 | 預設值 | xinst 欄位 |
|---|---|---|---|
| AGV 空車質量 | `ROBOT_MASS` | 300 kg | `<RobotMass>` |
| AGV 車體寬度 | `ROBOT_WIDTH` | 0.6 m | `<RobotWidth>` |
| AGV 車體長度 | `ROBOT_LENGTH` | 0.75 m | `<RobotLength>` |
| 重力加速度 | `GRAVITY` | 9.8 m/s² | 硬編碼，不可覆蓋 |
| 滾動摩擦係數 | `FRICTION` | 0.02 | `<RollingFriction>` |
| 傳動慣量等效係數 | `INERTIA` | 0.15 | `<InertiaCoeff>` |
| 轉彎半徑 | `ROBOT_RADIUS` | width/2 = 0.3 m | 由 width 自動計算 |
| Pod 升降高度 | `LIFT_HEIGHT` | 0.2 m | `<LiftHeight>` |
| Pod 架體質量 | `POD_FRAME_MASS` | 40 kg | `<PodFrameMass>` |
| 停站持續功率 | `STATION_HOLD_POWER_W` | 15 W | `<StationHoldPowerW>` |

**載入模塊**：`RAWSimO.Core/IO/DTOInstance.cs` → `DTOInstance.Submit()`
**參數儲存模塊**：`RAWSimO.Core/Metrics/EnergyConsumption.cs` → `Configure()` 靜態方法

Bot 特有動力學參數（加速度、最大速度、轉速、PodTransferTime）**不在此處設定**，
於每次計算時直接從 `bot.Physics.*` 和 `bot.TurnSpeed`、`bot.PodTransferTime` 讀取（来自 xinst Bot 定義）。

---

## 3. 質量輔助函數

### 3.1 `GetTotalMass(pod)` — 驅動能耗用（E1/E2/E3）

```
mTotal = ROBOT_MASS + POD_FRAME_MASS + pod.CapacityInUse   [若有載 pod]
       = ROBOT_MASS                                          [若空車]
```

- `pod.CapacityInUse`：pod 上實際貨物重量 [kg]
- **模塊**：`EnergyConsumption.GetTotalMass()` → 呼叫於 `BotNormal.setNextWaypoint()`

### 3.2 `GetLoadMass(pod)` — 升降/停站能耗用（E5/E6）

```
mLoad = POD_FRAME_MASS + pod.CapacityInUse   [若有載 pod]
      = 0                                     [若空車]
```

- **模塊**：`EnergyConsumption.GetLoadMass()` → 呼叫於 `BotPickupPod`、`BotSetdownPod`

---

## 4. 各能耗分項計算

### E1 — 加速段能耗

**觸發時機**：每次 `setNextWaypoint()` 成功取得下一個 waypoint
**觸發模塊**：`BotNormal.cs:542`（`setNextWaypoint` 內部）

**速度剖面分解**（三段式或兩段式）：

```
dAccel = vMax² / (2a)
dDecel = vMax² / (2d)

若 dAccel + dDecel ≤ distance：
    vPeak = vMax,  dCruise = distance - dAccel - dDecel   （三段式）
否則：
    vPeak = sqrt(2·a·d·distance / (a+d)),  dCruise = 0   （兩段式，短距離）
```

**E1 公式**：

```
E1 = mTotal × (g·μr + a·η) × vPeak² / (2a)   [J]
```

| 符號 | 意義 | 來源 |
|---|---|---|
| mTotal | 總質量 | `GetTotalMass(pod)` |
| g | 重力 9.8 m/s² | 硬編碼 |
| μr | 滾動摩擦係數 | `FRICTION` |
| a | 加速度 | `Physics.Acceleration`（每 bot） |
| η | 慣量係數 | `INERTIA` |
| vPeak | 峰值速度 | 由 a, d, vMax, distance 計算 |

**計算模塊**：`EnergyConsumption.ComputeSegmentEnergy()`

---

### E2 — 減速段能耗

**觸發時機**：同 E1，於同一次 `setNextWaypoint()` 呼叫
**觸發模塊**：`BotNormal.cs:542`

**公式**：

```
netCoeff = d·η - g·μr

若 netCoeff > 0：
    E2 = mTotal × netCoeff × vPeak² / (2d)   [J]
否則：
    E2 = 0   （摩擦輔助制動，不需額外做功）
```

**注意**：以目前預設值 d=1, η=0.15, g=9.8, μr=0.02：
- `d·η = 0.15`，`g·μr = 0.196`
- netCoeff = 0.15 − 0.196 = **−0.046 < 0**，故 **E2 = 0**（正常現象）

---

### E3 — 等速巡航段能耗

**觸發時機**：同 E1
**公式**：

```
E3 = mTotal × g × μr × dCruise   [J]
```

若兩段式（dCruise = 0）則 E3 = 0。

---

### E4 — 原地旋轉能耗

**觸發時機**：同一次 `setNextWaypoint()`，`rotateDuration > 0` 時
**觸發模塊**：`BotNormal.cs:553`

**公式**：

```
ω = 2π / TurnSpeed            （[rad/s]，TurnSpeed 單位為 s/rev）

I = (1/12) × ROBOT_MASS × (L² + W²)   （均質矩形慣量，對垂直質心軸）

E_kinetic = (1/2) × I × ω²            （旋轉動能）

E_friction = ROBOT_MASS × g × μr × ROBOT_RADIUS × θ   （旋轉摩擦耗能）

E4 = E_kinetic + E_friction   [J]
```

**θ 計算**（`BotNormal.cs:557`）：

```
θ [rad] = (rotateDuration / TurnSpeed) × 2π
```

**注意修正歷史**：
- 原始公式使用 `(1/6)` 且缺少 `0.5` 因子，導致 4× 高估
- 已修正為 `(1/12)` 與 `0.5 × I × ω²`

---

### E5a — 舉升 Pod 能耗

**觸發時機**：`BotPickupPod.Act()` 成功執行時
**觸發模塊**：`BotNormal.cs:1317`（`BotPickupPod` 內部狀態機）

**公式**：

```
E5a = mLoad × (g + 2h/t²) × h   [J]
```

| 符號 | 意義 | 來源 |
|---|---|---|
| mLoad | Pod 有效負載質量 | `GetLoadMass(bot.Pod)` |
| h | 升降高度 | `LIFT_HEIGHT` |
| t | Pod 搬運時間 | `bot.PodTransferTime`（每 bot） |

---

### E5b — 放下 Pod 能耗

**觸發時機**：`BotSetdownPod.Act()` 成功執行時
**觸發模塊**：`BotNormal.cs:1390`（`BotSetdownPod` 內部狀態機）

**重要**：`mLoad` 在 `SetdownPod()` 呼叫前取得（呼叫後 `bot.Pod` 即為 null）

**公式**：

```
E5b = max(0,  mLoad × (g - 2h/t²) × h)   [J]
```

重力輔助降下，公式值可能為負，`max(0, ...)` 截斷。

---

### E6 — 停站處理能耗

**觸發時機**：`DequeueState()` 在 `BotPutItems` 或 `BotGetItems` 狀態退出時
**觸發模塊**：`BotNormal.cs:472`

另有提前結算：若任務中途被新任務打斷（`AssignTask()` 呼叫時），於 `BotNormal.cs:323` 結算已累計的停站時間。

**公式**：

```
E6 = STATION_HOLD_POWER_W × duration   [J]
```

| 符號 | 意義 | 來源 |
|---|---|---|
| duration | Bot 停在站台的實際秒數 | `currentTime - StationEnterTime` |
| STATION_HOLD_POWER_W | 持站功率 | `EnergyConsumption.STATION_HOLD_POWER_W` |

**注意修正歷史**：
- 原始公式 `mLoad × g × μ_lift × duration` 單位為 N·s（衝量），不是 J（能量），物理上錯誤
- 已改為功率模型 `P × t`，STATION_HOLD_POWER_W 預設 15 W

---

### E7 等效 — 衝突停走能耗

**觸發時機**：Waypoint 預約失敗（`setNextWaypoint()` 返回 false），下一次成功預約時
**觸發模塊**：`BotNormal.cs:566`

**機制**（非獨立公式）：

```
當 waypoint 預約失敗：
    設 ConflictStopPending = true   （BotNormal.cs:586）

下一次 setNextWaypoint() 成功時：
    若 ConflictStopPending == true：
        StatEnergyConflictStopGoJ += e1   （該次加速的 E1 被標記為衝突後重啟動能）
        ConflictStopPending = false
```

本質是：**停車後重新加速的 E1 能耗**被額外計入 E7 桶，用以量化衝突的能耗代價。E7 是 E1 的子集，不額外加入 total。

---

## 5. Bot 層級累計統計

**模塊**：`BotNormal.cs` 欄位（每個 BotNormal 實例各自獨立）

| 欄位 | 內容 |
|---|---|
| `StatEnergyTotalJ` | E1+E2+E3+E4+E5a+E5b+E6 總和 [J] |
| `StatEnergyE1AccelJ` | 加速段累計 [J] |
| `StatEnergyE2DecelJ` | 減速段累計 [J] |
| `StatEnergyE3CruiseJ` | 巡航段累計 [J] |
| `StatEnergyE4RotationJ` | 旋轉累計 [J] |
| `StatEnergyE5LiftLowerJ` | 舉升+放下累計 [J] |
| `StatEnergyE6StationJ` | 停站持功累計 [J] |
| `StatEnergyConflictStopGoJ` | 衝突重啟動 E1 子集 [J] |
| `StatDistanceTraveledM` | 實際行駛距離累計 [m] |
| `StatOrdersCompleted` | 完成的 Extract 訂單數 |

重置時機：`ResetEnergyStatistics()`（`BotNormal.cs:225`）

---

## 6. 全隊層級統計

**模塊**：`RAWSimO.Core/InstanceStatistics.cs`（`Instance` 的 partial class）

```csharp
StatOverallEnergyTotalJ  = Bots.Sum(b => b.StatEnergyTotalJ)
StatOverallEnergyE1J     = Bots.Sum(b => b.StatEnergyE1AccelJ)
// … 各分項同理 …
StatOverallDistanceTraveledRizqi = Bots.Sum(b => b.StatDistanceTraveledM)
```

**輸出至 controllog.txt**（`InstanceStatistics.PrintStatistics()`）：

```
StatEnergyTotalKJ          = StatOverallEnergyTotalJ / 1000
StatEnergyE1AccelKJ        = StatOverallEnergyE1J / 1000
StatEnergyE2DecelKJ        = …
StatEnergyE3CruiseKJ       = …
StatEnergyE4RotationKJ     = …
StatEnergyE5LiftLowerKJ    = …
StatEnergyE6StationKJ      = …
StatEnergyConflictKJ       = …
StatEnergyPerOrderKJ       = Total / StatOverallOrdersHandled
StatEnergyPerMeterJoule    = TotalJ / StatOverallDistanceTraveledRizqi
```

---

## 7. Jenkins Baseline 驗證結果（2026-03-28）

設定：jenkinsinstance1.xinst + jenkinssetting1.xsett + jenkinsconfig1.xconf，Seed=42，模擬 8 小時

| 指標 | 數值 |
|---|---|
| E1 加速 | 3,143 kJ（33.3%） |
| E2 減速 | 0 kJ（0%，物理正確） |
| E3 巡航 | 3,584 kJ（38.0%） |
| E4 旋轉 | 873 kJ（9.2%） |
| E5 升降 | 1,231 kJ（13.0%） |
| E6 停站 | 612 kJ（6.5%） |
| E7 衝突 | 71 kJ（次項） |
| **總計** | **9,443 kJ** |
| 每訂單能耗 | 4.85 kJ（≈1.35 Wh，符合文獻 2–5 Wh 範圍） |
| 每公尺能耗 | 139.9 J/m |

---

## 8. 已知修正歷史（可供比對舊版）

| 項目 | 原始（錯誤）| 修正後 |
|---|---|---|
| E4 慣量矩 | `(1/6)·m·(L²+W²)` | `(1/12)·m·(L²+W²)`（均質矩形正確值） |
| E4 動能 | `I·ω²` | `(1/2)·I·ω²`（動能定義）|
| E6 公式 | `mLoad·g·μ_lift·t`（單位 N·s，錯誤）| `P_hold·t`（功率模型，單位 J）|
| Pod 架體質量 | 未計入（mLoad=cargo only）| `POD_FRAME_MASS + cargo`（40 kg 架體） |

---

## 9. 主要模塊檔案索引

| 模塊職責 | 檔案路徑 |
|---|---|
| 公式定義與物理常數 | `RAWSimO.Core/Metrics/EnergyConsumption.cs` |
| Per-bot 能耗累計（E1-E7 hooks）| `RAWSimO.Core/Bots/BotNormal.cs` |
| 全隊統計彙整與輸出 | `RAWSimO.Core/InstanceStatistics.cs` |
| xinst 參數載入與 Configure() 呼叫 | `RAWSimO.Core/IO/DTOInstance.cs` |
| xlayo 參數來源（bot kinematics）| `RAWSimO.Core/Configurations/LayoutConfiguration.cs` |
