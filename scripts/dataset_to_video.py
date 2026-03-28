"""
Dataset Viewer — convert .npz episode files to video
=====================================================
Loads observation images from a dataset .npz file and exports them
as an MP4 video (or animated GIF if ffmpeg is unavailable).

Usage:
    # Single bot, single episode:
    python scripts/dataset_to_video.py dataset/episode_0000/bot_0.npz

    # With options:
    python scripts/dataset_to_video.py dataset/episode_0000/bot_0.npz \\
        --fps 10 --output video.mp4 --start 0 --end 500

    # Show all bots in one episode as a grid video:
    python scripts/dataset_to_video.py dataset/episode_0000/ \\
        --grid --fps 10 --output episode_grid.mp4
"""

import argparse
import os
import sys
import numpy as np


def load_npz(path: str) -> dict:
    data = np.load(path)
    return {k: data[k] for k in data.files}


def make_single_video(npz_path: str, output: str, fps: int, start: int, end: int | None):
    """Export observations from one bot .npz as a video."""
    data = load_npz(npz_path)
    obs = data["obs"]          # (T, H, W, C) uint8
    rewards = data.get("rewards", None)
    actions = data.get("actions", None)

    T = obs.shape[0]
    end = min(end, T) if end is not None else T
    obs = obs[start:end]
    if rewards is not None:
        rewards = rewards[start:end]

    print(f"Loaded: {npz_path}  |  frames {start}–{end} of {T}  |  shape {obs.shape}")

    _write_video(obs, output, fps, rewards=rewards, actions=actions)


def make_grid_video(episode_dir: str, output: str, fps: int, start: int, end: int | None,
                    max_cols: int = 4):
    """Export a grid of all bots in one episode as a single video."""
    npz_files = sorted(
        f for f in os.listdir(episode_dir) if f.endswith(".npz")
    )
    if not npz_files:
        print(f"No .npz files found in {episode_dir}")
        sys.exit(1)

    all_obs = []
    for fname in npz_files:
        d = load_npz(os.path.join(episode_dir, fname))
        obs = d["obs"]
        T = obs.shape[0]
        e = min(end, T) if end is not None else T
        all_obs.append(obs[start:e])

    # Pad to same length
    max_T = max(o.shape[0] for o in all_obs)
    H, W, C = all_obs[0].shape[1:]
    padded = []
    for o in all_obs:
        pad_len = max_T - o.shape[0]
        if pad_len > 0:
            o = np.concatenate([o, np.zeros((pad_len, H, W, C), dtype=np.uint8)], axis=0)
        padded.append(o)

    n_bots = len(padded)
    n_cols = min(n_bots, max_cols)
    n_rows = (n_bots + n_cols - 1) // n_cols

    bot_labels = [f.replace(".npz", "") for f in npz_files]
    print(f"Grid video: {n_bots} bots, {n_rows}×{n_cols}, {max_T} frames")

    # Build grid frames
    grid_frames = []
    for t in range(max_T):
        row_imgs = []
        for r in range(n_rows):
            col_imgs = []
            for c in range(n_cols):
                idx = r * n_cols + c
                if idx < n_bots:
                    col_imgs.append(padded[idx][t])
                else:
                    col_imgs.append(np.zeros((H, W, C), dtype=np.uint8))
            row_imgs.append(np.concatenate(col_imgs, axis=1))
        grid_frames.append(np.concatenate(row_imgs, axis=0))

    grid_obs = np.stack(grid_frames, axis=0)
    _write_video(grid_obs, output, fps)


def _write_video(obs: np.ndarray, output: str, fps: int,
                 rewards=None, actions=None):
    """Write obs (T,H,W,C) uint8 to video file."""
    ext = os.path.splitext(output)[1].lower()

    # --- Try opencv first (fastest) ---
    if ext in (".mp4", ".avi", ".mkv"):
        try:
            import cv2
            _write_cv2(obs, output, fps, rewards, actions)
            return
        except ImportError:
            print("[cv2 not found] Falling back to imageio …")

    # --- Try imageio (supports mp4 via ffmpeg, gif natively) ---
    try:
        import imageio
        _write_imageio(obs, output, fps, rewards, actions)
        return
    except ImportError:
        print("[imageio not found] Falling back to matplotlib animation …")

    # --- Fallback: matplotlib animation → gif ---
    if not output.endswith(".gif"):
        output = os.path.splitext(output)[0] + ".gif"
    _write_matplotlib(obs, output, fps, rewards, actions)


