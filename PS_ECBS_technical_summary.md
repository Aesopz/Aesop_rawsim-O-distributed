# PS-ECBS 技術摘要

本文整理 `PS-ECBS: A New Algorithm for Multi-Agent Path Finding With Optimal Task Assignment` 的技術精神。重點不是完整復刻作者的演算法，而是掌握它如何把「上層任務/位置選擇」與「下層多機器人避碰路徑規劃」放進同一個可回饋的迭代框架。

## 1. Paper 基本資訊

- 標題：PS-ECBS: A New Algorithm for Multi-Agent Path Finding With Optimal Task Assignment
- 作者：Cheng Zhang, Shixin Liu
- 期刊：IEEE Robotics and Automation Letters, Vol. 10, No. 10, October 2025
- 問題場景：Robotic Mobile Picking System / warehouse MAPF
- 核心問題：倉儲中 task assignment 與 path planning 不能完全分開看，因為一組看似距離最短的 assignment，可能在實際多機器人避碰路徑中造成更高 travel cost 或 makespan。

作者提出的 PS-ECBS，全名是 Position-Selection Enhanced Conflict-Based Search。它的核心做法是：

1. 用 MILP 先解一個忽略路徑衝突的 position/task selection 問題。
2. 把 MILP 選出的結果交給 ECBS，計算 collision-free path 的實際成本。
3. 如果 ECBS 實際成本比 MILP 理想成本高，就把這個差距回饋成新的 MILP constraint。
4. 重複迭代，直到目前最佳實際成本已經不大於 MILP lower bound。

這個框架的精神是：上層 assignment 的理想距離只是 lower bound，不能直接等同真實執行成本；必須用下層 path planner 的回饋修正上層決策。

## 2. 問題背景：Warehouse MAPF 的兩種位置選擇

作者把 RMPS 中的任務拆成兩類：

### Picking-up task

Robot 要從 storage area 選 shelf，搬到指定 picking station。這裡的 position selection 是「選哪些 shelves」。

若同一種商品存在於多個 shelves，上層 assignment 不能只問哪個 shelf 最近，還要考慮這些 shelves 被多個 robots 同時搬運時會不會造成路徑衝突。

### Putting-back task

Robot 從 picking station 搬著 shelf，要把它放回 storage area 的 empty position。這裡的 position selection 是「選哪些 empty positions」。

Putting-back task 的 goal 不一定預先指定，而且還會受到倉儲規則限制，例如同類商品通常要放在鄰近區域。這使得 endpoint selection 也成為 task assignment 的一部分。

## 3. Picking-up MILP：理想成本 z

在 picking-up task 中，作者先用 Manhattan distance 建立 shelf 到 picking station 的理想距離：

```text
ci = |Xi - Xp0| + |Yi - Yp0|
```

接著定義 binary decision variable：

```text
xi = 1 表示 shelf i 被選中
xi = 0 表示 shelf i 未被選中
```

MILP 的目標是最小化理想成本 `z`，並確保選到的 shelves 能滿足 order demand：

```text
min z
z >= sum(ci * xi)
sum(ail * xi) >= dl
xi in {0, 1}
```

這裡的 `z` 是忽略 robot collision 的理想距離。它只代表在靜態、單機或無衝突假設下的 assignment cost，因此作者明確指出：

```text
z <= cost
```

其中 `cost` 是把同一組 assignment 丟進 ECBS 後得到的 collision-free path cost。

這點是整篇 paper 最重要的觀念：MILP 的上層距離不是實際成本，而是 lower bound。

## 4. ECBS 驗證：實際 collision-free cost

MILP 選出 shelves 後，作者使用 ECBS 進行多機器人 collision-free path planning。

ECBS 是 CBS 的 bounded-suboptimal 變體，使用 focal search 加速搜尋。對 PS-ECBS 而言，ECBS 的角色不是重新決定 assignment，而是驗證目前 MILP assignment 在實際避碰路徑下的成本。

