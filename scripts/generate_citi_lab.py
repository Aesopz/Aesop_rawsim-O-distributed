#!/usr/bin/env python3
"""
generate_citi_lab.py

Generates citi-lab.xinst (RAWSim-O instance) replicating the netlogo-rmfs warehouse layout:
  - 31 rows x 49 cols grid  (1 cell = 1.0 m)
  - 300 active pods, 30 robots
  - 3 OutputStations (order picking, left side)
  - 1 InputStation  (replenishment, right side)

Run from the RAWSim-O-CITI repo root:
    python scripts/generate_citi_lab.py

Output: Material/Instances/CoreBenchmark/citi-lab.xinst
"""

import random
import xml.etree.ElementTree as ET
from xml.dom import minidom
from pathlib import Path

# ─── Layout parameters (mirrors netlogo-rmfs/environment/config.py) ───────────
SEED = 42
POD_BATCH_H = 5        # pods per batch horizontally
POD_BATCH_V = 2        # pods per batch vertically
POD_BATCH_H_MAX = 5    # number of horizontal batches
POD_BATCH_V_MAX = 10   # number of vertical batches
RESERVED_COL_START = 9
RESERVED_COL_END = 9
RESERVED_COL_STATION = 5
ORDER_PICKER_TOTAL = 3
ORDER_REPLENISHMENT_TOTAL = 1
TOTAL_PODS_ACTIVE = 300
TOTAL_CHARGING_STATIONS = 10

CELL_SIZE = 1.0  # metres per cell

# ─── Tile codes (mirrors netlogo-rmfs/environment/config.py TILE_CODES) ───────
T_BLANK = 99
T_POD = 1
T_CHARGING = 2
T_AISLE = 3
T_HRAIL_A = 4
T_HRAIL_B = 5
T_VRAIL_A = 6
T_VRAIL_B = 7
T_STATION = 11
T_STATION_MIRROR = 21

# ─── RAWSim-O physical parameters ─────────────────────────────────────────────
BOT_COUNT = 30
BOT_RADIUS = 0.35
BOT_MAX_VELOCITY = 1.0
BOT_MAX_ACCEL = 0.25
BOT_MAX_DECEL = 0.25
BOT_TURN_SPEED = 1.0
BOT_POD_TRANSFER_TIME = 3.0
BOT_COLLISION_PENALTY = 0.5

POD_RADIUS = 0.4
POD_CAPACITY = 500      # avg of ~3000 total slots / 6 pod types

STATION_RADIUS = 0.4
OUTPUT_STATION_CAPACITY = 20
OUTPUT_STATION_TRANSFER_TIME = 8.0
INPUT_STATION_CAPACITY = 1000
INPUT_STATION_TRANSFER_TIME = 10.0

SEMAPHORE_CAPACITY = 2


# ─── Grid dimension helpers ────────────────────────────────────────────────────

def total_rows():
    return (POD_BATCH_V_MAX * POD_BATCH_V) + POD_BATCH_V_MAX + 1  # 31


def total_cols():
    pod_space = (POD_BATCH_H * POD_BATCH_H_MAX) + POD_BATCH_H_MAX - 1  # 29
    return (RESERVED_COL_START + 1) + pod_space + (RESERVED_COL_END + 1)  # 49


def determine_station_limits():
    return (POD_BATCH_V_MAX + 1) // 2  # 5


