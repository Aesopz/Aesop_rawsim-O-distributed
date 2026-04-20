																																								  我需要你幫我實作一個 Python 腳本,用於在 RAWSim-O(RMFS 模擬器)上
自動化執行 MAPF 方法比對實驗,並輸出詳細的 KPI 比對表。

---

## 專案背景

- 專案路徑:C:\Users\Aesop\Desktop\rawsim-o-rmfs-world-models
- 基底模擬器:RAWSim-O(基於 C#/.NET 的 RMFS 模擬平台)
- 本腳本目的:自動化跑兩個或多個 MAPF 方法的比較實驗,輸出統一格式的比對報告
- 使用者:研究生,會反覆執行不同 scenario 進行實驗

---

## 實例檔案位置

模擬實例資料夾:
C:\Users\Aesop\Desktop\rawsim-o-rmfs-world-models\Material\Instances\CoreBenchmark

每個 scenario 需要三個檔案:
1. xconf 檔 (*.xconf 或對應副檔名)  - 控制器設定
2. xlayo 檔 (*.xlayo 或對應副檔名)  - 倉庫佈局
3. xsett 檔 (*.xsett 或對應副檔名)  - 模擬參數

請你讀取資料夾下所有檔案,列出可用的 scenario 三元組供使用者選擇。

---

## 腳本功能需求

### 1. 執行介面
- 提供 CLI(argparse)與可選的 interactive mode
- 支援 headless 執行(不啟用任何 GUI,加速運行)
- 可批次跑多個 seed 後自動匯總

### 2. 使用者可配置參數
透過 CLI 參數或 YAML/JSON 設定檔指定:
- --xconf <path>  (或從 CoreBenchmark 選)
- --xlayo <path>
- --xsett <path>
- --methods <list>  (預設:["CBS-time", "CBS-energy"])
- --seeds <int or list>  (預設:5,支援 --seeds 1 2 3 4 5)
- --sim-duration <seconds>  (模擬時長,預設從 xsett 讀取)
- --output-dir <path>  (結果輸出位置,預設 ./results/<timestamp>/)
- --parallel <int>  (平行執行 seeds 的數量,預設 1)
- --verbose / --quiet

### 3. 執行流程
對每一個 (method, seed) 組合:
a. 複製 xconf/xlayo/xsett 到臨時工作目錄,注入 method 和 seed
b. 呼叫 RAWSim-O 以 headless 模式執行
c. 讀取模擬輸出 log / instance result files
d. 解析成結構化 KPI 字典
e. 寫入單次結果 CSV

全部完成後,彙整成比對表。

### 4. 必記錄 KPI 欄位

對每一次執行,要記錄以下資料:

**Throughput 類:**
- total_orders_completed          (總完成訂單數)
- throughput_per_hour             (每小時完成訂單數)

**Energy 類:**
- total_energy_kJ                 (總能耗)
- energy_per_order_kJ             (能效比 = 總能耗 / 總訂單數)

**Stop-and-go 類(空載):**
- stopgo_count_empty              (空載 stop-and-go 次數)
- stopgo_energy_empty_kJ          (空載 stop-and-go 總能耗)
- stopgo_energy_empty_pct         (空載 stop-and-go 佔總能耗 %)

**Stop-and-go 類(有載):**
- stopgo_count_loaded             (有載 stop-and-go 次數)
- stopgo_energy_loaded_kJ         (有載 stop-and-go 總能耗)
- stopgo_energy_loaded_pct        (有載 stop-and-go 佔總能耗 %)

**Turning 類:**
- turn_count                      (轉彎次數)
- turn_energy_kJ                  (轉彎總能耗)
- turn_energy_pct                 (轉彎能耗佔總能耗 %)

**Distance 類:**
- total_travel_distance           (總行走距離,單位統一為 m 或 cell)

**Wait time 類(P_idle 計算):**
- total_wait_time_seconds         (總等待時間,以 P_idle 計算)
- wait_energy_kJ                  (等待期間總能耗 = P_idle × wait_time)
- wait_energy_pct                 (等待能耗佔總能耗 %)

### 5. KPI 資料來源

請檢查 RAWSim-O 輸出的下列位置找尋資料(若路徑不同請自行探測):
- <instance_output_dir>/instance.csv
- <instance_output_dir>/statistics/*.csv
- 模擬 log 中的 event 紀錄

如果某些 KPI 不在預設輸出中,請:
a. 在腳本中明確標示「需要在 RAWSim-O 的 C# 端加 logging」
b. 用 TODO 註解指出具體該改哪個檔案哪個 event hook

### 6. 輸出報告格式

執行完成後,在 --output-dir 下產生三個檔案:

#### a. raw_results.csv
- 每 row = 一個 (method, seed) 組合
- 每 column = 一個 KPI

#### b. comparison_table.csv
對於每個 KPI,產生一個比較 row:
我需要你幫我實作一個 Python 腳本,用於在 RAWSim-O(RMFS 模擬器)上
自動化執行 MAPF 方法比對實驗,並輸出詳細的 KPI 比對表。

---

## 專案背景

- 專案路徑:C:\Users\Aesop\Desktop\rawsim-o-rmfs-world-models
- 基底模擬器:RAWSim-O(基於 C#/.NET 的 RMFS 模擬平台)
- 本腳本目的:自動化跑兩個或多個 MAPF 方法的比較實驗,輸出統一格式的比對報告
- 使用者:研究生,會反覆執行不同 scenario 進行實驗

---

## 實例檔案位置

模擬實例資料夾:
C:\Users\Aesop\Desktop\rawsim-o-rmfs-world-models\Material\Instances\CoreBenchmark

每個 scenario 需要三個檔案:
1. xconf 檔 (*.xconf 或對應副檔名)  - 控制器設定
2. xlayo 檔 (*.xlayo 或對應副檔名)  - 倉庫佈局
3. xsett 檔 (*.xsett 或對應副檔名)  - 模擬參數

請你讀取資料夾下所有檔案,列出可用的 scenario 三元組供使用者選擇。

---

## 腳本功能需求

### 1. 執行介面
- 提供 CLI(argparse)與可選的 interactive mode
- 支援 headless 執行(不啟用任何 GUI,加速運行)
- 可批次跑多個 seed 後自動匯總

### 2. 使用者可配置參數
透過 CLI 參數或 YAML/JSON 設定檔指定:
- --xconf <path>  (或從 CoreBenchmark 選)
- --xlayo <path>
- --xsett <path>
- --methods <list>  (預設:["CBS-time", "CBS-energy"])
- --seeds <int or list>  (預設:5,支援 --seeds 1 2 3 4 5)
- --sim-duration <seconds>  (模擬時長,預設從 xsett 讀取)
- --output-dir <path>  (結果輸出位置,預設 ./results/<timestamp>/)
- --parallel <int>  (平行執行 seeds 的數量,預設 1)
- --verbose / --quiet

### 3. 執行流程
對每一個 (method, seed) 組合:
a. 複製 xconf/xlayo/xsett 到臨時工作目錄,注入 method 和 seed
b. 呼叫 RAWSim-O 以 headless 模式執行
c. 讀取模擬輸出 log / instance result files
d. 解析成結構化 KPI 字典
e. 寫入單次結果 CSV

全部完成後,彙整成比對表。

### 4. 必記錄 KPI 欄位

對每一次執行,要記錄以下資料:

**Throughput 類:**
- total_orders_completed          (總完成訂單數)
- throughput_per_hour             (每小時完成訂單數)

**Energy 類:**
- total_energy_kJ                 (總能耗)
- energy_per_order_kJ             (能效比 = 總能耗 / 總訂單數)

**Stop-and-go 類(空載):**
- stopgo_count_empty              (空載 stop-and-go 次數)
- stopgo_energy_empty_kJ          (空載 stop-and-go 總能耗)
- stopgo_energy_empty_pct         (空載 stop-and-go 佔總能耗 %)

**Stop-and-go 類(有載):**
- stopgo_count_loaded             (有載 stop-and-go 次數)
- stopgo_energy_loaded_kJ         (有載 stop-and-go 總能耗)
- stopgo_energy_loaded_pct        (有載 stop-and-go 佔總能耗 %)

**Turning 類:**
- turn_count                      (轉彎次數)
- turn_energy_kJ                  (轉彎總能耗)
- turn_energy_pct                 (轉彎能耗佔總能耗 %)

**Distance 類:**
- total_travel_distance           (總行走距離,單位統一為 m 或 cell)

**Wait time 類(P_idle 計算):**
- total_wait_time_seconds         (總等待時間,以 P_idle 計算)
- wait_energy_kJ                  (等待期間總能耗 = P_idle × wait_time)
- wait_energy_pct                 (等待能耗佔總能耗 %)

### 5. KPI 資料來源

請檢查 RAWSim-O 輸出的下列位置找尋資料(若路徑不同請自行探測):
- <instance_output_dir>/instance.csv
- <instance_output_dir>/statistics/*.csv
- 模擬 log 中的 event 紀錄

如果某些 KPI 不在預設輸出中,請:
a. 在腳本中明確標示「需要在 RAWSim-O 的 C# 端加 logging」
b. 用 TODO 註解指出具體該改哪個檔案哪個 event hook

### 6. 輸出報告格式

執行完成後,在 --output-dir 下產生三個檔案:

#### a. raw_results.csv
- 每 row = 一個 (method, seed) 組合
- 每 column = 一個 KPI

#### b. comparison_table.csv
對於每個 KPI,產生一個比較 row:
KPI             | CBS-time (mean±std) | CBS-energy (mean±std) | abs_diff | pct_diff |
total_orders    | 1234±23             | 1100±30               | -134     | -10.86%  |
total_energy_kJ | 5600±120            | 4800±100              | -800     | -14.29%  |
- abs_diff = CBS-energy - CBS-time
- pct_diff = (CBS-energy - CBS-time) / CBS-time × 100%
- 顏色標示:若 pct_diff 為負且對應 KPI「越小越好」則綠色;反之紅色

#### c. comparison_report.md
Markdown 格式的人類可讀摘要:
- 實驗設定(scenario, seeds, sim duration)
- 每個方法的平均 KPI 表
- 關鍵差異 summary(例如:CBS-energy 相較 CBS-time 節省 14.3% 總能耗,但 throughput 下降 10.9%)
- Energy breakdown pie chart(若環境支援 matplotlib,就生成 .png)

### 7. 健壯性需求
- 單次模擬失敗要 log 錯誤但不中斷整批
- 每個 seed 獨立目錄,避免檔案衝突
- 若 parallel > 1,使用 multiprocessing,注意 RAWSim 是否支援多實例並行
- 所有路徑使用 pathlib.Path,跨平台友善
- 使用 argparse + click 或 typer(擇一),提供 --help 說明

### 8. 未來擴充性
設計時預留:
- 新方法加入只需在 methods 清單與 method-specific config 中增加項目
- 新 KPI 加入只需在 KPI dict schema 中新增欄位
- 用 dataclass 定義 RunConfig 和 RunResult,便於擴充

---

## 交付物

請你完成:

1. **主腳本** `run_comparison.py` — 包含所有執行邏輯
2. **設定檔範本** `config.example.yaml` — 示範如何用 YAML 配置
3. **KPI 解析器** `kpi_parser.py` — 獨立模組,負責從 RAWSim 輸出解析 KPI
4. **報告生成器** `report_generator.py` — 獨立模組,負責產生 CSV / MD
5. **README.md** — 使用說明,包含:
   - 安裝需求(dotnet runtime version, Python version)
   - 首次執行示範
   - 如何新增方法
   - 如何新增 KPI
6. **requirements.txt** — Python 依賴清單

---

## 技術約束

- Python 3.10+
- 使用 subprocess 呼叫 RAWSim-O(headless flag 請你探測 RAWSim 支援的 CLI 參數)
- 所有 KPI 數值統一使用 SI 單位(能耗 kJ,時間 s,距離 m)
- 日誌用 Python logging 模組,不要用 print
- 程式碼加 type hints 和 docstrings
- 每個模組獨立可測試

---

## 第一步請你做的事

1. **先讀取** `C:\Users\Aesop\Desktop\rawsim-o-rmfs-world-models` 根目錄結構
2. **定位** RAWSim-O 的 executable 位置 和 其 CLI 參數介面
3. **檢查** CoreBenchmark 資料夾中可用的 scenario 組合
4. **回報**:
   a. 你找到的 RAWSim executable 路徑
   b. RAWSim 支援的 CLI 參數(headless / method 切換 / seed 注入方式)
   c. 可用 scenario 三元組清單
   d. RAWSim 輸出的 KPI 欄位清單(哪些是直接可得、哪些需要加 logging)
5. **然後再** 開始實作

不要一開始就寫 code。先探索環境、釐清介面,再設計。

---

## 重要提醒

- 如果 RAWSim-O 目前不支援某些功能(如從 CLI 切換 MAPF method),
  請在回報中明確標示,並建議需要修改的 C# 原始檔位置與修改方式。
- 如果某些 KPI 無法從現有 log 取得,請生成「C# 端需要新增 logging 的 patch 建議清單」。
- 優先保證**正確性**和**可擴充性**,其次才是效能。
- 你需要依據目前代碼結構做判斷,不需要完全按照上述需求實作,但要在回報中說明你做了哪些調整和取捨。