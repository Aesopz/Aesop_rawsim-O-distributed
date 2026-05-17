你是一名熟悉 RAWSim-O、M1G/OJOOPT、WCHA*、ReservationTable、RMFS、multi-agent path planning、task allocation、Gurobi MILP 與實驗驗證的研究型工程代理。

請基於目前專案既有的 MAPFUU-inspired congestion-aware cost estimator，重新修正方法論並設計下一階段實驗。此任務重點不是立刻追求複雜實作，而是把研究方向修正為符合 RAWSim-O / WCHA* 物理語意的方法，並建立能檢驗 TP、PO、OD、RD 是否改善的驗證流程。

============================================================
一、研究目標
============================================================

目前研究目標是：

在 RAWSim-O 的線上 M1G/OJOOPT 決策中，將下層 WCHA* path planning 的實際執行成本回饋到上層 task allocation / pod selection / station assignment，使 M1G 不再只根據靜態距離做決策，而能納入未來路徑阻塞、等待、station queue 與工作站負載狀態。

最終希望達成：

1. 提升 TP
2. 提升 PO
3. 降低 OD
4. 降低 RD
5. 不顯著增加 M1G decision time
6. 不顯著增加 cost matrix build time

若無法同時提升 TP / PO 與降低 OD / RD，最低可接受定位是：

在 TP / PO 不退化的前提下降低 OD / RD，即 throughput-preserving efficiency improvement。

============================================================
二、方法論修正核心
============================================================

目前專案已有一套 MAPFUU-inspired estimator，包含：

- kinematic baseline + residual lookup
- tCursor time chaining
- qrps joint MILP option
- probabilistic density / Poisson-binomial option
- traversal / congestion feature logging
- M1G objective cost replacement

但此方法需要修正，原因是 MAPFUU 與 RAWSim-O 的物理假設不同。

MAPFUU 的不確定性來源是：

多台 robot 可以在同一 edge / region 中共存，彼此繞行、避讓、偏離理想軌跡，導致 edge traversal time 變成隨機值。

RAWSim-O / WCHA* 的不確定性來源是：

每個 node / grid cell 同一時間最多只能被一台 bot 佔用。其他 bot 不能在同一 edge 內繞行，而是必須等待進入下一個 node。

因此，請不要再把本研究的主要模型描述為：

edge traversal time uncertainty

而應修正為：

node-entry blocking / waiting delay

也就是說，本研究不應把壅塞主要視為「edge 內部通行時間變慢」，而應視為「嘗試進入下一個 node / queue / station entrance 前產生等待」。

請將方法定位改為：

WCHA*-informed Blocking-Aware Effective Distance
簡稱：BAED

中文：

WCHA* 感知之阻塞感知等效距離

============================================================
三、修正後的方法論定義
============================================================

請將原本模型：

T(edge)
= kinematic baseline
+ congestion residual(edge, carrying, density)

修正為：

T(u → v)
= MoveTime(u, v, carrying)
+ EntryDelay(v, arrivalAttemptTime, carrying, blockingState)

其中：

MoveTime(u, v, carrying)
代表在沒有阻塞時，bot 從 u 移動到 v 的物理移動時間。

arrivalAttemptTime
代表 bot 按照物理移動時間預計抵達 next node v 的時刻。

EntryDelay
代表 bot 在該時刻嘗試進入 next node v 時，因為 reservation、queue、station entrance、merge point 或 downstream blocking 而產生的預期等待時間。

最後，將等待時間轉換為等效距離：

BAED
= physicalDistance
+ referenceSpeed × expectedEntryDelay
+ stationModifier
+ optional stop-and-go penalty

請注意：

BAED 應該是 distance-like cost，而不是單純 seconds cost。這樣才能最大程度保留原始 M1G objective 中距離權重與 order reward / station capacity penalty 的相對語意。

============================================================
四、保留與暫停的研究元件
============================================================

請保留：

1. kinematic baseline
2. tCursor time chaining
3. robot → pod → station two-leg estimation
4. T_lift / pod pickup time
5. Phase C separable M1G cost hook
6. candidate-level logging
7. system-level KPI validation

請暫停作為主方法：

1. qrps joint MILP
2. Poisson-binomial density composition
3. Gaussian local density as main congestion source
4. full MAPFUU alignment narrative
5. neural predictor / GNN / complex learning model

這些可以保留作為 ablation 或 future work，但不應作為下一階段主線。