def _write_cv2(obs, output, fps, rewards, actions):
    import cv2

    T, H, W, C = obs.shape
    fourcc = cv2.VideoWriter_fourcc(*"mp4v")
    writer = cv2.VideoWriter(output, fourcc, fps, (W, H))

    for t in range(T):
        frame = obs[t]  # RGB uint8
        frame_bgr = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)

        # Overlay step / reward text
        info = f"step {t}"
        if rewards is not None:
            info += f"  r={rewards[t]:.3f}"
        cv2.putText(frame_bgr, info, (2, H - 4),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.28, (255, 255, 255), 1, cv2.LINE_AA)
        writer.write(frame_bgr)

    writer.release()
    print(f"Saved (cv2): {output}  [{T} frames @ {fps} fps]")


def _write_imageio(obs, output, fps, rewards, actions):
    import imageio

    T = obs.shape[0]
    ext = os.path.splitext(output)[1].lower()
    kwargs = {}
    if ext == ".gif":
        kwargs = {"loop": 0, "duration": 1.0 / fps}
        writer_fn = imageio.mimsave
    else:
        # mp4 via ffmpeg plugin
        writer_fn = imageio.mimsave
        kwargs = {"fps": fps, "codec": "libx264", "quality": 8}

    imageio.mimsave(output, [obs[t] for t in range(T)], **kwargs)
    print(f"Saved (imageio): {output}  [{T} frames @ {fps} fps]")


def _write_matplotlib(obs, output, fps, rewards, actions):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import matplotlib.animation as animation

    T, H, W, C = obs.shape
    fig, ax = plt.subplots(figsize=(W / 40, H / 40), dpi=80)
    ax.axis("off")
    im = ax.imshow(obs[0])

    title = ax.set_title("step 0", fontsize=8, color="white", pad=2)
    fig.patch.set_facecolor("black")

    def update(t):
        im.set_data(obs[t])
        info = f"step {t}"
        if rewards is not None:
            info += f"  r={rewards[t]:.3f}"
        title.set_text(info)
        return [im, title]

    ani = animation.FuncAnimation(fig, update, frames=T, interval=1000 // fps, blit=True)
    writer = animation.PillowWriter(fps=fps)
    ani.save(output, writer=writer)
    plt.close(fig)
    print(f"Saved (matplotlib/gif): {output}  [{T} frames @ {fps} fps]")


def main():
    parser = argparse.ArgumentParser(description="Convert dataset .npz to video")
    parser.add_argument("path", help=".npz file or episode directory (for --grid)")
    parser.add_argument("--output", "-o", default=None,
                        help="Output file (default: <input>.mp4)")
    parser.add_argument("--fps", type=int, default=10,
                        help="Frames per second (default: 10)")
    parser.add_argument("--start", type=int, default=0,
                        help="First frame index (default: 0)")
    parser.add_argument("--end", type=int, default=None,
                        help="Last frame index exclusive (default: all)")
    parser.add_argument("--grid", action="store_true",
                        help="Grid view: path must be an episode directory")
    parser.add_argument("--cols", type=int, default=4,
                        help="Columns in grid mode (default: 4)")
    args = parser.parse_args()

    path = args.path
    output = args.output

    if args.grid or os.path.isdir(path):
        if output is None:
            output = os.path.join(path, "episode_grid.mp4")
        make_grid_video(path, output, args.fps, args.start, args.end, args.cols)
    else:
        if not path.endswith(".npz"):
            print(f"Expected a .npz file or directory, got: {path}")
            sys.exit(1)
        if output is None:
            output = os.path.splitext(path)[0] + ".mp4"
        make_single_video(path, output, args.fps, args.start, args.end)


if __name__ == "__main__":
    main()
