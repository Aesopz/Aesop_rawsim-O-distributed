# RAWSim-O MAPF 方法比對報告

- 生成時間: 2026-04-20T02:06:56
- xlayo: `aesop-tiny.xinst`
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
| total_orders_completed | 500.000±23.780 | 500.600±21.338 | +0.600 | +0.12% | 🟢 |
| throughput_per_hour | 62.500±2.973 | 62.575±2.667 | +0.075 | +0.12% | 🟢 |
| total_energy_mech_kJ | 3869.328±77.079 | 3834.775±66.714 | -34.553 | -0.89% | 🟢 |
| total_energy_idle_kJ | 2569.407±0.486 | 2569.738±0.469 | +0.331 | +0.01% | 🔴 |
| total_energy_with_idle_kJ | 6438.735±76.667 | 6404.513±66.382 | -34.222 | -0.53% | 🟢 |
| energy_per_order_mech_kJ | 7.758±0.503 | 7.675±0.438 | -0.082 | -1.06% | 🟢 |
| energy_per_order_with_idle_kJ | 12.905±0.740 | 12.816±0.648 | -0.090 | -0.70% | 🟢 |
| stopgo_count_total | 3272.200±67.762 | 3362.600±64.143 | +90.400 | +2.76% | 🔴 |
| stopgo_count_empty | 611.200±16.438 | 608.600±12.837 | -2.600 | -0.43% | 🟢 |
| stopgo_count_loaded | 2661.000±52.759 | 2754.000±51.952 | +93.000 | +3.49% | 🔴 |
| stopgo_energy_empty_kJ | 259.292±4.007 | 260.003±6.302 | +0.711 | +0.27% | 🔴 |
| stopgo_energy_loaded_kJ | 3063.650±63.656 | 3033.188±56.840 | -30.462 | -0.99% | 🟢 |
| turn_count | 3270.200±66.773 | 3360.000±64.253 | +89.800 | +2.75% | 🔴 |
| turn_count_loaded | 2660.000±52.130 | 2753.200±52.328 | +93.200 | +3.50% | 🔴 |
| turn_energy_empty_kJ | 59.188±1.201 | 59.517±1.156 | +0.329 | +0.56% | 🔴 |
| turn_energy_loaded_kJ | 638.312±12.642 | 659.745±11.181 | +21.433 | +3.36% | 🔴 |
| turn_energy_kJ | 697.500±13.485 | 719.262±11.802 | +21.762 | +3.12% | 🟢 |
| total_travel_distance_m | 21568.905±416.686 | 21242.064±349.444 | -326.840 | -1.52% | 🟢 |
| loaded_distance_m | 18021.200±397.732 | 17689.600±365.562 | -331.600 | -1.84% | 🔴 |
| move_energy_empty_kJ | 200.104±3.551 | 200.486±6.700 | +0.381 | +0.19% | 🟢 |
| move_energy_loaded_kJ | 2425.338±52.160 | 2373.444±47.238 | -51.894 | -2.14% | 🔴 |
| total_wait_time_seconds | 6894.638±386.917 | 6913.990±328.721 | +19.352 | +0.28% | 🔴 |
| wait_energy_kJ | 620.517±34.823 | 622.259±29.585 | +1.742 | +0.28% | 🔴 |
| e_support_kJ | 620.517±34.823 | 622.259±29.585 | +1.742 | +0.28% | 🔴 |
| e_support_loaded_kJ | 550.492±36.395 | 552.800±30.946 | +2.307 | +0.42% | 🔴 |
| e_support_empty_kJ | 70.025±1.612 | 69.459±1.512 | -0.566 | -0.81% | 🟢 |
| e_support_per_order_kJ | 1.241±0.018 | 1.243±0.015 | +0.002 | +0.18% | 🔴 |
| stopgo_energy_loaded_kJ | 3063.650±63.656 | 3033.188±56.840 | -30.462 | -0.99% | 🟢 |
| stopgo_energy_empty_kJ | 259.292±4.007 | 260.003±6.302 | +0.711 | +0.27% | 🔴 |
| time_idle_sec | 0.420±0.000 | 0.420±0.000 | +0.000 | +0.00% | ⚪ |
| robot_utilization | 1.000±0.000 | 1.000±0.000 | +0.000 | +0.00% | ⚪ |
| e_support_to_mech_ratio | 0.161±0.012 | 0.162±0.011 | +0.002 | +1.16% | 🟢 |

## 關鍵差異摘要
- **total_energy_with_idle_kJ**: CBS-energy vs CBS-time = -0.53%
- **total_orders_completed**: CBS-energy vs CBS-time = +0.12%
- **energy_per_order_with_idle_kJ**: CBS-energy vs CBS-time = -0.70%
- **total_wait_time_seconds**: CBS-energy vs CBS-time = +0.28%

## 已知限制 (需 C# 端補 logging)
- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.
  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,
        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.