主線應該是：

Phase C separable M1G integration
+ blocking-aware entry-delay model
+ effective distance unit
+ station starvation / overload modifier

============================================================
五、為什麼要加入 station modifier
============================================================

若只最小化 BAED，模型可能較容易降低 OD / RD，但不一定提升 TP / PO。

因此需要納入 station utilization 的修正項，避免方法過度追求低路徑成本而造成 station starvation。

請設計：

AdjustedCost(r,p,s)
= BAED(r,p,s)
- η × StationStarvationRisk(s)
+ γ × StationOverloadRisk(s)

其中：

StationStarvationRisk(s)
表示 station s 缺少 inbound pod、可能導致 picker idle 的程度。

StationOverloadRisk(s)
表示 station s 已有太多 inbound pods 或 queue 壓力過高，可能造成 station entrance blocking 的程度。

直覺：

- station 快缺 pod：降低送往該 station 的成本，鼓勵補貨，提高 PO / TP
- station 已過載：提高送往該 station 的成本，避免 queue blocking，降低 OD / RD

station modifier 必須可控，不能大到壓過 BAED 本身，否則可能造成過度派車到某站。

============================================================
六、演算法層級流程
============================================================

請依照以下概念設計方法。

在每個 M1G decision epoch：

1. 分類 agents

external agents：
已經在執行任務、已有 WCHA* path / reservation 或明確 destination 的 robots。

internal agents：
本次 M1G 要安排任務的 candidate robots。

2. 建立 rolling blocking field

利用 external agents 的 WCHA* committed reservations 建立未來 time-bucket blocking field。

blocking field 應回答：

若某 candidate bot 預計在未來時間 t 嘗試進入 node v，該 node / 附近 station / queue / downstream 是否可能造成阻塞？

3. 估計 robot → pod

沿 guide path 或 shortest path 逐段推進：

tCursor = decision time

對每個 next node v：
- 計算 MoveTime(u,v,carrying=false)
- arrivalAttemptTime = tCursor + MoveTime
- 查詢 BlockingState(v, arrivalAttemptTime)
- 估計 EntryDelay
- tCursor = arrivalAttemptTime + EntryDelay

得到：

t_robot_arrive_pod

4. 加入 pickup / lift time

t_leg2_start = t_robot_arrive_pod + T_lift

5. 估計 pod → station

沿 pod → station path 逐段推進：

對每個 next node v：
- 計算 MoveTime(u,v,carrying=true)
- arrivalAttemptTime = tCursor + MoveTime
- 查詢 BlockingState(v, arrivalAttemptTime)
- 估計 EntryDelay
- tCursor = arrivalAttemptTime + EntryDelay

得到：

t_pod_arrive_station

6. 轉換為 M1G objective cost

將：

physicalDistance + referenceSpeed × totalEntryDelay + stationModifier

作為 M1G 的 routing cost coefficient。

7. 解 M1G

M1G 的變數與限制式第一階段不要改，僅替換 cost coefficient。

8. WCHA* 執行真實路徑

由 RAWSim-O 既有 WCHA* / reservation table 實際執行。

9. 記錄預測與實際差異

記錄 predicted BAED、predicted arrival times、actual execution cost、actual arrival times、actual waiting。

============================================================
七、檢核方式：不要先看 KPI，要先看 decision quality
============================================================

不要一開始直接跑長時間 KPI，因為系統 KPI 會受到太多因素干擾。

第一階段必須先做 candidate-level decision-quality validation。

在同一個 decision epoch 中，取 top-k candidates，例如 k = 5 或 10。

對每個 candidate 同時計算：

1. original distance cost
2. old congestion estimator cost
3. new BAED cost
4. actual WCHA* execution cost 或 replay / post-validation cost

比較三種 ranking 與 actual ranking 的接近程度。

至少檢查：

1. Spearman correlation
2. Kendall tau
3. top-1 regret
4. selected candidate actual cost

成功條件：

BAED 的 ranking 必須比 original distance ranking 更接近 actual WCHA* execution cost。

BAED 的 top-1 regret 必須小於 original distance。

BAED selected candidate 的 actual cost 必須低於或不高於 original distance selected candidate。

如果 candidate-level validation 沒有贏，不要進入大規模 KPI 實驗。

============================================================
八、KPI 驗證方式
============================================================

通過 decision-level validation 後，再做 system-level KPI 實驗。

第一輪：

5 seeds × 7200s

