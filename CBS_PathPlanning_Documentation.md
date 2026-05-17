# RAWSim-O CBS / Path Planning 模組完整文件

本文件針對 `EE-RAWSim-O_PP` 中與 **CBS（Conflict-Based Search）** 及整體 **Path Planning** 相關的模組做完整解析，包含：呼叫鏈、資料流、核心類別、觸發機制、設定參數。

---

## 0. 兩層架構：Core ↔ MultiAgentPathFinding

RAWSim-O 把 Path Planning 切成兩個 Assembly：

| 層級 | Project | 角色 |
|---|---|---|
| **Domain / Adapter** | `RAWSimO.Core.Control.PathManager` 子類別 | 把 RAWSim-O 的世界（Bot, Waypoint, Pod, Queue…）翻譯成輕量 Graph + Agent 結構，呼叫底層演算法 |
| **Algorithm** | `RAWSimO.MultiAgentPathFinding`（獨立 DLL） | 純演算法層：Graph、Agent、ReservationTable、CBS、ECBS、WHCA*、FAR、OD-ID、BCP、PAS 等 |

兩層透過 `PathFinder`（抽象類）+ `Agent`（DTO）+ `Graph`（lightweight）解耦。

```
┌──────────────────────────── RAWSimO.Core ────────────────────────────┐
│  Instance ── WaypointGraph ── BotNormal                              │
│                  │                │                                  │
│                  ▼                ▼  RequestReoptimization=true      │
│        ┌──────────────────────────────────────────┐                  │
│        │ PathManager (abstract)                   │                  │
│        │  Update() → _reoptimize() →              │                  │
│        │    getBotAgentDictionary()  ──► Agent[]  │                  │
│        │    PathFinder.FindPaths(...)             │                  │
│        └─────────────────┬────────────────────────┘                  │
│                          │                                            │
│        CBSPathManager / FARPathManager / WHCAxPathManager / ...      │
└──────────────────────────┼────────────────────────────────────────────┘
                           ▼
┌────────────────────── RAWSimO.MultiAgentPathFinding ──────────────────┐
│  PathFinder (abstract)                                                │
│   ├── CBSMethod          ── ConflictTree + SpaceTimeA* + RRA*         │
│   ├── ECBSMethod         ── EnergyConflictTree + ESpaceTimeA*         │
│   ├── WHCAvMethod / WHCAnMethod                                       │
│   ├── ODIDMethod / FARMethod / BCPMethod / PASMethod / DummyMethod    │
│   └── Toolbox：DeadlockHandler, AgentInfoExtractor, ...               │
│  DataStructures：ConflictTree, ReservationTable, DisjointIntervalTree │
│  Algorithms：SpaceTimeAStar, ReverseResumableAStar, OD, ID            │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 1. PathManager（基底類別） — `RAWSimO.Core/Control/PathManager.cs`

所有 Path Planning 演算法在 Core 層共用的 **驅動器**。負責：
1. 把 `WaypointGraph` 翻譯成輕量 `Graph`。
2. 維護 reservation table。
3. 每個 simulation tick 決定要不要呼叫底層 `PathFinder.FindPaths(...)`。

### 1.1 主要欄位

| 欄位 | 用途 |
|---|---|
| `PathFinder PathFinder` | 實際的底層演算法（CBSMethod, WHCAvMethod, …），由子類別建構時 new 出來。 |
| `BiDictionary<Waypoint,int> _waypointIds` | Waypoint ↔ int ID 雙向對映（底層只認 int）。 |
| `Dictionary<Waypoint,NodeInfo> _nodeMetaInfo` | 每個節點的 meta（IsLocked、IsObstacle、IsQueue、QueueTerminal）。 |
| `Dictionary<Waypoint,QueueManager> _queueManagers` | 為每個 Station/Elevator 入口維護的排隊管理器；隊伍裡的 bot 不送進 path planner。 |
| `ReservationTable _reservationTable` | 全域時空 reservation。 |
| `Dictionary<BotNormal,List<Interval>> _reservations` | 每台 bot 目前掛在 reservation 表上的 interval 清單。 |
| `double _lastCallTimeStamp` | 上一次呼叫 `_reoptimize` 的時間（搭配 `Clocking` 節流）。 |

### 1.2 `GenerateGraph()`（`PathManager.cs:112-199`）

把 `Instance.WaypointGraph` 轉成 `Graph`：
- 對每個 Waypoint 配一個整數 ID。
- 建立 `Edge[]`（同層）與 `ElevatorEdge[]`（跨層）。
- Edge 角度 `angle = atan2(Δy, Δx)`，後續轉彎成本由此來。
- 為每個 IQueuesOwner（InputStation / OutputStation / Elevator）的 queue waypoint 建立 `QueueManager`。

### 1.3 `UpdateLocksAndObstacles()`（`PathManager.cs:206-234`）

每次 reoptimize 前更新 graph 上的封鎖資訊：
- **Locked**：被 queue manager 鎖住的節點 + 正在 resting 的 bot 所站位置。
- **Obstacle**：所有 pod 目前佔據的位置（除非 `CanTunnel = true` 且 bot 沒揹 pod，否則不能穿過）。

### 1.4 `Update(lastTime, currentTime)` — 觸發 gate（`PathManager.cs:439-470`）

```csharp
public virtual void Update(double lastTime, double currentTime)
{
    foreach (var qm in _queueManagers.Values) qm.Update();

    if (_reservationTable == null) _initReservationTable();
    _reservationTable.Reorganize(currentTime);       // 砍掉已過時的 interval

    // Gate 1：節流（Clocking 之內就不重算）
    if (_lastCallTimeStamp + Clocking > currentTime) return;

    // Gate 2：任一 bot 提出 RequestReoptimization
    var reoptimize = Instance.Bots.Any(b => ((BotNormal)b).RequestReoptimization);

    if (reoptimize)
    {
        _lastCallTimeStamp = currentTime;
        _reoptimize(currentTime);
        foreach (var bot in Instance.Bots)
            ((BotNormal)bot).RequestReoptimization = false;
    }
}
```

**關鍵語意**：CBS 在 RAWSim-O 不是定時批次重算、也不是 one-shot 永久排好，而是 **「事件驅動 + Clocking 節流」**：
- 沒人 request 就完全不跑。
- 即使有 request，也至少要等 `Clocking` 秒才會跑一次。

### 1.5 `_reoptimize(currentTime)`（`PathManager.cs:240-293`）

```csharp
private void _reoptimize(double currentTime)
{
    _statStart(currentTime);
    DateTime before = DateTime.Now;

    Dictionary<BotNormal, Agent> botAgentsDict;
    getBotAgentDictionary(out botAgentsDict, currentTime); // 全部 bot 都打包成 Agent
    UpdateLocksAndObstacles();

    PathFinder.FindPaths(currentTime, botAgentsDict.Values.ToList()); // ★ 全體一起送進 CBS

    foreach (var botAgent in botAgentsDict)
        botAgent.Key.Path = botAgent.Value.Path;                       // 回灌 Path

    Instance.Observer.TimePathPlanning((DateTime.Now - before).TotalSeconds);
    _statEnd(currentTime);
}
```

### 1.6 `getBotAgentDictionary(...)`（`PathManager.cs:335-393`）

把 **每一台** BotNormal 翻譯成 `Agent` DTO：
- `NextNode`：bot 的下一個 waypoint（若已停在某點，就是 currentWaypoint）。
- `DestinationNode`：若 destination 在 queue 區域，就改成「queue 中分配給此 bot 的位置」。
- `ArrivalTimeAtNextNode`：根據既存 reservation 推算。
- `FixedPosition`：bot 不能離開當前點（例如正在 pick-up）。
- `RequestReoptimization`：傳遞 flag（CBS 不會直接讀，但其他 planner 像 WHCAv 會用來決定要不要重排）。
- `CurrentEnergyState`：CarryingPod / RobotWeight / PayloadWeight（給 ECBS 用）。

**跳過的 bot**：
- 已在 queue 內 (`bot.IsQueueing`)。
- `nextWaypoint == destination`（沒事做）。

---

## 2. CBSPathManager — `RAWSimO.Core/Control/Defaults/PathPlanning/CBSPathManager.cs`

PathManager 的 CBS 子類，建構時做三件事：

```csharp
public CBSPathManager(Instance instance) : base(instance)
{
    // 失敗時自動 request 重算（key behavior！）
    BotNormal.RequestReoptimizationAfterFailingOfNextWaypointReservation = true;

    var graph  = GenerateGraph();
    var config = instance.ControllerConfig.PathPlanningConfig as CBSPathPlanningConfiguration;

    PathFinder = new CBSMethod(graph, instance.SettingConfig.Seed, new PathPlanningCommunicator(...));
    var method = PathFinder as CBSMethod;
    method.LengthOfAWaitStep     = config.LengthOfAWaitStep;
    method.RuntimeLimitPerAgent  = config.RuntimeLimitPerAgent;
    method.RunTimeLimitOverall   = config.RunTimeLimitOverall;
    method.SearchMethod          = config.SearchMethod;

    if (config.AutoSetParameter)   // 論文建議的最佳預設
    {
        method.SearchMethod          = CBSMethod.CBSSearchMethod.BestFirst;
        method.RuntimeLimitPerAgent  = config.Clocking / instance.Bots.Count;
        method.RunTimeLimitOverall   = config.Clocking;
    }
}
```

---

## 3. CBSMethod — `RAWSimO.MultiAgentPathFinding/Methods/CBSMethod.cs`

完整 Sharon (2015) 風格 CBS，分兩層：
- **High level**：在 `ConflictTree` 上做 Best-First / Depth-First / Breadth-First 搜尋。
- **Low level**：對單一 agent 跑 `SpaceTimeAStar`，並以 `ReverseResumableAStar` 提供啟發式。

### 3.1 核心欄位

| 欄位 | 說明 |
|---|---|
| `_reservationTable` | low-level A* 用的「constraint 表」：每解一個 CT node 前清空、把 path constraint Add 進去。 |
| `_agentReservationTable` | high-level 衝突偵測用的「agent 預約表」：標 agent ID。 |
| `_deadlockHandler` | 解出來後若偵測到 deadlock，會做 random hop。 |
| `SearchMethod` | `BestFirst` / `DepthFirst` / `BreathFirst`，由 `nodeObjectiveSelector` 決定 Open queue 的 key。 |

### 3.2 `FindPaths(currentTime, agents)` 主流程（`CBSMethod.cs:69-190`）

```text
1. 初始化 ConflictTree、Open（Fibonacci heap）
2. _deadlockHandler.Update(agents, currentTime)
3. 對所有 FixedPosition agent：把節點標為 IsLocked
4. 對每個非 fixed agent 跑 Solve(root, agent)
   → 每個 agent 各自 ST-A* 跑出 path & reservations，存進 root
   solvable = AND of all Solve()
