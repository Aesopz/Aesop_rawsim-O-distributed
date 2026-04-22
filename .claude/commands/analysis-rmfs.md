# /analysis-rmfs — RMFS 模擬結果分析

## 用途
調閱一或兩個 RAWSim-O 模擬結果，輸出完整 L1–L6 KPI 比較表。

## 呼叫方式
```
/analysis-rmfs                          # 自動偵測 output/ 下最新兩個資料夾
/analysis-rmfs <dir>                    # 單一結果（僅顯示，不比較）
/analysis-rmfs <dir_A> <dir_B>          # 明確指定兩個資料夾（A 為基準，B 為比較方）
/analysis-rmfs <dir_A> <dir_B> <label_A> <label_B>   # 自訂標籤
```

## 執行步驟

### Step 1：解析路徑
- 若無引數：在 `output/`、`output_1bot_compare/`、`output_compare/` 等目錄中尋找最近修改的兩個子目錄。
- 若只有一個路徑：單次顯示，不做差值欄。
- 若有兩個路徑：A 為控制組，B 為實驗組；Δ = B − A，百分比 = (B−A)/A×100%。

### Step 2：讀取資料來源
每個模擬目錄必須讀取：
1. `kpi_report.csv` — 主要 KPI（layer, metric, empty, loaded, total, unit）
2. `statistics.txt` — 輔助文字統計（含 StatOverallEnergyE1J … E5J 等原始欄位）

若任一檔案不存在，標示 `N/A` 並繼續。

### Step 3：輸出格式（嚴格遵守）

按照下列順序輸出所有 section，**不得省略任何 section**。

---

#### 執行資訊表頭
```
配置 A：<dir_A 名稱>   配置 B：<dir_B 名稱>
Δ = B − A；正值 ↑ 表示 B 較大（能耗↑ = 惡化，throughput↑ = 改善）
```

---

#### L1 系統總覽

| 指標 | 單位 | A | B | Δ | Δ% |
|------|------|---|---|---|-----|
| orders_completed | orders | | | | |
| orders_per_hour | /h | | | | |
| E_mech | kJ | | | | |
| E_mech/order | kJ/order | | | | |
| E_total_with_idle | kJ | | | | |
| E_total/order | kJ/order | | | | |
| E_idle (P_IDLE×T) | kJ | | | | |
| total_distance_m | m | | | | |
| station_arrivals | count | | | | |

---

#### L2 Trip 分割（空載 E / 有載 L / 合計 T）

| 指標 | 空A | 有A | 合A | 空B | 有B | 合B | ΔE% | ΔL% |
|------|-----|-----|-----|-----|-----|-----|-----|-----|
| trip_count | | | | | | | | |
| distance_m | | | | | | | | |
| move+turn energy kJ | | | | | | | | |
| move+turn % of E_mech | | | — | | | — | | |
| avg_dist/trip m | | — | | | — | | | |
| avg_time/trip s | | — | | | — | | | |
| avg_energy/trip kJ | | — | | | — | | | |

---

#### L3 能耗相位細分

| 相位 | A (kJ) | A% mech | B (kJ) | B% mech | Δ kJ | Δ% |
|------|--------|---------|--------|---------|------|-----|
| E1 加速 | | | | | | |
| E2 煞車 | | | | | | |
| E3 巡航 | | | | | | |
| E4 轉向 | | | | | | |
| E5 升降 | | | | | | |
| E_mech 合計 | | 100% | | 100% | | |

---

#### L4 轉向分析

| 指標 | 空A | 有A | 合A | 空B | 有B | 合B | ΔE% | ΔL% |
|------|-----|-----|-----|-----|-----|-----|-----|-----|
| turn_count | | | | | | | | |
| turn_energy kJ | | | | | | | | |
| turn% of E_total | | — | | | — | | | |
| turn% of 各類 | | | — | | | — | | |
| turn/trip | | — | | | — | | | |
| turn/m | | — | | | — | | | |

---

#### L5 等待分析

| 指標 | 空A | 有A | 合A | 空B | 有B | 合B | ΔE% | ΔL% |
|------|-----|-----|-----|-----|-----|-----|-----|-----|
| wait_time_sec | | | | | | | | |
| wait_energy kJ | | | | | | | | |
| wait% of E_total | | — | | | — | | | |
| wait_ratio mean | | | | | | | | |
| wait_ratio median | | | | | | | | |
| wait_ratio p95 | | | | | | | | |

---

#### L6 五項組成（合計必須 = 100%）

| 組成項 | A% | B% | Δpp |
|--------|----|----|-----|
| move | | | |
| turn | | | |
| lift | | | |
| wait | | | |
| support | | | |
| **合計** | 100% | 100% | — |

---

#### E_support 與利用率

| 指標 | 單位 | A | B | Δ | Δ% |
|------|------|---|---|---|-----|
| E_support | kJ | | | | |
| E_support loaded | kJ | | | | |
| E_support empty | kJ | | | | |
| E_support/order | kJ/order | | | | |
| E_support/E_mech | ratio | | | | |
| P_ref_empty | W | | | | |
| P_ref_loaded | W | | | | |
| robot_utilization | ratio | | | | |
| E_empty/E_loaded | ratio | | | | |
| E/m loaded | J/m | | | | |
| E/m empty | J/m | | | | |

---

#### 解讀摘要
- 列出所有 Δ% > 1%（能耗/距離）、> 10%（等待）、> 2%（功率）的指標，並解釋**為何**出現此差異（路徑結構、物理模型、拓撲限制等）。
- 最後給一句結論：「B 在 [維度] 勝出 / 劣化，代價是 [維度]，符合 / 不符合 energy–throughput trade-off 預期。」

---

## 注意事項
- 所有能耗保留 **3 位小數**（kJ）
- 比例欄位保留 **2 位小數**
- 百分比差值用 **pp**（percentage point）單位
- 方向符號：↑ ↓ →
- 從 `kpi_report.csv` **和** `statistics.txt` 共同讀取，不可只用其中一個
- 若某欄 `statistics.txt` 有更精確原始值（如 StatOverallEnergyE1J），優先使用原始值
