# Energy Consumption Module — Implementation Guide

> 基於 Rizqi et al. AGV 能耗模型
> 實作檔案：`RAWSimO.Core/Metrics/EnergyConsumption.cs`、`RAWSimO.Core/Bots/BotNormal.cs`

---

## 一、整體架構概覽

能耗計算**不是即時物理積分**，而是在每個**狀態轉換的觸發點**一次性計算並累積到統計欄位。

```
BotNormal 統計欄位
├── StatEnergyE1AccelJ       加速能耗 [J]
├── StatEnergyE2DecelJ       減速能耗 [J]（通常為 0）
├── StatEnergyE3CruiseJ      等速巡航能耗 [J]
├── StatEnergyE4RotationJ    原地旋轉能耗 [J]
├── StatEnergyE5LiftLowerJ   抬起 / 放下 Pod 能耗 [J]
├── StatEnergyConflictStopGoJ  因交通衝突停止後再啟動的額外加速能耗 [J]
└── StatEnergyTotalJ         以上總和 [J]
```

---

## 二、物理參數

| 符號 | 名稱 | 預設值 | 來源 |
|---|---|---|---|
| `ROBOT_MASS` | AGV 空載底盤質量 | 300 kg | Rizqi 論文 |
| `POD_FRAME_MASS` | Pod 框架結構質量（不含貨物） | 40 kg | 自訂估算 |
| `g` | 重力加速度 | 9.8 m/s² | 標準 |
| `μ` (FRICTION) | 滾動摩擦係數 | 0.02 | 標準滾動摩擦 |
| `η` (INERTIA) | 傳動系統等效慣性係數 | 0.15 | Rizqi 論文 |
| `L` (ROBOT_LENGTH) | 底盤長度 | 0.75 m | 自訂 |
| `W` (ROBOT_WIDTH) | 底盤寬度 | 0.6 m | 自訂 |
| `r` (ROBOT_RADIUS) | 旋轉半徑 = W/2 | 0.3 m | 自動推算 |
| `h` (LIFT_HEIGHT) | Pod 抬升高度 | 0.2 m | 自訂 |

**動態質量**（每次計算時即時讀取）：

```
mTotal = ROBOT_MASS + POD_FRAME_MASS + pod.CapacityInUse   (攜帶 Pod 時)
mTotal = ROBOT_MASS                                         (空載時)
mLoad  = POD_FRAME_MASS + pod.CapacityInUse                (僅 Pod 本身，用於 E5)
```

> `pod.CapacityInUse` 為 Pod 上目前貨物總重量，會隨訂單進行動態變化。

---

## 三、Robot 完整移動生命週期與能耗觸發點

```
[任務指派 AssignTask]
        │
        ▼
[Phase A：空載前往 Pod 位置]
  └── 每移動一個 waypoint → E1/E2/E3 + E4（若需轉向）
        │
        ▼
[PickupPod 抬起 Pod]
  └── 觸發 E5a（抬升能耗）
        │
        ▼
[Phase B：載 Pod 前往 OutputStation]
  └── 每移動一個 waypoint → E1/E2/E3 + E4（若需轉向）
  └── mTotal 現在包含 Pod 重量，能耗高於 Phase A
        │
        ▼
[進入 OutputStation 排隊區 → 等待服務]
  └── E6（Station Hold Power）← 已移除，不計算
        │
        ▼
[SetdownPod 放下 Pod]
  └── 觸發 E5b（降下能耗）
        │
        ▼
[Phase C：空載返回 Pod 存放位置]
  └── 每移動一個 waypoint → E1/E2/E3 + E4（若需轉向）
        │
        ▼
[回到起點，等待下一個任務]
```

---

## 四、各能耗分項詳解

### E1 — 加速能耗

**觸發點**：`BotNormal.setNextWaypoint()` 登記下一個 waypoint 成功時，即刻計算整個 segment 的能耗。

**物理假設**：
- Bot 每個 segment 都是從靜止（v=0）加速到峰值速度，再減速到靜止（v=0）。
- 分為三段運動：加速 → 等速巡航（如果距離夠長）→ 減速。
- 峰值速度 vPeak = min(vMax, √(2·a·d·dist / (a+d)))

**公式**：

```
E1 = mTotal × (g·μ + a·η) × vPeak² / (2·a)   [J]
```

其中 `g·μ` 是摩擦力項，`a·η` 是傳動系統的等效慣性損失。

**推導過程**：

加速階段，施加力 F = m(g·μ + a·η)，走距離 d_accel = vPeak²/(2a)，
故 E1 = F × d_accel = m(g·μ + a·η) × vPeak²/(2a)。

---

### E2 — 減速能耗

**觸發點**：同 E1，在 `setNextWaypoint()` 同一次計算中得出。

**公式**：

```
E2 = max(0, mTotal × (d·η - g·μ) × vPeak² / (2·d))   [J]
```

**重要特性 — E2 在當前參數下恆為 0**：

條件 `d·η > g·μ` 即 `1.0 × 0.15 > 9.8 × 0.02 = 0.196`，不成立。

這意味著**制動時摩擦力已足以吸收所有動能**，電氣系統無需額外做功（被動制動假設）。若使用更高的 decel 值（>1.3 m/s²），E2 才會出現。

---

### E3 — 巡航能耗

**觸發點**：同 E1，在 `setNextWaypoint()` 同一次計算。

**公式**：

```
E3 = mTotal × g × μ × d_cruise   [J]
```

其中 d_cruise = max(0, distance − d_accel − d_decel) 為等速段距離。

短距離 segment（d_accel + d_decel > distance）不存在巡航段，E3 = 0。