5. Enqueue root
6. while Open not empty:
   p = Open.Dequeue()
   hasNoConflicts = ValidatePath(p, agents, out a1, out a2, out interval)
   if hasNoConflicts: bestNode = p; break
   if 超過時間預算 (runtime > RuntimeLimitPerAgent*N*0.9 or > RunTimeLimitOverall):
       SignalTimeout(); break
   else:
       node1 = new ConflictTree.Node(a1, interval, p); Solve(node1, a1); Open.Enqueue
       node2 = new ConflictTree.Node(a2, interval, p); Solve(node2, a2); Open.Enqueue
7. 對所有 agent：agent.Path = bestNode.getSolution(agent.ID)
   若 deadlockHandler 認為仍 deadlock：RandomHop()
```

**Suboptimal fallback**：跑超時就回傳目前 `bestNode`（用 `bestTime`：以 conflict 出現時間越晚為較佳）。

### 3.3 `ValidatePath(node, agents, ...)`（`CBSMethod.cs:192-232`）

衝突偵測：
1. `_agentReservationTable.Clear()`，先填入每台 agent **到下一節點為止已 commit** 的 reservation（`agent.ReservationsToNextNode`） — 這些是「不能改」的近未來。
2. 蒐集當前 CT node 上每台 agent 的解所需要的 interval，用 Fibonacci heap 依 `Start` 排序。
3. 逐一 Add；若 Add 前 `IntersectionFree` 失敗且重疊長度 > `TOLERANCE`，回傳 `(false, agentId1, agentId2, overlapInterval)`。

### 3.4 `Solve(node, currentTime, agent)`（`CBSMethod.cs:244-296`）

單一 agent low-level 規劃：
1. `_reservationTable.Clear()`
2. 沿著 CT 從 leaf 走回 root，把屬於這個 agent 的 constraint 全部加進去（`node.getConstraints(agent.ID)` 是個迭代器）。
3. 檢查 `ReservationsToNextNode` 不能違反 constraint（會違反 → return false）。
4. 建立 `ReverseResumableAStar`（從目標反向跑，提供 admissible heuristic）。
5. 建立 `SpaceTimeAStar`（window = +∞，wait step = `LengthOfAWaitStep`），執行 Search。
6. 把 path + reservations 寫回 CT node：`node.setSolution(agent.ID, path, reservations)`。

### 3.5 三種節點選擇策略

```csharp
case BestFirst   : return node.SolutionCost;
case BreathFirst : return node.Depth;
case DepthFirst  : return -node.Depth;
```

`SolutionCost` 是 root 為「所有 agent 旅程時間總和」、child 為「parent.SolutionCost − parent[此 agent] + new[此 agent]」（增量更新）。

---

## 4. ConflictTree — `DataStructures/ConflictTree.cs`

### 4.1 結構

```
ConflictTree
 └─ Root (AgentId = -1, 存所有 agent 的 solution & reservation)
     ├─ Child (AgentId = X, IntervalConstraint = Interval, 只存 X 的新 solution)
     │   ├─ ...
     └─ Child (AgentId = Y, IntervalConstraint = Interval)
