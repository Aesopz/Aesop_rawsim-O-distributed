你的問題本質上不是「triple 無法計算」，而是：

**你現在的控制單位太小。**
你用的是「局部 pairwise 讓車」；但多方衝突其實是 **wait-for graph（等待關係圖）** 的問題，不是三台車同時做三元組合最佳化的問題。

所以正確方向不是去算：

* A/B/C 三台誰先走
* A/B/C/D 四台誰先走
* 所有組合的聯合 action

這樣一定會 **動作空間爆炸**。

---

# 一句話先講結論

在你的離散環境中，**不要把 triple conflict 當成高維聯合動作問題**，而要把它改寫成：

1. **局部移動決策** 仍由 bot 自主輸出
2. **衝突解析層** 額外建立等待圖
3. 用 **cycle detection + timeout + 局部 reroute** 去解死鎖
4. 必要時再加 **reservation / token**，但只保留 1~2 step lookahead

這樣你不需要擴大 RL action space，也能處理多方衝突。

---

# 為什麼 triple 不能用 pairwise 疊加？

因為 pairwise 的邏輯是假設：

> A 只要處理跟 B 的關係、B 只要處理跟 C 的關係，就能推出整體可行性。

但三方衝突不是這樣。

例如：

* A 要進 WP1，但 WP1 被 B 的下一步卡住
* B 要進 WP2，但 WP2 被 C 的下一步卡住
* C 要進 WP3，但 WP3 被 A 的下一步卡住

這不是三個獨立 pair：

* A vs B
* B vs C
* C vs A

而是一個 **封閉等待環**。
只做 pairwise，你只能看到「我現在要讓誰」，看不到「我讓了之後整體還是死」。

就像三個人站在狹窄走廊彼此讓路，每個人都很有禮貌，但整體反而誰都走不了。

---

# 你真正要換的不是 action，而是 state abstraction

你現在如果想直接讓 RL 解 triple，會自然想到：

* 讓 agent 看 3 台、4 台、5 台鄰車狀態
* 再讓 agent 輸出複雜動作：停、走、繞、讓、換道、重規劃

這會爆炸，原因有兩個：

## 1. 鄰居數不固定

附近可能 1 台，也可能 5 台。

## 2. 聯合決策組合數太大

若每台車有 5 個可能 micro-action，
3 台就是 (5^3 = 125)，
5 台就是 (5^5 = 3125)。

而且這還沒算 waypoint 候選與時間先後。

所以不要讓 policy 直接學「多方協商」。
應該讓 policy 學「在衝突特徵下如何偏好行為」，而**真正的 deadlock resolution 交給規則層**。

---

# 你適合的架構：二層決策，不要一層全包

我建議你用下面這個架構。

---

## Layer A：RL / 原始決策層

負責：

* 選下一個局部 waypoint
* 選移動傾向
* 選速度偏好或讓路傾向
* 選是否請求 reroute

這層維持自主性。

---

## Layer B：Traffic Resolution Layer

負責：

* 偵測誰在等誰
* 建立 wait-for graph
* 找 cycle
* 決定最低優先者暫退或重規劃
* timeout 保底解鎖

這層不是取代 policy，而是像作業系統的 deadlock handler。

---

# 離散環境下，最實用的解法不是 ORCA，而是「等待圖 + reservation」

你前面整理得很準。
ORCA 很強，但它核心是**連續速度空間中的可行速度集合**。
你的環境是：

* graph / waypoint-based
* 離散前進
* 已有 path / turn point / intersection structure
* 有 phase priority

所以你最該做的不是硬套 ORCA，而是這三個：

---

## 第一個：Wait-for Graph

把衝突從「兩台比較」提升成「等待依賴」。

### 定義

若 A 本 tick 原本想進入 `next_wp`，但因 B 導致 A speedCap=0 或被阻擋，則記：

**A → B**

意思是：A 在等 B。

