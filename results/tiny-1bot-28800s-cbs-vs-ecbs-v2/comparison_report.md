# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T03:53:13
- xlayo: `aesop.xlayo`
- xsett: `aesop.xsett`
- 模擬時長: 28800 s
- Seeds: [42, 43, 44, 45, 46]
- Methods:
  - **CBS-time** ← `aesop-cbs.xconf`
  - **CBS-energy** ← `aesop-ecbs.xconf`

- 成功: 10 / 失敗: 0

## KPI 比較表

| KPI | CBS-time (mean±std) | CBS-energy (mean±std) | abs_diff | pct_diff | → |
|---|---|---|---|---|---|
| total_orders_completed | 5638.000±37.007 | 408.000±301.409 | -5230.000 | -92.76% | 🔴 |
| throughput_per_hour | 704.750±4.626 | 51.000±37.676 | -653.750 | -92.76% | 🔴 |
| total_energy_mech_kJ | 24284.606±192.092 | 1900.525±1554.168 | -22384.081 | -92.17% | 🟢 |
| total_energy_idle_kJ | 66913.336±6.192 | 65154.882±189.535 | -1758.454 | -2.63% | 🟢 |
| total_energy_with_idle_kJ | 91197.942±190.161 | 67055.407±1570.814 | -24142.535 | -26.47% | 🟢 |
| energy_per_order_mech_kJ | 4.308±0.062 | 4.528±0.502 | +0.220 | +5.11% | 🔴 |
| energy_per_order_with_idle_kJ | 16.176±0.139 | 213.260±88.180 | +197.083 | +1218.35% | 🔴 |
| stopgo_count_total | 47437.600±263.921 | 3479.800±2686.833 | -43957.800 | -92.66% | 🟢 |
| stopgo_count_empty | 11988.400±94.341 | 722.200±508.471 | -11266.200 | -93.98% | 🟢 |
| stopgo_count_loaded | 35449.200±240.570 | 2757.600±2178.751 | -32691.600 | -92.22% | 🟢 |
| stopgo_energy_empty_kJ | 1485.537±11.824 | 107.952±72.687 | -1377.585 | -92.73% | 🟢 |
| stopgo_energy_loaded_kJ | 17484.112±183.811 | 1351.420±1138.080 | -16132.692 | -92.27% | 🟢 |
| turn_count | 37406.600±199.778 | 2929.000±2251.590 | -34477.600 | -92.17% | 🟢 |
| turn_count_loaded | 26707.400±182.995 | 2266.400±1785.284 | -24441.000 | -91.51% | 🟢 |
| turn_energy_empty_kJ | 205.865±1.736 | 12.657±8.972 | -193.208 | -93.85% | 🟢 |
| turn_energy_loaded_kJ | 2663.772±25.684 | 212.319±175.843 | -2451.453 | -92.03% | 🟢 |
| turn_energy_kJ | 2869.637±25.675 | 224.976±184.808 | -2644.660 | -92.16% | 🔴 |
| total_travel_distance_m | 288836.104±1382.669 | 22994.800±17493.861 | -265841.304 | -92.04% | 🟢 |
| loaded_distance_m | 196789.400±1664.025 | 16392.000±13080.056 | -180397.400 | -91.67% | 🔴 |
| move_energy_empty_kJ | 1279.672±10.220 | 95.295±63.722 | -1184.378 | -92.55% | 🔴 |
| move_energy_loaded_kJ | 14820.340±169.872 | 1139.101±962.253 | -13681.239 | -92.31% | 🔴 |
| total_wait_time_seconds | 281750.395±1677.526 | 688180.924±27118.436 | +406430.529 | +144.25% | 🔴 |
| wait_energy_kJ | 25357.536±150.977 | 61936.283±2440.659 | +36578.748 | +144.25% | 🔴 |
| e_support_kJ | 25357.536±150.977 | 61936.283±2440.659 | +36578.748 | +144.25% | 🔴 |
| e_support_loaded_kJ | 15919.729±217.418 | 38297.099±10112.526 | +22377.370 | +140.56% | 🔴 |
| e_support_empty_kJ | 9437.807±198.025 | 23639.184±8984.674 | +14201.378 | +150.47% | 🔴 |
| e_support_per_order_kJ | 4.498±0.025 | 200.877±88.332 | +196.379 | +4366.23% | 🔴 |
| time_idle_sec | 10.920±0.000 | 10.920±0.000 | +0.000 | +0.00% | ⚪ |
| robot_utilization | 1.000±0.000 | 1.000±0.000 | +0.000 | +0.00% | ⚪ |
| e_support_to_mech_ratio | 1.044±0.013 | 45.415±20.686 | +44.370 | +4248.98% | 🟢 |

## 關鍵差異摘要
- **total_energy_with_idle_kJ**: CBS-energy vs CBS-time = -26.47%
- **total_orders_completed**: CBS-energy vs CBS-time = -92.76%
- **energy_per_order_with_idle_kJ**: CBS-energy vs CBS-time = +1218.35%
- **total_wait_time_seconds**: CBS-energy vs CBS-time = +144.25%

## 已知限制 (需 C# 端補 logging)
- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.
  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,
        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.
