# M1G-t Top-k Counterfactual Path Planning Experiment Notes

日期：2026-05-08  
分支：`leg-test`  
工作目標：用作者 M1G 的線上決策架構作基礎，建立一個小規模、可追蹤、可重播的實驗框架，證明 M1G 在 MILP 內只使用 ideal / shortest-path travel estimate，沒有把決策後 WCHA*n path planning 的多車衝突、繞路、等待成本納入求解，因此 MILP rank-1 不一定是實際執行成本最低的解。

## 研究脈絡

`online.pdf` 的核心精神是線上決策：系統在每個 decision tick 根據當下狀態做 order assignment / pod selection / task allocation，然後把任務交給底層 path planning 執行。

目前作者的 M1G 求解重點是：

- `OA`：Order Assignment。
- `PS`：Pod Selection。
- `TA`：Task Allocation。

但 M1G 的 objective 裡面，travel cost 仍是由靜態估計距離或理想最短路徑成本提供。這個成本沒有把真正執行時的 path planning 納入 MILP，例如：

- 多台 robot 同時出發造成的路徑衝突。
- WCHA*n 因 reservation / window / queue 而改走非最短路。
- robot 等待前車、避讓或排隊造成的時間成本。
- path planning 真實路徑與 MILP estimated path 不一致。

因此我們建立 M1G-t，不是要直接取代 M1G，而是作為 testing copy，用來產生 controlled counterfactual evidence：同一個 decision tick 下，MILP 看起來最好的解，放到真實 path planning 執行後，不一定仍然最好。

## 已建立的地基

### 1. M1G-t 測試版 order batching manager

新增檔案：

- `RAWSimO.Core/Control/Defaults/OrderBatching/M1GTManager.cs`

M1G-t 是從 M1G 複製出來的 testing manager，保留 M1G 原本的建模精神，但加上 top-k candidate collection、指定 candidate replay、實驗用 trace 與 early stop。

相關配置：

- `M1GTConfiguration`
- `OrderBatchingMethodType.GM1T`
- `Material/Instances/CoreBenchmark/m1gt_rank1.xconf`
- `Material/Instances/CoreBenchmark/m1gt_rank2.xconf`
- `Material/Instances/CoreBenchmark/m1gt_rank3.xconf`
- `Material/Instances/CoreBenchmark/m1gt_rank4.xconf`
- `Material/Instances/CoreBenchmark/m1gt_rank5.xconf`

每個 rank config 只改 `ValidationCandidateRank`，用同一個 layout / setting / seed 重跑，使五次 replay 對應同一個 decision tick 的 top-5 candidate。

### 2. Top-k distinct transport candidates

目前 top-k 不是完整 MILP solution pool 的所有變數差異，而是刻意定義成 transport signature 不同的解。

signature 使用：

- `yrp`: robot to pod，也就是哪台 robot 去搬哪個 pod。
- `xps`: pod to station，也就是哪個 pod 被送到哪個 output station。

no-good constraint 目前只針對：

```text
candidate.Xps + candidate.Yrp
```

這樣做的理由是：我們要驗證 path planning 差異，所以只改 `yaos` 或其他非 transport 變數沒有意義。不同的 `robot -> pod` / `pod -> station` 組合才會在 WCHA*n 下產生不同實際路徑、等待與壅塞成本。

目前已驗證：

- `m1gt_top5_candidates.csv` 中有 5 筆 candidate。
- 五筆 candidate 的 transport signature 沒有重複。
- 五個 replay run 的 top-5 candidates hash 一致，代表每次 replay 看到的是同一組 top-5。

### 3. Counterfactual replay

每次 replay 的流程是：

1. 使用同一 layout、setting、seed。
2. 在 `ValidationDecisionId` 指定的 decision tick 呼叫 M1G-t。
3. MILP 依 no-good constraint 連續解出 top-k distinct transport candidates。
4. 根據該 run 的 `ValidationCandidateRank` 選取其中一組 candidate。
5. 強制套用該 candidate 的 `xps / yrp / yaos / dops` 決策。
6. 等該批次被選中的 transport legs 執行完成後停止模擬。
7. 輸出該 candidate 的 estimated / actual path cost 與比較表。

目前輸出資料夾範例：

- `Results/M1GTTop5QueueStrict`

重要輸出：

- `m1gt_top5_candidates.csv`
- `m1gt_selected_executable_candidate.csv`
- `m1gt_decision_snapshot_fingerprint.csv`
- `m1g_path_estimate_actual_segments.csv`
- `m1g_path_estimate_actual_summary.csv`
- `m1g_waypoints.csv`
- `m1gt_counterfactual_rank_comparison.csv`
- `m1gt_counterexample_report.csv`
- `m1gt_counterfactual_dashboard.html`