整體就形成一張圖：

* 節點：bot
* 有向邊：waiting relation

### 好處

這樣三方衝突自動變成圖上的 cycle。

例如：

* A → B
* B → C
* C → A

只要做 DFS / Tarjan / union-find 類似的 cycle detection，就知道是 deadlock。

---

## 第二個：短視野 reservation

不是做全時域 reservation table，那太重。
只要做 **1-step 或 2-step reservation** 就夠了。

每個 bot 只宣告：

* 當前佔據的 waypoint
* 下一步想進的 waypoint
* 必要時加下一個 turn point

然後用一個表維護：

```text
waypoint -> current_owner
waypoint -> next_claimers
```

若多個人同時 claim 同一個 waypoint：

* 先比 phase priority
* 再比等待時間
* 再比方向規則（vertical > horizontal）
* 再比 FCFS

輸的人：

* 不前進
* 或進入 reroute candidate 狀態

### 為什麼這比完整 token passing 更適合你？

因為完整 TP/TPTS 很像把整張圖都拿去做分散式協商，對你目前系統太重。
但只做 **局部 reservation**，已經能解掉大部分 triple。

---

## 第三個：stall timeout

這是最便宜、最值得先上的。

若一台 bot：

* 速度極低
* 且不是因為 station service / pickup / dropoff
* 持續超過某個時間

就觸發：

* `RequestReoptimization = true`
* 或 `ForceYield = true`

這是很典型的 deadlock recovery 機制。

你前面寫的這段方向是對的：

```csharp
if (_currentSpeed < 0.01)
{
    if (_stallStartTime < 0) _stallStartTime = currentTime;
    else if (currentTime - _stallStartTime > STALL_TIMEOUT)
    {
        RequestReoptimization = true;
        _stallStartTime = -1;
    }
}
else _stallStartTime = -1;
```

但我要提醒你一個弱點：

## 這段不能直接上

因為它會把「正常排隊」跟「死鎖停滯」混在一起。

例如：

* station 前排隊
* pod lift / drop 正常等待
* 前車慢速通行

這些不是 deadlock。

所以你應該加條件：

```csharp
if (_currentSpeed < eps
    && !IsProcessingAtStation
    && !IsLoadingOrUnloading
    && IsBlockedByTraffic
    && DistanceToGoal > goalEps)
```

也就是只對「交通造成的異常停滯」計時。

---

# 真正可落地的方案：不要直接解 triple，改成這個流程

我建議你這樣做。

---

## Step 1：保留 pairwise speedCap

因為它便宜，而且對一般情況有效。

也就是說：

* 正常相遇
* 跟車
* 短距離禮讓

都還是由現有 TrafficArbiter 處理。

這層不要砍掉。

---

## Step 2：額外蒐集「誰因誰而停」

TrafficArbiter 在每 tick 算完後，不只輸出 speedCap，還要輸出：

* `blockedByBotId`
* `blockedReason`
* `desiredNextWaypoint`
* `actualGrantedWaypoint`

例如：

```csharp
bot.BlockedBy = otherBot;
bot.BlockedReason = BlockReason.ConflictAtWaypoint;
```

這樣之後就能建等待圖。

---

## Step 3：每 N tick 建 wait-for graph

不要每 tick 做全圖分析，太頻繁。
可以每 3~5 tick 做一次，或當 blocked bots 數量超過閾值才做。

### Graph construction

若：

* A 想進 wpX
* 但 B 目前佔用 wpX，或 B 已 claim wpX
* 導致 A 停止

則加邊：

**A → B**

---

## Step 4：偵測 cycle

只要 graph 裡有 cycle，就代表不是單純局部讓路，而是全局卡死。

### 解法

對環中的 bot 排序：

priority 可定義為：

[
Priority = w_1 \cdot Phase + w_2 \cdot Direction + w_3 \cdot WaitTime + w_4 \cdot Deadline/Urgency
]

