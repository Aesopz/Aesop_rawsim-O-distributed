# NN Cost Oracle for M1G — 設計規格書

**日期**：2026-05-08
**作者**：Aesop（莊凱翔）
**狀態**：Design draft，待實作

---

## 1. 研究目標與定位

### 1.1 一句話摘要

於 EE-RAWSim-O\_PP 中設計並訓練一個 **WHCA\*-consistent learning-based cost oracle**，
以神經網路（NN）預測每個 robot–pod–station 候選任務的 **arrival time** 與 **routing energy**，
取代 M1GTManager 目前 MILP 目標函數中使用的靜態 A\* 距離成本，
使上層 OA–PS–TA 聯合決策能間接考慮多機器人路徑規劃層的壅塞、等待、繞路與能耗效應。

### 1.2 研究紅線

| 紅線 | 說明 |
|------|------|
| NN ≠ 決策器 | NN 只產出 MILP 成本係數，不直接決定分配 |
| 非完整最佳化 | 不宣稱解決完整 OA–PS–TA–PP 全域最優 |
| 非全域最優 | 目標是改善 M1G 中距離成本與實際執行成本不一致的問題 |
| Policy consistency | NN 訓練 label 來自 WHCA\*，部署時 PP 也是 WHCA\*，保持一致 |

---

## 2. 現有代碼基礎

本設計建立在 EE-RAWSim-O\_PP 的既有架構上，下列檔案是整合與修改的關鍵：

### 2.1 M1GTManager.cs（核心整合點）

**路徑**：`RAWSimO.Core/Control/Defaults/OrderBatching/M1GTManager.cs`

**關鍵方法**：

| Line | 方法 | 角色 |
|------|------|------|
| 135–150 | `EstimateBotPodDistance(Bot bot, Pod pod)` | 回傳 robot 到 pod 的 A\* 距離（公尺）。**這是要被 NN 取代的函數之一** |
| 152–170 | `EstimatePodStationDistance(Pod pod, OutputStation station)` | 回傳 pod 到 station queue 邊界的 A\* 距離。**另一個被取代目標** |
| 991–999 | `wrapper.SetObjective(...)` | MILP 目標函數，呼叫上述兩個 Estimate 函數作為決策變數 `xps`、`yrp` 的係數 |
| 1264 | `DecideAboutPendingOrders()` | M1GT 決策入口，每次 workstation capacity ≥ δ 時觸發 |

**目前 MILP 目標函數結構**（line 991–999）：

```csharp
wrapper.SetObjective(
    LinearExpression.Sum( deVarNamexps... * EstimatePodStationDistance(v.pod, v.outputstation) )
  + LinearExpression.Sum( deVarNameyrp... * EstimateBotPodDistance(v.robot, v.pod) ) * w1
  + LinearExpression.Sum( deVarNameyos... ) * w2
  + LinearExpression.Sum( deVarNameus... ) * w3,
  OptimizationSense.Minimize);
```

`xps_p_w` 為 pod p 分配給 station w 的 binary 變數；
`yrp_r_p` 為 robot r 取 pod p 的 binary 變數。

**重點限制**：原成本是 **leg1（bot→pod）與 leg2（pod→station）分開**的兩個係數。
NN 預測的 `T̂_rpw` 是三元組總時間，無法直接拆成 leg1 + leg2。
因此需要新增三元組 linking variable `q_rpw`（見第 4.2 節）。

### 2.2 M1GPathTrace.cs（訓練資料天然來源）

**路徑**：`RAWSimO.Core/Metrics/M1GPathTrace.cs`

每次 M1GT 決策同時記錄 **estimated** 與 **actual** 兩組數值，自動寫入 CSV：

| 欄位 | 來源 | 用途 |
|------|------|------|
| `EstimatedTravelTime` | `CalculateIdealTravelTime`（A\* + 物理計算下界） | 距離法的時間估算 |
| `EstimatedDistance` | A\* 路徑長度 | 距離法 baseline |
| `ActualDuration` | `TripRecord.DurationSec`（WHCA\* 真實執行） | **NN 訓練 label** |
| `ActualEnergy` | `TripRecord.EnergyJ` | **NN 訓練 label**（能耗版） |
| `ActualWaitTime` | `TripRecord.WaitTimeSec` | 輔助分析特徵 |
| `ActualTurnCount` | `TripRecord.TurnCount` | 輔助分析特徵 |
| `BotId/PodId/StationId/SegmentType` | 決策當下記錄 | 對齊 query 與 label 的 key |

**輸出檔**：`m1g_path_estimate_actual_segments.csv`

**關鍵觀察**：每筆紀錄是 **per-segment**（BotToPod 與 PodToStation 兩段分開），
我們需要在處理時聚合成 (bot, pod, station) 三元組的 total time。

### 2.3 BotNormal.cs（執行時狀態與物理特性）

**路徑**：`RAWSimO.Core/Bots/BotNormal.cs`

