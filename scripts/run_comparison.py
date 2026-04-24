"""RAWSim-O MAPF 方法比對實驗自動化腳本.

對指定 scenario (xlayo + xsett) 跑多個 MAPF method (xconf) × 多個 seed,
解析 statistics.txt 取得 KPI, 產出 raw_results.csv / comparison_table.csv /
comparison_report.md.

用法:
  python scripts/run_comparison.py \\
      --xlayo Material/Instances/CoreBenchmark/aesop.xlayo \\
      --xsett Material/Instances/CoreBenchmark/aesop.xsett \\
      --methods "CBS-time=Material/Instances/CoreBenchmark/aesop-cbs.xconf" \\
                "CBS-energy=Material/Instances/CoreBenchmark/aesop-ecbs.xconf" \\
      --seeds 42 43 44 \\
      --output-dir results/aesop_cbs_vs_ecbs

或透過 YAML 設定檔:
  python scripts/run_comparison.py --config scripts/config.example.yaml
"""
from __future__ import annotations

import argparse
import csv
import io
import logging
import os as _os
import sys as _sys

# Windows cp1252 預設會讓中文 print 炸; 統一 stdout/stderr 為 UTF-8.
if _sys.platform == "win32":
    try:
        _sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
        _sys.stderr.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
    except Exception:
        pass
import re
import statistics as stats
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from concurrent.futures import ProcessPoolExecutor, as_completed
from dataclasses import asdict, dataclass, field, fields
from datetime import datetime
from pathlib import Path
from typing import Any

LOG = logging.getLogger("run_comparison")

REPO_ROOT = Path(__file__).resolve().parent.parent
CLI_PROJECT = REPO_ROOT / "RAWSimO.CLI" / "RAWSimO.CLI.csproj"
BENCHMARK_DIR = REPO_ROOT / "Material" / "Instances" / "CoreBenchmark"

# P_IDLE 常數 (與 EnergyConsumption.cs 一致; BotNormal 每 tick 累計)
P_IDLE_WATT = 90

# KPI: statistics.txt 的 key 到 RunResult 欄位的映射
STATS_KEY_MAP: dict[str, str] = {
    "StatOverallOrdersHandled": "total_orders_completed",
    "StatOverallDistanceTraveled": "total_travel_distance_m",
    "StatEnergyTotalKJ": "total_energy_mech_kJ",
    "StatEnergyIdleKJ": "total_energy_idle_kJ",
    "StatEnergyTotalWithIdleKJ": "total_energy_with_idle_kJ",
    "StatEnergyPerOrderKJ": "energy_per_order_mech_kJ",
    "StatEnergyPerOrderWithIdleKJ": "energy_per_order_with_idle_kJ",
    "StatTurningCount": "turn_count",
    "StatLoadedTurningCount": "turn_count_loaded",
    "StatStopAndGoCount": "stopgo_count_total",
    "StatLoadedStopAndGoCount": "stopgo_count_loaded",
    "StatEmptyStopAndGoCount": "stopgo_count_empty",
    "StatStopAndGoEnergyEmptyKJ": "stopgo_energy_empty_kJ",
    "StatStopAndGoEnergyLoadedKJ": "stopgo_energy_loaded_kJ",
    "StatWaitTimeSec": "total_wait_time_seconds",
    "StatMoveEnergyEmptyKJ": "move_energy_empty_kJ",
    "StatMoveEnergyLoadedKJ": "move_energy_loaded_kJ",
    "StatTurnEnergyEmptyKJ": "turn_energy_empty_kJ",
    "StatTurnEnergyLoadedKJ": "turn_energy_loaded_kJ",
    "StatLoadedDistanceM": "loaded_distance_m",
    # Support & Utilization (requires updated C# logging)
    "StatESupportKJ": "e_support_kJ",
    "StatESupportLoadedKJ": "e_support_loaded_kJ",
    "StatESupportEmptyKJ": "e_support_empty_kJ",
    "StatESupportPerOrderKJ": "e_support_per_order_kJ",
    "StatTimeIdleSec": "time_idle_sec",
    "StatRobotUtilization": "robot_utilization",
    "StatESupportToMechRatio": "e_support_to_mech_ratio",
    # Per-trip tracking (added 2026-04-20)
    "StatTripCountLoaded": "n_trips_loaded",
    "StatTripCountEmpty": "n_trips_empty",
    "StatTripWaitRatioLoadedMean": "trip_wait_ratio_loaded_mean",
    "StatTripWaitRatioLoadedMedian": "trip_wait_ratio_loaded_median",
    "StatTripWaitRatioLoadedP95": "trip_wait_ratio_loaded_p95",
    "StatTripWaitRatioEmptyMean": "trip_wait_ratio_empty_mean",
    "StatTripWaitRatioEmptyMedian": "trip_wait_ratio_empty_median",
    "StatTripWaitRatioEmptyP95": "trip_wait_ratio_empty_p95",
    # Motion times (added 2026-04-20)
    "StatMoveTimeEmptySec": "move_time_empty_sec",
    "StatMoveTimeLoadedSec": "move_time_loaded_sec",
    "StatTurnTimeEmptySec": "turn_time_empty_sec",
    "StatTurnTimeLoadedSec": "turn_time_loaded_sec",
}