比較：

B0: Original M1G distance
B1: Current residual density estimator
B2: BAED only
B3: BAED + delay cap
B4: BAED + station modifier
B5: BAED + delay cap + station modifier

KPI：

1. TP，越高越好
2. PO，越高越好
3. OD，越低越好
4. RD，越低越好
5. M1G decision time
6. cost matrix build time
7. station idle time
8. station queue length
9. average waiting before edge / node entry

成功條件：

B5 相對 B0：
- TP >= baseline
- PO > baseline
- OD < baseline
- RD < baseline

B5 相對 B1：
- OD / RD 不退步
- TP / PO 有改善或至少不退化

若 B5 在 7200s 成功，才進入：

5 seeds × 28800s

目的：

檢查長時間 compounding 是否反轉。

最後最佳版本再跑：

15 seeds × 7200s paired validation

統計檢定：

使用 paired seed design。

報告：

1. mean delta
2. median delta
3. win count
4. Wilcoxon signed-rank one-sided p-value
5. effect size

方向：

TP: greater is better
PO: greater is better
OD: lower is better
RD: lower is better

============================================================
九、Ablation 設計
============================================================

請至少設計以下 ablation：

A0: original distance
A1: kinematic baseline only
A2: current residual density estimator
A3: BAED entry-delay estimator
A4: BAED + delay cap
A5: BAED + station modifier
A6: BAED + delay cap + station modifier

每個 ablation 的目的：

A1 vs A0：
檢查 kinematic / physical movement correction 是否有效。

A3 vs A2：
檢查 blocking-aware 語意是否比 density residual 更符合 RAWSim-O。

A5 vs A3：
檢查 station modifier 是否能改善 TP / PO。

A6 vs A5：
檢查 delay cap 是否能避免過度保守與長期 compounding 反轉。

============================================================
十、主要成功與失敗判準
============================================================

方法成功的最低標準：

1. candidate-level ranking 比原始 distance 更接近 actual WCHA* cost
2. system-level OD / RD 下降
3. TP / PO 不退化

方法理想成功標準：

1. TP 上升
2. PO 上升
3. OD 下降
4. RD 下降
5. decision time overhead 可接受
6. cost matrix build time 可接受

方法失敗判準：

1. BAED ranking 不比 distance ranking 更接近 actual cost
2. OD / RD 下降但 TP / PO 顯著下降
3. TP / PO 上升但 OD / RD 顯著惡化
4. 只有某一組超參有效，敏感度過高
5. decision time overhead 過大，失去 online decision 可行性

============================================================
十一、方法論敘述
============================================================

請使用以下研究敘述作為準則：

本研究將原本 MAPFUU-inspired density residual estimator 修正為 RAWSim-O / WCHA* 一致的 blocking-aware cost estimator。由於 RAWSim-O 採用硬式互斥路徑規劃，壅塞主要表現為進入下一節點前的等待，而非 edge 內部繞行造成的通行時間變異。因此，本研究以 WCHA* committed reservations 建立時間相依 blocking field，並沿 robot-to-pod 與 pod-to-station 路徑逐段推進，在每個 next node 的預估抵達時間查詢 entry delay，將該等待延遲轉換為等效距離回饋至 M1G。同時加入 station starvation / overload modifier，使成本回饋不僅降低路徑延遲，也維持 workstation utilization，以達到提升 TP / PO 並降低 OD / RD 的目標。

============================================================
十二、本輪不要做的事
============================================================

本輪請不要將以下內容作為主方法：

1. full MAPFUU implementation
2. Poisson-binomial as main method
3. Gaussian local density as main blocking source
4. qrps joint MILP as main method
5. NN / GNN / deep learning predictor
6. full WCHA* validation for all candidates
7. only prediction MAE validation without decision ranking
8. only KPI validation without candidate-level validation

這些可以保留為 future work、ablation 或附錄，但不是下一階段主線。

============================================================
十三、最終交付
============================================================

請產出：

1. 修正後的方法設計文件
2. BAED 的演算法流程圖或偽程式
3. candidate-level validation 設計
4. KPI 實驗矩陣
5. ablation 設計
6. 成功 / 失敗判準
7. 若要實作，請優先實作最小可行版本：
   BAED + delay cap + station modifier + decision ranking logging

請優先回答：

此方法是否能更合理地改善 TP / PO / OD / RD？
若不能，請指出最可能失敗的原因與需要觀察的診斷指標。