你已有：

* Phase B > C > A
* vertical > horizontal
* FCFS

我建議再加一個：

* **waiting time aging**

也就是等越久，優先權慢慢上升。
這樣避免低優先車永遠被壓著。

---

## Step 5：環中只處罰一台，不要全部重規劃

這是關鍵。

很多人一看到 cycle 就全部 reroute，結果更亂。
正確做法是：

### 只選一個 loser

通常選：

* 最低優先序者
* 或最容易退讓者
* 或 reroute cost 最低者

讓他：

* 暫停一小段時間
* 或退到最近安全 waypoint
* 或重新規劃到下一個 turn point

這樣就能破環。

這個想法很像作業系統 deadlock recovery：
不用重排全部程序，只要打破一條循環邊即可。

---

# 你的離散圖最適合的是「局部讓路點」而不是全圖 reroute

你如果每次 deadlock 都從當前位置重跑整條 A*，成本高、也容易震盪。

我建議你在地圖中預先標記：

* turn point
* intersection buffer
* side pocket / waiting bay
* nearest safe node

然後 deadlock 發生時，不是整條路重畫，而是：

## 只做局部避讓

例如：

* 退回最近 turn point
* 進入最近 waiting node
* 跳過當前衝突路口後再接回原 path

這叫 **local reroute**，比 global reroute 穩很多。

你可以把它想成：

不是「我迷路了重導航」，而是「我先靠邊讓一下，再回原本路線」。

---

# 最重要的一點：不要讓 RL 直接輸出完整 reroute 路徑

這會炸掉。

RL 最多只要輸出這種低維決策：

* keep
* yield
* request_local_reroute
* request_priority_pass
* stop

真正的 reroute 由 rule-based planner 完成。

這樣做有三個好處：

## 1. action space 小

例如 4~6 個 action 就夠。

## 2. 可解釋

你論文比較好寫。
因為可以說：

> RL 負責高層交互策略偏好，圖搜尋模組負責可行路徑生成。

## 3. 可收斂

否則 policy 同時學：

* collision avoidance
* topological reroute
* multi-agent negotiation

太重。

---

# 我建議的最小可行版本（MVP）

你現在不要一次做太大。
先做這三個就有感。

---

## MVP-1：BlockedBy 記錄

TrafficArbiter 算完後，每台 bot 記：

* `BlockedBy`
* `DesiredWp`
* `WaitTicks`

---

## MVP-2：stall timeout

若 bot 因 traffic 被卡超過門檻：

* `RequestLocalReroute = true`

不是直接 global reroute。

---

## MVP-3：cycle detection

每幾個 tick 建等待圖，若有 cycle：

* 選一個 loser
* loser 退到最近 safe waypoint 或 local reroute

這三個加起來，通常就能把 triple deadlock 壓掉很多。

---

# 你論文裡最有價值的 contribution 會是什麼？

不是「我發明了一個完美多機器人全局最優算法」。
那太大，也難證明。

比較強、也比較能落地的說法是：

## 可行的 contribution framing

**A conflict-aware hierarchical routing framework for RMFS in discrete waypoint environments**

核心點是：

1. 保留 decentralized policy autonomy
2. 使用 pairwise arbitration 做高效率局部避碰
3. 加上 graph-based multi-agent deadlock detection
4. 以 timeout-triggered local reroute 解決 circular wait
5. 不擴大 agent action space，維持 RL 可訓練性

這個 framing 很合理，而且和你系統一致。

---

# 你目前設計的弱點，我直接挑出來

## 弱點一：只靠 phase priority 不夠

因為 priority 只能決定「誰比較重要」，不能保證**等待環一定被打破**。

---

## 弱點二：所有 bot 每 tick 各自獨立算，太短視

這就是你說的 chain braking 問題。
每個人都局部正確，但整體吞吐崩。

---