# 「越小越好」的 KPI: report 中 pct_diff<0 視為改善 (綠色)
LOWER_IS_BETTER = {
    "total_energy_mech_kJ", "total_energy_idle_kJ", "total_energy_with_idle_kJ",
    "energy_per_order_mech_kJ", "energy_per_order_with_idle_kJ",
    "turn_count", "turn_count_loaded", "turn_energy_empty_kJ", "turn_energy_loaded_kJ",
    "turn_energy_kJ",
    "stopgo_count_total", "stopgo_count_loaded", "stopgo_count_empty",
    "stopgo_energy_loaded_kJ", "stopgo_energy_empty_kJ",
    "total_wait_time_seconds", "wait_energy_kJ",
    "total_travel_distance_m", "loaded_distance_m", "distance_empty_m",
    "move_energy_empty_kJ", "move_energy_loaded_kJ",
    "energy_empty_kJ", "energy_loaded_kJ",
    "e_support_kJ", "e_support_loaded_kJ", "e_support_empty_kJ", "e_support_per_order_kJ",
    "time_idle_sec",
    # Per-trip & motion metrics (added 2026-04-20)
    "move_time_empty_sec", "move_time_loaded_sec",
    "turn_time_empty_sec", "turn_time_loaded_sec",
    "travel_time_empty_sec", "travel_time_loaded_sec",
    "trip_wait_ratio_loaded_mean", "trip_wait_ratio_loaded_median", "trip_wait_ratio_loaded_p95",
    "trip_wait_ratio_empty_mean", "trip_wait_ratio_empty_median", "trip_wait_ratio_empty_p95",
}


@dataclass
class RunConfig:
    xlayo: Path
    xsett: Path
    xconf: Path
    method_label: str
    seed: int
    output_dir: Path  # base dir (RAWSim 會在底下建 <inst>-<sett>-<conf>-<seed>/)

    @staticmethod
    def _xml_name(path: Path) -> str:
        """讀 XML 的 <Name> 標籤; fallback 到 filename stem."""
        try:
            root = ET.parse(path).getroot()
            el = root.find("Name")
            if el is not None and el.text:
                return el.text.strip()
        except Exception:
            pass
        return path.stem

    @property
    def sim_output_subdir(self) -> Path:
        # RAWSim-O 用 XML 內部 <Name> 屬性命名輸出目錄 (Program.cs:171-172)
        inst = self._xml_name(self.xlayo)
        sett = self._xml_name(self.xsett)
        conf = self._xml_name(self.xconf)
        return self.output_dir / f"{inst}-{sett}-{conf}-{self.seed}"


