"""
Observation Preview — generate a grid image of obs frames from a .npz file.

Usage:
    python scripts/obs_preview.py dataset/episode_0000/bot_0.npz
    python scripts/obs_preview.py dataset/episode_0000/bot_0.npz --start 0 --output obs_preview.png
"""

import argparse
import os
import sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt


def main():
    parser = argparse.ArgumentParser(description="Generate obs grid preview PNG")
    parser.add_argument("path", help=".npz file or episode directory (picks bot_0.npz)")
    parser.add_argument("--output", "-o", default="obs_preview.png",
                        help="Output PNG path (default: obs_preview.png)")
    parser.add_argument("--start", type=int, default=0,
                        help="First frame index (default: 0)")
    parser.add_argument("--rows", type=int, default=4,
                        help="Rows in grid (default: 4)")
    parser.add_argument("--cols", type=int, default=5,
                        help="Columns in grid (default: 5)")
    args = parser.parse_args()

    path = args.path
    if os.path.isdir(path):
        candidate = os.path.join(path, "bot_0.npz")
        if not os.path.exists(candidate):
            npz_files = sorted(f for f in os.listdir(path) if f.endswith(".npz"))
            if not npz_files:
                print(f"No .npz files found in {path}")
                sys.exit(1)
            candidate = os.path.join(path, npz_files[0])
        path = candidate
        print(f"Using: {path}")

    d = np.load(path)
    n = args.rows * args.cols
    start = args.start

    fig, axes = plt.subplots(args.rows, args.cols, figsize=(args.cols * 3, args.rows * 3))
    for i, ax in enumerate(axes.flat):
        ax.imshow(d["obs"][start + i])
        ax.set_title(f"step {start + i}")
        ax.axis("off")
    plt.tight_layout()
    plt.savefig(args.output, bbox_inches="tight")
    print(f"saved {args.output}")


if __name__ == "__main__":
    main()
