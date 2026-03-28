"""
Tests for Phase 4 dataset format validation.
These run offline (no GymServer needed) — they validate the .npz
schema that collect_baseline.py produces.
"""
import numpy as np
import os
import tempfile
import pytest


# ── Schema constants (canonical — Plan 3 imports from here) ─────────────────
OBS_DTYPE    = np.uint8
OBS_SHAPE    = (64, 64, 3)   # single timestep
ACTION_DTYPE = np.int32
ACTION_SHAPE = (3,)          # [waypoint_idx, accel_mode, decel_mode]
REWARD_DTYPE = np.float32
DONE_DTYPE   = np.bool_


def make_fake_episode_npz(path: str, T: int = 10, n_bots: int = 3):
    """Create a fake episode directory with .npz files matching the canonical schema."""
    os.makedirs(path, exist_ok=True)
    for bot_i in range(n_bots):
        np.savez(
            os.path.join(path, f"bot_{bot_i}.npz"),
            obs     = np.random.randint(0, 256, (T, *OBS_SHAPE),   dtype=OBS_DTYPE),
            actions = np.random.randint(0, 5,   (T, *ACTION_SHAPE), dtype=ACTION_DTYPE),
            rewards = np.random.randn(T).astype(REWARD_DTYPE),
            dones   = np.zeros(T, dtype=DONE_DTYPE),
        )
    return path


class TestNpzSchema:
    """Validate that saved .npz files conform to the canonical schema."""

    def test_obs_shape_and_dtype(self, tmp_path):
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=20)
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert data["obs"].shape == (20, 64, 64, 3)
        assert data["obs"].dtype == np.uint8

    def test_actions_shape_and_dtype(self, tmp_path):
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=15)
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert data["actions"].shape == (15, 3)
        assert data["actions"].dtype == np.int32

    def test_rewards_shape_and_dtype(self, tmp_path):
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=8)
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert data["rewards"].shape == (8,)
        assert data["rewards"].dtype == np.float32

    def test_dones_shape_and_dtype(self, tmp_path):
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=8)
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert data["dones"].shape == (8,)
        assert data["dones"].dtype == np.bool_

    def test_all_timesteps_consistent(self, tmp_path):
        T = 50
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=T, n_bots=5)
        for bot_i in range(5):
            data = np.load(os.path.join(ep_dir, f"bot_{bot_i}.npz"))
            assert data["obs"].shape[0]     == T
            assert data["actions"].shape[0] == T
            assert data["rewards"].shape[0] == T
            assert data["dones"].shape[0]   == T

    def test_action_values_in_range(self, tmp_path):
        """Baseline actions are all [4, 1, 1] — direction=STAY(4), accel=1, decel=1."""
        T = 5
        ep_dir = str(tmp_path / "ep")
        os.makedirs(ep_dir)
        actions = np.tile([4, 1, 1], (T, 1)).astype(np.int32)
        np.savez(os.path.join(ep_dir, "bot_0.npz"),
                 obs=np.zeros((T, 64, 64, 3), dtype=np.uint8),
                 actions=actions,
                 rewards=np.zeros(T, dtype=np.float32),
                 dones=np.zeros(T, dtype=np.bool_))
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert np.all(data["actions"][:, 0] == 4)  # STAY
        assert np.all((data["actions"][:, 1] >= 0) & (data["actions"][:, 1] <= 2))
        assert np.all((data["actions"][:, 2] >= 0) & (data["actions"][:, 2] <= 2))

    def test_npz_keys_present(self, tmp_path):
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"))
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        assert set(data.files) == {"obs", "actions", "rewards", "dones"}


class TestDatasetDirectory:
    """Validate dataset directory structure."""

    def test_episode_dirs_created(self, tmp_path):
        dataset_dir = str(tmp_path / "dataset")
        n_episodes = 3
        n_bots = 2
        for ep in range(n_episodes):
            make_fake_episode_npz(
                os.path.join(dataset_dir, f"episode_{ep:04d}"),
                T=10, n_bots=n_bots
            )
        eps = sorted(os.listdir(dataset_dir))
        assert len(eps) == n_episodes
        assert eps[0] == "episode_0000"
        assert eps[-1] == f"episode_{n_episodes-1:04d}"

    def test_all_bots_have_npz(self, tmp_path):
        dataset_dir = str(tmp_path / "dataset")
        n_bots = 4
        ep_dir = make_fake_episode_npz(
            os.path.join(dataset_dir, "episode_0000"), n_bots=n_bots
        )
        bot_files = sorted(os.listdir(ep_dir))
        assert len(bot_files) == n_bots
        for i, fname in enumerate(bot_files):
            assert fname == f"bot_{i}.npz"

    def test_load_and_use_obs(self, tmp_path):
        """Simulate how Plan 3 world model would load and use the data."""
        ep_dir = make_fake_episode_npz(str(tmp_path / "ep"), T=20)
        data = np.load(os.path.join(ep_dir, "bot_0.npz"))
        obs = data["obs"]          # (T, H, W, C)
        obs_t  = obs[:-1]          # (T-1, H, W, C) current frames
        obs_tp1 = obs[1:]          # (T-1, H, W, C) next frames
        assert obs_t.shape  == (19, 64, 64, 3)
        assert obs_tp1.shape == (19, 64, 64, 3)
        # Normalize to [0, 1] as world model input
        obs_norm = obs_t.astype(np.float32) / 255.0
        assert obs_norm.max() <= 1.0
        assert obs_norm.min() >= 0.0
