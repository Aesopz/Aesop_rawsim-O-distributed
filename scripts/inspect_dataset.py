"""
Dataset Inspector
=================
Inspect isi dataset .npz untuk 1 robot.

Usage:
    python scripts/inspect_dataset.py dataset/episode_0000/bot_0.npz
    python scripts/inspect_dataset.py dataset/episode_0000/bot_0.npz --frames 5
    python scripts/inspect_dataset.py dataset/episode_0000/bot_0.npz --save-frames
"""

import argparse
import os
import sys
import numpy as np

def inspect(path: str, n_frames: int = 3, save_frames: bool = False):
    print(f"\n{'='*60}")
    print(f"File : {path}")
    print(f"Size : {os.path.getsize(path) / 1024:.1f} KB")
    print(f"{'='*60}")

    data = np.load(path)

    # ── 1. Keys & shapes ─────────────────────────────────────────
    print("\n[Keys & Shapes]")
    for key in data.files:
        arr = data[key]
        print(f"  {key:<10} shape={str(arr.shape):<20} dtype={arr.dtype}")

    obs     = data["obs"]      # (T, H, W, C)
    actions = data["actions"]  # (T, 3)
    rewards = data["rewards"]  # (T,)
    dones   = data["dones"]    # (T,)
    T = len(rewards)

    # ── 2. Episode summary ────────────────────────────────────────
    print(f"\n[Episode Summary]")
    print(f"  Total steps     : {T}")
    print(f"  Image size      : {obs.shape[1]}x{obs.shape[2]} px, {obs.shape[3]} channels")
    print(f"  Total reward    : {rewards.sum():.4f}")
    print(f"  Avg reward/step : {rewards.mean():.4f}")
    print(f"  Min reward      : {rewards.min():.4f}")
    print(f"  Max reward      : {rewards.max():.4f}")
    print(f"  Episodes done   : {dones.sum()} times done=True")

    # ── 3. Obs stats ──────────────────────────────────────────────
    print(f"\n[Observation Stats]")
    print(f"  Pixel min  : {obs.min()}")
    print(f"  Pixel max  : {obs.max()}")
    print(f"  Pixel mean : {obs.mean():.2f}")
    print(f"  Pixel std  : {obs.std():.2f}")

    # ── 4. Action distribution ────────────────────────────────────
    print(f"\n[Action Distribution]")
    waypoints = actions[:, 0]
    accels    = actions[:, 1]
    decels    = actions[:, 2]

    print(f"  Waypoint idx (0-8, 8=STAY):")
    for v, cnt in sorted(zip(*np.unique(waypoints, return_counts=True))):
        bar = "#" * int(cnt / T * 40)
        print(f"    [{v}] {cnt:5d} ({cnt/T*100:5.1f}%)  {bar}")

    print(f"  Accel mode  : {dict(zip(*[x.tolist() for x in np.unique(accels, return_counts=True)]))}")
    print(f"  Decel mode  : {dict(zip(*[x.tolist() for x in np.unique(decels, return_counts=True)]))}")

    # ── 5. Sample frames ──────────────────────────────────────────
    n_frames = min(n_frames, T)
    indices  = np.linspace(0, T - 1, n_frames, dtype=int)

    print(f"\n[Sample Frames] (showing {n_frames} frames)")
    for i in indices:
        frame = obs[i]
        print(f"  step={i:4d} | action={actions[i].tolist()} "
              f"| reward={rewards[i]:+.4f} | done={dones[i]} "
              f"| px_mean={frame.mean():.1f}")

    # ── 6. Save sample frames as PNG ─────────────────────────────
    if save_frames:
        try:
            from PIL import Image
            out_dir = os.path.splitext(path)[0] + "_frames"
            os.makedirs(out_dir, exist_ok=True)
            for i in indices:
                img = Image.fromarray(obs[i], "RGB")
                img_path = os.path.join(out_dir, f"step_{i:04d}.png")
                img.save(img_path)
            print(f"\n[Saved Frames] → {out_dir}/")
        except ImportError:
            print("\n[!] Pillow not installed. Run: pip install Pillow")

    print(f"\n{'='*60}\n")


def main():
    parser = argparse.ArgumentParser(description="Inspect dataset .npz for 1 robot")
    parser.add_argument("path", help="Path to .npz file, e.g. dataset/episode_0000/bot_0.npz")
    parser.add_argument("--frames",      type=int,  default=3,     help="Number of sample frames to show (default: 3)")
    parser.add_argument("--save-frames", action="store_true",      help="Save sample frames as PNG (requires Pillow)")
    args = parser.parse_args()

    if not os.path.exists(args.path):
        print(f"[Error] File not found: {args.path}")
        sys.exit(1)

    inspect(args.path, n_frames=args.frames, save_frames=args.save_frames)


if __name__ == "__main__":
    main()