| 屬性 / 方法 | 用途 |
|-------------|------|
| `X, Y, Orientation` | bot 即時座標與朝向 |
| `Pod` | 當前載運的 pod（null = 空車） |
| `CurrentTask` | 當前任務型別（ExtractTask, ParkPodTask, RestTask, DummyTask） |
| `StatEnergyTotalJ` | 累計能耗 |
| `StatDistanceTraveledM` | 累計距離 |
| `Physics.getTimeNeededToTurn` | 物理引擎轉向時間計算 |
| `TripRecord` struct | 每段任務的 metric 容器 |

### 2.4 BotStateType.cs

`BotStateType` enum 定義八種狀態：`PickupPod, SetdownPod, GetItems, PutItems, Rest, Move, Evade, UseElevator`。

NN 輸入會將此 enum encode 為 one-hot（8 維）。

### 2.5 BalancedBotManager（TA 派工的副路徑）

**路徑**：`RAWSimO.Core/Control/BotManagerPodSelection.cs:1147`（`DoExtractTaskForStation1`）

當 M1GT 在 Ra=0 時只更新 `_Ziops` 任務池，後續閒置 bot 由 BalancedBotManager 從 `_Ziops` 自行配對。
**第一階段不修改此路徑**，僅替換 M1GT 在 Ra>0 時的 MILP 成本。第二階段再評估是否擴展。

### 2.6 M1GTConfiguration

**路徑**：`RAWSimO.Core/Configurations/MethodConfigurationsOB.cs:318`

繼承自 `M1GConfiguration`。本設計新增 `NNCostConfiguration` 子設定（見第 5 節）。

---

## 3. 整體架構

### 3.1 系統圖

```
┌──────────────────────────── 模擬端（C#） ─────────────────────────────┐
│                                                                       │
│  M1GT 決策觸發                                                        │
│      │                                                                │
│      ▼                                                                │
│  ┌──────────────────┐    ┌────────────────────┐                       │
│  │ NNCostProvider   │───▶│ FeatureBuilder     │                       │
│  │ (新增模組)       │    │ 從 Instance 取狀態 │                       │
│  └────────┬─────────┘    └────────────────────┘                       │
│           │                                                           │
│           │ ZeroMQ REQ-REP（本機 loopback，<1ms 延遲）                │
│           ▼                                                           │
└───────────┼───────────────────────────────────────────────────────────┘
            │
            │ Protobuf or JSON：{snapshot, queries: [(r,p,w), ...]}
            ▼
┌────────────────────────── NN 服務（Python） ──────────────────────────┐
│                                                                       │
│  ┌───────────────┐    ┌──────────┐    ┌────────────────────┐          │
│  │ FastAPI /     │───▶│ Feature  │───▶│ MLP (PyTorch)      │          │
│  │ ZeroMQ server │    │ tensor   │    │ → [T̂, Ê] per query │          │
│  └───────────────┘    └──────────┘    └─────────┬──────────┘          │
│                                                 │                     │
└─────────────────────────────────────────────────┼─────────────────────┘
                                                  │
                                                  │ {predictions: [...]}
                                                  ▼
            回到 C#：填入 Dictionary<(r,p,w), (T̂, Ê)>
                       │
                       ▼
            M1GT.solve() 用 q_rpw 變數 + T̂_rpw 為係數構建 MILP
                       │
                       ▼
                 Gurobi 求解
                       │
                       ▼
            分配結果寫回 _Ziops / BottoPod
                       │
                       ▼
            WHCA* 規劃路徑 → bot 執行 → M1GPathTrace 記錄 actual
                       │
                       ▼
            訓練資料 CSV（用於下一輪離線訓練）
```

### 3.2 模組職責劃分

| 模組 | 語言 | 職責 | 新增/修改 |
|------|------|------|-----------|
| `FeatureBuilder` | C# | 從 `Instance` 抽取 snapshot + 候選 query 特徵 | **新增** |
| `NNCostProvider` | C# | 透過 IPC 呼叫 Python 服務並回傳預測值 | **新增** |
| `M1GTManager.solve` | C# | 使用 NN 預測值構建 MILP 目標函數，加入 q_rpw 變數 | **修改** |
| `M1GTConfiguration` | C# | 新增 NN 開關與 endpoint 設定 | **修改** |
| `nn_server` | Python | 接收 query、推論、回傳 | **新增** |
| `train_nn.py` | Python | 從 `m1g_path_estimate_actual_segments.csv` 訓練模型 | **新增** |
| `data_collector.py` | Python | 讀取 RAWSim-O 輸出 CSV，整理為訓練資料 | **新增** |

---

## 4. 數學模型擴充

### 4.1 原 M1G 變數回顧

| 變數 | 意義 |
|------|------|
| `xps_{p,w}` | pod p 是否分配給 station w（binary）|
| `yrp_{r,p}` | robot r 是否負責取 pod p（binary）|
| `yos_{o,w}` | order o 是否分配給 station w |
| `yaos_{o,w}` | order o 是否確定指派給 station w |
| `us_{w}` | station w 的未利用容量 |

### 4.2 新增三元組 linking variable