因此每一輪會得到兩個值：

- `zk`：第 k 輪 MILP 解出的理想 assignment cost
- `costk`：第 k 輪 assignment 經 ECBS 規劃後的實際 path cost

若 `costk` 明顯大於 `zk`，代表這組 assignment 在上層看起來便宜，但在下層路徑規劃中造成了額外成本。

## 5. Iterative feedback constraint：把實際成本回灌上層

PS-ECBS 的關鍵是 equation (6) 類型的額外 constraint。概念上，它在下一輪 MILP 中要求：

如果 solver 又選到同一組 binary assignment，那麼 `z` 不能再維持原本被低估的理想值，而必須至少反映上一輪 ECBS 得到的 `costk`。

paper 的 constraint 形式可理解為：

```text
z - costk >= (sum(xi for selected set Ik1) - |Ik1|) * G
```

其中：

- `Ik1` 是第 k 輪被選中的 decision variables 集合。
- `G` 是足夠大的常數。
- 當下一輪選到完全相同的 selected set 時，右側會變成 0，因此要求 `z >= costk`。
- 當下一輪選擇不同 assignment 時，右側會變成負的大數，constraint 幾乎不限制新解。

這個 constraint 的精神不是直接禁止舊 assignment，而是修正舊 assignment 的 lower-bound 成本，使它不再因為忽略路徑衝突而被低估。

## 6. UB/LB 收斂邏輯

PS-ECBS 在每輪維護兩個界：

```text
UB = min(costr), r = 1..k
LBk = zk
```

- `UB` 是目前看過的最佳實際 ECBS cost。
- `LBk` 是目前 MILP 能提出的理想 lower bound。

如果：

```text
UB - LBk > 0
```

代表理論上仍可能存在更好的 assignment，所以繼續迭代。

如果：

```text
UB <= LBk
```

代表目前最佳實際成本已經不大於 MILP lower bound，演算法停止。此時最佳解是目前所有 ECBS `cost` 中最小的那一輪 assignment。

這個停止條件的意義是：不是相信最後一輪 MILP 解，而是相信「已驗證過的最佳 ECBS 實際成本」與「剩餘 MILP lower bound」之間已經沒有改善空間。

## 7. Putting-back task：選 empty position

Putting-back task 的結構和 picking-up 類似，但 decision variable 改成 empty position：

```text
xj = 1 表示 empty position j 被選為 return goal
xj = 0 表示未被選
```

MILP 目標仍是最小化理想距離 `z`：

```text
min z
z >= sum(cj * xj)
sum(xj for j in Img) = Mg
xj in {0, 1}
```

其中 `Img` 表示某一組受限制的 empty positions，`Mg` 表示該組要選出的 return goals 數量。

作者強調 putting-back 不只是最近 empty slot 的問題，還要符合倉儲規則，例如同類 shelf 要回到相近區域。因此它同樣是 optimal task assignment + MAPF 的耦合問題。

## 8. Sorting principle：降低 makespan，而不是只看總距離

Putting-back task 中，作者加入 sorting principle 來降低最低完成時間。

paper 的例子指出，兩種 assignment 的 total cost 可能相同，但 makespan 不同。也就是說：

```text
sum of path costs 相同，不代表最後一台 robot 抵達時間相同
```

Sorting principle 的精神是把 return sequence / priority 與遠近 goal 做配對，讓較高優先權或較需要先處理的 shelf 分配到合適的遠端位置，藉此降低整體完成時間。

作者把 sorting principle + ECBS 稱為 lowest-time ECBS。它的目的不是改變 PS-ECBS 的 lower-bound feedback 架構，而是在 ECBS evaluation 階段減少不理想的 assignment/path 組合，讓搜尋更快收斂。

## 9. Conflict disappearing：goal collision 的處理

作者也修改 ECBS，加入 conflict disappearing。

在 warehouse layout 中，多個 robots 可能前往同一個 picking station 或 goal-adjacent area。如果 robot 到達 goal 後仍停在 goal cell，後續 robots 會和它產生 goal-position collision。

