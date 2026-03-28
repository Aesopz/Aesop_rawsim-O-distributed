"""Utilities for decoding raw observation bytes from GymServer."""
import numpy as np


def bytes_to_obs(raw: bytes, h: int, w: int, c: int = 3) -> np.ndarray:
    """Convert raw RGB bytes to a numpy array of shape (H, W, C) uint8."""
    arr = np.frombuffer(raw, dtype=np.uint8)
    if arr.size != h * w * c:
        raise ValueError(f"Expected {h*w*c} bytes, got {arr.size}")
    return arr.reshape(h, w, c).copy()


def stack_obs(obs_list: list[np.ndarray]) -> np.ndarray:
    """Stack a list of (H, W, C) observations into (N, H, W, C)."""
    return np.stack(obs_list, axis=0)