```
q_{r,p,w} ∈ {0,1}    iff robot r 取 pod p 並送至 station w
```

**線性化約束**（加入 `solve()` 方法的約束區塊）：

```
q_{r,p,w} ≤ yrp_{r,p}
q_{r,p,w} ≤ xps_{p,w}
q_{r,p,w} ≥ yrp_{r,p} + xps_{p,w} - 1
```

### 4.3 三種目標函數版本

#### M1G-D（baseline，不修改）

```
min  w1 · [ Σ xps_{p,w} · d(p,w) + Σ yrp_{r,p} · d(r,p) ]
   + w2 · Σ yos_{o,w}
   + w3 · Σ us_{w}
```

#### M1G-NN-T（時間版）

```
min  w1 · Σ q_{r,p,w} · T̂_{r,p,w}
   + w2 · Σ yos_{o,w}
   + w3 · Σ us_{w}
```

#### M1G-NN-TE（時間 + 能耗版）

```
min  w1 · Σ q_{r,p,w} · T̂_{r,p,w}
   + w_E · Σ q_{r,p,w} · Ê_{r,p,w}
   + w2 · Σ yos_{o,w}
   + w3 · Σ us_{w}
```

`w_E` 為能耗權重，初值由 `T̂` 與 `Ê` 的數值範圍正規化後設定（建議 `w_E = mean(T̂) / mean(Ê)`）。

### 4.4 變數規模分析

| 場景 | \|R_a\| | \|P_a\| | \|W\| | q 變數數量 |
|------|---------|---------|-------|-----------|
| 小規模 | 6–12 | ~50 | 2 | ~1200 |
| 大規模 | 60–120 | ~600 | 4–12 | ~720,000 |

大規模情境下 q 變數爆炸，**必須做候選剪枝**：
- 只對 (r,p) 距離小於 threshold 的組合建立 q 變數
- 只對 pod 可滿足的 (p,w) 組合建立 q 變數
- 由 NN 服務批次預測時也只送被啟用的候選

---

## 5. C# 端實作細節

### 5.1 新增配置：`NNCostConfiguration`

加入 `MethodConfigurationsOB.cs`，附加在 `M1GTConfiguration` 內：

```csharp
public class NNCostConfiguration
{
    public bool UseNNCost = false;            // 主開關
    public string Endpoint = "tcp://127.0.0.1:5557";
    public int RequestTimeoutMs = 2000;
    public bool UseEnergy = false;            // false = M1G-NN-T, true = M1G-NN-TE
    public double EnergyWeight = 0.0;         // w_E
    public string FallbackOnError = "distance"; // "distance" or "abort"
    public int CandidatePruneTopK = 0;        // 0 = 不剪枝；>0 = 每 (p,w) 只留 top-K bots
}
```

**M1GTConfiguration** 新增欄位：

```csharp
public NNCostConfiguration NNCost = new NNCostConfiguration();
```

### 5.2 FeatureBuilder（新增）

**路徑**：`RAWSimO.Core/Control/Defaults/OrderBatching/NNCost/FeatureBuilder.cs`

職責：從 `Instance` 抽取**全域 snapshot** 與**每個 query 的特徵**。

```csharp
public class FeatureBuilder
{
    private readonly Instance _instance;

    public WarehouseSnapshot BuildSnapshot(double currentTime)
    {
        var bots = _instance.Bots.Select(b => new BotFeature {
            Id = b.ID,
            X = b.X, Y = b.Y,
            Orientation = (b as BotNormal)?.Orientation ?? 0,
            Velocity = (b as BotNormal)?.GetCurrentVelocity() ?? 0,
            HasPod = b.Pod != null,
            Weight = b.Pod != null ? (b.Weight + b.Pod.Weight) : b.Weight,
            TaskType = EncodeTaskType(b.CurrentTask),
            BotState = EncodeBotState(b.StateQueueCount > 0 ? b.GetState() : null),
            Energy = (b as BotNormal)?.StatEnergyTotalJ ?? 0,
            ElapsedInTask = currentTime - (b as BotNormal)?.GetTaskStartTime() ?? 0,
            DestX = b.GetInfoDestinationWaypoint()?.X ?? double.NaN,
            DestY = b.GetInfoDestinationWaypoint()?.Y ?? double.NaN,
        }).ToList();

        var stations = _instance.OutputStations.Select(s => new StationFeature {
            Id = s.ID,
            QueueLen = CountQueuedBots(s),
            AvailCap = s.Capacity - s.AssignedOrders.Count(),
            PodsEnRoute = CountPodsEnRoute(s),
        }).ToList();

        return new WarehouseSnapshot {
            Time = currentTime,
            Bots = bots,
            Stations = stations,
        };
    }

    public QueryFeature BuildQuery(Bot r, Pod p, OutputStation w)
    {
        var botWp = GetBotReferenceWaypoint(r);
        var podWp = GetPodReferenceWaypoint(p);
        var stWp = GetStationQueueBoundaryWaypoint(p, w);

        double dBotPod = Distances.CalculateShortestPath(botWp, podWp, _instance);
        double dPodSt = Distances.CalculateShortestPathPodSafe1(podWp, stWp, _instance);
        int turnsLeg1 = CountTurnsAStar(botWp, podWp, false);
        int turnsLeg2 = CountTurnsAStar(podWp, stWp, true);
        double manhDistLeg1 = Math.Abs(botWp.X - podWp.X) + Math.Abs(botWp.Y - podWp.Y);
        double detourLeg1 = manhDistLeg1 > 0 ? dBotPod / manhDistLeg1 : 1.0;
        // ... leg2 同理

        return new QueryFeature {
            BotId = r.ID, PodId = p.ID, StationId = w.ID,
            DBotPod = dBotPod,
            DPodStation = dPodSt,
            TurnsLeg1 = turnsLeg1,
            TurnsLeg2 = turnsLeg2,
            DetourRatioLeg1 = detourLeg1,
            // 動態壅塞特徵
            DensityNearBot = CountBotsWithinRadius(botWp, RADIUS),
            DensityNearPod = CountBotsWithinRadius(podWp, RADIUS),
            DensityNearStation = CountBotsWithinRadius(stWp, RADIUS),
            BotsToSameStation = CountBotsTargeting(w),
            BotsTargetingPod = CountBotsTargetingPod(p),
        };
    }
}
```

