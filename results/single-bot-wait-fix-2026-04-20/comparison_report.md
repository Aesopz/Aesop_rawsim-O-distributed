# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T10:56:37
- xlayo: `aesop.xlayo`
- xsett: `aesop.xsett`
- 模擬時長: 28800 s
- Seeds: [42]
- Methods:
  - **CBS-time** ← `aesop-cbs.xconf`

- 成功: 1 / 失敗: 0

## KPI 比較表

| KPI | CBS-time (mean±std) |
|---|---|
| total_orders_completed | 5678.000±0.000 |
| throughput_per_hour | 709.750±0.000 |
| total_energy_mech_kJ | 24216.597±0.000 |
| total_energy_idle_kJ | 66910.192±0.000 |
| total_energy_with_idle_kJ | 91126.789±0.000 |
| energy_per_order_mech_kJ | 4.265±0.000 |
| energy_per_order_with_idle_kJ | 16.049±0.000 |
| stopgo_count_total | 47262.000±0.000 |
| stopgo_count_empty | 12065.000±0.000 |
| stopgo_count_loaded | 35197.000±0.000 |
| stopgo_energy_empty_kJ | 1492.461±0.000 |
| stopgo_energy_loaded_kJ | 17433.676±0.000 |
| turn_count | 37247.000±0.000 |
| turn_count_loaded | 26468.000±0.000 |
| turn_energy_empty_kJ | 207.424±0.000 |
| turn_energy_loaded_kJ | 2645.280±0.000 |
| turn_energy_kJ | 2852.704±0.000 |
| total_travel_distance_m | 288241.546±0.000 |
| loaded_distance_m | 195912.000±0.000 |
| distance_empty_m | 92329.546±0.000 |
| move_energy_empty_kJ | 1285.037±0.000 |
| move_energy_loaded_kJ | 14788.396±0.000 |
| move_time_empty_sec | 123939.834±0.000 |
| move_time_loaded_sec | 291140.414±0.000 |
| turn_time_empty_sec | 11389.328±0.000 |
| turn_time_loaded_sec | 26847.636±0.000 |
| travel_time_empty_sec | 135329.161±0.000 |
| travel_time_loaded_sec | 317988.050±0.000 |
| energy_empty_kJ | 1492.461±0.000 |
| energy_loaded_kJ | 17433.676±0.000 |
| total_wait_time_seconds | 258072.003±0.000 |
| wait_energy_kJ | 23226.480±0.000 |
| e_support_kJ | 25477.516±0.000 |
| e_support_loaded_kJ | 15701.769±0.000 |
| e_support_empty_kJ | 9775.747±0.000 |
| e_support_per_order_kJ | 4.487±0.000 |
| time_idle_sec | 10.920±0.000 |
| robot_utilization | 1.000±0.000 |
| e_support_to_mech_ratio | 1.052±0.000 |
| n_trips_loaded | 3124.000±0.000 |
| n_trips_empty | 3140.000±0.000 |
| trip_wait_ratio_loaded_mean | 0.302±0.000 |
| trip_wait_ratio_loaded_median | 0.305±0.000 |
| trip_wait_ratio_loaded_p95 | 0.508±0.000 |
| trip_wait_ratio_empty_mean | 0.232±0.000 |
| trip_wait_ratio_empty_median | 0.152±0.000 |
| trip_wait_ratio_empty_p95 | 0.668±0.000 |

## 關鍵差異摘要

## 已知限制 (需 C# 端補 logging)
- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.
  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,
        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.
