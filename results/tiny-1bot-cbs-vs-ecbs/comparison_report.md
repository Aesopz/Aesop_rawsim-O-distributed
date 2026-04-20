# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T01:57:51
- xlayo: `aesop-tiny.xinst`
- xsett: `test-short.xsett (1800s)`
- 模擬時長: 1800 s
- Seeds: [42, 43, 44, 45, 46]
- Methods:
  - **CBS-time** ← `aesop-cbs.xconf`
  - **CBS-energy** ← `aesop-ecbs.xconf`

- 成功: 10 / 失敗: 0

## KPI 比較表

| KPI | CBS-time (mean±std) | CBS-energy (mean±std) | abs_diff | pct_diff | → |
|---|---|---|---|---|---|
| total_orders_completed | 49.200±3.114 | 49.200±3.114 | +0.000 | +0.00% | ⚪ |
| throughput_per_hour | 98.400±6.229 | 98.400±6.229 | +0.000 | +0.00% | ⚪ |
| total_energy_mech_kJ | 185.639±10.230 | 185.569±10.262 | -0.070 | -0.04% | 🟢 |
| total_energy_idle_kJ | 160.760±0.060 | 160.760±0.088 | -0.001 | -0.00% | 🟢 |
| total_energy_with_idle_kJ | 346.399±10.175 | 346.329±10.186 | -0.071 | -0.02% | 🟢 |
| energy_per_order_mech_kJ | 3.795±0.446 | 3.794±0.450 | -0.001 | -0.03% | 🟢 |
| energy_per_order_with_idle_kJ | 7.073±0.660 | 7.072±0.664 | -0.001 | -0.02% | 🟢 |
| stopgo_count_total | 167.600±7.635 | 168.200±7.694 | +0.600 | +0.36% | 🔴 |
| stopgo_count_empty | 32.600±2.074 | 32.600±2.074 | +0.000 | +0.00% | ⚪ |
| stopgo_count_loaded | 135.000±7.071 | 135.600±7.092 | +0.600 | +0.44% | 🔴 |
| stopgo_energy_empty_kJ | 14.281±1.292 | 14.343±1.290 | +0.062 | +0.44% | 🔴 |
| stopgo_energy_loaded_kJ | 144.857±8.778 | 144.884±8.730 | +0.026 | +0.02% | 🔴 |
| turn_count | 167.600±7.635 | 168.200±7.694 | +0.600 | +0.36% | 🔴 |
| turn_count_loaded | 135.000±7.071 | 135.600±7.092 | +0.600 | +0.44% | 🔴 |
| turn_energy_empty_kJ | 3.155±0.183 | 3.217±0.205 | +0.062 | +1.98% | 🔴 |
| turn_energy_loaded_kJ | 30.829±1.764 | 30.930±1.751 | +0.101 | +0.33% | 🔴 |
| turn_energy_kJ | 33.983±1.875 | 34.147±1.862 | +0.164 | +0.48% | 🟢 |
| total_travel_distance_m | 1090.913±29.309 | 1086.139±29.410 | -4.774 | -0.44% | 🟢 |
| loaded_distance_m | 895.400±47.501 | 895.000±47.376 | -0.400 | -0.04% | 🔴 |
| move_energy_empty_kJ | 11.126±1.341 | 11.126±1.341 | +0.000 | +0.00% | ⚪ |
| move_energy_loaded_kJ | 114.028±7.033 | 113.954±7.021 | -0.075 | -0.07% | 🔴 |
| total_wait_time_seconds | 666.258±33.512 | 666.800±33.515 | +0.542 | +0.08% | 🔴 |
| wait_energy_kJ | 59.963±3.016 | 60.012±3.016 | +0.049 | +0.08% | 🔴 |
| e_support_kJ | 59.963±3.016 | 60.012±3.016 | +0.049 | +0.08% | 🔴 |
| e_support_loaded_kJ | 56.294±3.209 | 56.315±3.220 | +0.022 | +0.04% | 🔴 |
| e_support_empty_kJ | 3.669±0.224 | 3.697±0.235 | +0.027 | +0.74% | 🔴 |
| e_support_per_order_kJ | 1.220±0.034 | 1.221±0.033 | +0.001 | +0.08% | 🔴 |
| stopgo_energy_loaded_kJ | 144.857±8.778 | 144.884±8.730 | +0.026 | +0.02% | 🔴 |
| stopgo_energy_empty_kJ | 14.281±1.292 | 14.343±1.290 | +0.062 | +0.44% | 🔴 |
| time_idle_sec | 0.420±0.000 | 0.420±0.000 | +0.000 | +0.00% | ⚪ |
| robot_utilization | 1.000±0.000 | 1.000±0.000 | +0.000 | +0.00% | ⚪ |
| e_support_to_mech_ratio | 0.324±0.033 | 0.325±0.033 | +0.000 | +0.12% | 🟢 |

## 關鍵差異摘要
- **total_energy_with_idle_kJ**: CBS-energy vs CBS-time = -0.02%
- **total_orders_completed**: CBS-energy vs CBS-time = +0.00%
- **energy_per_order_with_idle_kJ**: CBS-energy vs CBS-time = -0.02%
- **total_wait_time_seconds**: CBS-energy vs CBS-time = +0.08%

## 已知限制 (需 C# 端補 logging)
- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.
  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,
        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.