```

### 4.2 Node 重要方法

| 方法 | 行為 |
|---|---|
| `setSolution(agentId, path, intervals)` | 只在 root 存全部；child 只覆寫被加 constraint 的那個 agent；同步更新 `SolutionCost`。 |
| `getSolution(agentId)` / `getReservation(agentId)` | 一路向上找：「最近一個對 `agentId` 有解的 ancestor」。這是 CBS 增量解的關鍵設計。 |
| `getConstraints(agentId)` | 回傳 `ConstraintCollector`（IEnumerable）：從 this 一路向上 yield 出 `AgentId == agentId` 的 ancestor — 等同收集所有「該 agent 在這條 CT 路徑上的 constraint」。 |
| `validate()` | Debug：把 constraint 加進臨時 ReservationTable，看會不會 throw `IntervalIntersectionException`。 |

### 4.3 ConstraintCollector / ConstraintEnumerator

逐節點向上爬，跳過 `AgentId != target` 的節點：

```csharp
public bool MoveNext()
{
    if (_currentNode == null) return false;
    if (_firstCall) _firstCall = false;
    else _currentNode = _currentNode.Parent;
    while (_currentNode != null && _currentNode.AgentId != _agentId)
        _currentNode = _currentNode.Parent;
    return _currentNode != null;
}
```

---

## 5. ReservationTable — `DataStructures/ReservationTable.cs`

時空 reservation 的核心資料結構，CBS 的 low-level、PathManager 的全域 reservation 都靠它。

### 5.1 內部表示

- `DisjointIntervalTree[] _intervallTrees` — 每個 graph node 一棵 disjoint interval tree（lazy 初始化）。
- 可選 `storeAgentIds`、`storePrios`：是否把 agent ID 與 priority 也存進去（高層衝突偵測用）。
- `fastClear` 模式下用 `_touchedNodes: HashSet<int>` 記錄哪些節點被碰過，加速 `Clear()`。

### 5.2 主要 API

| API | 用途 |
|---|---|
| `Add(node, start, end, agentId, prio)` / `Add(Interval)` / `Add(List<Interval>)` | 插入預約 |
| `Remove(Interval)` | 用 `(start+end)/2` 切點移除相交的 interval |
| `IntersectionFree(...)` 多載 | 檢查單一 interval / 多個 / checkpoint list 是否衝突；可選 `out agentId` / `out List<Collision>` |
| `GetOverlappingInterval(Interval)` | 回傳重疊段，用於 CBS 建 split constraint |
| `Reorganize(currentTime)` | 砍掉 `End <= currentTime` 的 interval（每個 tick 在 `PathManager.Update` 開頭呼叫） |
| `GetCheckPointNodes(...)` / `CreateIntervals(...)` | 把路徑（節點序列）按物理模型轉成時空 interval 序列 |

### 5.3 Interval（巢狀類）

```csharp
public class Interval { public int Node; public double Start; public double End; }
```

CBS 中：
- **constraint interval**：「禁止 agent X 在 Node N 的 [start, end] 出現」。
- **reservation interval**：「agent X 預定在 Node N 的 [start, end] 出現」。

兩者結構相同，差別在於語意。

---

## 6. Low-level：SpaceTimeAStar + ReverseResumableAStar

### 6.1 SpaceTimeAStar — `Algorithms/AStar/SpaceTimeAStar.cs`

實作 Silver (2005) 的 WHCA*，但 CBS 用時把 window 設為 `+∞`。

關鍵欄位：

| 欄位 | 用途 |
|---|---|
| `NodeBackpointerId / NodeBackpointerLastStopId / NodeBackpointerEdge / NodeTime` | 把搜尋節點（generated node id）映射回 2D node、轉彎前一停點、進來的 Edge、抵達時間。 |
| `_lengthOfAWaitStep` | 每次「等待」的 time step（也是 CBS constraint 的最小切割單位）。 |
| `_lengthOfAWindow` | CBS 用 `+∞`，WHCA* 用有限值。 |
| `_RRAStar` | ReverseResumableAStar，提供 admissible heuristic：`h(n) = RRA*.g(node2d) + turning cost + biased cost`。 |
| `FinalReservation` | 若 true，要求 goal 從抵達時間到 `+∞` 都 reservation-free（拿來保證 bot 停在終點時不會卡到別人）。 |

`StopCondition(n)`（`SpaceTimeAStar.cs:260-268`）：

```csharp
if ((NodeTo2D(n) == _agent.DestinationNode || NodeTime[n] >= _lengthOfAWindow)
    && (!FinalReservation || _reservationTable.IntersectionFree(NodeTo2D(n), NodeTime[n], double.PositiveInfinity)))
    GoalNode = n;