### 5.3 NNCostProvider（新增）

**路徑**：`RAWSimO.Core/Control/Defaults/OrderBatching/NNCost/NNCostProvider.cs`

```csharp
public class NNCostProvider : IDisposable
{
    private readonly NNCostConfiguration _config;
    private readonly RequestSocket _socket;  // ZeroMQ REQ socket
    private readonly Dictionary<(int,int,int), (double T, double E)> _cache;

    public NNCostProvider(NNCostConfiguration config) {
        _config = config;
        if (_config.UseNNCost) {
            _socket = new RequestSocket();
            _socket.Connect(_config.Endpoint);
            _socket.Options.Linger = TimeSpan.Zero;
        }
        _cache = new Dictionary<(int,int,int), (double, double)>();
    }

    public bool TryGetCosts(WarehouseSnapshot snapshot, List<QueryFeature> queries,
                            out Dictionary<(int,int,int), (double T, double E)> result)
    {
        result = new Dictionary<(int,int,int), (double, double)>();
        if (!_config.UseNNCost) return false;

        try {
            string payload = JsonSerializer.Serialize(new {
                snapshot = snapshot,
                queries = queries
            });
            _socket.SendFrame(payload);
            if (!_socket.TryReceiveFrameString(TimeSpan.FromMilliseconds(_config.RequestTimeoutMs),
                                               out string response))
                return false;

            var preds = JsonSerializer.Deserialize<NNResponse>(response);
            foreach (var pred in preds.Predictions) {
                result[(pred.BotId, pred.PodId, pred.StationId)] = (pred.T, pred.E);
            }
            return true;
        }
        catch (Exception ex) {
            // 視 FallbackOnError 處理
            return false;
        }
    }
}
```

**注意**：使用 ZeroMQ（NetMQ NuGet）而非 gRPC，原因：
1. 部署只需單檔 Python 腳本，無需 protobuf 編譯流程
2. 本機 loopback 延遲 < 1ms
3. 已有成熟 .NET 4.x 相容版本

### 5.4 修改 M1GTManager.solve()

在 `solve()` 方法開頭加入 NN 成本獲取：

```csharp
public Dictionary<Symbol, int> solve(...)
{
    LinearModel wrapper = new LinearModel(...);
    // ... 既有變數宣告 ...

    // ========== 新增：取得 NN 成本 ==========
    Dictionary<(int,int,int), (double T, double E)> nnCosts = null;
    bool useNN = _config.NNCost.UseNNCost;
    if (useNN) {
        var snapshot = _featureBuilder.BuildSnapshot(Instance.Controller.CurrentTime);
        var queries = BuildAllCandidateQueries(Ra, Pa, Cs.Keys);  // 視剪枝策略
        if (!_nnProvider.TryGetCosts(snapshot, queries, out nnCosts)) {
            useNN = false;  // fallback to distance
        }
    }

    // ========== 新增：q_rpw 變數宣告 ==========
    var qVarNames = new List<(Bot r, Pod p, OutputStation w, string name)>();
    if (useNN) {
        foreach (var r in Ra)
            foreach (var p in Pa)
                foreach (var w in Cs.Keys) {
                    if (!nnCosts.ContainsKey((r.ID, p.ID, w.ID))) continue;
                    string qName = $"q_{r.ID}_{p.ID}_{w.ID}";
                    qVarNames.Add((r, p, w, qName));
                }
    }

    // ========== 修改：目標函數 ==========
    if (useNN) {
        var transportCost = LinearExpression.Sum(
            qVarNames.Select(q => variablesBinary[q.name] * nnCosts[(q.r.ID, q.p.ID, q.w.ID)].T),
            wrapper);
        if (_config.NNCost.UseEnergy) {
            transportCost = transportCost + LinearExpression.Sum(
                qVarNames.Select(q => variablesBinary[q.name] * nnCosts[(q.r.ID, q.p.ID, q.w.ID)].E),
                wrapper) * _config.NNCost.EnergyWeight;
        }
        wrapper.SetObjective(
            transportCost * w1
          + LinearExpression.Sum(deVarNameyos.Select(v => variablesBinary[v.name])) * w2
          + LinearExpression.Sum(deVarNameus.Select(v => variablesInteger3[v.name])) * w3,
          OptimizationSense.Minimize);
    } else {
        // 原 distance-based 目標函數（line 992-999）保留
    }

    // ========== 新增：q 變數線性化約束 ==========
    foreach (var q in qVarNames) {
        wrapper.AddConstr(variablesBinary[q.name]
            <= variablesBinary[$"yrp_{q.r.ID}_{q.p.ID}"], "qlin1");
        wrapper.AddConstr(variablesBinary[q.name]
            <= variablesBinary[$"xps_{q.p.ID}_{q.w.ID}"], "qlin2");
        wrapper.AddConstr(variablesBinary[q.name]
            >= variablesBinary[$"yrp_{q.r.ID}_{q.p.ID}"]
            + variablesBinary[$"xps_{q.p.ID}_{q.w.ID}"] - 1, "qlin3");
    }

    // ... 原有約束保留 ...
}
```

