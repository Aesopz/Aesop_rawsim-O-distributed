"""
Phase B data loader. Joins traversal_log.csv with congestion_features.csv per
(BotId, FromNode, ToNode, ReadyTime) and concatenates over a set of seeds.

Usage:
    from load_data import load_seeds
    df = load_seeds(["out_stageA_s0", "out_stageA_s1", ...])
"""
from __future__ import annotations
import os
import pandas as pd

# CSV files use ';' as delimiter and a leading '#' on the header line.
def _read_log(path: str) -> pd.DataFrame:
    with open(path, "r", encoding="utf-8") as f:
        header = f.readline().lstrip("#").strip()
    cols = header.split(";")
    df = pd.read_csv(path, sep=";", comment="#", header=None, names=cols)
    return df


def load_seed(seed_dir: str, run_subdir: str | None = None) -> pd.DataFrame:
    """Load traversal + congestion logs from one seed run and merge."""
    if run_subdir is None:
        # Auto-detect the single benchmark_* subdir.
        children = [d for d in os.listdir(seed_dir) if os.path.isdir(os.path.join(seed_dir, d))]
        run_subdir = children[0]
    base = os.path.join(seed_dir, run_subdir)
    trav = _read_log(os.path.join(base, "traversal_log.csv"))
    cong = _read_log(os.path.join(base, "congestion_features.csv"))
    # Join on (BotId, FromNode, ToNode, ReadyTime == TimeStamp). Both rows are emitted at the
    # same setNextWaypoint call so the quadruple uniquely identifies a segment.
    cong = cong.rename(columns={"TimeStamp": "ReadyTime"})
    merged = trav.merge(cong, on=["BotId", "FromNode", "ToNode", "ReadyTime"], how="inner")
    # Add seed column based on the directory name.
    seed = int(os.path.basename(seed_dir.rstrip(os.sep)).rsplit("_s", 1)[-1])
    merged["Seed"] = seed
    return merged


def load_seeds(seed_dirs: list[str]) -> pd.DataFrame:
    parts = [load_seed(d) for d in seed_dirs]
    return pd.concat(parts, ignore_index=True)


if __name__ == "__main__":
    import sys
    dirs = sys.argv[1:] if len(sys.argv) > 1 else [f"out_stageA_s{s}" for s in range(5)]
    df = load_seeds(dirs)
    print(f"loaded rows: {len(df)}")
    print(df.head(3).to_string())
    print("--- dtypes ---")
    print(df.dtypes)