@dataclass
class RunResult:
    method: str
    seed: int
    status: str  # "ok" / "failed"
    error: str = ""
    sim_duration_sec: float = 0.0

    total_orders_completed: float = float("nan")
    throughput_per_hour: float = float("nan")

    total_energy_mech_kJ: float = float("nan")
    total_energy_idle_kJ: float = float("nan")
    total_energy_with_idle_kJ: float = float("nan")
    energy_per_order_mech_kJ: float = float("nan")
    energy_per_order_with_idle_kJ: float = float("nan")

    stopgo_count_total: float = float("nan")
    stopgo_count_empty: float = float("nan")
    stopgo_count_loaded: float = float("nan")
    stopgo_energy_empty_kJ: float = float("nan")
    stopgo_energy_loaded_kJ: float = float("nan")

    turn_count: float = float("nan")
    turn_count_loaded: float = float("nan")
    turn_energy_empty_kJ: float = float("nan")
    turn_energy_loaded_kJ: float = float("nan")
    turn_energy_kJ: float = float("nan")  # empty + loaded

    total_travel_distance_m: float = float("nan")
    loaded_distance_m: float = float("nan")
    move_energy_empty_kJ: float = float("nan")
    move_energy_loaded_kJ: float = float("nan")

    total_wait_time_seconds: float = float("nan")
    wait_energy_kJ: float = float("nan")  # = P_IDLE × wait / 1000

    # Support energy & utilization (requires updated C# logging)
    e_support_kJ: float = float("nan")
    e_support_loaded_kJ: float = float("nan")
    e_support_empty_kJ: float = float("nan")
    e_support_per_order_kJ: float = float("nan")
    time_idle_sec: float = float("nan")
    robot_utilization: float = float("nan")  # fleet-level: 1 - Σidle / (T × n_bots)
    e_support_to_mech_ratio: float = float("nan")  # E_support / E_mech (congestion overhead)

    # Per-trip tracking (added 2026-04-20)
    n_trips_loaded: float = float("nan")
    n_trips_empty: float = float("nan")
    trip_wait_ratio_loaded_mean: float = float("nan")
    trip_wait_ratio_loaded_median: float = float("nan")
    trip_wait_ratio_loaded_p95: float = float("nan")
    trip_wait_ratio_empty_mean: float = float("nan")
    trip_wait_ratio_empty_median: float = float("nan")
    trip_wait_ratio_empty_p95: float = float("nan")

    # Motion times (added 2026-04-20)
    move_time_empty_sec: float = float("nan")
    move_time_loaded_sec: float = float("nan")
    turn_time_empty_sec: float = float("nan")
    turn_time_loaded_sec: float = float("nan")

    # Derived: travel times (computed in parse_statistics)
    travel_time_empty_sec: float = float("nan")
    travel_time_loaded_sec: float = float("nan")
    distance_empty_m: float = float("nan")
    energy_empty_kJ: float = float("nan")
    energy_loaded_kJ: float = float("nan")


# ---------------------------------------------------------------------------
# Scenario discovery
# ---------------------------------------------------------------------------

def discover_scenarios(benchmark_dir: Path = BENCHMARK_DIR) -> dict[str, dict[str, list[Path]]]:
    """掃 CoreBenchmark/ 列出可能的 scenario 三元組."""
    groups: dict[str, dict[str, list[Path]]] = {}
    for p in sorted(benchmark_dir.iterdir()):
        if not p.is_file():
            continue
        ext = p.suffix.lstrip(".")
        if ext not in {"xinst", "xlayo", "xsett", "xconf"}:
            continue
        # 用檔名 prefix 做分組 (aesop, test-congestion, jenPP ...)
        prefix = p.stem.split("-")[0].split("_")[0]
        groups.setdefault(prefix, {}).setdefault(ext, []).append(p)
    return groups


# ---------------------------------------------------------------------------
# Simulation execution
# ---------------------------------------------------------------------------

def read_sim_duration(xsett_path: Path) -> float:
    """從 xsett XML 讀 simulation duration (秒)."""
    try:
        tree = ET.parse(xsett_path)
        root = tree.getroot()
        for tag in ("SimulationDuration", "SimulationWarmupTime"):
            el = root.find(tag)
            if el is not None and el.text and tag == "SimulationDuration":
                return float(el.text)
    except Exception as e:
        LOG.warning("讀 xsett 失敗 %s: %s", xsett_path, e)
    return 0.0


def run_single(cfg: RunConfig, dotnet_config: str = "Release", timeout_sec: int = 0) -> RunResult:
    """呼叫 dotnet run 執行一次模擬."""
    LOG.info("▶ run %s seed=%s", cfg.method_label, cfg.seed)
    cfg.output_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        "dotnet", "run", "--project", str(CLI_PROJECT),
        "-c", dotnet_config,
        "--",
        str(cfg.xlayo), str(cfg.xsett), str(cfg.xconf),
        str(cfg.output_dir) + "/",  # RAWSim 要結尾 /
        str(cfg.seed),
        f"{cfg.method_label}-seed{cfg.seed}",
    ]
    LOG.debug("cmd: %s", " ".join(cmd))

    t0 = time.time()
    try:
        proc = subprocess.run(
            cmd, cwd=REPO_ROOT,
            capture_output=True, text=True,
            timeout=timeout_sec if timeout_sec > 0 else None,
        )
    except subprocess.TimeoutExpired as e:
        return RunResult(method=cfg.method_label, seed=cfg.seed,
                         status="failed", error=f"timeout after {timeout_sec}s")
    except Exception as e:
        return RunResult(method=cfg.method_label, seed=cfg.seed,
                         status="failed", error=f"subprocess error: {e}")

    elapsed = time.time() - t0
    LOG.info("  完成 in %.1fs (exit=%d)", elapsed, proc.returncode)

    if proc.returncode != 0:
        # 保留 stderr 頭尾方便 debug
        err = (proc.stderr or "")[-1500:]
        return RunResult(method=cfg.method_label, seed=cfg.seed,
                         status="failed", error=f"exit {proc.returncode}: {err}")

    stats_file = cfg.sim_output_subdir / "statistics.txt"
    if not stats_file.exists():
        return RunResult(method=cfg.method_label, seed=cfg.seed,
                         status="failed",
                         error=f"statistics.txt 不存在於 {cfg.sim_output_subdir}")

    sim_duration = read_sim_duration(cfg.xsett)
    result = parse_statistics(stats_file, cfg.method_label, cfg.seed, sim_duration)
    return result