return base.StopCondition(n);
```

`Successors(n)` 同時產生：
- **Wait edge**：`backpointer = n, time += waitStep`
- **Move edge**：考慮所有同向直行 / 轉彎後直行，前進到「最近一個 reservation 不衝突的位置」。

### 6.2 ReverseResumableAStar

從目標反向跑、可暫停可恢復（保留 Open / Closed），給 SpaceTimeAStar 當 heuristic 用，能在多次呼叫間共享運算成果。

---

## 7. Agent DTO — `Elements/Agent.cs`

Core ↔ MAPF 之間的傳遞單元：

```csharp
class Agent {
    int ID;
    int NextNode;
    List<Interval> ReservationsToNextNode;     // 已 commit 的近未來
    double ArrivalTimeAtNextNode;
    double OrientationAtNextNode;
    int DestinationNode;
    int FinalDestinationNode;                  // 跟 queue 有關
    bool FixedPosition;
    bool Resting;
    bool CanGoThroughObstacles;                // CanTunnel && bot 沒揹 pod
    Physics Physics;
    Path Path;                                 // by-ref，被 planner 填回
    bool RequestReoptimization;
    bool Queueing;
    EnergyState CurrentEnergyState { CarryingPod, RobotWeight, PayloadWeight }
}
```

`Path` 是 `LinkedList<Action>`，`Action = { Node, StopAtNode, WaitTimeAfterStop }`。

---

## 8. RequestReoptimization 的設定點 — `Bots/BotNormal.cs`

什麼時候 bot 會在下一個 PathManager tick 主動 request 重算？

| 行 | 情境 |
|---|---|
| `617, 636, 665, 685, 925` | `AssignTask(...)` 切換到 ParkPod / RepositionPod / Insert / Extract / Resting 任務時 |
| `1691` | 剛切換到 Move state，但 bot 還沒 Path |
| `1758` | 走完目前 Path，但還沒到 destination |
| 整段 `RequestReoptimizationAfterFailingOfNextWaypointReservation = true`（由 `CBSPathManager` ctor 設定） | 下一節點 reservation 失敗時自動 request |

因此 CBS 觸發節奏的真實樣貌是：**「TA 派新任務 / 走完一段 / 預約衝突」三類事件累積 → 每 `Clocking` 秒批次解一次**。

---

## 9. CBSPathPlanningConfiguration — `Configurations/MethodConfigurationsPP.cs:279`

```csharp
public class CBSPathPlanningConfiguration : PathPlanningConfiguration
{
    public override PathPlanningMethodType GetMethodType() => PathPlanningMethodType.CBS;
    public CBSMethod.CBSSearchMethod SearchMethod = CBSMethod.CBSSearchMethod.BestFirst;
    // 從父類別繼承：
    //   AutoSetParameter     (bool, default false)
    //   CanTunnel            (bool, default true)   ← 沒揹 pod 時可穿過 pod
    //   LengthOfAWaitStep    (double, default 2.0)
    //   RuntimeLimitPerAgent (double, default 0.1)
    //   RunTimeLimitOverall  (double, default 1.0)
    //   Clocking             (double, default 1.0)  ← 重規劃節流間隔
}
```

`PathPlanningConfiguration` 定義在 `MethodConfiguration.cs:519-577`。

---

## 10. ECBSMethod（Energy-aware 變體） — 簡述

`RAWSimO.MultiAgentPathFinding/Methods/ECBSMethod.cs` 是本研究小組在 CBS 上加上能耗考量的版本：

- 用 `EnergyConflictTree`（節點額外攜帶能耗 bound）。
- Low-level 換成 `ESpaceTimeAStar` — 與 `SpaceTimeAStar` 同構，但 g/h 函數含能耗項，依 `Agent.CurrentEnergyState` 算動態權重。
- 自帶 `EnergyDeadlockHandler` + `_stuckRounds` 機制：committed wait 不算 livelock，連 `StuckHopThreshold = 3` 次真卡住才允許 RandomHop（避免誤觸發破壞 commit）。
- 仍與 CBS 共用 `ReservationTable` / `ConflictTree` 介面。

> 註：你的論文已從 CBS-energy 轉向 Learning-Augmented M1G，但 ECBS 仍是 PP 層的 baseline。

---

## 11. 其他 PathPlanning 方法（Method ↔ Manager 對應表）

| 演算法 | Method 類別 | PathManager 子類 |
|---|---|---|
| CBS | `CBSMethod` | `CBSPathManager` |
| ECBS (energy-aware) | `ECBSMethod` | （目前以 `CBSPathManager` 或 m1g 分支內的對應 manager 接入） |
| WHCA*（v / n） | `WHCAvMethod` / `WHCAnMethod` | `WHCAvStarPathManager` / `WHCAnStarPathManager` |
| OD-ID | `ODIDMethod` | `ODIDPathManager` |
| FAR (Flow Annotation Replanning) | `FARMethod` | `FARPathManager` |
| BCP (Branch-and-Cut-and-Price) | `BCPMethod` | `BCPPathManager` |
| PAS (Prioritized A*) | `PASMethod` | `PASPathManager` |
| Dummy | `DummyMethod` | `DummyPathManager` |

所有 Method 都繼承 `PathFinder`，因此 `PathManager._reoptimize` 的呼叫方式對它們都一樣。

---

## 12. 完整資料流圖（CBS 一次 tick）

```
[Simulation tick]
   │
   ▼
