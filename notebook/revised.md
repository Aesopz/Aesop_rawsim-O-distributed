# Energy Consumption Module — Revised Spec

> 整理日期：2026-04-02
> 基準 commit：b8e57a6 (fix the energy calculation module)
> 作者：Aesop (莊凱翔)

---

## 一、模組概觀

本系統實作 **Rizqi et al. AGV 能耗模型**，將 AGV 在 RMFS 中完整任務週期的能量分解為七個物理分量（E1–E7）。核心計算邏輯集中於靜態類別 `EnergyConsumption`，由 `BotNormal` 在各關鍵事件點呼叫。

---

## 二、牽動檔案一覽

| 檔案 | 角色 |
|------|------|
| `RAWSimO.Core/Metrics/EnergyConsumption.cs` | **核心計算模組**：所有 E1–E6 公式實作、物理常數、Configure() |
| `RAWSimO.Core/Bots/BotNormal.cs` | **Hook 觸發點**：在移動/轉彎/Pickup/Setdown/Station 事件呼叫計算並累積統計 |
| `RAWSimO.Core/IO/DTOInstance.cs` | **參數注入**：讀取 xinst `<EnergyParameters>` XML 區塊，呼叫 `EnergyConsumption.Configure()` |
| `RAWSimO.Core/Configurations/SettingConfiguration.cs` | **模式開關**：`LegBasedEnergyModel` 旗標決定 E1/E2/E3 計算粒度 |

---

## 三、物理常數（可由 xinst 覆寫）

| 常數 | 預設值 | 說明 | XML 欄位 |
|------|--------|------|----------|
| `ROBOT_MASS` | 300.0 kg | AGV 空車底盤質量 | `RobotMass` |
| `ROBOT_WIDTH` | 0.6 m | AGV 車寬 | `RobotWidth` |
| `ROBOT_LENGTH` | 0.75 m | AGV 車長 | `RobotLength` |
| `ROBOT_RADIUS` | 0.3 m | 轉彎半徑（= Width/2，自動計算） | — |
| `GRAVITY` | 9.8 m/s² | 重力加速度（硬編碼） | — |
| `FRICTION` | 0.02 | 滾動摩擦係數 μ\_r | `RollingFriction` |
| `INERTIA` | 0.15 | 傳動系統等效慣性係數 η（來自 Rizqi 論文） | `InertiaCoeff` |
| `LIFT_HEIGHT` | 0.2 m | Pod 升降高度 h | `LiftHeight` |
| `POD_FRAME_MASS` | 40.0 kg | Pod 架構質量（不含貨物） | `PodFrameMass` |
| `STATION_HOLD_POWER_W` | 15.0 W | 在站台保持 Pod 升起的連續功率 | `StationHoldPowerW` |

Bot 動力學參數（`Acceleration`、`Deceleration`、`MaxSpeed`、`TurnSpeed`、`PodTransferTime`）直接從 `Bot.Physics` 讀取，**不在此 Configure()** 中設定。

---

## 四、質量輔助函式

```
GetTotalMass(pod)  = ROBOT_MASS + POD_FRAME_MASS + pod.CapacityInUse   ← 用於 E1/E2/E3/E4
GetLoadMass(pod)   = POD_FRAME_MASS + pod.CapacityInUse                ← 用於 E5/E6
                     (pod == null → 0)
```

---

## 五、各分量計算原理

### E1 — 加速階段能量

**觸發時機**：每次 leg 結束（`FlushLegEnergy()`）或每個 waypoint segment（Legacy 模式）

**公式**：
```
E1 = mTotal × (g·μr + a·η) × vPeak² / (2a)     [J]
```

- 為對 `v(t) = a·t` 在 `t = 0..vPeak/a` 的功積分
- `vPeak = min(vMax, √(2ad·D/(a+d)))` — 短 segment 不一定能達到 vMax

**代表意義**：克服滾動摩擦 + 加速傳動系統慣性所做的功

---

### E2 — 減速階段能量

**觸發時機**：同 E1，與 E1 同一次 `ComputeSegmentEnergy()` 呼叫

**公式**：
```
E2 = mTotal × max(0, d·η − g·μr) × vPeak² / (2d)    [J]
```

**重要說明**：
- 只有當 `d·η > g·μr` 時才為正（煞車慣性力超過摩擦回饋）
- 以目前預設值（η=0.15, μr=0.02, g=9.8, d≈1.0）：`d·η = 0.15 < g·μr = 0.196` → **E2 永遠為 0**
- 若要啟用 E2，須將 η 調高或 d 調快

---

### E3 — 定速巡航能量

**觸發時機**：同 E1

**公式**：
```
E3 = mTotal × g × μr × d_cruise    [J]
```

- `d_cruise = segment_distance - d_accel - d_decel`（三段式才有巡航段）
- 物理意義：僅克服滾動摩擦

---

### E1/E2/E3 計算模式：Leg-based vs Legacy

由 `SettingConfiguration.LegBasedEnergyModel`（預設 `true`）控制：

| 模式 | 說明 | 觸發點 |
|------|------|--------|
| **Leg-based**（預設） | 連續直線 waypoint 合併為一個「腿」（leg），在腿末端一次計算 E1/E2/E3。物理上正確（不會每 1m 都 stop-and-go）| `FlushLegEnergy()` |
| **Legacy** | 每個 waypoint-to-waypoint segment 都視為獨立 stop-and-go | 每次 `MoveToNextWaypoint()` |