# ---------------------------------------------------------------------------
# KPI parsing
# ---------------------------------------------------------------------------

_STAT_LINE_RE = re.compile(r"^(Stat[A-Za-z0-9]+):\s*([-+0-9.eE]+)\s*$")


def parse_statistics(stats_file: Path, method: str, seed: int, sim_duration: float) -> RunResult:
    """解析 statistics.txt 的 `StatXxx: value` 格式 (只取 >>> Overall / Energy /
    Motion Behavior section 的 aggregate, 忽略 per-bot 段)."""
    parsed: dict[str, float] = {}
    in_aggregate_section = False
    try:
        with stats_file.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.rstrip()
                if line.startswith(">>> "):
                    in_aggregate_section = line[4:] in {"Overall", "Energy (Rizqi model)", "Motion Behavior", "Support & Utilization"}
                    continue
                if not in_aggregate_section:
                    continue
                m = _STAT_LINE_RE.match(line)
                if m:
                    try:
                        parsed[m.group(1)] = float(m.group(2))
                    except ValueError:
                        pass
    except OSError as e:
        return RunResult(method=method, seed=seed, status="failed",
                         error=f"無法讀 statistics.txt: {e}")

    r = RunResult(method=method, seed=seed, status="ok", sim_duration_sec=sim_duration)
    for stat_key, field_name in STATS_KEY_MAP.items():
        if stat_key in parsed:
            setattr(r, field_name, parsed[stat_key])

    # 衍生欄位
    if sim_duration > 0 and r.total_orders_completed == r.total_orders_completed:
        r.throughput_per_hour = r.total_orders_completed / (sim_duration / 3600.0)
    # 轉彎總能耗 (empty+loaded)
    if not _is_nan(r.turn_energy_empty_kJ) and not _is_nan(r.turn_energy_loaded_kJ):
        r.turn_energy_kJ = r.turn_energy_empty_kJ + r.turn_energy_loaded_kJ
    # Wait 能耗 = P_IDLE × wait_time
    if not _is_nan(r.total_wait_time_seconds):
        r.wait_energy_kJ = P_IDLE_WATT * r.total_wait_time_seconds / 1000.0
    # Stop-and-go mech energy split: 優先使用 C# 直接輸出的值
    # 如果 C# 沒有輸出, fallback 到 move + turn 組合
    if _is_nan(r.stopgo_energy_loaded_kJ):
        if not _is_nan(r.move_energy_loaded_kJ) and not _is_nan(r.turn_energy_loaded_kJ):
            r.stopgo_energy_loaded_kJ = r.move_energy_loaded_kJ + r.turn_energy_loaded_kJ
    if _is_nan(r.stopgo_energy_empty_kJ):
        if not _is_nan(r.move_energy_empty_kJ) and not _is_nan(r.turn_energy_empty_kJ):
            r.stopgo_energy_empty_kJ = r.move_energy_empty_kJ + r.turn_energy_empty_kJ

    # Travel times (sum of move + turn times, added 2026-04-20)
    if not _is_nan(r.move_time_empty_sec) and not _is_nan(r.turn_time_empty_sec):
        r.travel_time_empty_sec = r.move_time_empty_sec + r.turn_time_empty_sec
    if not _is_nan(r.move_time_loaded_sec) and not _is_nan(r.turn_time_loaded_sec):
        r.travel_time_loaded_sec = r.move_time_loaded_sec + r.turn_time_loaded_sec

    # Empty distance (total - loaded)
    if not _is_nan(r.total_travel_distance_m) and not _is_nan(r.loaded_distance_m):
        r.distance_empty_m = r.total_travel_distance_m - r.loaded_distance_m

    # Total energy by load state (move + turn)
    if not _is_nan(r.move_energy_empty_kJ) and not _is_nan(r.turn_energy_empty_kJ):
        r.energy_empty_kJ = r.move_energy_empty_kJ + r.turn_energy_empty_kJ
    if not _is_nan(r.move_energy_loaded_kJ) and not _is_nan(r.turn_energy_loaded_kJ):
        r.energy_loaded_kJ = r.move_energy_loaded_kJ + r.turn_energy_loaded_kJ

    return r


