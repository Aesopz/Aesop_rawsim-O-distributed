"""Unit tests for rmfs_gym Python package (no server needed)."""
import numpy as np
import pytest
from rmfs_gym.utils.action_utils import (
    encode_action, decode_action, random_action,
    DIRECTION_NORTH, DIRECTION_SOUTH, DIRECTION_EAST, DIRECTION_WEST,
    DIRECTION_STAY, WAYPOINT_IDX_STAY, N_DIRECTIONS,
)
from rmfs_gym.utils.obs_utils import bytes_to_obs, stack_obs


class TestActionUtils:
    def test_encode_decode_roundtrip(self):
        for d_idx in range(N_DIRECTIONS):   # 0..4
            for a in range(3):
                for d in range(3):
                    action = encode_action(d_idx, a, d)
                    d2, a2, dd2 = decode_action(action)
                    assert d2 == d_idx
                    assert a2 == a
                    assert dd2 == d

    def test_encode_dtype(self):
        action = encode_action(3, 1, 2)
        assert action.dtype == np.int32
        assert action.shape == (3,)

    def test_cardinal_constants(self):
        assert DIRECTION_NORTH == 0
        assert DIRECTION_SOUTH == 1
        assert DIRECTION_EAST  == 2
        assert DIRECTION_WEST  == 3
        assert DIRECTION_STAY  == 4
        assert N_DIRECTIONS    == 5

    def test_stay_action(self):
        action = encode_action(DIRECTION_STAY, 1, 1)
        assert action[0] == 4

    def test_stay_backward_compat_alias(self):
        # WAYPOINT_IDX_STAY must equal DIRECTION_STAY for backward compatibility
        assert WAYPOINT_IDX_STAY == DIRECTION_STAY
        action = encode_action(WAYPOINT_IDX_STAY, 1, 1)
        assert action[0] == 4

    def test_random_action_shape(self):
        rng = np.random.default_rng(42)
        for _ in range(100):
            a = random_action(rng)
            assert a.shape == (3,)
            assert 0 <= a[0] <= 4   # 0=NORTH..4=STAY
            assert 0 <= a[1] <= 2
            assert 0 <= a[2] <= 2


class TestObsUtils:
    def test_bytes_to_obs_shape(self):
        raw = bytes(64 * 64 * 3)
        obs = bytes_to_obs(raw)
        assert obs.shape == (64, 64, 3)
        assert obs.dtype == np.uint8

    def test_bytes_to_obs_values(self):
        raw = bytes([i % 256 for i in range(64 * 64 * 3)])
        obs = bytes_to_obs(raw)
        assert obs[0, 0, 0] == 0
        assert obs[0, 0, 1] == 1

    def test_bytes_to_obs_wrong_size_raises(self):
        with pytest.raises(ValueError):
            bytes_to_obs(bytes(10))

    def test_stack_obs(self):
        obs_list = [np.zeros((64, 64, 3), dtype=np.uint8) for _ in range(5)]
        stacked = stack_obs(obs_list)
        assert stacked.shape == (5, 64, 64, 3)