## 弱點三：若 reroute 太頻繁，會造成 path oscillation

也就是：

* A reroute
* 下一秒又想回原路
* 又被擋
* 再 reroute

所以你需要：

* cooldown time
* hysteresis
* minimum reroute interval

例如：

```csharp
if (currentTime - _lastRerouteTime > 1.5)
    RequestLocalReroute = true;
```

---

## 弱點四：若沒有 aging，低優先車會飢餓

所以 priority 要從靜態改成：

**static priority + dynamic aging**

---

# 我建議你的 priority function 長這樣

你可以用簡單線性分數：

[
P_i = \alpha \cdot PhaseScore_i + \beta \cdot DirectionScore_i + \gamma \cdot WaitTime_i + \delta \cdot ProgressPressure_i
]

其中：

* `PhaseScore`: B > C > A
* `DirectionScore`: 垂直 > 水平
* `WaitTime`: 等越久越高
* `ProgressPressure`: 越接近站點、越接近完成任務，可給更高權重

這樣不會只靠靜態 phase。

---

# 一個你可以直接放進系統的結構

## Bot 狀態

```csharp
public BotNormal BlockedBy;
public Waypoint DesiredWaypoint;
public int WaitTicks;
public bool RequestLocalReroute;
public double LastRerouteTime;
public double StallStartTime = -1;
```

---

## TrafficArbiter 輸出

```csharp
public class ArbitrationResult
{
    public double SpeedCap;
    public BotNormal BlockedBy;
    public Waypoint DesiredWaypoint;
    public bool HasConflict;
}
```

---

## Wait-for graph

```csharp
public class ConflictGraph
{
    private readonly Dictionary<BotNormal, List<BotNormal>> _edges = new();

    public void AddEdge(BotNormal waiter, BotNormal blocker)
    {
        if (!_edges.ContainsKey(waiter))
            _edges[waiter] = new List<BotNormal>();

        _edges[waiter].Add(blocker);
    }

    public List<List<BotNormal>> DetectCycles()
    {
        // DFS or Tarjan SCC
    }
}
```

---

## Resolve policy

```csharp
foreach (var cycle in cycles)
{
    var loser = cycle
        .OrderBy(b => GetDynamicPriority(b))
        .First();

    if (CanStepBackToSafeNode(loser))
        loser.RequestStepBack = true;
    else
        loser.RequestLocalReroute = true;
}
```

---

# 最後給你最務實的實作順序

## 第一階段：先救系統穩定

1. 保留 pairwise TrafficArbiter
2. 加 `BlockedBy`
3. 加 traffic-specific stall timeout
4. 加 local reroute cooldown

這階段就會明顯改善。

---

## 第二階段：處理 triple / circular wait

1. 建 wait-for graph
2. 每 N tick 做 cycle detection
3. 環中選 loser
4. loser 局部退讓或 reroute

這才是真正解 triple 的核心。

---

## 第三階段：再跟 MAPPO 整合

把這些衝突特徵餵給 observation：

* 是否被阻擋
* blocker phase
* local conflict count
* 是否位於 cycle
* 自己 wait time
* 前方 waypoint claim 數

然後讓 MAPPO 學：

* 何時主動 yield
* 何時請求 reroute
* 何時加速通過

而不是讓 MAPPO 直接求解整個多方協商。

---

# 最後一句最重要

**你不需要讓 RL 直接解 triple conflict。**
你需要的是：

> 用 graph 把多方衝突「表示出來」，
> 再用小成本的規則層把死鎖打破，
> 讓 RL 只學習在這種結構下如何做更好的局部選擇。

這樣才符合你的離散 waypoint 環境，也不會讓動作空間爆炸。

如果你要，我下一步可以直接幫你把這套設計落成：
**「TrafficArbiter + ConflictGraph + stall timeout + local reroute」的具體程式架構與偽代碼**，用你現在的 RMFS/RawSIM-O 風格來寫。
