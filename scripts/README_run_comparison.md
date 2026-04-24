# run_comparison.py — MAPF 方法比對自動化

自動對同一個 scenario (xlayo + xsett) 跑多個 MAPF method (不同 xconf) × 多個 seed,
解析 `statistics.txt` 產出三份報告:

- `raw_results.csv` — 每列 = (method, seed), 所有 KPI
- `comparison_table.csv` — 每列 = 一個 KPI, mean±std, abs/pct diff
- `comparison_report.md` — 人類可讀摘要

## 前置

- **.NET 6+** (Windows 上 dotnet CLI 可用)
- **Python 3.10+**
- 先 build: `dotnet build -c Release RAWSimO.sln`
- Python 依賴: `pip install -r scripts/requirements.txt`
  (pyyaml 是選用; 不用 `--config` 就不需要)

## 最小跑法 (aesop CBS vs ECBS)

```bash
python scripts/run_comparison.py \
    --xlayo Material/Instances/CoreBenchmark/aesop.xlayo \
    --xsett Material/Instances/CoreBenchmark/aesop.xsett \
    --methods "CBS-time=Material/Instances/CoreBenchmark/aesop-cbs.xconf" \
              "CBS-energy=Material/Instances/CoreBenchmark/aesop-ecbs.xconf" \
    --seeds 42 43 44 \
    --output-dir results/aesop_cbs_vs_ecbs
```

## 用 YAML 設定檔

```bash
python scripts/run_comparison.py --config scripts/config.example.yaml
```

## Interactive mode

不帶任何參數執行, 會掃 `Material/Instances/CoreBenchmark/` 讓你挑:

```bash
python scripts/run_comparison.py
```

## 新增方法

1. 準備新的 `.xconf` (同樣 scenario 格式, 換 `<PathPlanningConfig xsi:type="...">`)
2. 在 `--methods` 或 YAML 的 `methods:` 加一行 `label=path`

## 新增 KPI

1. 在 `RunResult` dataclass 加欄位 (預設 `float("nan")`)
2. 在 `STATS_KEY_MAP` 加 `StatXxx → new_field_name` 映射
3. 若屬「越小越好」, 加到 `LOWER_IS_BETTER` 集合
4. 把欄位名加到 `NUMERIC_KPI_FIELDS` 列表 (控制報表顯示順序)

## 已知限制

- **stop-and-go 能耗未分 empty/loaded**: C# 端只有 count 分, 能耗未分.
  Python 端會顯示 `NA`; 要完整資料需在 `RAWSimO.Core/Bots/BotNormal.cs` 加
  `StatStopGoEnergy{Empty,Loaded}J` 欄位, 並在 `InstanceStatistics.cs` 的
  `WriteStatistics` (約 line 1060+) 加輸出.

- **Throughput per hour**: 用 `StatOverallOrdersHandled / (SimulationDuration / 3600)`
  計算. 若 xsett 有 warmup, 分母請自行調整.

- **Parallel 執行**: `--parallel N` 會 spawn N 個 dotnet 子 process.
  建議先用 1 跑通再調大; 大 scenario 吃 RAM, 依機器而定.

## Troubleshooting

- **`statistics.txt 不存在`**: 模擬本身 crash (看 stderr 片段); 常見原因是
  `aesop.xinst` vs `aesop.xlayo` 混用 (請用 xlayo, 見 MEMORY).
- **能耗欄位全 NA**: 該 RAWSim-O 版本還沒有 `StatEnergyTotalKJ` logging;
  確認 `InstanceStatistics.cs:1055+` 的 `>>> Energy (Rizqi model)` section 有輸出.
