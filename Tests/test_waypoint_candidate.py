"""
Validate CardinalDirection angle classification logic (Python mirror of WaypointCandidateCache.cs).
These tests run offline — no GymServer or C# needed.

Angle convention (matches Edge.cs):
  0° = East, increasing clockwise
  EAST  =   0° (±45°: 315° < a ≤ 45°, wraps around)
  SOUTH =  90° (±45°: 45°  < a ≤ 135°)
  WEST  = 180° (±45°: 135° < a ≤ 225°)
  NORTH = 270° (±45°: 225° < a ≤ 315°)
"""
import pytest

NORTH = 0
SOUTH = 1
EAST  = 2
WEST  = 3
STAY  = 4


def classify_angle(angle: int) -> int:
    """Python mirror of CardinalDirection.ClassifyAngle in WaypointCandidateCache.cs."""
    a = angle % 360
    if a <= 45 or a > 315:
        return EAST
    if 45 < a <= 135:
        return SOUTH
    if 135 < a <= 225:
        return WEST
    return NORTH  # 225 < a <= 315


def get_action_mask(directional: list) -> list:
    """Python mirror of GymSimulationHost.GetActionMask."""
    return [
        directional[NORTH] is not None,
        directional[SOUTH] is not None,
        directional[EAST]  is not None,
        directional[WEST]  is not None,
        True,  # STAY always valid
    ]


# ── Cardinal angle classification ────────────────────────────────────────────

class TestClassifyAngle:
    def test_east_0(self):
        assert classify_angle(0) == EAST

    def test_east_45_boundary(self):
        assert classify_angle(45) == EAST   # boundary belongs to EAST (a <= 45)

    def test_east_wrap_350(self):
        assert classify_angle(350) == EAST  # 350 > 315 → EAST

    def test_east_wrap_316(self):
        assert classify_angle(316) == EAST

    def test_south_90(self):
        assert classify_angle(90) == SOUTH

    def test_south_46(self):
        assert classify_angle(46) == SOUTH  # just past EAST boundary

    def test_south_135_boundary(self):
        assert classify_angle(135) == SOUTH  # boundary belongs to SOUTH (a <= 135)

    def test_west_180(self):
        assert classify_angle(180) == WEST

    def test_west_136(self):
        assert classify_angle(136) == WEST

    def test_west_225_boundary(self):
        assert classify_angle(225) == WEST  # boundary belongs to WEST (a <= 225)

    def test_north_270(self):
        assert classify_angle(270) == NORTH

    def test_north_226(self):
        assert classify_angle(226) == NORTH

    def test_north_315_boundary(self):
        assert classify_angle(315) == NORTH  # boundary belongs to NORTH (a <= 315)

    def test_full_360_wraps_to_east(self):
        assert classify_angle(360) == EAST  # 360 % 360 = 0 → EAST

    def test_negative_angle_normalizes(self):
        assert classify_angle(-90) == NORTH  # -90 % 360 = 270


# ── Cardinal constants ────────────────────────────────────────────────────────

class TestCardinalConstants:
    def test_north_is_0(self):   assert NORTH == 0
    def test_south_is_1(self):   assert SOUTH == 1
    def test_east_is_2(self):    assert EAST  == 2
    def test_west_is_3(self):    assert WEST  == 3
    def test_stay_is_4(self):    assert STAY  == 4

    def test_all_four_cardinals_distinct(self):
        assert len({NORTH, SOUTH, EAST, WEST}) == 4

    def test_stay_different_from_cardinals(self):
        assert STAY not in {NORTH, SOUTH, EAST, WEST}


# ── Action mask logic ─────────────────────────────────────────────────────────

class TestActionMask:
    def test_all_directions_available(self):
        directional = ["wp_N", "wp_S", "wp_E", "wp_W"]
        mask = get_action_mask(directional)
        assert mask == [True, True, True, True, True]

    def test_no_directions_available(self):
        directional = [None, None, None, None]
        mask = get_action_mask(directional)
        assert mask == [False, False, False, False, True]  # STAY always True

    def test_only_north_available(self):
        directional = ["wp_N", None, None, None]
        mask = get_action_mask(directional)
        assert mask[NORTH] is True
        assert mask[SOUTH] is False
        assert mask[EAST]  is False
        assert mask[WEST]  is False
        assert mask[STAY]  is True

    def test_stay_always_true_regardless(self):
        for combo in [[None]*4, ["wp"]*4, [None, "wp", None, "wp"]]:
            mask = get_action_mask(combo)
            assert mask[STAY] is True, f"STAY must be True for {combo}"

    def test_mask_length_is_5(self):
        mask = get_action_mask([None, None, None, None])
        assert len(mask) == 5


# ── Direction coverage ────────────────────────────────────────────────────────

class TestDirectionCoverage:
    def test_every_10_degrees_classified(self):
        """Every angle 0-359 must map to one of the 4 cardinals."""
        for angle in range(0, 360, 10):
            result = classify_angle(angle)
            assert result in {NORTH, SOUTH, EAST, WEST}, \
                f"Angle {angle} → {result} is not a valid cardinal"

    def test_cardinal_distribution_roughly_equal(self):
        """Each cardinal should cover roughly 90° of the 360° range."""
        counts = {NORTH: 0, SOUTH: 0, EAST: 0, WEST: 0}
        for angle in range(360):
            counts[classify_angle(angle)] += 1
        for direction, count in counts.items():
            assert 88 <= count <= 92, \
                f"Direction {direction} covers {count} degrees, expected ~90"
