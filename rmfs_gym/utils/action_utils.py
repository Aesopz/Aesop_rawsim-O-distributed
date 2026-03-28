"""
CANONICAL action encoding for rmfs_gym.
Plan 3 (World Model) imports from here — do not rename or move.

Action format: [direction_idx, accel_mode, decel_mode]
  direction_idx: 0=NORTH, 1=SOUTH, 2=EAST, 3=WEST, 4=STAY
  accel_mode:    0=low(0.5x), 1=normal(1.0x), 2=high(1.5x)
  decel_mode:    0=gentle(0.5x), 1=normal(1.0x), 2=hard(1.5x)

Cardinal angle convention (matches Edge.cs in RAWSimO.GymServer):
  0°=East, increasing clockwise → NORTH=270°, SOUTH=90°, EAST=0°, WEST=180°
"""
import numpy as np

DIRECTION_NORTH = 0
DIRECTION_SOUTH = 1
DIRECTION_EAST  = 2
DIRECTION_WEST  = 3
DIRECTION_STAY  = 4

N_DIRECTIONS   = 5   # total choices for action[0]: 0-3=cardinal, 4=STAY
N_ACCEL_MODES  = 3
N_DECEL_MODES  = 3
ACTION_DIM     = 3   # [direction_idx, accel_mode, decel_mode]

# Backward-compatibility alias
WAYPOINT_IDX_STAY = DIRECTION_STAY


def encode_action(direction_idx: int, accel_mode: int, decel_mode: int) -> np.ndarray:
    """Encode a single agent's action as an int32 array of shape (3,)."""
    return np.array([direction_idx, accel_mode, decel_mode], dtype=np.int32)


def decode_action(action: np.ndarray) -> tuple[int, int, int]:
    """Decode action array into (direction_idx, accel_mode, decel_mode)."""
    return int(action[0]), int(action[1]), int(action[2])


def random_action(rng: np.random.Generator | None = None) -> np.ndarray:
    """Sample a uniformly random action."""
    if rng is None:
        rng = np.random.default_rng()
    direction_idx = rng.integers(0, N_DIRECTIONS)   # 0..4
    accel_mode    = rng.integers(0, N_ACCEL_MODES)
    decel_mode    = rng.integers(0, N_DECEL_MODES)
    return encode_action(int(direction_idx), int(accel_mode), int(decel_mode))


# One-hot encoding dimensi per sub-action, dikoncatenasi → (11,)
# Digunakan sebagai input MDN-RNN (Plan 3).
# Alasan concatenated per-dimensi (bukan flat one-hot 45):
#   - Preserves independence antar sub-action (accel/decel tidak bergantung pada direction)
#   - Lebih kompak (11 vs 45) → LSTM input_size lebih kecil
ACTION_ONEHOT_DIM = N_DIRECTIONS + N_ACCEL_MODES + N_DECEL_MODES  # 5+3+3 = 11


def encode_action_onehot(action: np.ndarray) -> np.ndarray:
    """MultiDiscrete [5,3,3] → concatenated one-hot (11,).

    Encoding:
        direction one-hot (5,) | accel one-hot (3,) | decel one-hot (3,)

    Used as MDN-RNN input in Plan 3 (World Model).
    """
    dir_idx, acc_idx, dec_idx = int(action[0]), int(action[1]), int(action[2])
    dir_oh  = np.eye(N_DIRECTIONS, dtype=np.float32)[dir_idx]   # (5,)
    acc_oh  = np.eye(N_ACCEL_MODES, dtype=np.float32)[acc_idx]  # (3,)
    dec_oh  = np.eye(N_DECEL_MODES, dtype=np.float32)[dec_idx]  # (3,)
    return np.concatenate([dir_oh, acc_oh, dec_oh])              # (11,)


def encode_action_onehot_batch(actions: np.ndarray) -> np.ndarray:
    """Batch version: (B, T, 3) int → (B, T, 11) float32."""
    B, T, _ = actions.shape
    out = np.zeros((B, T, ACTION_ONEHOT_DIM), dtype=np.float32)
    for b in range(B):
        for t in range(T):
            out[b, t] = encode_action_onehot(actions[b, t])
    return out