---

### E4 — 原地旋轉能耗

**觸發點**：`setNextWaypoint()` 中，當 `_rotateDuration > 0`（需要轉向）時計算。

**重要**：使用 `mTotal`（含 Pod），因為 Pod 與底盤一起旋轉，慣性矩與摩擦力都受載重影響。

**公式**：

```
ω   = 2π / TurnSpeed   [rad/s]
I   = (1/12) × mTotal × (L² + W²)   [kg·m²]   — 均質矩形薄板繞重心轉軸

E4  = (1/2)·I·ω²  +  mTotal·g·μ·r·θ   [J]
      ─────────────    ────────────────
      旋轉動能（啟動）   旋轉過程摩擦耗功
```

**假設**：
- Pod 與底盤視為合體矩形，使用機器人的 L、W 作為等效尺寸（近似處理）。
- TurnSpeed 單位為 s/rev（完整一圈所需秒數），由 xinst 設定。
- θ（實際轉角，radians）由 `_rotateDuration / TurnSpeed × 2π` 推算。

---

### E5a — 抬起 Pod（Pickup）

**觸發點**：`BotPickupPod` 狀態執行成功後立即計算（`BotNormal.cs:1447`）。

**公式**：

```
E5a = mLoad × (g + 2h/t²) × h   [J]
```

其中 `t = bot.PodTransferTime`（Pod 抬升所需秒數，由 xinst 設定）。

**物理推導**：
假設均勻加速從靜止抬起高度 h，所需加速度 a_lift = 2h/t²，
則 E5a = F × h = mLoad(g + a_lift) × h。

---

### E5b — 放下 Pod（Setdown）

**觸發點**：`BotSetdownPod` 狀態執行成功後計算（`BotNormal.cs:1521`）。
注意：mLoad 需在 `SetdownPod()` 呼叫**之前**取得，因為放下後 `bot.Pod == null`。

**公式**：

```
E5b = max(0, mLoad × (g − 2h/t²) × h)   [J]
```

重力輔助下降，電氣系統只需提供制動力。若 `t` 非常小（快速下降），`2h/t²` 可能超過 `g`，此時理論上電氣需額外做功，但 Rizqi 模型以 `max(0, ...)` 截斷負值（不計入能耗）。

---

### E7 等效 — 衝突停止重啟加速能耗

**觸發點**：`setNextWaypoint()` 成功時，若前一次登記因 TC 停止讓行而失敗（`ConflictStopPending == true`），則將本次的 E1 額外累積到 `StatEnergyConflictStopGoJ`。

**語意**：這不是新的能耗，而是**標記**本次加速能耗是由於 TC 停讓所引發的「額外啟動成本」，用於比較 TC-ON 與 TC-OFF 模式下的衝突引發能耗差異。

**設置流程**：
```
setNextWaypoint() 登記失敗（TC block 或其他原因）
  → ConflictStopPending = true

下一次 setNextWaypoint() 登記成功
  → StatEnergyConflictStopGoJ += E1（本次加速能耗）
  → ConflictStopPending = false
```

---

## 五、TC-ON vs TC-OFF 能耗差異

| 項目 | TC-OFF | TC-ON |
|---|---|---|
| E1/E2/E3 計算方式 | 相同（upfront，登記 waypoint 時計算） | 相同 |
| E4 質量 | mTotal（已修正） | mTotal（已修正） |
| 衝突停讓 | 不發生 | 發生 → ConflictStopPending，多計 E1 |
| Bot 停止等待 | 不發生 | setNextWaypoint 登記失敗 → 等待重試 |
| E6 Station Hold | 不計算（已移除） | 不計算 |

**結論**：TC-ON 模式下，總能耗的主要差異來自於 **衝突停讓後的重新加速**（`StatEnergyConflictStopGoJ`），此部分代表交通管理帶來的額外能源成本。

---

## 六、計算流程總覽（程式碼層面）

```
setNextWaypoint(waypoint, currentTime)          [BotNormal.cs ~548]
  ├── RegisterNextWaypoint()
  │     └── TC: ShouldBlock() → return false → ConflictStopPending = true
  │
  ├── 登記成功 → 計算 E1/E2/E3（ComputeSegmentEnergy）
  │              計算 E4（若 _rotateDuration > 0，E4_Rotation with mTotal）
  │              若 ConflictStopPending → StatEnergyConflictStopGoJ += E1
  │
  └── 登記失敗 → _waitUntil += 0.05s（retry），ConflictStopPending = true

BotPickupPod.Act()                              [BotNormal.cs ~1447]
  └── 抬起成功 → E5a_LiftPod(mLoad, podTransferTime)

BotSetdownPod.Act()                             [BotNormal.cs ~1521]
  └── 放下成功 → E5b_LowerPod(mLoad, podTransferTime)
```

---

## 七、輸出欄位對應（statistics.txt / pathfinding.csv）

| C# 欄位 | 說明 |
|---|---|
| `StatEnergyE1AccelJ` | 所有加速段電氣能耗總和 |
| `StatEnergyE2DecelJ` | 制動能耗（預期恆為 0） |
| `StatEnergyE3CruiseJ` | 巡航滾動摩擦耗功 |
| `StatEnergyE4RotationJ` | 原地轉向能耗（含 Pod 質量） |
| `StatEnergyE5LiftLowerJ` | 抬起 + 放下 Pod 合計 |
| `StatEnergyConflictStopGoJ` | TC 停讓引發的重啟加速能耗 |
| `StatEnergyTotalJ` | 以上全部加總 |
| `StatDistanceTraveledM` | 總行駛距離 [m] |
