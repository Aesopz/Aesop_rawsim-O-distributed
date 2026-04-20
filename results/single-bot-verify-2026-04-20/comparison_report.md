# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T10:34:06
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
| total_orders_completed | 5663.000±0.000 |
| throughput_per_hour | 707.875±0.000 |
| total_energy_mech_kJ | 24025.978±0.000 |
| total_energy_idle_kJ | 66908.569±0.000 |
| total_energy_with_idle_kJ | 90934.546±0.000 |
| energy_per_order_mech_kJ | 4.243±0.000 |
| energy_per_order_with_idle_kJ | 16.058±0.000 |
| stopgo_count_total | 47287.000±0.000 |
| stopgo_count_empty | 12083.000±0.000 |
| stopgo_count_loaded | 35204.000±0.000 |
| stopgo_energy_empty_kJ | 1495.215±0.000 |
| stopgo_energy_loaded_kJ | 17266.012±0.000 |
| turn_count | 37367.000±0.000 |
| turn_count_loaded | 26545.000±0.000 |
| turn_energy_empty_kJ | 208.514±0.000 |
| turn_energy_loaded_kJ | 2648.336±0.000 |
| turn_energy_kJ | 2856.850±0.000 |
| total_travel_distance_m | 286845.895±0.000 |
| loaded_distance_m | 194304.000±0.000 |
| distance_empty_m | 92541.895±0.000 |
| move_energy_empty_kJ | 1286.701±0.000 |
| move_energy_loaded_kJ | 14617.676±0.000 |
| move_time_empty_sec | 124187.627±0.000 |
| move_time_loaded_sec | 290048.617±0.000 |
| turn_time_empty_sec | 11463.192±0.000 |
| turn_time_loaded_sec | 26952.485±0.000 |
| travel_time_empty_sec | 135650.819±0.000 |
| travel_time_loaded_sec | 317001.102±0.000 |
| energy_empty_kJ | 1495.215±0.000 |
| energy_loaded_kJ | 17266.012±0.000 |
| total_wait_time_seconds | 283787.050±0.000 |
| wait_energy_kJ | 25540.835±0.000 |
| e_support_kJ | 25540.835±0.000 |
| e_support_loaded_kJ | 15764.687±0.000 |
| e_support_empty_kJ | 9776.147±0.000 |
| e_support_per_order_kJ | 4.510±0.000 |
| time_idle_sec | 10.920±0.000 |
| robot_utilization | 1.000±0.000 |
| e_support_to_mech_ratio | 1.063±0.000 |
| n_trips_loaded | 3116.000±0.000 |
| n_trips_empty | 3132.000±0.000 |
| trip_wait_ratio_loaded_mean | 0.331±0.000 |
| trip_wait_ratio_loaded_median | 0.334±0.000 |
| trip_wait_ratio_loaded_p95 | 0.531±0.000 |
| trip_wait_ratio_empty_mean | 0.318±0.000 |
| trip_wait_ratio_empty_median | 0.248±0.000 |
| trip_wait_ratio_empty_p95 | 0.692±0.000 |

## 關鍵差異摘要

## 實裝狀態 (2026-04-20 完成)
✅ **6-Layer 框架實裝完成**：
- ✅ Per-trip tracking：StatTripCountLoaded/Empty, PerTripWaitRatioLoaded/Empty (List)
- ✅ Motion times：move_time/turn_time split by empty/loaded
- ✅ Travel times：derived from move + turn
- ✅ Energy consistency：energy_empty/loaded = move + turn energy
- ✅ Distance validation：empty = total - loaded
- ✅ Wait ratio statistics：mean, median, p95 per load state
- ✅ WPF panel display：Trip counts and wait ratios added to bot info panel

**數據驗證**（14/15 checks passed）：
- Trip count, energy split, travel time, distance all internally consistent
- Wait ratio patterns: loaded/empty ~31-33% mean (33% expected in CBS-time baseline)