### 5.5 候選剪枝（大規模場景必要）

```csharp
private List<QueryFeature> BuildAllCandidateQueries(
    HashSet<Bot> Ra, HashSet<Pod> Pa, IEnumerable<OutputStation> stations)
{
    var queries = new List<QueryFeature>();
    int topK = _config.NNCost.CandidatePruneTopK;
    foreach (var p in Pa) {
        foreach (var w in stations) {
            // 對每 (p,w)，只留距離最近的 top-K bots
            var candidateBots = topK > 0
                ? Ra.OrderBy(r => EstimateBotPodDistance(r, p)).Take(topK)
                : Ra;
            foreach (var r in candidateBots)
                queries.Add(_featureBuilder.BuildQuery(r, p, w));
        }
    }
    return queries;
}
```

剪枝後，未進入 NN 的 (r,p,w) 組合自動不出現在 q 變數，等價於 cost = +∞，
不會被 MILP 選中。建議 `CandidatePruneTopK = 5` for 大規模。

---

## 6. Python NN 服務實作細節

### 6.1 目錄結構

```
ml/                                 # 新增於 EE-RAWSim-O_PP 根目錄
├── nn_server.py                    # ZeroMQ REP 服務主程式
├── model.py                        # PyTorch MLP 定義
├── feature_spec.py                 # 特徵 schema 與正規化常數
├── data_collector.py               # 從 m1g_path_*.csv 整理訓練資料
├── train_nn.py                     # 訓練腳本
├── eval_nn.py                      # 驗證腳本（MAE / RMSE / ranking）
├── checkpoints/                    # 訓練輸出
└── requirements.txt
```

### 6.2 Feature Schema（`feature_spec.py`）

```python
QUERY_FEATURES = [
    # 路徑幾何
    "d_bot_pod",          # A* 距離 leg1（公尺）
    "d_pod_station",      # A* 距離 leg2（公尺）
    "turns_leg1",         # 估算轉彎次數 leg1
    "turns_leg2",
    "detour_ratio_leg1",  # A* / Manhattan
    "detour_ratio_leg2",
    # bot 自身狀態
    "bot_x", "bot_y",
    "bot_orientation",
    "bot_velocity",
    "bot_weight",
    "bot_energy",
    "bot_elapsed_in_task",
    # 任務狀態（one-hot 8 維 + task_type 4 維）
    "bot_state_pickup", "bot_state_setdown", "bot_state_get",
    "bot_state_put", "bot_state_rest", "bot_state_move",
    "bot_state_evade", "bot_state_elevator",
    "task_extract", "task_park", "task_rest", "task_dummy",
    # 壅塞特徵
    "density_near_bot",
    "density_near_pod",
    "density_near_station",
    "bots_to_same_station",
    "bots_targeting_pod",
    # Station 狀態
    "station_queue_len",
    "station_avail_cap",
    "station_pods_en_route",
]

# 共 32 維輸入向量

LABELS = ["arrival_time", "energy"]
```

每個欄位附帶 mean / std，存於 `feature_spec.json`，訓練後固定。

### 6.3 Model（`model.py`）

```python
import torch
import torch.nn as nn

class CostOracleMLP(nn.Module):
    def __init__(self, input_dim=32, hidden_dim=128, output_dim=2):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(input_dim, hidden_dim),
            nn.ReLU(),
            nn.Dropout(0.1),
            nn.Linear(hidden_dim, hidden_dim),
            nn.ReLU(),
            nn.Dropout(0.1),
            nn.Linear(hidden_dim, hidden_dim // 2),
            nn.ReLU(),
            nn.Linear(hidden_dim // 2, output_dim),
        )

    def forward(self, x):
        out = self.net(x)
        # softplus 確保輸出非負
        return torch.nn.functional.softplus(out)
```