作者的處理是：

```text
robot 到達 goal 後，立即被 relocation 到附近空位，或在 planner 視角中消失
```

這讓 goal cell 不會長時間被已完成任務的 robot 佔用，避免多個 robots 在同一 goal 上發生衝突。

這個設計比較像 warehouse operation 的簡化假設：到站後的 queueing、卸載、停放行為被抽象掉，讓 MAPF 專注在 travel path conflict。

## 10. 實驗結論

paper 的實驗重點不是證明 PS-ECBS 的 path cost 一定比 ECBS-TA 小很多，而是證明它的 runtime 更穩定，尤其在 warehouse layout 和較大規模下更明顯。

作者給出的理由包括：

- MILP 先處理 position/task selection，避免 ECBS-TA 在 task assignment 上做大量枚舉。
- 每輪新增 constraint，會逐步修正被低估的 assignment。
- Sorting principle 可排除很多 makespan 較差的解。
- 隨 warehouse side length 增加，ECBS-TA runtime 明顯上升，但 PS-ECBS 維持較短 runtime。

因此 PS-ECBS 的優勢主要是「把 assignment 搜尋空間用 MILP 結構化，並用 ECBS 實際成本回饋修正」，而不是單純宣稱某個路徑規劃器比較強。

## 11. 對目前 M1G estimated path vs actual path 實驗的啟發

這篇 paper 對目前 M1G 追蹤實驗最有價值的部分，是它清楚區分了：

- 上層 assignment / optimization 使用的 ideal estimated cost
- 下層 path planning / execution 產生的 actual cost

對 M1G 而言，目前上層在決策當下使用的 bot-to-pod、pod-to-station distance/time，可以視為一種 lower-bound style estimate。它可能沒有完整反映：

- WCHA* 或實際 planner 因避碰產生的 detour
- robot 數量增加造成的 congestion
- station queue point 與 station location 的邊界差異
- lift-up 到 station queue entry 之間的真實 leg2 travel time
- 等待時間與旅行時間的切分

因此，我們不需要把 PS-ECBS 完整搬進 M1G。更實用的借鏡是：

1. 把 M1G 上層 estimated path/time 明確記錄成決策當下的理想成本。
2. 把 execution 中實際發生的 leg travel time 明確切出來。
3. 在不同 robot 數量下比較 estimated vs actual gap。
4. 用 congestion escalation 證明上層估計在高密度場景下會更偏離真實執行。

這正好支撐目前的實驗命題：上層最佳組合不代表真實執行成本最小，因為上層估計缺少下層路徑規劃與擁擠回饋。

## 12. 可以借用的技術精神

可以借用：

- 將上層成本視為 lower bound，而不是 ground truth。
- 同時記錄 estimated cost 與 actual execution/path cost。
- 用 actual cost 反證 assignment cost 的低估。
- 設計 congestion-controlled experiment，觀察 gap 是否隨 robot 數量上升。
- 將 queue boundary 定義清楚，避免把等待時間混入 travel time。

暫時不需要借用：

- 完整 MILP cut constraint implementation。
- 完整 PS-ECBS iterative solver。
- ECBS-TA benchmark reproduction。
- Paper 中的 sorting principle，除非後續研究目標改成 makespan-aware return-position assignment。
- Conflict disappearing，除非我們要重定義 station/goal occupancy 的 MAPF 模型。

## 13. 一句話總結

PS-ECBS 的精神是：先讓上層 optimization 用簡化成本快速產生候選 assignment，再用下層 collision-free path planning 揭露真實成本，並把真實成本回饋到上層，逐步修正那些「看起來便宜、實際很貴」的決策。

對目前 M1G 研究而言，最重要的不是復刻 PS-ECBS，而是用同樣的思想嚴格證明 estimated path/time 與 actual execution path/time 之間的差距，並觀察這個差距如何隨 robot congestion 放大。