### 4. Decision snapshot fingerprint

為了讓五次 replay 不只是口頭說「同 seed」，M1G-t 目前在 validation decision tick 輸出：

- `m1gt_decision_snapshot_fingerprint.csv`

目前 fingerprint 包含：

- decision time。
- bots：位置、座標、是否攜帶 pod、目前 task。
- pods：位置、是否被 bot 攜帶、座標。
- pending orders。
- available robots `Ra`。
- unavailable robots `Rb`。
- available pods `Pa`。
- unavailable pods `Pb`。
- `PodToBot`。
- station capacity map `Cs`。

這個 fingerprint 的意義是：五個 candidate replay 不是五個不同初始條件，而是在同一 decision snapshot 下，強制執行不同 candidate 的 counterfactual world line。

目前已驗證：

```text
rank1..rank5 snapshot_sha256 完全一致
```

代表至少在我們目前記錄的狀態維度上，五次 replay 的 decision tick 狀態一致。

## Estimated path / actual path 的追蹤框架

### 1. Trace manager

核心檔案：

- `RAWSimO.Core/Metrics/M1GPathTrace.cs`

這個 trace module 追蹤每個 M1G-t selected transport 的兩段 path：

- `BotToPod`
- `PodToStation`

每個 segment 會記錄：

- MILP decision id。
- segment id。
- bot id。
- pod id。
- station id。
- estimated start / end waypoint。
- estimated travel time。
- estimated objective cost。
- estimated path nodes。
- actual start / end time。
- actual duration。
- actual distance。
- actual wait time。
- actual turn count。
- actual start / end waypoint。
- actual path nodes。
- visual tail endpoint。

### 2. Leg1: bot to pod

leg1 的語意：

```text
從 robot 在 decision / dispatch 時的 waypoint 出發，到 pod pickup / lift-up 完成為止。
```

目前 estimated leg1：

- 使用 bot reference waypoint 到 pod reference waypoint。
- 加入 bot 的 pod transfer time，讓 estimated 範圍與 actual 範圍都包含 pickup / lift-up。

目前 actual leg1：

- 從 bot 真正開始執行該 trip 起算。
- 到 `BotPickupPod` 完成時 close trip。

### 3. Leg2: pod to station queue boundary

這是目前最重要的嚴謹性修正。

原先問題：

- M1G estimated `pod to station` 使用 `station.Waypoint`，也就是 station process point。
- actual leg2 則希望只算到 station queue point，不能把 station queue waiting 算進 travel cost。
- 這會造成 estimated / actual endpoint 不一致。

目前 M1G-t 已改成：

```text
estimated leg2 endpoint = station queue boundary waypoint
actual leg2 endpoint    = station queue boundary waypoint
visual tail endpoint    = station process point
```

其中 `station queue boundary waypoint` 來自 output station 的 `Queues` 結構，而不是矩形 queue zone 內任意 waypoint。

這樣的意義是：

- travel cost 只計算到進入 station queue 的邊界。
- station queue 內等待、向前推進、進站處理不混入 leg2 actual cost。
- 但為了視覺化不跳點，仍然會繼續追蹤 visual tail 到 process point。

### 4. Visual tail

前一版圖上曾出現 robot 從 queue point 直接跳到 station point 的問題。現在處理方式是：

1. actual leg2 到 queue boundary 時，成本 segment 完成。
2. trace id 不立即丟掉，而是轉成 visual-only trace。
3. robot 後續繼續在 queue 中移動。
4. 到 `BotPutItems` / station process point 時，補上 visual tail endpoint。
5. actual cost 不增加，但 visual path 連續。

這讓 dashboard 可以觀察完整路徑，不會有突兀跳點，同時不污染 leg2 travel cost。

## API / 控制流程串接

### 1. Controller entry

`OrderBatchingMethodType.GM1T` 會建立：

```text
M1GTManager
```

這代表 M1G-t 是標準 RAWSim-O controller pipeline 的一部分，不是額外寫一個外部 mock simulator。

### 2. Early stop

M1G-t 的目標不是跑完整 7200 或 28800 ticks，而是單批次 counterfactual validation。

停止條件：

```text
該 validation batch 中所有 selected leg2 都到達 station queue boundary，
且 visual tail 都到達 station process point 後，request stop。
```

相關狀態：

- `_m1gtPendingLeg2Keys`
- `_m1gtPendingLeg2VisualKeys`
- `StopRequested`

這個設計的意義是：

