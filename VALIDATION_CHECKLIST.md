# 完整統計欄位驗證清單 (2026-04-20)

## ✅ C# 實裝驗證

### BotNormal.cs
- [x] 新增 `StatStopAndGoEnergyEmptyJ` 和 `StatStopAndGoEnergyLoadedJ` 欄位
- [x] 在 `ResetEnergyStatistics()` 初始化為 0.0
- [x] 在 `setNextWaypoint()` 累加 `moveE + turnE`（分別給 loaded/empty）
- [x] 編譯無誤（只有 XML comment warning）

### InstanceStatistics.cs
- [x] 新增 `StatOverallStopAndGoEnergyEmptyJ` property (sum across bots)
- [x] 新增 `StatOverallStopAndGoEnergyLoadedJ` property (sum across bots)
- [x] 在 `WriteStatistics()` 的 Motion Behavior 段輸出兩個新欄位（kJ 單位）
- [x] 編譯無誤

### SimulationInfoObject.cs
- [x] SimulationInfoBot 新增 6 個 TextBlock 欄位（3×empty/loaded）
- [x] 在 `InfoPanelUpdate()` 更新這 6 個欄位
- [x] 在 `InfoPanelInit()` 生成 UI（6 個 WrapPanel）
- [x] 編譯無誤

## ✅ Python 實裝驗證

### run_comparison.py
- [x] STATS_KEY_MAP 加入 `StatStopAndGoEnergyEmpty/LoadedKJ` 映射
- [x] `parse_statistics()` 優先使用 C# 直接輸出，fallback 到 move+turn 組合
- [x] `main()` 新增 critical_fields 列表（25+ 個關鍵指標）
- [x] 強制列印統計摘要（mean ± std）

## 🔄 實驗驗證（進行中）

### 28800s × 5 seed 實驗
- [ ] 完成 10 個模擬 (5 seeds × 2 methods)
- [ ] ✓ 第 1 個完成：CBS-time seed 42
  - ✓ `StatStopAndGoEnergyEmptyKJ: 1497.27 kJ`
  - ✓ `StatStopAndGoEnergyLoadedKJ: 17551.03 kJ`
- [ ] 待完成：其他 9 個模擬

### 輸出驗證清單（待）
- [ ] raw_results.csv：stopgo_energy_empty/loaded_kJ 欄位有值（非 NaN）
- [ ] comparison_table.csv：stop-and-go energy 行正確計算 mean±std
- [ ] comparison_report.md：可讀性檢查
- [ ] 控制台：critical_fields 統計摘要完整列印

## 📋 預期輸出格式

### statistics.txt (Motion Behavior 段)
```
StatStopAndGoCount: XXXX
StatLoadedStopAndGoCount: XXXX
StatEmptyStopAndGoCount: XXXX
StatStopAndGoEnergyEmptyKJ: XXXX.XX      ← 新增
StatStopAndGoEnergyLoadedKJ: XXXX.XX     ← 新增
StatMoveEnergyEmptyKJ: XXXX.XX
StatMoveEnergyLoadedKJ: XXXX.XX
StatTurnEnergyEmptyKJ: XXXX.XX
StatTurnEnergyLoadedKJ: XXXX.XX
```

### raw_results.csv (表頭)
```
method,seed,status,...,stopgo_count_total,stopgo_count_empty,stopgo_count_loaded,stopgo_energy_empty_kJ,stopgo_energy_loaded_kJ,...
```

### 控制台統計摘要
```
【CBS-time】
  stopgo_count_total               =       3272.20 ±    67.76
  stopgo_count_empty               =        611.20 ±    16.44
  stopgo_count_loaded              =       2661.00 ±    52.76
  stopgo_energy_empty_kJ           =       1497.27 ±    XX.XX
  stopgo_energy_loaded_kJ          =      17551.03 ±    XX.XX
  turn_count                       =       3270.20 ±    66.77
  turn_energy_empty_kJ             =        266.39 ±     X.XX
  turn_energy_loaded_kJ            =       2658.69 ±    XX.XX
  move_energy_empty_kJ             =       1290.58 ±    XX.XX
  move_energy_loaded_kJ            =      14892.33 ±    XX.XX
  ...
```

## 註記

- **兼容性**：舊的 statistics.txt（2026-04-17 前）不會有新欄位，parser fallback 到 move+turn 組合
- **驗證方式**：檢查 raw_results.csv 中 stopgo_energy_empty/loaded_kJ 是否有數字（非 NaN 或 "nan"）
- **面板使用**：WPF GUI 中每個 bot 會顯示 6 個新指標（可選折疊以節省空間）