def _is_nan(x: float) -> bool:
    return x != x


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

NUMERIC_KPI_FIELDS: list[str] = [
    "total_orders_completed", "throughput_per_hour",
    "total_energy_mech_kJ", "total_energy_idle_kJ", "total_energy_with_idle_kJ",
    "energy_per_order_mech_kJ", "energy_per_order_with_idle_kJ",
    "stopgo_count_total", "stopgo_count_empty", "stopgo_count_loaded",
    "stopgo_energy_empty_kJ", "stopgo_energy_loaded_kJ",
    "turn_count", "turn_count_loaded",
    "turn_energy_empty_kJ", "turn_energy_loaded_kJ", "turn_energy_kJ",
    "total_travel_distance_m", "loaded_distance_m", "distance_empty_m",
    "move_energy_empty_kJ", "move_energy_loaded_kJ",
    "move_time_empty_sec", "move_time_loaded_sec",
    "turn_time_empty_sec", "turn_time_loaded_sec",
    "travel_time_empty_sec", "travel_time_loaded_sec",
    "energy_empty_kJ", "energy_loaded_kJ",
    "total_wait_time_seconds", "wait_energy_kJ",
    "e_support_kJ", "e_support_loaded_kJ", "e_support_empty_kJ", "e_support_per_order_kJ",
    "time_idle_sec", "robot_utilization",
    "e_support_to_mech_ratio",
    # Per-trip tracking (added 2026-04-20)
    "n_trips_loaded", "n_trips_empty",
    "trip_wait_ratio_loaded_mean", "trip_wait_ratio_loaded_median", "trip_wait_ratio_loaded_p95",
    "trip_wait_ratio_empty_mean", "trip_wait_ratio_empty_median", "trip_wait_ratio_empty_p95",
]


def write_raw_csv(results: list[RunResult], out_path: Path) -> None:
    if not results:
        return
    field_names = [f.name for f in fields(RunResult)]
    with out_path.open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=field_names)
        w.writeheader()
        for r in results:
            w.writerow(asdict(r))


def _mean_std(values: list[float]) -> tuple[float, float]:
    clean = [v for v in values if not _is_nan(v)]
    if not clean:
        return float("nan"), float("nan")
    if len(clean) == 1:
        return clean[0], 0.0
    return stats.mean(clean), stats.stdev(clean)


def write_comparison_csv(results: list[RunResult], methods: list[str], out_path: Path) -> None:
    """每 KPI 一列, 每 method 兩欄 (mean, std), 加 abs_diff / pct_diff."""
    ok = [r for r in results if r.status == "ok"]
    per_method: dict[str, list[RunResult]] = {m: [r for r in ok if r.method == m] for m in methods}

    header = ["kpi"]
    for m in methods:
        header += [f"{m}_mean", f"{m}_std"]
    if len(methods) >= 2:
        header += ["abs_diff", "pct_diff", "direction"]

    with out_path.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        for kpi in NUMERIC_KPI_FIELDS:
            row: list[Any] = [kpi]
            means: list[float] = []
            for m in methods:
                vals = [getattr(r, kpi) for r in per_method.get(m, [])]
                mu, sd = _mean_std(vals)
                means.append(mu)
                row += [f"{mu:.4f}" if not _is_nan(mu) else "NA",
                        f"{sd:.4f}" if not _is_nan(sd) else "NA"]
            if len(methods) >= 2:
                base, cmp_ = means[0], means[1]
                if _is_nan(base) or _is_nan(cmp_) or base == 0:
                    row += ["NA", "NA", ""]
                else:
                    abs_d = cmp_ - base
                    pct = abs_d / abs(base) * 100.0
                    direction = _direction_tag(kpi, pct)
                    row += [f"{abs_d:.4f}", f"{pct:+.2f}%", direction]
            w.writerow(row)