- 只比較該 batch 的實際 path execution cost。
- 不讓後續新訂單、新任務、新批次污染比較。
- 每個 candidate 都只驗證「如果當下選這組解，這批 transport 實際執行成本會是多少」。

## Visualization

新增工具：

- `Tools/M1GTVisualization/generate_m1gt_dashboard.ps1`

輸出：

- `m1gt_counterfactual_dashboard.html`

dashboard 目前支援：

- 比較 MILP rank 與 actual rank。
- 顯示每個 candidate 的 actual total duration。
- 顯示 wait time。
- 顯示每個 rank 的 map path。
- 同一 rank 內用不同顏色區分不同 robot。
- `BotToPod` / `PodToStation` 使用不同線型。
- 使用 `m1g_waypoints.csv` 還原 waypoint 座標與連線。

## 目前已得到的關鍵反例

最新嚴格 queue-boundary 結果資料夾：

- `Results/M1GTTop5QueueStrict`

反例摘要：

```text
MILP rank 1 actual rank = 3
actual best candidate   = rank 2
rank1 actual duration   = 222.580100984334
rank2 actual duration   = 180.37602451262
rank1 比 actual-best 慢 = 42.204076471714 秒
rank1_is_actual_best    = False
```

這代表：

```text
M1G MILP objective 下的 rank-1 solution，
在 WCHA*n 真實 path execution 後，
不是該 top-5 candidate set 裡 actual execution cost 最低的解。
```

這正是我們要證明的現象：M1G 的 ideal / shortest-path estimate 忽略 path planning 執行層的壅塞與等待成本，會導致 MILP 排序和真實執行排序不一致。

## unusedDopsPods 清理為什麼會讓某些解少一個 transport

這件事很重要，因為它影響「MILP 原始解」和「實際可執行解」的差異。

M1G 的 MILP 會選出：

- `xps`: pod 被派到 station。
- `yrp`: robot 被派去搬 pod。
- `yaos`: order 被派到 station。
- `dops`: order 的需求由哪個 pod/station 支援。

但是 MILP 解出來後，程式還會在模型外重新產生實際 picking allocation，也就是 `ziops`。這段邏輯會根據實際 inventory availability 與 order demand，判斷哪些 pod 真的有被用來 fulfill order。

流程上會先收集：

```text
dopsPodsSelected = MILP 宣稱會用到的 pod
dopsPodsUsed     = 模型外 ziops 實際分配後真的用到的 pod
unusedDopsPods   = dopsPodsSelected - dopsPodsUsed
```

如果某個 pod 出現在 MILP 的 transport selection 裡，但在後續模型外 `ziops` 分配中沒有真的供應任何 item，程式就會把它清掉：

```text
IsdeVarNameyrp.Remove(...)
BottoPod.Remove(...)
ReleasePod(...)
UnregisterInboundPod(...)
IsdeVarNamexps.Remove(...)
```

因此某些 candidate 原本 MILP 有 6 組 transport，但清理後 executable candidate 只剩 5 組。

這不是 path planning 造成的，而是 M1G 原始流程中「MILP 解」到「實際可派發任務」之間還有一層 post-processing。它的意義是：

```text
MILP 選了某個 pod/robot/station transport，
但後續實際 item allocation 發現該 pod 沒有必要送去 station，
所以這個 transport 被取消。
```

目前我們沒有隱藏這個現象，而是在：

- `m1gt_selected_executable_candidate.csv`
- `m1gt_counterfactual_rank_comparison.csv`

保留：

- `original_xps_count`
- `original_yrp_count`
- `executable_xps_count`
- `executable_yrp_count`
- `executable_xps`
- `executable_yrp`

這樣可以清楚區分：

```text
MILP original candidate
vs
post-processing 後真正執行的 executable candidate
```

### 這對論文實驗的影響

如果我們的實驗命題是：

```text
在 M1G 實際系統流程下，MILP rank 與 actual execution rank 可能不一致。
```

那目前做法是合理的，因為它忠實保留 M1G 從 MILP 到 dispatch 的原始 post-processing。

但如果命題要更嚴格變成：

```text
五個 MILP 原始 top-k 解，每一組都完整保留同數量 transport，逐一離線驗證真實 path cost。
```

那下一步需要新增 candidate filter：

```text
只接受 post-processing 後 executable_xps / executable_yrp 數量仍完整的 candidate。
```

或者要把 `ziops` 後處理也納入 top-k candidate generation，使 top-k ranking 的對象從一開始就是 executable transport plan，而不是 raw MILP solution。

## 目前實驗框架的意涵

這個框架已經建立了三層證據：