Controller.Update(t)
   │
   ▼
PathManager.Update(t)
   │ Clocking 節流？  ─── no ──► return
   │ 任一 bot.RequestReoptimization？  ─── no ──► return
   ▼
PathManager._reoptimize(t)
   │
   ├── getBotAgentDictionary()  ──►  List<Agent>
   │     for each BotNormal b：
   │       建立 Agent { NextNode, ReservationsToNextNode,
   │                    ArrivalTimeAtNextNode, DestinationNode,
   │                    FixedPosition, Resting, Physics,
   │                    RequestReoptimization, EnergyState }
   │
   ├── UpdateLocksAndObstacles()
   │     graph.NodeInfo[*].IsLocked / IsObstacle 重設
   │
   ▼
CBSMethod.FindPaths(t, agents)
   │
   ├── _deadlockHandler.Update(agents, t)
   │
   ├── 初始 root：對每個非 fixed agent 跑 Solve()
   │       Solve = clear reservationTable
   │              + 加入 CT 路徑上屬於該 agent 的 constraint
   │              + SpaceTimeAStar.Search()（heuristic 由 RRA* 給）
   │              + node.setSolution(agentId, path, intervals)
   │
   ├── while Open not empty：
   │     p = Open.Dequeue()                    // 依 SearchMethod 取最佳
   │     ValidatePath(p, agents) 找衝突
   │     若無衝突 → bestNode = p; break
   │     若有衝突 (a1, a2, interval)：
   │       node1 = Node(a1, interval, p); Solve(node1, a1); enqueue
   │       node2 = Node(a2, interval, p); Solve(node2, a2); enqueue
   │     若超時 → SignalTimeout; break
   │
   └── for each agent：agent.Path = bestNode.getSolution(agent.ID)
                       deadlockHandler 後處理 (RandomHop)
   │
   ▼
