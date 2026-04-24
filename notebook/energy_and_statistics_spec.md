# Energy Consumption & Statistics Module — Migration Spec

> **目的**：本文件完整記錄 RAWSim-O fork 中 Rizqi 能耗模型與統計輸出的所有實作細節，
> 供在全新 RAWSim-O 專案中逐步重建功能使用。
> 所有公式、常數、hook 位置、輸出格式均以原始碼為準。

---

## 目錄

1. [檔案清單與相依關係](#1-檔案清單與相依關係)
2. [XML 設定擴充（xinst）](#2-xml-設定擴充xinst)
3. [組態載入（DTOInstance）](#3-組態載入dtoinstance)
4. [能耗計算核心（EnergyConsumption.cs）](#4-能耗計算核心energyconsumptioncs)
5. [Bot 端統計欄位（BotNormal.cs）](#5-bot-端統計欄位botnormalcs)
6. [生命週期 Hook 一覽表](#6-生命週期-hook-一覽表)
7. [全車隊統計聚合（InstanceStatistics.cs）](#7-全車隊統計聚合instancestatisticscs)
8. [statistics.txt 輸出格式](#8-statisticstxt-輸出格式)
9. [Warmup Reset 行為](#9-warmup-reset-行為)
10. [已知邊界條件與陷阱](#10-已知邊界條件與陷阱)
11. [移植 Checklist](#11-移植-checklist)

---

## 1. 檔案清單與相依關係

| # | 檔案 (相對路徑) | 異動類型 | 說明 |
|---|---|---|---|
| 1 | `RAWSimO.Core/Metrics/EnergyConsumption.cs` | **新增** | 靜態類，所有公式 |
| 2 | `RAWSimO.Core/IO/DTOInstance.cs` | **修改** | 新增 `DTOEnergyParameters` 類 + `Submit()` 呼叫 `Configure()` |
| 3 | `RAWSimO.Core/Bots/BotNormal.cs` | **修改** | 新增統計欄位 + 7 處 hook |
| 4 | `RAWSimO.Core/InstanceStatistics.cs` | **修改** | 新增 fleet 聚合屬性 + `statistics.txt` 輸出區段 + warmup reset |

**相依方向**：`BotNormal` → `EnergyConsumption`（呼叫計算方法），`InstanceStatistics` → `BotNormal`（讀取 per-bot 累加器），`DTOInstance` → `EnergyConsumption`（初始化常數）。

---

## 2. XML 設定擴充（xinst）

在 `.xinst` 檔案的 `<Instance>` 根元素下新增 `<EnergyParameters>` 區塊。
若省略此區塊，DTOEnergyParameters 的欄位預設值即生效。

```xml
<Instance Name="aesop">
  <EnergyParameters>
    <RobotMass>300</RobotMass>           <!-- AGV 底盤質量 [kg] -->
    <RobotWidth>0.6</RobotWidth>         <!-- AGV 車身寬度 [m] -->
    <RobotLength>0.75</RobotLength>      <!-- AGV 車身長度 [m] -->
    <RollingFriction>0.02</RollingFriction>  <!-- 滾動摩擦係數 [-] -->
    <InertiaCoeff>0.15</InertiaCoeff>    <!-- 驅動系慣性等效係數 [-] -->
    <LiftHeight>0.2</LiftHeight>         <!-- Pod 舉降高度 [m] -->
    <PodFrameMass>40</PodFrameMass>      <!-- Pod 空架質量 [kg] -->
    <StationHoldPowerW>15</StationHoldPowerW> <!-- 站點託舉功率 [W] -->
  </EnergyParameters>
  <!-- 其餘 Bots / Pods / Waypoints 等既有內容 -->
</Instance>
```

---

## 3. 組態載入（DTOInstance）

### 3.1 新增類別 `DTOEnergyParameters`

位置：`RAWSimO.Core/IO/DTOInstance.cs`，放在 namespace 內、`DTOInstance` 類別外。

```csharp
public class DTOEnergyParameters
{
    public double RobotMass = 300.0;
    public double RobotWidth = 0.6;
    public double RobotLength = 0.75;
    public double RollingFriction = 0.02;
    public double InertiaCoeff = 0.15;
    public double LiftHeight = 0.2;
    public double PodFrameMass = 40.0;
    public double StationHoldPowerW = 15.0;
}
```

### 3.2 DTOInstance 類別增加欄位

```csharp
public DTOEnergyParameters EnergyParameters = new DTOEnergyParameters();
```

此欄位有預設值 → XML 序列化反序列化向後相容（無 `<EnergyParameters>` 時使用預設值）。

### 3.3 Submit() 內呼叫 Configure()

**插入位置**：`Submit()` 方法內，在 `instance.Name = Name;` 之後、所有 override 邏輯之前。

```csharp
var ep = EnergyParameters ?? new DTOEnergyParameters();
RAWSimO.Core.Metrics.EnergyConsumption.Configure(
    ep.RobotMass, ep.RobotWidth, ep.RobotLength,
    ep.RollingFriction, ep.InertiaCoeff,
    ep.LiftHeight, ep.PodFrameMass, ep.StationHoldPowerW);
```

**時序要求**：必須在任何 Bot 物件建立前呼叫，因為後續 `setNextWaypoint()` 會立即使用這些常數。

---

## 4. 能耗計算核心（EnergyConsumption.cs）

**命名空間**：`RAWSimO.Core.Metrics`
**位置**：`RAWSimO.Core/Metrics/EnergyConsumption.cs`（新增資料夾 `Metrics`）
**性質**：`public static class`，純計算無狀態（全域常數除外）。

### 4.1 可配置常數

| 欄位名 | 預設值 | 單位 | 說明 |
|---|---|---|---|
| `ROBOT_MASS` | 300.0 | kg | AGV 空車底盤質量 |
| `GRAVITY` | 9.8 | m/s² | 重力加速度（不可配置） |
| `FRICTION` | 0.02 | - | 滾動摩擦係數 μ_r |
| `INERTIA` | 0.15 | - | 驅動系慣性等效係數 η |
| `ROBOT_WIDTH` | 0.6 | m | 車身寬度 |
| `ROBOT_LENGTH` | 0.75 | m | 車身長度 |
| `ROBOT_RADIUS` | 0.3 | m | 迴轉半徑（自動計算 = WIDTH/2） |
| `LIFT_HEIGHT` | 0.2 | m | Pod 舉降高度 |
| `POD_FRAME_MASS` | 40.0 | kg | Pod 空架質量 |
| `STATION_HOLD_POWER_W` | 15.0 | W | 站點託舉功率 |

### 4.2 Configure() 方法

```csharp
public static void Configure(
    double robotMass, double robotWidth, double robotLength,
    double rollingFriction, double inertiaCoeff,
    double liftHeight, double podFrameMass, double stationHoldPowerW)
{
    ROBOT_MASS           = robotMass;
    ROBOT_WIDTH          = robotWidth;
    ROBOT_LENGTH         = robotLength;
    ROBOT_RADIUS         = robotWidth / 2.0;   // ← 自動計算
    FRICTION             = rollingFriction;
    INERTIA              = inertiaCoeff;
    LIFT_HEIGHT          = liftHeight;
    POD_FRAME_MASS       = podFrameMass;
    STATION_HOLD_POWER_W = stationHoldPowerW;
}
```

### 4.3 質量輔助方法

```csharp
// 行車能耗用（E1/E2/E3/E4）：底盤 + Pod 架 + 貨物
public static double GetTotalMass(Elements.Pod pod)
{
    if (pod == null) return ROBOT_MASS;
    return ROBOT_MASS + POD_FRAME_MASS + pod.CapacityInUse;
}

// 舉降能耗用（E5a/E5b）：Pod 架 + 貨物（不含底盤）
public static double GetLoadMass(Elements.Pod pod)
{
    if (pod == null) return 0.0;
    return POD_FRAME_MASS + pod.CapacityInUse;
}
```

### 4.4 E1 + E2 + E3 — 直線段行車能耗

**方法簽名**：

```csharp
public static double ComputeSegmentEnergy(
    double mTotal, double accel, double decel, double vMax, double distance,
    out double e1J, out double e2J, out double e3J)
```

**參數來源**：
- `mTotal` ← `GetTotalMass(bot.Pod)`
- `accel` ← `bot.Physics.Acceleration`
- `decel` ← `bot.Physics.Deceleration`
- `vMax` ← `bot.Physics.MaxSpeed`
- `distance` ← `currentWaypoint.GetDistance(nextWaypoint)` (歐氏距離)

**運動學分解（v=0 → vPeak → v=0）**：

```
dAccel = vMax² / (2·accel)        // 加速段距離
dDecel = vMax² / (2·decel)        // 減速段距離

if dAccel + dDecel ≤ distance:    // 三段式（加速→巡航→減速）
    vPeak = vMax
    dCruise = distance - dAccel - dDecel
else:                              // 二段式（加速→減速，短線段）
    vPeak = √( 2·accel·decel·distance / (accel+decel) )
    dCruise = 0
```

**E1 — 加速段能耗**：

```
E1 = mTotal × (g·μ_r + a·η) × vPeak² / (2·a)   [J]
```

來源：對 `F·v dt` 在加速段積分，其中 `F = m(g·μ_r + a·η)`，`v = a·t`。

**E2 — 減速段能耗**：

```
netCoeff = d·η - g·μ_r
E2 = (netCoeff > 0) ? mTotal × netCoeff × vPeak² / (2·d) : 0   [J]
```

**重要**：以預設參數 η=0.15, μ_r=0.02 → `d·η = d×0.15`, `g·μ_r = 9.8×0.02 = 0.196`。
除非 `d > 0.196/0.15 = 1.307 m/s²`，否則 **E2 永遠為 0**。

**E3 — 巡航段能耗**：

```
E3 = mTotal × g × μ_r × dCruise   [J]
```

### 4.5 E4 — 原地旋轉能耗

**方法簽名**：

```csharp
public static double E4_Rotation(double thetaRad, double turnSpeed)
```

**參數來源**：
- `thetaRad` ← `rotateDuration / TurnSpeed × 2π` （在 `setNextWaypoint()` 內計算）
- `turnSpeed` ← `bot.TurnSpeed`（xinst 中定義的每 360° 秒數）

**公式**：

```
ω = 2π / turnSpeed              [rad/s]

I = (1/12) × ROBOT_MASS × (L² + W²)   [kg·m²]
                                         （均質矩形體繞垂直中心軸）

E4 = (1/2)·I·ω² + ROBOT_MASS·g·μ_r·r·θ   [J]
     ──────────   ───────────────────────
     轉動動能      旋轉摩擦
```

**注意**：
- E4 的 `I` 和摩擦項使用 `ROBOT_MASS`（不含 Pod 質量），但 `setNextWaypoint()` 呼叫前已用 `GetTotalMass()` 計算 mTotal 給 E1-E3。E4 函數本身只用 `ROBOT_MASS`。
- 修正了原始 Rizqi 模型的兩個錯誤：`I` 從 `(1/6)` 改為 `(1/12)`，動能從 `I·ω²` 改為 `(1/2)·I·ω²`。

### 4.6 E5a — Pod 舉升能耗

```csharp
public static double E5a_LiftPod(double mLoad, double podTransferTime)
```

```
E5a = mLoad × (g + 2h/t²) × h   [J]
```

其中 `h` = LIFT_HEIGHT, `t` = podTransferTime（假設均勻加速舉升）。

### 4.7 E5b — Pod 降下能耗

```csharp
public static double E5b_LowerPod(double mLoad, double podTransferTime)
```

```
E5b = max(0, mLoad × (g - 2h/t²) × h)   [J]
```

重力輔助降下，因此淨功 ≤ 舉升。若降下夠慢（t 大），結果接近 `mLoad·g·h`。

### 4.8 E6 — 站點停留能耗

```csharp
public static double E6_StationProcessing(double duration)
```

```
E6 = STATION_HOLD_POWER_W × duration   [J]   (恆定功率模型)
```

取代了原始 `mLoad·g·μ_lift·t` 公式（該公式單位為 N·s ≠ J，維度錯誤）。

---

## 5. Bot 端統計欄位（BotNormal.cs）

### 5.1 新增欄位

**位置**：`BotNormal` 類別內，建議放在 `#region Energy Statistics (Rizqi Model)` 區塊中。

```csharp
// ── 累加器 [J] ──
public double StatEnergyTotalJ;              // E1+E2+E3+E4+E5+E6+E7
public double StatEnergyE1AccelJ;            // 加速段
public double StatEnergyE2DecelJ;            // 減速段（通常 0）
public double StatEnergyE3CruiseJ;           // 巡航段
public double StatEnergyE4RotationJ;         // 原地旋轉
public double StatEnergyE5LiftLowerJ;        // Pod 舉降（E5a + E5b 合計）
public double StatEnergyE6StationJ;          // 站點停留
public double StatEnergyConflictStopGoJ;     // 衝突引起的再加速（E7 等效）

// ── 輔助欄位 ──
public int    StatOrdersCompleted;           // 已完成 extract 訂單數
public double StatDistanceTraveledM;         // 行車距離 [m]（能耗模組自己追蹤的）
public double CurrentTotalMassKg =>
    RAWSimO.Core.Metrics.EnergyConsumption.GetTotalMass(Pod);

// ── 內部狀態 ──
internal bool   ConflictStopPending = false;  // 上次 reservation 失敗旗標
internal double StationEnterTime = -1.0;      // 進站時間戳 [s]，-1 = 不在站
```

### 5.2 Reset 方法

```csharp
public void ResetEnergyStatistics()
{
    StatEnergyTotalJ = 0.0;
    StatEnergyE1AccelJ = 0.0;
    StatEnergyE2DecelJ = 0.0;
    StatEnergyE3CruiseJ = 0.0;
    StatEnergyE4RotationJ = 0.0;
    StatEnergyE5LiftLowerJ = 0.0;
    StatEnergyE6StationJ = 0.0;
    StatEnergyConflictStopGoJ = 0.0;
    StatOrdersCompleted = 0;
    StatDistanceTraveledM = 0.0;
    ConflictStopPending = false;
    StationEnterTime = -1.0;
}
```

### 5.3 需要的 using

```csharp
using RAWSimO.Core.Metrics;  // ← 加在 BotNormal.cs 檔頭
```

---

## 6. 生命週期 Hook 一覽表

以下是 BotNormal.cs 內需要插入能耗計算的 **7 個精確位置**。

### Hook 1：E1 + E2 + E3 + E4 — 移動段（setNextWaypoint 成功時）

**位置**：`setNextWaypoint()` 方法內，在 waypoint reservation 成功、計算 `_driveDuration` 之後。

```csharp
// ── 成功取得 reservation 後 ──
double segmentDistance = CurrentWaypoint.GetDistance(NextWaypoint);
_driveDuration = Physics.getTimeNeededToMove(0, segmentDistance);

// ── Energy Hook: E1 + E2 + E3 ──
double mTotal = EnergyConsumption.GetTotalMass(Pod);
double e1, e2, e3;
EnergyConsumption.ComputeSegmentEnergy(
    mTotal, Physics.Acceleration, Physics.Deceleration, Physics.MaxSpeed,
    segmentDistance, out e1, out e2, out e3);

StatEnergyE1AccelJ += e1;
StatEnergyE2DecelJ += e2;
StatEnergyE3CruiseJ += e3;

// ── Energy Hook: E4 ──
double e4 = 0.0;
if (_rotateDuration > 0.0 && TurnSpeed > 0.0)
{
    double thetaRad = _rotateDuration / TurnSpeed * 2.0 * Math.PI;
    e4 = EnergyConsumption.E4_Rotation(thetaRad, TurnSpeed);
    StatEnergyE4RotationJ += e4;
}

double segmentTotal = e1 + e2 + e3 + e4;
StatEnergyTotalJ += segmentTotal;
StatDistanceTraveledM += segmentDistance;
```

### Hook 2：E7 等效 — 衝突再加速標記（setNextWaypoint 成功時，接在 Hook 1 後）

```csharp
// ── Energy Hook: E7 conflict ──
if (ConflictStopPending)
{
    StatEnergyConflictStopGoJ += e1;  // 此次 E1 歸因於衝突
    ConflictStopPending = false;
}
```

### Hook 3：衝突旗標設定（setNextWaypoint 失敗時）

**位置**：`setNextWaypoint()` 的 else 分支（reservation 失敗）。

```csharp
// ── Energy Hook: E7 conflict flag ──
ConflictStopPending = true;
```

### Hook 4：E5a — Pod 舉升（BotPickupPod.Act 成功時）

**位置**：`BotPickupPod.Act()` 內，`PickupPod()` 成功後。

```csharp
// ── Energy Hook: E5a (lift pod) ──
double mL = EnergyConsumption.GetLoadMass(bot.Pod);
double e5a = EnergyConsumption.E5a_LiftPod(mL, bot.PodTransferTime);
bot.StatEnergyE5LiftLowerJ += e5a;
bot.StatEnergyTotalJ += e5a;
```

### Hook 5：E5b — Pod 降下（BotSetdownPod.Act 成功時）

**位置**：`BotSetdownPod.Act()` 內。

**關鍵陷阱**：必須在 `SetdownPod()` 呼叫**之前**擷取 mLoad，因為 `SetdownPod()` 會將 `bot.Pod` 設為 null。

```csharp
// ── 在 SetdownPod() 之前擷取 ──
Pod pod = bot.Pod;
double mLBeforeSetdown = EnergyConsumption.GetLoadMass(pod);

if (bot.SetdownPod(currentTime))
{
    // ... 其他邏輯 ...

    // ── Energy Hook: E5b (lower pod) ──
    double e5b = EnergyConsumption.E5b_LowerPod(mLBeforeSetdown, bot.PodTransferTime);
    bot.StatEnergyE5LiftLowerJ += e5b;
    bot.StatEnergyTotalJ += e5b;
}
```

### Hook 6：E6 計時開始（BotGetItems.Act / BotPutItems.Act 初始化時）

**位置**：兩個 state 類別的 `Act()` 方法內，`_initialized = true` 時。

```csharp
// 在 BotGetItems.Act() 的 if (!_initialized) 內：
bot.StationEnterTime = currentTime;

// 在 BotPutItems.Act() 的 if (!_initialized) 內：
bot.StationEnterTime = currentTime;
```

### Hook 7：E6 結算（DequeueState / AssignTask）

**位置 A**：`DequeueState()` 方法內，dequeue 後立即檢查。

```csharp
IBotState dequeuedState = StateQueueDequeue();

if ((dequeuedState.Type == BotStateType.PutItems
     || dequeuedState.Type == BotStateType.GetItems)
    && StationEnterTime >= 0.0)
{
    double stationDuration = currentTime - StationEnterTime;
    double e6 = EnergyConsumption.E6_StationProcessing(stationDuration);
    StatEnergyE6StationJ += e6;
    StatEnergyTotalJ += e6;
    StationEnterTime = -1.0;
}
```

**位置 B**：`AssignTask()` 方法內，清空 state queue 前的防護結算。

```csharp
// 在 StateQueueClear() 之前：
if (StationEnterTime >= 0.0)
{
    double stationDuration = Instance.Controller.CurrentTime - StationEnterTime;
    double e6 = EnergyConsumption.E6_StationProcessing(stationDuration);
    StatEnergyE6StationJ += e6;
    StatEnergyTotalJ += e6;
    StationEnterTime = -1.0;
}
```

### Hook 7 補充：StatOrdersCompleted

```csharp
// 在 DequeueState() 內，E6 結算之後：
if (dequeuedState.Type == BotStateType.PutItems)
    StatOrdersCompleted++;
```

---

## 7. 全車隊統計聚合（InstanceStatistics.cs）

### 7.1 新增聚合屬性

**位置**：`InstanceStatistics.cs`（即 `Instance` partial class 中 statistics 區段），放在 `StatOverallDistanceTraveled` 附近。

```csharp
public double StatOverallEnergyTotalJ =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyTotalJ);
public double StatOverallEnergyE1J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE1AccelJ);
public double StatOverallEnergyE2J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE2DecelJ);
public double StatOverallEnergyE3J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE3CruiseJ);
public double StatOverallEnergyE4J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE4RotationJ);
public double StatOverallEnergyE5J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE5LiftLowerJ);
public double StatOverallEnergyE6J =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyE6StationJ);
public double StatOverallEnergyConflictJ =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatEnergyConflictStopGoJ);

public int StatOverallOrdersCompletedByBots =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatOrdersCompleted);
public double StatOverallDistanceTraveledRizqi =>
    Bots.OfType<Bots.BotNormal>().Sum(b => b.StatDistanceTraveledM);
```

### 7.2 需要的 using

```csharp
using RAWSimO.Core.Metrics;  // ← 加在 InstanceStatistics.cs 檔頭
```

---

## 8. statistics.txt 輸出格式

**位置**：`InstanceStatistics.cs` 的 `WriteReadableStatistics()` 方法末尾（在既有統計輸出之後）。

```csharp
// Energy statistics (Rizqi model)
sb.AppendLine(">>> Energy (Rizqi model)");
sb.AppendLine("StatEnergyTotalKJ: "       + (StatOverallEnergyTotalJ    / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE1AccelKJ: "     + (StatOverallEnergyE1J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE2DecelKJ: "     + (StatOverallEnergyE2J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE3CruiseKJ: "    + (StatOverallEnergyE3J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE4RotationKJ: "  + (StatOverallEnergyE4J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE5LiftLowerKJ: " + (StatOverallEnergyE5J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyE6StationKJ: "   + (StatOverallEnergyE6J       / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyConflictKJ: "    + (StatOverallEnergyConflictJ / 1000.0).ToString(IOConstants.FORMATTER));
sb.AppendLine("StatEnergyPerOrderKJ: "    + (StatOverallOrdersHandled > 0
    ? (StatOverallEnergyTotalJ / 1000.0 / StatOverallOrdersHandled).ToString(IOConstants.FORMATTER) : "0"));
sb.AppendLine("StatEnergyPerMeterJoule: " + (StatOverallDistanceTraveledRizqi > 0
    ? (StatOverallEnergyTotalJ / StatOverallDistanceTraveledRizqi).ToString(IOConstants.FORMATTER) : "0"));
sb.AppendLine("StatDistanceTraveledRizqiM: " + StatOverallDistanceTraveledRizqi.ToString(IOConstants.FORMATTER));
```

**輸出單位**：除 `StatEnergyPerMeterJoule`（J/m）和 `StatDistanceTraveledRizqiM`（m）外，全部以 **KJ** 為單位（J / 1000）。

**範例輸出**：

```
>>> Energy (Rizqi model)
StatEnergyTotalKJ: 49240.360437827505
StatEnergyE1AccelKJ: 11900.023741198187
StatEnergyE2DecelKJ: 0
StatEnergyE3CruiseKJ: 26776.468398836925
StatEnergyE4RotationKJ: 2934.4451495131425
StatEnergyE5LiftLowerKJ: 6048.810497666277
StatEnergyE6StationKJ: 1580.6126506129333
StatEnergyConflictKJ: 405.6956431346489
StatEnergyPerOrderKJ: 7.443743074501512
StatEnergyPerMeterJoule: 137.21978814642478
StatDistanceTraveledRizqiM: 358858.123...
```

---

## 9. Warmup Reset 行為

**位置**：`InstanceStatistics.cs` 的 `StatReset()`（warmup 結束時呼叫）。

```csharp
foreach (var b in Bots)
{
    b.ResetStatistics();       // 原本的統計 reset
    if (b is Bots.BotNormal bn)
        bn.ResetEnergyStatistics();  // ← 新增
}
```

**效果**：warmup 期間的能耗不計入最終統計，與其他原生統計指標行為一致。

---

## 10. 已知邊界條件與陷阱

### 10.1 E2 永遠為 0

以預設參數 η=0.15, μ_r=0.02：
- `d·η = decel × 0.15`
- `g·μ_r = 9.8 × 0.02 = 0.196`
- 除非 `decel > 1.307 m/s²`，否則 `netCoeff ≤ 0`，E2 = 0

這代表被動煞車足夠，不需電氣回收制動。

### 10.2 E5b 的 mLoad 必須在 SetdownPod() 前擷取

`SetdownPod()` 會將 `bot.Pod` 設為 null。若在之後才取 `GetLoadMass(bot.Pod)`，結果為 0。
**必須**先暫存 pod 引用再計算。

### 10.3 E6 有兩個結算路徑

1. 正常路徑：`DequeueState()` 在 PutItems/GetItems 完成時結算
2. 異常路徑：`AssignTask()` 在任務中斷時防護結算

兩條路徑都必須實作，否則中斷的站點停留時間會遺失。

### 10.4 E4 用 ROBOT_MASS 而非 mTotal

E4 的慣性矩 `I = (1/12)·ROBOT_MASS·(L²+W²)` 和摩擦項都只用 `ROBOT_MASS`。
這是因為 E4_Rotation() 內部直接讀取靜態常數 `ROBOT_MASS`，不接受 mTotal 參數。
但 `setNextWaypoint()` 內 E1-E3 使用的 `mTotal` 是包含 Pod 的。

### 10.5 質量是動態的

`pod.CapacityInUse` 會隨著物品在站點放入/取出而變化。每段移動的能耗反映**當下**的載重，不是任務開始時的載重。

### 10.6 E7 不是新能量類型

`StatEnergyConflictStopGoJ` 是 E1 的子集標記（label），不會額外加到 `StatEnergyTotalJ`。
它標記「哪些 E1 是因為衝突停車後重新加速而產生的」，用於分析衝突對能耗的影響。

### 10.7 Gym 加減速乘數

`BotNormal.setNextWaypoint()` 在計算 segment energy 前，會檢查 `AccelerationMultiplier` / `DecelerationMultiplier` 並據此重建 `Physics` 物件。能耗計算使用的是**乘數後**的 accel/decel 值。

---

## 11. 移植 Checklist

按順序執行：

- [ ] **Step 1**：建立 `RAWSimO.Core/Metrics/` 資料夾
- [ ] **Step 2**：複製 `EnergyConsumption.cs` 完整內容（269 行）
- [ ] **Step 3**：在 `DTOInstance.cs` 加入 `DTOEnergyParameters` 類別
- [ ] **Step 4**：在 `DTOInstance` 加入 `EnergyParameters` 欄位
- [ ] **Step 5**：在 `DTOInstance.Submit()` 加入 `Configure()` 呼叫
- [ ] **Step 6**：在 `BotNormal.cs` 加入 `using RAWSimO.Core.Metrics;`
- [ ] **Step 7**：在 `BotNormal.cs` 加入所有統計欄位（§5.1）
- [ ] **Step 8**：在 `BotNormal.cs` 加入 `ResetEnergyStatistics()` 方法
- [ ] **Step 9**：在 `setNextWaypoint()` 成功分支插入 Hook 1 + Hook 2
- [ ] **Step 10**：在 `setNextWaypoint()` 失敗分支插入 Hook 3
- [ ] **Step 11**：在 `BotPickupPod.Act()` 插入 Hook 4
- [ ] **Step 12**：在 `BotSetdownPod.Act()` 插入 Hook 5（注意先擷取 mLoad）
- [ ] **Step 13**：在 `BotGetItems.Act()` 初始化區插入 Hook 6
- [ ] **Step 14**：在 `BotPutItems.Act()` 初始化區插入 Hook 6
- [ ] **Step 15**：在 `DequeueState()` 插入 Hook 7（位置 A）
- [ ] **Step 16**：在 `AssignTask()` 插入 Hook 7（位置 B）+ StatOrdersCompleted
- [ ] **Step 17**：在 `InstanceStatistics.cs` 加入 `using RAWSimO.Core.Metrics;`
- [ ] **Step 18**：在 `InstanceStatistics.cs` 加入全車隊聚合屬性（§7.1）
- [ ] **Step 19**：在 `WriteReadableStatistics()` 加入 statistics.txt 能耗區段
- [ ] **Step 20**：在 `StatReset()` 加入 `ResetEnergyStatistics()` 呼叫
- [ ] **Step 21**：在 xinst 檔案加入 `<EnergyParameters>` 區塊（或驗證預設值）

### 驗證方式

1. Build 成功（`dotnet build RAWSimO.sln`）
2. 執行模擬，檢查 `statistics.txt` 是否出現 `>>> Energy (Rizqi model)` 區段
3. 驗證 `StatEnergyTotalKJ ≈ E1 + E2 + E3 + E4 + E5 + E6`（加總一致性）
4. 驗證 `StatEnergyE2DecelKJ = 0`（預設參數下）
5. 驗證 `StatEnergyConflictKJ ≤ StatEnergyE1AccelKJ`（E7 是 E1 子集）
6. 對比舊專案的相同 seed 輸出，數值應完全一致
