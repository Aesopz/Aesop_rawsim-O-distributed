# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T09:49:35
- xlayo: `aesop.xlayo`
- xsett: `aesop.xsett`
- 模擬時長: 28800 s
- Seeds: [42]
- Methods:
  - **CBS-time** ← `aesop-cbs.xconf`

- 成功: 0 / 失敗: 1

### ⚠ 失敗清單
- `CBS-time` seed=42: timeout after 300s

## KPI 比較表

| KPI | CBS-time (mean±std) |
|---|---|
| total_orders_completed | NA |
| throughput_per_hour | NA |
| total_energy_mech_kJ | NA |
| total_energy_idle_kJ | NA |
| total_energy_with_idle_kJ | NA |
| energy_per_order_mech_kJ | NA |
| energy_per_order_with_idle_kJ | NA |
| stopgo_count_total | NA |
| stopgo_count_empty | NA |
| stopgo_count_loaded | NA |
| stopgo_energy_empty_kJ | NA |
| stopgo_energy_loaded_kJ | NA |
| turn_count | NA |
| turn_count_loaded | NA |
| turn_energy_empty_kJ | NA |
| turn_energy_loaded_kJ | NA |
| turn_energy_kJ | NA |
| total_travel_distance_m | NA |
| loaded_distance_m | NA |
| distance_empty_m | NA |
| move_energy_empty_kJ | NA |
| move_energy_loaded_kJ | NA |
| move_time_empty_sec | NA |
| move_time_loaded_sec | NA |
| turn_time_empty_sec | NA |
| turn_time_loaded_sec | NA |
| travel_time_empty_sec | NA |
| travel_time_loaded_sec | NA |
| energy_empty_kJ | NA |
| energy_loaded_kJ | NA |
| total_wait_time_seconds | NA |
| wait_energy_kJ | NA |
| e_support_kJ | NA |
| e_support_loaded_kJ | NA |
| e_support_empty_kJ | NA |
| e_support_per_order_kJ | NA |
| time_idle_sec | NA |
| robot_utilization | NA |
| e_support_to_mech_ratio | NA |
| n_trips_loaded | NA |
| n_trips_empty | NA |
| trip_wait_ratio_loaded_mean | NA |
| trip_wait_ratio_loaded_median | NA |
| trip_wait_ratio_loaded_p95 | NA |
| trip_wait_ratio_empty_mean | NA |
| trip_wait_ratio_empty_median | NA |
| trip_wait_ratio_empty_p95 | NA |

## 關鍵差異摘要

## 已知限制 (需 C# 端補 logging)
- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.
  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,
        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.