### 第一層：M1G objective 的 travel cost 是估計成本

M1G-t 會記錄 MILP 當下使用的 estimated path / cost。這代表我們可以看到 M1G 在決策時「以為」每個 transport 的成本是多少。

### 第二層：WCHA*n 實際執行會產生不同成本

actual trace 會記錄 robot 真正走過的 path nodes、duration、wait time、turn count。這讓我們可以量化：

- estimated vs actual time gap。
- estimated path vs actual path 差異。
- 等待時間占比。
- 哪個 leg 造成主要落差。

### 第三層：MILP 排序和 actual 排序會反轉

top-k counterfactual replay 顯示，同一 decision snapshot 下，MILP rank-1 不一定 actual-best。這比單純說 estimated < actual 更強，因為它證明：

```text
忽略 path planning 不只是造成 cost underestimation，
而是可能造成 wrong decision ranking。
```

這正好支撐後續論文主張：若要改進 online decision framework，應該把 path planning 層的 congestion-aware cost feedback 納入 OA / PS / TA 求解，或至少用更接近 WCHA*n 執行成本的 surrogate cost 取代靜態 shortest-path estimate。

## 目前已驗證狀態

最近一次 build：

```text
dotnet build RAWSimO.CLI/RAWSimO.CLI.csproj -c Debug -p:Platform=x64
```

結果：

```text
Build succeeded
0 errors
```

最近一次五 rank replay：

```text
Results/M1GTTop5QueueStrict
```

驗證重點：

- top-5 candidates hash 一致。
- decision snapshot hash 一致。
- top-5 transport signature 無重複。
- selected executable `xps/yrp` 與 trace 中的 segment 對得上。
- estimated leg2 不再以 station process point 為終點。
- actual leg2 不再以 station process point 為成本終點。
- visual tail 完成，dashboard 不再直接跳點。
- 找到 MILP rank-1 不是 actual-best 的反例。

## 尚未完全解決或下一步建議

### 1. 嚴格處理 post-processing 後 transport 減少

目前 rank 2 / rank 5 有出現：

```text
original transport count = 6
executable transport count = 5
```

這來自 `unusedDopsPods` 清理。若要讓每個 candidate 都是同樣完整的 transport batch，下一步應該新增：

- executable candidate filter。
- 或 top-k collection 時直接用 post-processed executable signature 做排名。

### 2. 區分 actual cost path 與 visual path

目前 `actual_path_nodes` 會包含 visual tail，用於 dashboard 連續顯示。雖然 cost 已經在 queue boundary 截斷，但欄位名稱可能讓人誤解。

建議之後拆成：

- `actual_cost_path_nodes`
- `visual_path_nodes`

這樣論文分析資料會更乾淨。

### 3. 擴大實驗

目前小規模實驗已找到反例。下一步可以控制：

- robot 數量。
- order density。
- station 數量。
- WCHA*n window / reservation settings。
- congestion level。

用多組 seed 統計：

- rank-1 不是 actual-best 的比例。
- MILP rank 與 actual rank 的 Kendall tau / Spearman correlation。
- estimated vs actual gap distribution。
- wait-time share。
- leg1 / leg2 哪一段造成主要誤差。

## 一句話總結

目前我們已經建立了一個 M1G-t counterfactual replay framework：它可以在同一 decision snapshot 下取得 top-5 distinct transport candidates，逐一強制 replay，追蹤 bot-to-pod / pod-to-station-queue 的 estimated 與 actual execution cost，並已經找到 MILP rank-1 不是 actual-best 的反例。這個框架的研究意義是證明 M1G 忽略 path planning 執行成本不是單純低估時間，而是會實際改變解的優劣排序。
## 2026-05-10 M1G-time PP-Include 7200-tick Rolling Oracle 實作紀錄

### 目前正在跑的實驗

實驗目標是比較：

```text
M1G-time
vs
M1G-time(pp include)
```

兩者都使用同一個 10-bot core benchmark、同一個 7200-tick setting、同一個 seed。

Baseline 是原始 `m1g_time.xconf` 完整跑 7200 tick。Treatment 則使用 rolling top-5 actual-cost replay oracle：每次 M1G-time 做決策時，固定先前已選的 oracle policy，對當前 decision 的 top-5 candidate 各自 replay 到 selected bot 完成 bot-to-pod / pod-to-station queue boundary，然後用實際 PP 執行後的 travel duration 重新計算原 M1G-time objective。

核心 actual objective 定義：

```text
actual_objective =
  sum(actual bot-to-pod / pod-to-station duration)
  + objective_order_term
  + objective_capacity_term
```