### 6.4 Server（`nn_server.py`）

```python
import zmq, json, torch
from model import CostOracleMLP
from feature_spec import QUERY_FEATURES, load_normalizer

def main():
    ctx = zmq.Context()
    sock = ctx.socket(zmq.REP)
    sock.bind("tcp://127.0.0.1:5557")

    model = CostOracleMLP()
    model.load_state_dict(torch.load("checkpoints/best.pt"))
    model.eval()

    norm = load_normalizer("feature_spec.json")

    print("NN cost oracle listening on tcp://127.0.0.1:5557")
    while True:
        try:
            msg = sock.recv_string()
            req = json.loads(msg)
            queries = req["queries"]
            snapshot = req["snapshot"]  # 用於計算 density 等

            # 將每個 query 轉成 feature vector
            X = build_feature_matrix(queries, snapshot, norm)

            with torch.no_grad():
                pred = model(torch.tensor(X, dtype=torch.float32))
                T = pred[:, 0].numpy()
                E = pred[:, 1].numpy()

            response = {
                "predictions": [
                    {"bot_id": q["bot_id"], "pod_id": q["pod_id"],
                     "station_id": q["station_id"],
                     "T": float(T[i]), "E": float(E[i])}
                    for i, q in enumerate(queries)
                ]
            }
            sock.send_string(json.dumps(response))
        except Exception as e:
            sock.send_string(json.dumps({"error": str(e)}))
```

### 6.5 訓練資料整理（`data_collector.py`）

從 `m1g_path_estimate_actual_segments.csv` 聚合：

```python
import pandas as pd

def build_training_set(segment_csv, snapshot_jsonl, out_csv):
    df = pd.read_csv(segment_csv)
    # 1. 過濾完成的紀錄（actual_duration 非 NaN）
    df = df[df["actual_duration"].notna()]
    # 2. 把 BotToPod 與 PodToStation 兩段合併成三元組總時間
    grouped = df.groupby(["decision_id", "bot_id", "pod_id", "station_id"])
    aggregated = grouped.agg({
        "actual_duration": "sum",
        "actual_energy": "sum",
        "actual_wait_time": "sum",
        "actual_turn_count": "sum",
        "decision_time": "first",
    }).reset_index()
    # 3. 對每筆三元組，從 snapshot 抽當下狀態並計算 query 特徵
    snapshots = load_snapshots(snapshot_jsonl)
    rows = []
    for _, row in aggregated.iterrows():
        snap = find_snapshot_at(snapshots, row["decision_time"])
        feat = compute_query_features(row, snap)
        feat["arrival_time"] = row["actual_duration"]
        feat["energy"] = row["actual_energy"]
        rows.append(feat)
    pd.DataFrame(rows).to_csv(out_csv, index=False)
```

需要 RAWSim-O 額外輸出 `snapshots.jsonl`（每秒一筆 snapshot），見第 7 節。

### 6.6 Training Loop（`train_nn.py`）

```python
def train(epochs=200, lr=1e-3, batch_size=256):
    df = pd.read_csv("training_data.csv")
    # train/val split by simulation run id (避免 leakage)
    train_df, val_df = split_by_run(df, val_ratio=0.2)

    model = CostOracleMLP()
    opt = torch.optim.Adam(model.parameters(), lr=lr, weight_decay=1e-5)
    loss_fn = nn.SmoothL1Loss()  # Huber, 對 outlier 較穩健

    for epoch in range(epochs):
        for X, y in DataLoader(train_df, batch_size, shuffle=True):
            pred = model(X)
            loss = loss_fn(pred, y)
            opt.zero_grad(); loss.backward(); opt.step()
        val_metrics = evaluate(model, val_df)
        print(f"Epoch {epoch}: MAE_T={val_metrics['mae_T']:.2f}s "
              f"MAE_E={val_metrics['mae_E']:.2f}J")
        if is_best(val_metrics):
            torch.save(model.state_dict(), "checkpoints/best.pt")
```

---

## 7. Snapshot 資料管道（每秒輸出）

> **重要區分**：本文有兩種 snapshot 用途，避免混淆：
> 1. **Runtime snapshot**（第 5.2 節 `FeatureBuilder.BuildSnapshot`）：M1GT 決策時即時建構的記憶體物件，序列化後送給 Python NN 服務做推論。**不寫硬碟**。
> 2. **Logged snapshot**（本節 `SnapshotWriter`）：每秒自動寫入 `snapshots.jsonl` 的訓練資料，配合 `M1GPathTrace` 的 segment CSV 給 `data_collector.py` 離線整理 training set。**只在訓練資料收集 run 時開啟**。
>
> 兩者結構大致相同，但用途與生命週期完全不同。

### 7.1 觸發機制

在 `Instance.cs` 主迴圈或 `Observer.cs` 加入每秒回呼：

