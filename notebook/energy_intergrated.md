---                                                                                                                                                           
  RAWSimO-RMFS 能量消耗計算技術報告                                                                                                                                                                                                                                                                                             
  版本： 1.0　日期： 2026-03-28　作者： 莊凱翔（Aesop）                                                                                                         

  ---
  1. 概述

  本系統實作 Rizqi et al. AGV 能耗模型，整合於 RAWSim-O 離散事件模擬框架。能耗計算採用事件驅動 + 解析積分架構，在機器人狀態機轉換時點計算能量，而非逐 tick      
  近似，確保與 RAWSim-O 連續時間模型一致。

  核心實作分布於以下兩個檔案：

  ┌───────────────────────────────────────────┬────────────────────────────────────────┐
  │                   檔案                    │                  職責                  │
  ├───────────────────────────────────────────┼────────────────────────────────────────┤
  │ RAWSimO.Core/Metrics/EnergyConsumption.cs │ 純靜態公式庫，所有方法為 pure function │
  ├───────────────────────────────────────────┼────────────────────────────────────────┤
  │ RAWSimO.Core/Bots/BotNormal.cs            │ 能量累計器與事件 Hook                  │
  └───────────────────────────────────────────┴────────────────────────────────────────┘

  ---
  2. 物理常數

  ROBOT_MASS   = 300.0  kg    AGV 空載質量
  GRAVITY      = 9.8   m/s²  重力加速度
  FRICTION     = 0.02         滾動摩擦係數（直線行駛）
  INERTIA      = 0.15         慣量等效係數（含傳動損耗）
  LIFT_COEF    = 0.2          托架支撐摩擦係數（Station 停靠）
  ROBOT_WIDTH  = 0.6   m
  ROBOT_LENGTH = 0.75  m
  ROBOT_RADIUS = 0.3   m      轉彎半徑 = width / 2
  LIFT_HEIGHT  = 0.2   m      Pod 升降高度

  動態總質量為：

  m_total = ROBOT_MASS + m_L
  m_L = pod.CapacityInUse  （貨物重量，單位 kg）

  ---
  3. 能耗組成與公式

  3.1 E1 — 加速階段能耗

  觸發時機： setNextWaypoint() 預約成功時

  物理意涵： 機器人從靜止加速到峰值速度 v_peak，克服摩擦力與克服慣量所做的功。

  公式：

  E1 = m_total × (g×μ_r + a×η) × v_peak² / (2a)

  其中：
    a     = Physics.Acceleration（有效加速度，m/s²）
    v_peak = min(vMax, sqrt(2ad·distance/(a+d)))

  推導來源：
  E1 = ∫₀^t_a  F(t) × v(t) dt
     = ∫₀^t_a  m×(g×μ_r + a×η) × (a×t) dt
     = m×(g×μ_r + a×η) × v_peak² / (2a)

  ---
  3.2 E2 — 減速階段能耗

  觸發時機： 同 E1，同一次 setNextWaypoint() 呼叫中計算

  物理意涵： 制動期間，慣量力超過摩擦力的部分仍需耗能（摩擦力可輔助制動，不另消耗）。

  公式：

  net_coeff = d×η − g×μ_r

  若 net_coeff > 0：
    E2 = m_total × net_coeff × v_peak² / (2d)
  若 net_coeff ≤ 0：
    E2 = 0  （摩擦力足以制動，不需額外耗能）

  本次模擬結果： StatEnergyE2DecelKJ = 0，因為預設參數下 d×0.15 < 9.8×0.02 = 0.196，即減速度需 > 1.307 m/s² 才會出現 E2，符合物理預期。

  ---
  3.3 E3 — 等速巡航階段能耗

  觸發時機： 同 E1/E2，在 ComputeSegmentEnergy() 中一次計算

  物理意涵： 維持等速行駛只需克服滾動摩擦。

  公式：

  t_cruise = d_cruise / v_peak
  E3 = m_total × g × μ_r × v_peak × t_cruise
     = m_total × g × μ_r × d_cruise

  ---
  3.4 三相分解邏輯

  ComputeSegmentEnergy() 首先判斷路段是否足以達到 vMax：

  d_accel = vMax² / (2a)   加速需要的距離
  d_decel = vMax² / (2d)   減速需要的距離

  若 d_accel + d_decel ≤ distance：
      三相模式：v_peak = vMax，d_cruise = distance − d_accel − d_decel
  否則：
      兩相模式：v_peak = sqrt(2ad·distance/(a+d))，d_cruise = 0

  ---
  3.5 E4 — 原地旋轉能耗

  觸發時機： setNextWaypoint() 中，當 _rotateDuration > 0 時

  物理意涵： 機器人轉向需要克服轉動慣量和腳輪摩擦。

  公式：

  ω = 2π / TurnSpeed                         角速度（TurnSpeed = 完整一圈所需秒數）
  I = (1/6) × ROBOT_MASS × (l² + w²)        矩形物體轉動慣量

  E4 = I×ω² + ROBOT_MASS×g×μ_r×ROBOT_RADIUS×θ

  其中：
    θ = (_rotateDuration / TurnSpeed) × 2π   實際轉過的弧度

  注意： E4 僅使用 ROBOT_MASS（不含 Pod 質量），假設旋轉時 Pod 對轉動慣量貢獻可忽略。

  ---
  3.6 E5a — Pod 舉升能耗

  觸發時機： BotPickupPod.Act() 中，PickupPod() 成功後

  物理意涵： 將 Pod 從地面舉升 LIFT_HEIGHT，包含重力位能與加速項。

  公式：

  E5a = m_L × (g + 2h / t_lift²) × h

  其中：
    m_L         = pod.CapacityInUse（貨物重量）
    h           = LIFT_HEIGHT = 0.2 m
    t_lift      = bot.PodTransferTime（舉升作業時間）

  ---
  3.7 E5b — Pod 降落能耗

  觸發時機： BotSetdownPod.Act() 中，SetdownPod() 成功後（注意：m_L 需在 SetdownPod() 呼叫前預先擷取，避免 bot.Pod 被歸零）

  物理意涵： 降落過程部分位能回收，但仍需克服動力制動。

  公式：

  E5b = max(0,  m_L × (g − 2h / t_lift²) × h )

  若降落足夠慢（t_lift 大），重力項主導，結果趨近於 0（重力做功抵消）。

  ---
  3.8 E6 — Station 停靠支撐能耗

  觸發時機： DequeueState() 中，當離開 BotPutItems 或 BotGetItems 狀態時結算

  物理意涵： 機器人在揀貨站/補貨站長時間舉著 Pod，托架持續承受 Pod 重量造成的摩擦耗能。

  公式：

  E6 = m_L × g × μ_lift × Δt_station

  其中：
    μ_lift        = LIFT_COEF = 0.2
    Δt_station    = currentTime − StationEnterTime

  計時機制：
  - 進入 BotGetItems.Act() 初始化時記錄 StationEnterTime = currentTime
  - 進入 BotPutItems.Act() 初始化時同樣記錄
  - 離開狀態時在 DequeueState() 計算 Δt 並結算 E6
  - 若 AssignTask() 中途中斷任務，亦會觸發強制結算

  本次模擬結果： E6 = 89,706 kJ，佔總能耗 76.7%，機器人大量時間停在 Station 支撐 Pod 是主要瓶頸。

  ---
  3.9 E7 — 衝突停等再加速能耗

  觸發時機： setNextWaypoint() 預約失敗 → 設定 ConflictStopPending = true；下次預約成功時，將該次 E1 計入 E7。

  物理意涵： 衝突導致機器人額外等待後重新加速，這份 E1 被標記為「衝突誘發」耗能。

  實作邏輯：

  // 預約失敗
  if (!RegisterNextWaypoint(...))
      ConflictStopPending = true;

  // 下次預約成功
  if (ConflictStopPending) {
      StatEnergyConflictStopGoJ += e1;   // 標記這次加速是衝突後的重啟
      ConflictStopPending = false;
  }

  說明： RAWSim-O 的機器人在 Waypoint 之間始終從 v=0 出發，不存在緊急剎車，因此衝突只影響等待時間，不增加額外制動能耗，只增加一次重啟加速能耗（E1）。

  ---
  4. RAWSim-O 狀態機與 Hook 對應

  Bot 狀態機：

  [Idle]
    │
    ▼ AssignTask()
  [BotMove] ──────────────────── setNextWaypoint() 成功 ──→ 計算 E1+E2+E3+E4
    │                              setNextWaypoint() 失敗 ──→ ConflictStopPending=true
    ▼ 到達目標 Waypoint
  [BotPickupPod] ──────────────── PickupPod() 成功 ────────→ 計算 E5a
    │
    ▼
  [BotMove]（帶 Pod 行駛，m_total 增加）
    │
    ├──→ [BotGetItems]（補貨站）── 初始化記錄 StationEnterTime
    │         │                    離開時計算 E6
    │         ▼
    │    [BotMove]（返回）
    │
    └──→ [BotPutItems]（揀貨站）── 初始化記錄 StationEnterTime
              │                    離開時計算 E6 + StatOrdersCompleted++
              ▼
         [BotSetdownPod] ───────── SetdownPod() 成功 ────────→ 計算 E5b
              │
              ▼
           [Idle]

  ---
  5. 能量累計欄位（per-bot）

  ┌───────────────────────────┬──────────┬──────────────────────┐
  │           欄位            │   型別   │         說明         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyTotalJ          │ double   │ 所有分項加總 [J]     │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE1AccelJ        │ double   │ 加速能耗 [J]         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE2DecelJ        │ double   │ 減速能耗 [J]         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE3CruiseJ       │ double   │ 巡航能耗 [J]         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE4RotationJ     │ double   │ 旋轉能耗 [J]         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE5LiftLowerJ    │ double   │ 升降能耗 [J]         │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyE6StationJ      │ double   │ Station 停靠能耗 [J] │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatEnergyConflictStopGoJ │ double   │ 衝突重啟加速能耗 [J] │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatOrdersCompleted       │ int      │ 完成訂單數           │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ StatDistanceTraveledM     │ double   │ 累計行駛距離 [m]     │
  ├───────────────────────────┼──────────┼──────────────────────┤
  │ CurrentTotalMassKg        │ property │ 即時動態總質量 [kg]  │
  └───────────────────────────┴──────────┴──────────────────────┘

  ---
  6. 輸出指標（CLI headless 模式）

  模擬結束後 controllog.txt 輸出：

  >>> Energy (Rizqi model)
  StatEnergyTotalKJ:        艦隊總能耗 [kJ]
  StatEnergyE1AccelKJ:      加速能耗
  StatEnergyE2DecelKJ:      減速能耗
  StatEnergyE3CruiseKJ:     巡航能耗
  StatEnergyE4RotationKJ:   旋轉能耗
  StatEnergyE5LiftLowerKJ:  升降能耗
  StatEnergyE6StationKJ:    Station 停靠能耗
  StatEnergyConflictKJ:     衝突重啟能耗
  StatEnergyPerOrderKJ:     每訂單平均能耗 [kJ/order]
  StatEnergyPerMeterJoule:  每公尺能耗 [J/m]
  StatDistanceTraveledRizqiM: 行駛距離（Rizqi tracker）[m]

  baseline 參考值（8小時，30機器人，jenkinssetting）：

  ┌────────────┬───────────────┐
  │    指標    │     數值      │
  ├────────────┼───────────────┤
  │ 總能耗     │ 116,968 kJ    │
  ├────────────┼───────────────┤
  │ 每訂單能耗 │ 15.0 kJ/order │
  ├────────────┼───────────────┤
  │ 每公尺能耗 │ 637.8 J/m     │
  ├────────────┼───────────────┤
  │ E6 佔比    │ 76.7%         │
  └────────────┴───────────────┘

  ---
  7. 已知限制

  ┌───────────────────┬──────────────────────────────────────────────────────────────────────┐
  │       項目        │                                 說明                                 │
  ├───────────────────┼──────────────────────────────────────────────────────────────────────┤
  │ E2=0              │ 預設減速度不足以觸發慣量制動功，可調整 INERTIA 或減速度參數          │
  ├───────────────────┼──────────────────────────────────────────────────────────────────────┤
  │ E4 不含 Pod 質量  │ 旋轉慣量計算僅用 ROBOT_MASS，Pod 對轉動慣量的貢獻未建模              │
  ├───────────────────┼──────────────────────────────────────────────────────────────────────┤
  │ E5 需 mL > 0      │ 空 Pod 或 Pod 無貨物時 E5=0，設計上正確但需確認 CapacityInUse 初始化 │
  ├───────────────────┼──────────────────────────────────────────────────────────────────────┤
  │ E6 不計 idle 停靠 │ 僅在 PutItems/GetItems 狀態計時，Bot 因其他原因停在 Station 不計入   │
  ├───────────────────┼──────────────────────────────────────────────────────────────────────┤
  │ 無電池容量模型    │ 不模擬電量耗盡或充電行為                                             │
  └───────────────────┴──────────────────────────────────────────────────────────────────────┘

  ---
  8. 論文應用重點

  - Baseline KPI： StatEnergyPerOrderKJ = 15.0 kJ/order，MAPPO 策略目標是降低此值
  - 主要優化空間： E6 佔 76.7%，降低 Station 等待時間（走廊路由優化）可顯著節能
  - Reward 訊號： 可使用 StatEnergyTotalJ 增量作為負向獎勵（−γ × ΔE）
  - 觀測空間： CurrentTotalMassKg 反映載重狀態，影響路段能耗，可納入 observation vector