這裡的 actual travel 是 selected executable transport segments 的 duration 加總，不是 batch makespan。若要分析「最後一台 bot 完成造成的阻塞時間」，後續應另加 `actual_batch_makespan = max(leg2 actual_end_time) - decision_time`。

### 主要新增 / 修改檔案

```text
RAWSimO.Core/Configurations/MethodConfigurationsOB.cs
RAWSimO.Core/Control/Defaults/OrderBatching/M1GTManager.cs
Tools/M1GTReplay/Invoke-M1GTimeTop5Replay.ps1
Tools/M1GTReplay/Summarize-M1GTimeTop5Replay.ps1
Tools/M1GTReplay/Invoke-M1GTimeRollingOracle.ps1
Tools/M1GTReplay/Invoke-M1GTime7200PPIncludeComparison.ps1
Results/m1g_time_vs_ppinclude_7200_20260510/experiment_purpose_summary.md
```

### 實作重點

- `M1GTConfiguration` 新增 `ForcedDecisionPolicyPath`，讓 replay 可以強制套用先前 oracle 已選擇的 decision policy。
- `M1GTManager` 支援 forced prior decisions；validation decision 仍可收集 top-k candidate 並強制指定 candidate rank。
- top-k candidate manifest 與 selected executable candidate CSV 增加 order term、capacity term、estimated executable travel 等欄位。
- `Summarize-M1GTimeTop5Replay.ps1` 會用 executable xps/yrp 對應的 bot/pod/station key 過濾 actual path trace，避免先前 forced decisions 的 segment 污染當前 decision 的 actual cost。
- rolling oracle 會逐 decision 寫出 `oracle_policy.csv`、`oracle_decision_trace.csv`、`oracle_conclusion.md`。
- rolling oracle 支援 resume：若已存在 policy/trace，會從下一個 decision 繼續。
- rolling conclusion 寫檔加入 retry / temp-file replace，避免 Windows 檔案短暫鎖定中斷長跑。
- 新增 7200 比較 wrapper，完成後會自動比較 baseline 與 PP-include final replay KPI，並產出 `m1g_time_ppinclude_7200_conclusion.md`。

### 目前已完成的 baseline 結果

Baseline run:

```text
Results/m1g_time_vs_ppinclude_7200_20260510/baseline_m1g_time
```

Baseline 7200-tick KPI：

```text
orders_completed       = 648
orders_per_hour        = 324
system_order_pile_on   = 3.01395348837209
order_distance_m       = 23.924085873378
total_distance_m       = 15502.807645949
station_arrivals_total = 344
wait_time_sec          = 529.834408763358
```

Baseline M1G-time 在 7200 tick 內有 824 次 M1G decision，因此完整 PP-include oracle 需要最多 `824 * top5` 次 replay，加上最後一次 final policy replay。這是長跑實驗，不是單次 7200 模擬。

### 目前 PP-Include 長跑進度

目前 rolling oracle 仍在背景執行：

```text
rolling oracle PID = 79244
watcher PID        = 77868
```

目前已推進到：

```text
decision 75 / 824
```

最新已知 flip 例子：

```text
decision 74: chosen rank 4, rank1 regret 3.088333
decision 75: chosen rank 2, rank1 regret 0.596667
```

早期強反例：

```text
decision 1:
  MILP rank 1 actual objective = -1418.078593
  actual best rank             = 2
  actual best objective        = -1475.321104
  rank1 regret                 = 57.242511
```

### 最終輸出位置

rolling oracle artifacts：

```text
Results/m1g_time_vs_ppinclude_7200_20260510/ppinclude_rolling_oracle/oracle_policy.csv
Results/m1g_time_vs_ppinclude_7200_20260510/ppinclude_rolling_oracle/oracle_decision_trace.csv
Results/m1g_time_vs_ppinclude_7200_20260510/ppinclude_rolling_oracle/oracle_conclusion.md
```

最終 KPI 對比預期輸出：

```text
Results/m1g_time_vs_ppinclude_7200_20260510/m1g_time_ppinclude_7200_conclusion.md
```

### 研究意義

這個實驗不是要證明目前 M1G-time 已經找到真正 online optimal，而是要用後驗 replay 證明：即使維持 M1G-time 原始 objective 語意，只把 travel term 從 proxy estimate 換成 PP actual execution cost，同一個 top-k candidate set 內仍可能出現更好的決策。若最終 7200 KPI 改善，代表 PP-aware cost feedback 有實際系統收益；若沒有改善，也能界定單純 actual travel sum 作為 objective 的限制，提示後續需要 makespan、future value 或 station congestion term。