PathManager._reoptimize 把 agent.Path 回灌到 bot.Path
   │
   ▼
BotNormal 在後續 tick 依 Path 推進；
若到下一節點 reservation 失敗 / 任務切換 / Path 走完 → RequestReoptimization=true
   │
   ▼
迴圈
```

---

## 13. 關鍵設計觀念總結

1. **「事件驅動 + Clocking 節流 + 全體批次規劃」**：CBS 在 RAWSim-O 是 online MAPF，但不是每 tick 都重算，而是「有人要求且過了節流間隔」才一次性對全體 bot 重排。
2. **`ReservationsToNextNode` 是 commit 邊界**：bot 已經在路上，下一節點的 reservation 視為不可變；CBS 規劃時把這段塞進 `_agentReservationTable` 當硬約束，後段路徑才能改。
3. **ConflictTree 增量解**：child 只存「被加 constraint 的那個 agent 的新解」，其他 agent 的解透過 `getSolution` 沿 parent chain 找。記憶體不會隨 CT 深度線性增加 N 倍。
4. **Low-level 與 high-level 共用 ReservationTable**：但分別代表「對某 agent 的 constraint」（low-level，clear-after-solve）與「全體 agent 的當前 commit 衝突檢查表」（high-level，每次 ValidatePath 重建）。
5. **Suboptimal but anytime**：超時 fallback 為「目前最深 / 最晚衝突的 CT node」，配合 deadlock handler 的 RandomHop 保證系統不會永遠卡住。
6. **能耗整合（ECBS）**：把 g、h 函數改成「時間 + 能耗」加權；ConflictTree 升級成 EnergyConflictTree；reservation 機制不變。

---

## 14. 重要程式座標索引（速查）

| 主題 | 檔案:行 |
|---|---|
| PathManager 觸發 gate | `RAWSimO.Core/Control/PathManager.cs:439-470` |
| `_reoptimize` 主流程 | `RAWSimO.Core/Control/PathManager.cs:240-293` |
| Bot → Agent 翻譯 | `RAWSimO.Core/Control/PathManager.cs:335-393` |
| GenerateGraph / Edge 角度 | `RAWSimO.Core/Control/PathManager.cs:112-199` |
| Locks & Obstacles 更新 | `RAWSimO.Core/Control/PathManager.cs:206-234` |
| CBSPathManager ctor | `RAWSimO.Core/Control/Defaults/PathPlanning/CBSPathManager.cs:29-58` |
| CBS FindPaths 主迴圈 | `RAWSimO.MultiAgentPathFinding/Methods/CBSMethod.cs:69-190` |
| CBS ValidatePath | `RAWSimO.MultiAgentPathFinding/Methods/CBSMethod.cs:192-232` |
| CBS Solve | `RAWSimO.MultiAgentPathFinding/Methods/CBSMethod.cs:244-296` |
| ConflictTree.Node | `RAWSimO.MultiAgentPathFinding/DataStructures/ConflictTree.cs:61-310` |
| ConstraintCollector | `RAWSimO.MultiAgentPathFinding/DataStructures/ConflictTree.cs:316-468` |
| ReservationTable | `RAWSimO.MultiAgentPathFinding/DataStructures/ReservationTable.cs` |
| SpaceTimeAStar | `RAWSimO.MultiAgentPathFinding/Algorithms/AStar/SpaceTimeAStar.cs` |
| ReverseResumableAStar | `RAWSimO.MultiAgentPathFinding/Algorithms/AStar/ReverseResumableAStar.cs` |
| Agent DTO | `RAWSimO.MultiAgentPathFinding/Elements/Agent.cs` |
| RequestReoptimization 設定點 | `RAWSimO.Core/Bots/BotNormal.cs:617, 636, 665, 685, 925, 1691, 1758` |
| CBSPathPlanningConfiguration | `RAWSimO.Core/Configurations/MethodConfigurationsPP.cs:279-310` |
| PathPlanningConfiguration 基底 | `RAWSimO.Core/Configurations/MethodConfiguration.cs:519-577` |
| ECBSMethod | `RAWSimO.MultiAgentPathFinding/Methods/ECBSMethod.cs` |
| EnergyConflictTree | `RAWSimO.MultiAgentPathFinding/DataStructures/EnergyConflictTree.cs` |
| ESpaceTimeAStar | `RAWSimO.MultiAgentPathFinding/Algorithms/AStar/ESpaceTimeAStar.cs` |