```csharp
// Instance.cs 既有 Update() 的某處
private double _nextSnapshotTime = 0.0;
public void RecordSnapshotIfDue(double currentTime)
{
    if (currentTime < _nextSnapshotTime) return;
    _nextSnapshotTime = Math.Floor(currentTime) + 1.0;
    SnapshotWriter.Write(currentTime, this);
}
```

### 7.2 SnapshotWriter（新增）

**路徑**：`RAWSimO.Core/Metrics/SnapshotWriter.cs`

每筆 JSON line：

```json
{"t": 123,
 "bots": [
   {"id": 1, "x": 12.3, "y": 4.5, "ori": 1.57, "vel": 1.2,
    "p": [pod_x, pod_y] | null,
    "s": [st_x, st_y] | null,
    "task": "extract", "state": "Move",
    "e": 1234.5, "w": 250.0,
    "tpa": 145.2 | null,    // null 多數時候，抵達 pod 瞬間填值
    "tsa": null}
 ],
 "stations": [
   {"id": 0, "queue_len": 3, "avail_cap": 4}
 ]}
```

### 7.3 tpa / tsa 注入點

| 事件 | 注入位置 |
|------|----------|
| Bot 抵達 pod 瞬間 | `BotNormal.PickupPod` 開始時，記錄 `tpa = currentTime` 至該 bot 的 `_currentTaskTpa` |
| Bot 抵達 station queue | `M1GPathTrace.CompleteActualSegmentAtBoundary` 中 `actualEndKind == "StationQueueEntry"` 時記錄 `tsa` |
| Bot 任務切換時 | 重置 `_currentTaskTpa = null, _currentTaskTsa = null` |

**注意**：`tpa / tsa` 在 snapshot 中只用於建構 label（離線資料整理），
**不進入 NN 的 inference input**。snapshot 中保留它們是為了讓 `data_collector.py`
能對齊 (decision_time → arrival_time → completion_time) 三個時間點。

---

## 8. 訓練資料生產流程

### 8.1 資料生產配置矩陣

|實驗組 | bot 數 | order 數 | δ | 時長 | 隨機種子 | 用途 |
|------|--------|----------|---|------|----------|------|
| Train-S | 6, 8, 10 | 70, 100 | 1, 2 | 1h | 5 seeds | 小規模訓練資料 |
| Train-L | 60, 80, 100 | 300, 500 | 1 | 30min | 3 seeds | 大規模訓練資料 |
| Val | 7, 9, 11, 70, 90 | 各組搭配 | 1 | 1h | 2 seeds | 驗證集（不重疊） |
| Test | 12, 120 | 100, 500 | 1 | 1h | 2 seeds | 最終評估 |

### 8.2 一次資料生產執行

```bash
# 單次模擬產出：
#   results/<run_id>/m1g_path_estimate_actual_segments.csv
#   results/<run_id>/snapshots.jsonl
RAWSimO.CLI.exe --instance instance.xosgen --setting setting.xsetting \
                --controller controller.xchsg --output results/run_001
```

### 8.3 資料整合

```bash
cd ml/
python data_collector.py \
  --runs ../results/Train-S-* \
  --out training_data_S.csv

python train_nn.py --data training_data_S.csv --out checkpoints/m1g_nn_S.pt
```

預估訓練資料量：每次 1h 模擬約產生 1500–3000 筆三元組決策紀錄，
20 次模擬可產出 30k–60k 筆訓練樣本，足以訓練 32→128→128→64→2 的 MLP。

---

## 9. 實驗評估設計

### 9.1 系統效能對比（主結果）

對每組 (bots, orders) 配置，跑三個版本並比較：

| 版本 | 設定 |
|------|------|
| M1G-D | 原始 distance cost |
| M1G-NN-T | NN 預測 arrival time |
| M1G-NN-TE | NN time + energy（w_E 待調） |

**主要指標**（從 `InstanceStatistics` 既有輸出）：

- Throughput（orders / hour）
- Order completion time 平均與中位數
- Station utilization rate
- Robot total distance
- Robot total energy
- Average wait time / order
- Stop-and-go count / order
- Turning count / order
- M1GT 決策耗時（含 NN 推論延遲）

### 9.2 NN 預測品質驗證

從 `m1g_path_estimate_actual_segments.csv` 對 NN 預測值 vs `ActualDuration` 做：

- MAE / RMSE（絕對誤差）
- MAPE（百分比誤差）
- Pearson / Spearman 相關係數
- **Ranking accuracy**：對每次決策的所有 (r,p,w) 候選排序，比較 NN 與 distance 對 actual time 的排序一致性
- **Top-K recall**：NN 預測的 top-K 是否包含真正最快的 (r,p,w)

### 9.3 特徵重要性分析（論文章節）

訓練後做 SHAP 分析或單純的 feature ablation：

- 移除「壅塞特徵」整組，看 MAE 變化多少
- 移除「路徑幾何特徵」整組
- 移除「station 狀態特徵」整組

用以論證哪些特徵對改善估算最重要，回應「為何 distance 不足」。

### 9.4 失敗情境分析

在哪些場景 NN 仍輸給 distance？

