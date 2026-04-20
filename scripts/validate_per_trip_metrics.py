#!/usr/bin/env python3
"""驗證 per-trip metrics 的合理性"""

import csv
import math
from pathlib import Path

def validate_metrics(csv_path: Path):
    """驗證新 per-trip 指標的合理性"""
    if not csv_path.exists():
        print(f"❌ 檔案不存在: {csv_path}")
        return False

    print(f"\n=== 驗證 Per-Trip 指標合理性 ===\n")

    with open(csv_path, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        rows = list(reader)

    if not rows:
        print("❌ CSV 檔案為空")
        return False

    ok_rows = [r for r in rows if r.get('status') == 'ok']
    if not ok_rows:
        print(f"❌ 無成功的模擬結果 (共 {len(rows)} 行)")
        return False

    row = ok_rows[0]
    print(f"✓ 找到成功的模擬: {row.get('method')} seed={row.get('seed')}")

    # 驗證清單
    checks = []

    # 1. Trip counts 應 > 0
    n_trips_loaded = safe_float(row.get('n_trips_loaded'))
    n_trips_empty = safe_float(row.get('n_trips_empty'))
    checks.append(("Trip Count (Loaded) > 0", n_trips_loaded > 0, f"{n_trips_loaded:.0f}"))
    checks.append(("Trip Count (Empty) > 0", n_trips_empty > 0, f"{n_trips_empty:.0f}"))

    # 2. Wait ratio 應在 [0, 1]
    wait_ratio_loaded = safe_float(row.get('trip_wait_ratio_loaded_mean'))
    wait_ratio_empty = safe_float(row.get('trip_wait_ratio_empty_mean'))
    checks.append(("Wait Ratio (Loaded) ∈ [0,1]", 0 <= wait_ratio_loaded <= 1, f"{wait_ratio_loaded:.1%}"))
    checks.append(("Wait Ratio (Empty) ∈ [0,1]", 0 <= wait_ratio_empty <= 1, f"{wait_ratio_empty:.1%}"))

    # 3. Motion times 應 > 0
    move_time_loaded = safe_float(row.get('move_time_loaded_sec'))
    move_time_empty = safe_float(row.get('move_time_empty_sec'))
    turn_time_loaded = safe_float(row.get('turn_time_loaded_sec'))
    turn_time_empty = safe_float(row.get('turn_time_empty_sec'))
    checks.append(("Move Time (Loaded) > 0", move_time_loaded > 0, f"{move_time_loaded:.1f}s"))
    checks.append(("Move Time (Empty) > 0", move_time_empty > 0, f"{move_time_empty:.1f}s"))
    checks.append(("Turn Time (Loaded) ≥ 0", turn_time_loaded >= 0, f"{turn_time_loaded:.1f}s"))
    checks.append(("Turn Time (Empty) ≥ 0", turn_time_empty >= 0, f"{turn_time_empty:.1f}s"))

    # 4. Travel time = move + turn
    travel_time_loaded = safe_float(row.get('travel_time_loaded_sec'))
    travel_time_empty = safe_float(row.get('travel_time_empty_sec'))
    expected_travel_loaded = move_time_loaded + turn_time_loaded
    expected_travel_empty = move_time_empty + turn_time_empty
    checks.append(
        ("Travel Time (L) = Move + Turn",
         abs(travel_time_loaded - expected_travel_loaded) < 0.1,
         f"{travel_time_loaded:.1f}s ≈ {expected_travel_loaded:.1f}s")
    )
    checks.append(
        ("Travel Time (E) = Move + Turn",
         abs(travel_time_empty - expected_travel_empty) < 0.1,
         f"{travel_time_empty:.1f}s ≈ {expected_travel_empty:.1f}s")
    )

    # 5. Energy = move + turn
    energy_loaded = safe_float(row.get('energy_loaded_kJ'))
    energy_empty = safe_float(row.get('energy_empty_kJ'))
    move_e_loaded = safe_float(row.get('move_energy_loaded_kJ'))
    move_e_empty = safe_float(row.get('move_energy_empty_kJ'))
    turn_e_loaded = safe_float(row.get('turn_energy_loaded_kJ'))
    turn_e_empty = safe_float(row.get('turn_energy_empty_kJ'))

    expected_e_loaded = move_e_loaded + turn_e_loaded
    expected_e_empty = move_e_empty + turn_e_empty
    checks.append(
        ("Energy (L) = Move + Turn",
         abs(energy_loaded - expected_e_loaded) < 0.1,
         f"{energy_loaded:.1f}kJ ≈ {expected_e_loaded:.1f}kJ")
    )
    checks.append(
        ("Energy (E) = Move + Turn",
         abs(energy_empty - expected_e_empty) < 0.1,
         f"{energy_empty:.1f}kJ ≈ {expected_e_empty:.1f}kJ")
    )

    # 6. Distance consistency
    distance_loaded = safe_float(row.get('loaded_distance_m'))
    distance_empty = safe_float(row.get('distance_empty_m'))
    distance_total = safe_float(row.get('total_travel_distance_m'))
    expected_total = distance_loaded + distance_empty
    checks.append(
        ("Distance: Total = Loaded + Empty",
         abs(distance_total - expected_total) < 1.0,
         f"{distance_total:.1f}m ≈ {expected_total:.1f}m")
    )

    # 7. Wait ratio 與 wait time 的一致性
    wait_time_loaded = safe_float(row.get('total_wait_time_seconds'))
    # wait_ratio 應該約等於 wait_time / total_time
    total_time_loaded = travel_time_loaded + wait_time_loaded
    if total_time_loaded > 0:
        expected_wait_ratio = wait_time_loaded / total_time_loaded
        checks.append(
            ("Wait Ratio ≈ Wait Time / Total Time",
             abs(wait_ratio_loaded - expected_wait_ratio) < 0.1,
             f"{wait_ratio_loaded:.1%} ≈ {expected_wait_ratio:.1%}")
        )

    # 8. 訂單完成數 > 0
    orders = safe_float(row.get('total_orders_completed'))
    checks.append(("Orders Completed > 0", orders > 0, f"{orders:.0f}"))

    # 列印結果
    passed = sum(1 for _, result, _ in checks if result)
    print(f"\n驗證結果: {passed}/{len(checks)} 通過\n")
    for check_name, result, value in checks:
        status = "✓" if result else "❌"
        print(f"{status} {check_name:40s} → {value}")

    # 摘要統計
    print(f"\n=== 摘要統計 ===")
    print(f"訂單完成: {orders:.0f}")
    print(f"有載 Trip: {n_trips_loaded:.0f}")
    print(f"空載 Trip: {n_trips_empty:.0f}")
    print(f"有載等待比: {wait_ratio_loaded:.1%}")
    print(f"空載等待比: {wait_ratio_empty:.1%}")
    print(f"總移動時間: {move_time_loaded + move_time_empty:.1f}s")
    print(f"總轉向時間: {turn_time_loaded + turn_time_empty:.1f}s")
    print(f"機械能耗: {safe_float(row.get('total_energy_mech_kJ')):.1f}kJ")
    print(f"每訂單能耗: {safe_float(row.get('energy_per_order_mech_kJ')):.2f}kJ")

    all_passed = passed == len(checks)
    print(f"\n{'✓ 所有驗證通過' if all_passed else '⚠ 有驗證失敗'}")
    return all_passed

def safe_float(val) -> float:
    """安全轉換為 float"""
    if val is None or val == '' or val == 'nan':
        return float('nan')
    try:
        return float(val)
    except:
        return float('nan')

if __name__ == '__main__':
    import sys
    csv_file = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("results/single-bot-verify-2026-04-20/raw_results.csv")
    validate_metrics(csv_file)