def get_station_positions(n_stations):
    """Return sorted list of 1-based station slot indices (mirrors layout.py)."""
    numbers = list(range(1, determine_station_limits() + 1))
    mid = len(numbers) // 2
    selected = []
    for i in range(n_stations):
        offset = (i // 2) if i % 2 == 0 else -(i // 2) - 1
        selected.append(numbers[mid + offset])
    return sorted(selected)


def get_station_row_range(pos):
    """Return (start_row, end_row) for a station at slot position pos."""
    start = (pos - 1) * (2 * POD_BATCH_V + 2)
    return start, start + POD_BATCH_V + 1


def get_value_for_station(row, col, ranges, start_col=0, mirrored=False):
    """Return tile code for a cell in the station reserved columns."""
    offset = -1 if mirrored else 1
    rail_triangle = 24 if mirrored else 14
    rail_0 = 22 if mirrored else 12
    rail_1 = 23 if mirrored else 13
    corner_0 = 26 if mirrored else 16
    corner_1 = 27 if mirrored else 17
    corner_2 = 28 if mirrored else 18
    corner_3 = 29 if mirrored else 19
    station_val = T_STATION_MIRROR if mirrored else T_STATION

    for start, end in ranges:
        if row == start:
            if col == start_col + 2 * offset:
                return corner_0
            elif col in (start_col + 3 * offset, start_col + 4 * offset):
                return rail_0
            else:
                return T_BLANK
        elif row == end:
            if col == start_col + 2 * offset:
                return corner_1
            elif col in (start_col + 3 * offset, start_col + 4 * offset):
                return rail_1
            else:
                return T_BLANK
        elif start < row < end:
            if row == start + 1:
                if col == start_col + 1 * offset:
                    return station_val
                elif col == start_col + 2 * offset:
                    return rail_triangle
                elif col == start_col + 3 * offset:
                    return rail_1
                elif col == start_col + 4 * offset:
                    return corner_2
                else:
                    return T_BLANK
            elif row == start + 2:
                if col == start_col + 2 * offset:
                    return rail_triangle
                elif col == start_col + 3 * offset:
                    return rail_0
                elif col == start_col + 4 * offset:
                    return corner_3
                else:
                    return T_BLANK
            else:
                return T_BLANK
    return T_BLANK


# ─── Grid generation ──────────────────────────────────────────────────────────

def generate_grid():
    """Build the 31x49 tile matrix, matching layout.py logic exactly."""
    rows = total_rows()
    cols = total_cols()

    op_ranges = [get_station_row_range(p) for p in get_station_positions(ORDER_PICKER_TOTAL)]
    rep_ranges = [get_station_row_range(p) for p in get_station_positions(ORDER_REPLENISHMENT_TOTAL)]

    h_switch = False
    matrix = []

    for row in range(rows):
        v_switch = False
        current_row = []

        for col in range(cols):
            # Station reserved columns
            if col < RESERVED_COL_STATION:
                val = get_value_for_station(row, col, op_ranges, start_col=0, mirrored=False)
            elif col >= cols - RESERVED_COL_STATION:
                val = get_value_for_station(row, col, rep_ranges, start_col=cols - 1, mirrored=True)
            else:
                # Pod area + junctions
                if RESERVED_COL_START <= col < (cols - RESERVED_COL_END):
                    if row % (POD_BATCH_V + 1) == 0:
                        # Cross-aisle row
                        if (col - RESERVED_COL_START) % (POD_BATCH_H + 1) == 0:
                            val = T_AISLE
                            v_switch = not v_switch
                        else:
                            val = T_HRAIL_A if h_switch else T_HRAIL_B
                    else:
                        # Pod row
                        pod_index = (col - RESERVED_COL_START - 1) % (POD_BATCH_H + 1)
                        if pod_index < POD_BATCH_H:
                            val = T_POD
                        else:
                            val = T_VRAIL_A if v_switch else T_VRAIL_B
                            v_switch = not v_switch
                else:
                    # Junction columns (cols 5-8 and 40-43)
                    val = T_VRAIL_A if v_switch else T_VRAIL_B
                    v_switch = not v_switch

            current_row.append(val)

        matrix.append(current_row)
        h_switch = not h_switch

    # Adjust pod availability using deterministic seed
    rng = random.Random(SEED)
    current_pods = sum(1 for r in matrix for c in r if c == T_POD)
    if current_pods > TOTAL_PODS_ACTIVE:
        to_deactivate = current_pods - (TOTAL_PODS_ACTIVE + TOTAL_CHARGING_STATIONS)
        to_convert = TOTAL_CHARGING_STATIONS
    else:
        to_deactivate = 0
        to_convert = 0

    pod_positions = [(r, c) for r in range(rows) for c in range(cols) if matrix[r][c] == T_POD]

    if to_deactivate > 0:
        for r, c in rng.sample(pod_positions, to_deactivate):
            matrix[r][c] = 0
            pod_positions.remove((r, c))

    if to_convert > 0:
        for r, c in rng.sample(pod_positions, to_convert):
            matrix[r][c] = T_CHARGING

    return matrix


# ─── Instance building ────────────────────────────────────────────────────────

def cell_center(row, col):
    """Return (x, y) centre of cell (col, row) in metres."""
    return col * CELL_SIZE + CELL_SIZE / 2.0, row * CELL_SIZE + CELL_SIZE / 2.0


def is_navigable(tile):
    return tile != T_BLANK


def is_pod_storage_location(tile):
    """True for active pods and deactivated-but-valid pod slots."""
    return tile in (T_POD, 0)


def build_instance(matrix):
    rows = len(matrix)
    cols = len(matrix[0])

    # ── Determine queue cells BEFORE building any paths ───────────────────────
    # Queue cells are the station approach cells at each station's row.
    # They will be isolated from the bidirectional main grid.
    #
    # OutputStation i at (s_row, col=1):
    #   queue cells = (s_row, cols 1..8)   [station + approach through junction]
    #   entry from main grid: (s_row, 9)  → (s_row, 8)  [one-way into queue]
    #   exit  from station:   (s_row, 1)  → (s_row, 9)  [one-way out to main grid]
    #
    # InputStation i at (s_row, col=cols-2=47):
    #   queue cells = (s_row, cols 40..47) [right junction + station approach]
    #   entry from main grid: (s_row, 39) → (s_row, 40) [one-way into queue]
    #   exit  from station:   (s_row, 47) → (s_row, 39) [one-way out to main grid]

    op_ranges  = [get_station_row_range(p) for p in get_station_positions(ORDER_PICKER_TOTAL)]
    rep_ranges = [get_station_row_range(p) for p in get_station_positions(ORDER_REPLENISHMENT_TOTAL)]

    # Map station_row → (station_type, station_id)
    output_station_rows = {s_start + 1: i for i, (s_start, _) in enumerate(op_ranges)}
    input_station_rows  = {s_start + 1: i for i, (s_start, _) in enumerate(rep_ranges)}

    # All (row, col) cells that belong to a queue (isolated from bidirectional grid)
    queue_cell_set: set = set()
    for s_row in output_station_rows:
        for c in range(1, 9):        # cols 1–8
            queue_cell_set.add((s_row, c))
    for s_row in input_station_rows:
        for c in range(cols - 9, cols - 1):   # cols 40–47
            queue_cell_set.add((s_row, c))

    # ── 1. Create waypoints (one per navigable cell) ───────────────────────────
    wp_id_map = {}   # (row, col) -> waypoint_id
    waypoints = []

    for r in range(rows):
        for c in range(cols):
            tile = matrix[r][c]
            if not is_navigable(tile):
                continue
            wp_id = len(waypoints)
            wp_id_map[(r, c)] = wp_id
            x, y = cell_center(r, c)
            waypoints.append({
                'id': wp_id,
                'x': x, 'y': y,
                'tier': 0,
                'output_station': -1,
                'input_station': -1,
                'elevator': -1,
                'pod': -1,
                'pod_storage': is_pod_storage_location(tile),
                'is_queue': False,
                'paths': [],
            })

    # ── 2. Build 4-directional adjacency paths (main grid only) ───────────────
    # Queue cells are excluded — they get their own unidirectional chain below.
    for (r, c), wp_id in wp_id_map.items():
        if (r, c) in queue_cell_set:
            continue  # queue cells get special paths; skip bidirectional build
        for dr, dc in [(-1, 0), (1, 0), (0, -1), (0, 1)]:
            nb = (r + dr, c + dc)
            # Connect only to non-queue navigable neighbours
            if nb in wp_id_map and nb not in queue_cell_set:
                waypoints[wp_id]['paths'].append(wp_id_map[nb])

    # ── 3. Create pods at active pod cells ─────────────────────────────────────
    pods = []
    pod_id = 0
    for r in range(rows):
        for c in range(cols):
            if matrix[r][c] == T_POD:
                wp_id = wp_id_map[(r, c)]
                x, y = cell_center(r, c)
                pods.append({'id': pod_id, 'x': x, 'y': y})
                waypoints[wp_id]['pod'] = pod_id
                pod_id += 1

    # ── 4. Output stations — queue chains ─────────────────────────────────────
    # Structure (mirroring 1-1-1-2-22 benchmark):
    #   Queue list = [station, Q1, Q2, ..., Q_last]   (station is first / innermost)
    #   Path chain: Q_last→...→Q1→station  (each wp has ONE path toward station)
    #   Station path: station → exit_wp   (first wp in main grid after leaving station)
    #   Entry edge:  main_grid_entry → Q_last  (one-way; guarded by semaphore)
    #
    # OutputStation approach: station at col=1, Q_last at col=8, exit/entry at col=9
    output_stations = []

    for i, (s_start, _) in enumerate(op_ranges):
        s_row  = s_start + 1
        s_col  = 1          # station column
        exit_col   = RESERVED_COL_START          # col 9 — first pod-area aisle
        entry_col  = exit_col                    # same column serves as entry point

        station_wp = wp_id_map.get((s_row, s_col))
        if station_wp is None:
            print(f"  WARNING: OutputStation {i} not found at ({s_row},{s_col})")
            continue

        # Mark station waypoint
        waypoints[station_wp]['output_station'] = i
        waypoints[station_wp]['is_queue'] = True

        # Build queue waypoints (cols 2..8, inward → outward from station)
        queue_wps = [station_wp]  # station is first (innermost)
        for qc in range(s_col + 1, 9):   # cols 2, 3, 4, 5, 6, 7, 8
            qwp = wp_id_map.get((s_row, qc))
            if qwp is not None:
                waypoints[qwp]['is_queue'] = True
                # IS/OS stay at -1 for non-station queue waypoints (matches benchmark)
                queue_wps.append(qwp)

        # Unidirectional chain: Q_last→...→Q1→station
        for idx in range(len(queue_wps) - 1, 0, -1):
            outer_wp = queue_wps[idx]
            inner_wp = queue_wps[idx - 1]
            waypoints[outer_wp]['paths'].append(inner_wp)

        # Station exits to main grid at col=exit_col
        exit_wp = wp_id_map.get((s_row, exit_col))
        if exit_wp is not None:
            waypoints[station_wp]['paths'].append(exit_wp)

        # One-way entry from main grid into queue (entry_col → Q_last=col 8)
        entry_main_wp  = wp_id_map.get((s_row, entry_col))
        queue_last_wp  = queue_wps[-1]   # col 8
        if entry_main_wp is not None:
            waypoints[entry_main_wp]['paths'].append(queue_last_wp)

        x, y = cell_center(s_row, s_col)
        output_stations.append({
            'id': i, 'x': x, 'y': y,
            'queue': queue_wps,
            'row': s_row,
            'entry_main_wp': entry_main_wp,
            'queue_last_wp': queue_last_wp,
            'station_wp': station_wp,
            'exit_wp': exit_wp,
        })

    # ── 5. Input station — queue chain ────────────────────────────────────────
    # InputStation approach: station at col=cols-2=47, Q_last at col=40,
    #                        exit/entry at col=cols-RESERVED_COL_END-1=39
    input_stations = []

    for i, (s_start, _) in enumerate(rep_ranges):
        s_row  = s_start + 1
        s_col  = cols - 2   # col 47
        exit_col  = cols - RESERVED_COL_END - 1  # col 39 — last pod-area aisle
        entry_col = exit_col

        station_wp = wp_id_map.get((s_row, s_col))
        if station_wp is None:
            print(f"  WARNING: InputStation {i} not found at ({s_row},{s_col})")
            continue

        waypoints[station_wp]['input_station'] = i
        waypoints[station_wp]['is_queue'] = True

        # Build queue waypoints (cols 46..40, inward → outward from station)
        queue_wps = [station_wp]  # station is first (innermost)
        for qc in range(s_col - 1, cols - RESERVED_COL_END - 1, -1):  # 46, 45, ..., 40
            qwp = wp_id_map.get((s_row, qc))
            if qwp is not None:
                waypoints[qwp]['is_queue'] = True
                queue_wps.append(qwp)

        # Unidirectional chain: Q_last→...→Q1→station
        for idx in range(len(queue_wps) - 1, 0, -1):
            outer_wp = queue_wps[idx]
            inner_wp = queue_wps[idx - 1]
            waypoints[outer_wp]['paths'].append(inner_wp)

        # Station exits to main grid at col=exit_col
        exit_wp = wp_id_map.get((s_row, exit_col))
        if exit_wp is not None:
            waypoints[station_wp]['paths'].append(exit_wp)

        # One-way entry: main grid (col=39) → Q_last (col=40)
        entry_main_wp = wp_id_map.get((s_row, entry_col))
        queue_last_wp = queue_wps[-1]   # col 40
        if entry_main_wp is not None:
            waypoints[entry_main_wp]['paths'].append(queue_last_wp)

        x, y = cell_center(s_row, s_col)
        input_stations.append({
            'id': i, 'x': x, 'y': y,
            'queue': queue_wps,
            'row': s_row,
            'entry_main_wp': entry_main_wp,
            'queue_last_wp': queue_last_wp,
            'station_wp': station_wp,
            'exit_wp': exit_wp,
        })

    # ── 6. Bots: place in junction cross-aisle rows, then pod-area aisles ──────
    bot_candidates = []

    for r in range(rows):
        if r % (POD_BATCH_V + 1) == 0:  # cross-aisle row
            # Left junction (cols 5-8) and right junction (cols 40-43)
            for c in list(range(5, 9)) + list(range(cols - 9, cols - 5)):
                if (r, c) in wp_id_map and (r, c) not in queue_cell_set:
                    bot_candidates.append((r, c))

    # Supplement with pod-area vertical aisle intersections
    for r in range(rows):
        if r % (POD_BATCH_V + 1) == 0:
            for c in range(RESERVED_COL_START, cols - RESERVED_COL_END, POD_BATCH_H + 1):
                if (r, c) in wp_id_map and (r, c) not in queue_cell_set:
                    bot_candidates.append((r, c))

    # Deduplicate while preserving order
    seen = set()
    unique_candidates = []
    for pos in bot_candidates:
        if pos not in seen:
            seen.add(pos)
            unique_candidates.append(pos)

    # Shuffle so bots are spread across the warehouse, not bunched at first rows
    rng_bots = random.Random(SEED)
    rng_bots.shuffle(unique_candidates)

    bots = []
    for i in range(BOT_COUNT):
        r, c = unique_candidates[i]
        x, y = cell_center(r, c)
        bots.append({'id': i, 'x': x, 'y': y})

    # ── 7. Semaphores — matching benchmark pattern ─────────────────────────────
    # Per station: 3 guards (same structure as 1-1-1-2-22.xinst benchmark)
    #   Guard 1: main_entry → Q_last,          Entry=true,  Barrier=true
    #   Guard 2: station    → exit_main,        Entry=false, Barrier=false
    #   Guard 3: Q_last-1   → Q_last,           Entry=true,  Barrier=false
    # Capacity = SEMAPHORE_CAPACITY (2) — max robots in queue simultaneously
    semaphores = []
    sem_id = 0

    for st in output_stations:
        queue_wps = st['queue']
        q_last    = st['queue_last_wp']          # col 8
        q_second  = queue_wps[-2] if len(queue_wps) >= 2 else None  # col 7
        entry_wp  = st['entry_main_wp']          # col 9
        station_w = st['station_wp']             # col 1
        exit_w    = st['exit_wp']                # col 9

        guards = [
            {'from': entry_wp,  'to': q_last,   'entry': True,  'barrier': True},
            {'from': station_w, 'to': exit_w,   'entry': False, 'barrier': False},
        ]
        if q_second is not None:
            guards.append(
                {'from': q_second, 'to': q_last, 'entry': True, 'barrier': False}
            )

        semaphores.append({'id': sem_id, 'capacity': SEMAPHORE_CAPACITY, 'guards': guards})
        sem_id += 1

    for st in input_stations:
        queue_wps = st['queue']
        q_last    = st['queue_last_wp']          # col 40
        q_second  = queue_wps[-2] if len(queue_wps) >= 2 else None  # col 41
        entry_wp  = st['entry_main_wp']          # col 39
        station_w = st['station_wp']             # col 47
        exit_w    = st['exit_wp']                # col 39

        guards = [
            {'from': entry_wp,  'to': q_last,   'entry': True,  'barrier': True},
            {'from': station_w, 'to': exit_w,   'entry': False, 'barrier': False},
        ]
        if q_second is not None:
            guards.append(
                {'from': q_second, 'to': q_last, 'entry': True, 'barrier': False}
            )

        semaphores.append({'id': sem_id, 'capacity': SEMAPHORE_CAPACITY, 'guards': guards})
        sem_id += 1

    return {
        'bots': bots,
        'pods': pods,
        'input_stations': input_stations,
        'output_stations': output_stations,
        'waypoints': waypoints,
        'semaphores': semaphores,
        'tier_length': cols * CELL_SIZE,
        'tier_width': rows * CELL_SIZE,
    }


# ─── XML serialisation ────────────────────────────────────────────────────────

def fmt(v):
    """Format a float for XML output."""
    return f"{v:.4f}"


def write_xml(data, output_path: Path):
    root = ET.Element('Instance')
    root.set('xmlns:xsi', 'http://www.w3.org/2001/XMLSchema-instance')
    root.set('xmlns:xsd', 'http://www.w3.org/2001/XMLSchema')
    root.set('Name', 'citi-lab')

    # Bots
    bots_el = ET.SubElement(root, 'Bots')
    for b in data['bots']:
        ET.SubElement(bots_el, 'Bot',
            ID=str(b['id']),
            PodTransferTime=fmt(BOT_POD_TRANSFER_TIME),
            MaxAcceleration=fmt(BOT_MAX_ACCEL),
            MaxDeceleration=fmt(BOT_MAX_DECEL),
            MaxVelocity=fmt(BOT_MAX_VELOCITY),
            TurnSpeed=fmt(BOT_TURN_SPEED),
            CollisionPenaltyTime=fmt(BOT_COLLISION_PENALTY),
            X=fmt(b['x']),
            Y=fmt(b['y']),
            Radius=fmt(BOT_RADIUS),
            Orientation='0',
            Tier='0',
        )

    # Pods
    pods_el = ET.SubElement(root, 'Pods')
    for p in data['pods']:
        ET.SubElement(pods_el, 'Pod',
            ID=str(p['id']),
            X=fmt(p['x']),
            Y=fmt(p['y']),
            Radius=fmt(POD_RADIUS),
            Orientation='0',
            Tier='0',
            Capacity=str(POD_CAPACITY),
        )

    # Elevators (none for single-tier)
    ET.SubElement(root, 'Elevators')

    # InputStations
    ist_el = ET.SubElement(root, 'InputStations')
    for st in data['input_stations']:
        st_elem = ET.SubElement(ist_el, 'InputStation',
            ID=str(st['id']),
            X=fmt(st['x']),
            Y=fmt(st['y']),
            Radius=fmt(STATION_RADIUS),
            Tier='0',
            Capacity=str(INPUT_STATION_CAPACITY),
            ItemBundleTransferTime=fmt(INPUT_STATION_TRANSFER_TIME),
        )
        q_el = ET.SubElement(st_elem, 'Queue')
        for wp_id in st['queue']:
            ET.SubElement(q_el, 'Queue').text = str(wp_id)

    # OutputStations
    ost_el = ET.SubElement(root, 'OutputStations')
    for st in data['output_stations']:
        st_elem = ET.SubElement(ost_el, 'OutputStation',
            ID=str(st['id']),
            X=fmt(st['x']),
            Y=fmt(st['y']),
            Radius=fmt(STATION_RADIUS),
            Tier='0',
            Capacity=str(OUTPUT_STATION_CAPACITY),
            ItemTransferTime=fmt(OUTPUT_STATION_TRANSFER_TIME),
        )
        q_el = ET.SubElement(st_elem, 'Queue')
        for wp_id in st['queue']:
            ET.SubElement(q_el, 'Queue').text = str(wp_id)

    # Tiers
    tiers_el = ET.SubElement(root, 'Tiers')
    ET.SubElement(tiers_el, 'Tier',
        ID='0',
        Length=fmt(data['tier_length']),
        Width=fmt(data['tier_width']),
        RelativePositionX='0',
        RelativePositionY='0',
        RelativePositionZ='0',
    )

    # Waypoints
    wps_el = ET.SubElement(root, 'Waypoints')
    for wp in data['waypoints']:
        wp_elem = ET.SubElement(wps_el, 'Waypoint',
            ID=str(wp['id']),
            X=fmt(wp['x']),
            Y=fmt(wp['y']),
            Tier='0',
            OutputStation=str(wp['output_station']),
            InputStation=str(wp['input_station']),
            Elevator=str(wp['elevator']),
            Pod=str(wp['pod']),
            PodStorageLocation=str(wp['pod_storage']).lower(),
            IsQueueWaypoint=str(wp['is_queue']).lower(),
        )
        paths_el = ET.SubElement(wp_elem, 'Paths')
        for nb_id in wp['paths']:
            ET.SubElement(paths_el, 'Waypoint').text = str(nb_id)

    # Semaphores
    sems_el = ET.SubElement(root, 'Semaphores')
    for sem in data['semaphores']:
        sem_elem = ET.SubElement(sems_el, 'Semaphore',
            ID=str(sem['id']),
            Capacity=str(sem['capacity']),
        )
        guards_el = ET.SubElement(sem_elem, 'Guards')
        for g in sem['guards']:
            ET.SubElement(guards_el, 'Guard',
                **{'From': str(g['from']),
                   'To': str(g['to']),
                   'Entry': str(g['entry']).lower(),
                   'Barrier': str(g['barrier']).lower(),
                   'Semaphore': str(sem['id']),
                }
            )

    # Pretty-print with declaration
    xml_str = ET.tostring(root, encoding='unicode')
    dom = minidom.parseString(xml_str)
    pretty = dom.toprettyxml(indent='  ', encoding='utf-8')

    output_path.write_bytes(pretty)
    print(f"Written: {output_path}")


# ─── Entry point ──────────────────────────────────────────────────────────────

def main():
    script_dir = Path(__file__).resolve().parent
    repo_root = script_dir.parent
    out_dir = repo_root / 'Material' / 'Instances' / 'CoreBenchmark'
    out_dir.mkdir(parents=True, exist_ok=True)

    print("=== citi-lab instance generator ===")
    print(f"Grid:  {total_rows()} rows x {total_cols()} cols  ({CELL_SIZE} m/cell)")

    print("Generating grid...")
    matrix = generate_grid()

    active_pods = sum(1 for r in matrix for c in r if c == T_POD)
    charging = sum(1 for r in matrix for c in r if c == T_CHARGING)
    deactivated = sum(1 for r in matrix for c in r if c == 0)
    print(f"  Active pods: {active_pods}  |  Charging slots: {charging}  |  Empty pod slots: {deactivated}")

    print("Building RAWSim-O instance...")
    data = build_instance(matrix)

    print(f"  Bots:           {len(data['bots'])}")
    print(f"  Pods:           {len(data['pods'])}")
    print(f"  OutputStations: {len(data['output_stations'])}")
    print(f"  InputStations:  {len(data['input_stations'])}")
    print(f"  Waypoints:      {len(data['waypoints'])}")
    print(f"  Semaphores:     {len(data['semaphores'])}")
    print(f"  Tier:           {data['tier_length']} m (length) x {data['tier_width']} m (width)")

    out_path = out_dir / 'citi-lab.xinst'
    write_xml(data, out_path)
    print("Done.")


if __name__ == '__main__':
    main()