- 極低壅塞（n_bots / n_pods 很小）
- 極高壅塞（n_bots 接近 saturation）
- station 接近滿載

這部分作為 limitation 章節。

---

## 10. 開發路線圖（漸進實作）

### Phase 0 — 基礎建設（1 週）

- [ ] 新增 `SnapshotWriter`，每秒輸出 jsonl
- [ ] 新增 `tpa/tsa` 注入點（`BotNormal.PickupPod`、`M1GPathTrace`）
- [ ] 寫 `data_collector.py` 把現有 CSV 與 snapshot 對齊
- [ ] **驗證點**：跑一次模擬，確認資料齊全可訓練

### Phase 1 — Python NN 服務（1 週）

- [ ] 寫 `model.py`、`train_nn.py`、`feature_spec.py`
- [ ] 用 Phase 0 收集的資料訓練初版 MLP
- [ ] 寫 `nn_server.py`（ZeroMQ REP）
- [ ] **驗證點**：Python 端 MAE < distance 法的誤差

### Phase 2 — C# 整合（2 週）

- [ ] 加 NetMQ NuGet 至 `RAWSimO.Core`
- [ ] 新增 `FeatureBuilder`、`NNCostProvider`
- [ ] 修改 `M1GTConfiguration` 加入 `NNCostConfiguration`
- [ ] 修改 `M1GTManager.solve()` 接 q 變數與 NN 成本
- [ ] **驗證點**：UseNNCost=false 時行為與原 M1GT 完全相同

### Phase 3 — 候選剪枝與大規模測試（1 週）

- [ ] 實作 `CandidatePruneTopK` 邏輯
- [ ] 跑大規模實例驗證 q 變數規模可控
- [ ] **驗證點**：120 bots × 600 pods × 12 stations 場景下 MILP 可在 5 秒內解完

### Phase 4 — 實驗與論文（4 週）

- [ ] 跑完整實驗矩陣（M1G-D / NN-T / NN-TE × 多場景）
- [ ] 撰寫評估章節
- [ ] 特徵重要性分析
- [ ] 失敗案例討論
- [ ] **驗證點**：完整實驗結果 + 論文初稿章節

---

## 11. 已知風險與緩解

| 風險 | 影響 | 緩解 |
|------|------|------|
| NN 預測壞掉導致 MILP 解出極差結果 | 模擬 throughput 下降 | `FallbackOnError = "distance"`，異常時退回 distance |
| q 變數爆炸 | MILP 求解超時 | `CandidatePruneTopK = 5` |
| training data 不足 | NN underfitting | 從多種場景組合產資料；先小規模驗證 pipeline 通暢 |
| WHCA\* 隨機性使同樣 input 對不同 actual time | 預測上限受限 | 訓練時用同 seed 多次平均當 label，或只取 median |
| Python ↔ C# IPC 失敗 | 模擬卡住 | timeout + fallback |
| 大規模 MILP + NN 推論延遲過大 | 模擬太慢 | 候選剪枝 + 批次推論一次處理所有 query |

---

## 12. 不在本設計範圍

- **不修改 BalancedBotManager 派工邏輯**（Ra=0 時 `_Ziops` 任務池路徑）。
  第二階段再評估是否擴展 NN cost 至此處。
- **不修改 WHCA\* 路徑規劃器**。NN 學的是 WHCA\* 行為，不取代它。
- **不引入 RL / 線上學習**。NN 在離線階段訓練固定，部署時不更新。
- **不處理 replenishment（ROA/RPS/RTA）**。聚焦 picking process。
- **不做完整 OA/PS/TA/PP 全域最優**。只改 M1G 的成本係數。
- **不分散式部署**。NN 服務固定在本機。

---

## 13. 附錄：關鍵檔案清單

### 新增檔案

```
RAWSimO.Core/Control/Defaults/OrderBatching/NNCost/
├── FeatureBuilder.cs
├── NNCostProvider.cs
├── WarehouseSnapshot.cs       # POCO data class
└── QueryFeature.cs

RAWSimO.Core/Metrics/SnapshotWriter.cs

ml/
├── nn_server.py
├── model.py
├── feature_spec.py
├── data_collector.py
├── train_nn.py
├── eval_nn.py
└── requirements.txt
```

### 修改檔案

```
RAWSimO.Core/Control/Defaults/OrderBatching/M1GTManager.cs
  - solve()：加入 NN cost 路徑與 q 變數
  - DecideAboutPendingOrders()：初始化 NNCostProvider

RAWSimO.Core/Configurations/MethodConfigurationsOB.cs
  - M1GTConfiguration：加入 NNCost 子設定
  - 新增 NNCostConfiguration class

RAWSimO.Core/Bots/BotNormal.cs
  - PickupPod 開始時記錄 tpa
  - 新增 GetTaskStartTime / GetCurrentVelocity 公開方法（給 FeatureBuilder）

RAWSimO.Core/Metrics/M1GPathTrace.cs
  - CompleteActualSegmentAtBoundary 中於 StationQueueEntry 時注入 tsa
```