**Leg 邊界條件**（觸發 FlushLegEnergy）：
1. 偵測到轉彎（`isTurn == true`）
2. Bot 抵達目的地 waypoint
3. TC 仲裁衝突導致強制停車（`ConflictStopPending`）

---

### E4 — 原地旋轉能量

**觸發時機**：轉彎事件（`isTurn == true` 且 `_rotateDuration > 0`）

**公式**：
```
I          = (1/12) × mTotal × (L² + W²)    [kg·m²]  矩形均質體慣性矩
ω          = 2π / TurnSpeed                  [rad/s]
E_kinetic  = (1/2) × I × ω²                 [J]
E_friction = mTotal × g × μr × r × θ        [J]
E4         = E_kinetic + E_friction          [J]
```

- `θ` = 實際旋轉角度（rad）= `_rotateDuration / TurnSpeed × 2π`
- `mTotal` 包含 Pod（有掛 Pod 時 E4 顯著增加）

---

### E5a — Pod 舉起能量

**觸發時機**：`BotNormal` state machine 的 **PickupPod** 完成時（`bot.Instance.WaypointGraph.PodPickup()`之後）

**公式**：
```
E5a = mLoad × (g + 2h/t²) × h    [J]
```

- `mLoad = POD_FRAME_MASS + pod.CapacityInUse`
- `h` = LIFT_HEIGHT
- `t` = `bot.PodTransferTime`（xinst 設定）
- 物理意義：均等加速升降（h = ½at²）的功積分

---

### E5b — Pod 放下能量

**觸發時機**：**SetdownPod** 完成時（`bot.SetdownPod()` 前捕捉 mLoad）

**公式**：
```
E5b = max(0, mLoad × (g − 2h/t²) × h)    [J]
```

- 重力輔助下降，若加速度夠小則 E5b > 0；若 `2h/t² > g` 則夾為 0
- 注意：mLoad 在 `SetdownPod()` 呼叫後 bot.Pod 會變 null，因此在呼叫前先快取

---

### E6 — 在站台停留能量（⚠️ 實作不完整）

**設計原理**：
```
E6 = STATION_HOLD_POWER_W × duration    [J]
```
功率模型（恆定功率 × 停留時間），取代原始 Rizqi 公式 `mLoad·g·μ_lift·t`（後者單位不正確 N·s ≠ J）

**目前狀態 — 存在實作缺口**：

| 步驟 | 狀態 |
|------|------|
| `E6_StationProcessing()` 函式定義於 `EnergyConsumption.cs` | ✅ 已實作 |
| `StationEnterTime` 在 PickupFromStation / PutdownToStation 狀態初始化時設定 | ✅ 已設定 |
| 離開站台時讀取 `StationEnterTime`，呼叫 `E6_StationProcessing()` 累積至 `StatEnergyE6StationJ` | ❌ **尚未實作** |

**結果**：`StatEnergyE6StationJ` 在模擬中永遠為 0。

---

### E7（等效）— 衝突停車後重新加速能量

**觸發時機**：TC 仲裁失敗（`CanReserveMoveTarget()` 回傳 false）→ `ConflictStopPending = true` → 下一次 FlushLegEnergy 時累積

**計算方式**：非獨立公式，而是將該腿的 **E1 值標記為衝突誘發重加速能耗**：
```
StatEnergyConflictStopGoJ += e1   // 當 ConflictStopPending == true 時
```

---

## 六、統計欄位彙整（BotNormal）

| 欄位 | 對應分量 | 累積時機 |
|------|----------|----------|
| `StatEnergyTotalJ` | E1+E2+E3+E4+E5+E6 | 每個分量計算後即加入 |
| `StatEnergyE1AccelJ` | E1 | FlushLegEnergy / 每 segment |
| `StatEnergyE2DecelJ` | E2 | FlushLegEnergy / 每 segment |
| `StatEnergyE3CruiseJ` | E3 | FlushLegEnergy / 每 segment |
| `StatEnergyE4RotationJ` | E4 | 轉彎事件 |
| `StatEnergyE5LiftLowerJ` | E5a + E5b | Pickup / Setdown |
| `StatEnergyE6StationJ` | E6 | ⚠️ **永遠為 0（缺實作）** |
| `StatEnergyConflictStopGoJ` | E7 等效 | 衝突停車後重加速 |
| `StatDistanceTraveledM` | — | FlushLegEnergy / 每 segment |

---

## 七、初始化流程

```
1. 解析 .xinst XML
   └─ DTOInstance.Submit()
       └─ EnergyConsumption.Configure(ep.RobotMass, ep.RobotWidth, ...)

2. 模擬開始，每個 Bot 的動力學參數（a, d, vMax, TurnSpeed）直接從 Physics 讀取

3. 每次模擬 Reset 時：
   └─ BotNormal.ResetEnergyStatistics() — 清零所有統計 + 重置 Leg 狀態
```

---

## 八、待補強項目

1. **E6 實作缺口**：在 PickupFromStation / PutdownToStation state 的完成回呼中加入：
   ```csharp
   if (bot.StationEnterTime >= 0)
   {
       double e6 = EnergyConsumption.E6_StationProcessing(currentTime - bot.StationEnterTime);
       bot.StatEnergyE6StationJ += e6;
       bot.StatEnergyTotalJ += e6;
       bot.StationEnterTime = -1.0;
   }
   ```

2. **E2 永遠為 0**：確認論文中 η 值的適用場景（η=0.15 對應特定傳動比），若實驗中需要 E2 非零需重新校定參數。

3. **GymServer TCP 回傳**：目前 TCP reward payload 是否包含個別 E1–E7 分量待確認（供 Python 側 reward shaping 使用）。