def _direction_tag(kpi: str, pct_diff: float) -> str:
    if pct_diff == 0:
        return "flat"
    improved = (pct_diff < 0) if kpi in LOWER_IS_BETTER else (pct_diff > 0)
    return "improved" if improved else "worse"


def write_markdown_report(
    results: list[RunResult], methods: list[str],
    scenario_info: dict[str, Any], out_path: Path,
) -> None:
    ok = [r for r in results if r.status == "ok"]
    failed = [r for r in results if r.status != "ok"]
    per_method: dict[str, list[RunResult]] = {m: [r for r in ok if r.method == m] for m in methods}

    lines: list[str] = []
    lines.append(f"# RAWSim-O MAPF 方法比對報告")
    lines.append("")
    lines.append(f"- 生成時間: {datetime.now().isoformat(timespec='seconds')}")
    lines.append(f"- xlayo: `{scenario_info['xlayo']}`")
    lines.append(f"- xsett: `{scenario_info['xsett']}`")
    lines.append(f"- 模擬時長: {scenario_info['sim_duration_sec']:.0f} s")
    lines.append(f"- Seeds: {scenario_info['seeds']}")
    lines.append(f"- Methods:")
    for m, path in scenario_info["methods"].items():
        lines.append(f"  - **{m}** ← `{path}`")
    lines.append("")
    lines.append(f"- 成功: {len(ok)} / 失敗: {len(failed)}")
    if failed:
        lines.append("")
        lines.append("### ⚠ 失敗清單")
        for r in failed:
            lines.append(f"- `{r.method}` seed={r.seed}: {r.error[:200]}")
    lines.append("")
    lines.append("## KPI 比較表")
    lines.append("")

    header = ["KPI"] + [f"{m} (mean±std)" for m in methods]
    if len(methods) >= 2:
        header += ["abs_diff", "pct_diff", "→"]
    lines.append("| " + " | ".join(header) + " |")
    lines.append("|" + "|".join(["---"] * len(header)) + "|")

    for kpi in NUMERIC_KPI_FIELDS:
        cells: list[str] = [kpi]
        means: list[float] = []
        for m in methods:
            vals = [getattr(r, kpi) for r in per_method.get(m, [])]
            mu, sd = _mean_std(vals)
            means.append(mu)
            cells.append("NA" if _is_nan(mu) else f"{mu:.3f}±{sd:.3f}")
        if len(methods) >= 2:
            base, cmp_ = means[0], means[1]
            if _is_nan(base) or _is_nan(cmp_) or base == 0:
                cells += ["NA", "NA", ""]
            else:
                abs_d = cmp_ - base
                pct = abs_d / abs(base) * 100.0
                tag = _direction_tag(kpi, pct)
                icon = {"improved": "🟢", "worse": "🔴", "flat": "⚪"}.get(tag, "")
                cells += [f"{abs_d:+.3f}", f"{pct:+.2f}%", icon]
        lines.append("| " + " | ".join(cells) + " |")

    lines.append("")
    lines.append("## 關鍵差異摘要")
    if len(methods) >= 2 and all(per_method.get(m) for m in methods[:2]):
        base_m, cmp_m = methods[0], methods[1]
        for kpi in ["total_energy_with_idle_kJ", "total_orders_completed",
                    "energy_per_order_with_idle_kJ", "total_wait_time_seconds"]:
            b = _mean_std([getattr(r, kpi) for r in per_method[base_m]])[0]
            c = _mean_std([getattr(r, kpi) for r in per_method[cmp_m]])[0]
            if not _is_nan(b) and not _is_nan(c) and b != 0:
                pct = (c - b) / abs(b) * 100.0
                lines.append(f"- **{kpi}**: {cmp_m} vs {base_m} = {pct:+.2f}%")

    lines.append("")
    lines.append("## 已知限制 (需 C# 端補 logging)")
    lines.append("- `stopgo_energy_empty_kJ` / `stopgo_energy_loaded_kJ`: 目前只有 stop-and-go 次數分 empty/loaded, 能耗未分.")
    lines.append("  TODO: 在 `RAWSimO.Core/Bots/BotNormal.cs` 加 `StatStopGoEnergyEmpty/LoadedJ` 欄位,")
    lines.append("        於 `RAWSimO.Core/InstanceStatistics.cs:1060+` 輸出.")
    lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8")


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def parse_methods_arg(items: list[str]) -> dict[str, Path]:
    out: dict[str, Path] = {}
    for item in items:
        if "=" not in item:
            raise argparse.ArgumentTypeError(
                f"--methods 格式: label=xconf_path (got: {item})")
        label, path = item.split("=", 1)
        out[label.strip()] = Path(path.strip()).resolve()
    return out


def load_yaml_config(path: Path) -> dict[str, Any]:
    try:
        import yaml  # type: ignore
    except ImportError:
        sys.exit("缺 pyyaml; pip install pyyaml 或改用 CLI 參數")
    with path.open("r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def interactive_pick_scenario() -> tuple[Path, Path, dict[str, Path]]:
    """若無 CLI 參數, 掃 CoreBenchmark 讓使用者挑."""
    groups = discover_scenarios()
    print("\n偵測到 scenario 群組:")
    keys = list(groups.keys())
    for i, k in enumerate(keys):
        exts = ", ".join(f"{e}×{len(v)}" for e, v in groups[k].items())
        print(f"  [{i}] {k}  ({exts})")
    sel = int(input("選 scenario 編號: ").strip())
    g = groups[keys[sel]]

    def pick(ext: str) -> Path:
        opts = g.get(ext, [])
        if not opts:
            sys.exit(f"找不到 .{ext}")
        if len(opts) == 1:
            return opts[0]
        for i, p in enumerate(opts):
            print(f"  [{i}] {p.name}")
        i = int(input(f"選 {ext}: ").strip())
        return opts[i]

    layout_ext = "xlayo" if "xlayo" in g else "xinst"
    xlayo = pick(layout_ext)
    xsett = pick("xsett")
    print("\n可用 .xconf (選多個, 逗號分隔索引):")
    xconfs = g.get("xconf", [])
    for i, p in enumerate(xconfs):
        print(f"  [{i}] {p.name}")
    idx = [int(x) for x in input("選: ").split(",")]
    methods = {xconfs[i].stem: xconfs[i] for i in idx}
    return xlayo, xsett, methods


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--config", type=Path, help="YAML 設定檔 (覆寫 CLI 預設)")
    ap.add_argument("--xlayo", type=Path)
    ap.add_argument("--xsett", type=Path)
    ap.add_argument("--methods", nargs="+",
                    help='格式: "label=xconf_path" (可多個)')
    ap.add_argument("--seeds", nargs="+", type=int, default=[42, 43, 44])
    ap.add_argument("--output-dir", type=Path)
    ap.add_argument("--parallel", type=int, default=1)
    ap.add_argument("--dotnet-config", default="Release", choices=["Debug", "Release"])
    ap.add_argument("--timeout-sec", type=int, default=0, help="0 = 不限")
    ap.add_argument("--verbose", "-v", action="store_true")
    ap.add_argument("--quiet", "-q", action="store_true")
    args = ap.parse_args(argv)

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else (logging.WARNING if args.quiet else logging.INFO),
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
        datefmt="%H:%M:%S",
    )

    # 合併 yaml + CLI (CLI 優先)
    cfg: dict[str, Any] = {}
    if args.config:
        cfg = load_yaml_config(args.config)

    xlayo = args.xlayo or Path(cfg.get("scenario", {}).get("xlayo", ""))
    xsett = args.xsett or Path(cfg.get("scenario", {}).get("xsett", ""))
    if args.methods:
        methods = parse_methods_arg(args.methods)
    elif cfg.get("methods"):
        methods = {k: Path(v).resolve() for k, v in cfg["methods"].items()}
    else:
        methods = {}
    seeds = args.seeds if args.seeds != [42, 43, 44] else cfg.get("seeds", args.seeds)
    output_dir = args.output_dir or Path(cfg.get("output_dir", ""))
    parallel = args.parallel if args.parallel != 1 else int(cfg.get("parallel", 1))

    if not xlayo or not xlayo.exists() or not xsett or not xsett.exists() or not methods:
        LOG.warning("缺必要參數, 進入 interactive mode")
        xlayo, xsett, methods = interactive_pick_scenario()
        if not seeds:
            seeds = [int(s) for s in input("seeds (空白分隔): ").split()]

    if not output_dir:
        output_dir = REPO_ROOT / "results" / datetime.now().strftime("%Y%m%d_%H%M%S")
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    sim_duration = read_sim_duration(xsett)
    LOG.info("Scenario: %s + %s (duration=%.0fs)", xlayo.name, xsett.name, sim_duration)
    LOG.info("Methods: %s", list(methods.keys()))
    LOG.info("Seeds: %s", seeds)
    LOG.info("Output: %s", output_dir)

    # 組 run configs
    run_configs: list[RunConfig] = []
    for label, xconf in methods.items():
        for seed in seeds:
            run_configs.append(RunConfig(
                xlayo=xlayo.resolve(), xsett=xsett.resolve(),
                xconf=xconf.resolve(),
                method_label=label, seed=int(seed),
                output_dir=output_dir / "sim_out",
            ))

    results: list[RunResult] = []
    if parallel > 1:
        with ProcessPoolExecutor(max_workers=parallel) as pool:
            futures = {pool.submit(run_single, c, args.dotnet_config, args.timeout_sec): c
                       for c in run_configs}
            for fut in as_completed(futures):
                c = futures[fut]
                try:
                    results.append(fut.result())
                except Exception as e:
                    results.append(RunResult(method=c.method_label, seed=c.seed,
                                             status="failed", error=str(e)))
    else:
        for c in run_configs:
            try:
                results.append(run_single(c, args.dotnet_config, args.timeout_sec))
            except Exception as e:
                LOG.exception("run_single crashed")
                results.append(RunResult(method=c.method_label, seed=c.seed,
                                         status="failed", error=str(e)))

    results.sort(key=lambda r: (r.method, r.seed))

    # 輸出
    method_order = list(methods.keys())
    raw_csv = output_dir / "raw_results.csv"
    cmp_csv = output_dir / "comparison_table.csv"
    md_report = output_dir / "comparison_report.md"
    write_raw_csv(results, raw_csv)
    write_comparison_csv(results, method_order, cmp_csv)
    write_markdown_report(results, method_order, {
        "xlayo": xlayo.name, "xsett": xsett.name,
        "sim_duration_sec": sim_duration, "seeds": list(seeds),
        "methods": {k: v.name for k, v in methods.items()},
    }, md_report)

    ok_count = sum(1 for r in results if r.status == "ok")
    LOG.info("完成: %d/%d 成功", ok_count, len(results))
    LOG.info("輸出: %s", output_dir)
    print(f"\n✓ raw:        {raw_csv}")
    print(f"✓ comparison: {cmp_csv}")
    print(f"✓ report:     {md_report}")

    # 列印關鍵指標統計摘要 (強制確保每次執行都報告這些指標)
    if ok_count > 0:
        print("\n" + "=" * 80)
        print("統計摘要 (Mean ± Std):")
        print("=" * 80)
        for method in method_order:
            method_results = [r for r in results if r.method == method and r.status == "ok"]
            if not method_results:
                continue
            print(f"\n【{method}】")
            # 必報指標: 訂單、能耗、E_support、轉向、距離、效率、per-trip 追蹤
            critical_fields = [
                "total_orders_completed", "throughput_per_hour",
                "total_energy_mech_kJ", "energy_per_order_mech_kJ",
                "total_energy_with_idle_kJ", "energy_per_order_with_idle_kJ",
                "stopgo_count_total", "stopgo_count_empty", "stopgo_count_loaded",
                "stopgo_energy_empty_kJ", "stopgo_energy_loaded_kJ",
                "turn_count", "turn_count_loaded",
                "turn_energy_empty_kJ", "turn_energy_loaded_kJ",
                "total_travel_distance_m", "loaded_distance_m", "distance_empty_m",
                "move_energy_empty_kJ", "move_energy_loaded_kJ",
                "move_time_empty_sec", "move_time_loaded_sec",
                "turn_time_empty_sec", "turn_time_loaded_sec",
                "travel_time_empty_sec", "travel_time_loaded_sec",
                "energy_empty_kJ", "energy_loaded_kJ",
                "total_wait_time_seconds", "wait_energy_kJ",
                "e_support_kJ", "e_support_loaded_kJ", "e_support_empty_kJ",
                "e_support_per_order_kJ", "e_support_to_mech_ratio",
                "time_idle_sec", "robot_utilization",
                # Per-trip tracking (added 2026-04-20)
                "n_trips_loaded", "n_trips_empty",
                "trip_wait_ratio_loaded_mean", "trip_wait_ratio_loaded_median", "trip_wait_ratio_loaded_p95",
                "trip_wait_ratio_empty_mean", "trip_wait_ratio_empty_median", "trip_wait_ratio_empty_p95",
            ]
            for field_name in critical_fields:
                values = [getattr(r, field_name) for r in method_results]
                mean_val, std_val = _mean_std(values)
                if not _is_nan(mean_val):
                    print(f"  {field_name:40s} = {mean_val:12.2f} ± {std_val:8.2f}")

    return 0 if ok_count == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
