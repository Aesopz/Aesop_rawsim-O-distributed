# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

RAWSim-O is a discrete event-based simulation framework for **Robotic Mobile Fulfillment Systems (RMFS)** — automated warehouses where robots carry storage pods to human pickers. It is a research tool for benchmarking decision algorithms (pathfinding, task allocation, storage placement, etc.).

## Build & Run

All commands should be run from the repository root.

**Build:**
```bash
dotnet build RAWSimO.sln
dotnet build -c Release RAWSimO.sln
```

**Run tests:**
```bash
dotnet test
# or with specific configuration:
dotnet test --configuration XPlat --verbosity normal
```

**Run GUI (Windows only — requires WPF/net6.0-windows):**
```bash
dotnet run --project RAWSimO.Visualization/RAWSimO.Visualization.csproj
```

**Run CLI simulation (headless/cross-platform):**
```bash
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- <Instance.xinst> <Setting.xsett> <Config.xconf> <OutputDir> <Seed> [Tag]

# Example with bundled benchmark files:
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- Material/Instances/CoreBenchmark/jenkinsinstance1.xinst Material/Instances/CoreBenchmark/jenkinssetting1.xsett Material/Instances/CoreBenchmark/jenkinsconfig1.xconf output/ 42
```

Output is written to `<OutputDir>/<InstanceName>-<SettingName>-<ConfigName>-<Seed>/` containing `controllog.txt` and statistics CSVs.

Sample benchmark files are in `Material/Instances/CoreBenchmark/` (`.xinst`, `.xsett`, `.xconf`).

## Architecture

### Three-File Configuration System

Every simulation run requires three XML config files:
- **`.xinst`** — warehouse layout (grid size, robots, pods, stations)
- **`.xsett`** — simulation parameters (duration, speeds, order rates)
- **`.xconf`** — controller config (which algorithm to use for each decision problem)

These map to `LayoutConfiguration`, `SettingConfiguration`, and `MethodConfiguration` in `RAWSimO.Core/Configurations/`.

### Project Layout

| Project | Purpose |
|---|---|
| `RAWSimO.Core` | Core simulation engine — all domain models, simulation logic, statistics |
| `RAWSimO.CLI` | Cross-platform headless runner |
| `RAWSimO.Visualization` | WPF GUI for interactive use (Windows only) |
| `RAWSimO.MultiAgentPathFinding` | Multi-agent pathfinding algorithm implementations |
| `RAWSimO.Toolbox` | Utility library with no external dependencies |
| `RAWSimO.Playground` | Experimental space with instance/config generators and ad-hoc tests |
| `RAWSimO.Hardware` | Real hardware integration (robots, stations) via OpenCV, HID, serial |
| `RAWSimO.DEA` / `RAWSimO.MDPSolve` | Optimization tools (DEA, MDP) using linear programming |
| `Tests/RAWSimO.Core.Tests` | xUnit test suite |

### Core Simulation Model (`RAWSimO.Core`)

**`Instance`** is the central class (split across `InstanceCore.cs`, `InstanceCreation.cs`, `InstanceStatistics.cs`, `InstanceEvents.cs`, etc.). It holds all simulation state: Bots, Pods, Elevators, InputStations, OutputStations, Waypoints, and Semaphores.

Physical elements live in `Elements/`: `Bot`, `Pod`, `InputStation`, `OutputStation`, `Elevator`, `Tier`, `Waypoint`.

**`Controller`** orchestrates all decision-making via a **strategy pattern** — each decision problem has a manager base class and multiple pluggable implementations:

| Decision Problem | Manager Class | # Implementations |
|---|---|---|
| Path planning | `PathManager` | 9 (WHCAvStar, CBS, BCP, FAR, PAS, …) |
| Task allocation | `BotManager` | 6 |
| Item storage | `ItemStorageManager` | 8 |
| Pod storage | `PodStorageManager` | 8 |
| Order batching | `OrderManager` | 10+ |
| Station activation | `StationManager` | 4 |
| Repositioning | `RepositioningManager` | 5 |

### Adding a New Decision Algorithm

1. Find the manager base class for the decision problem (e.g., `OrderManager` in `RAWSimO.Core/Control/`)
2. Create a new class that inherits from it and implements the required abstract methods
3. Add a corresponding configuration class in `RAWSimO.Core/Configurations/`
4. Register the new type in the relevant factory/switch so the `.xconf` XML can reference it

### Key Interfaces

- `IUpdateable` — objects that receive simulation tick updates
- `IRandomizer` — random number generation abstraction
- Info interfaces (`IBotInfo`, `IPodInfo`, etc.) — read-only views of simulation elements exposed to decision managers

### Statistics

`SimulationObserver` tracks metrics during execution; results aggregate into `InstanceStatistics`. GnuPlot (optional, must be on PATH) is used by `GnuPlotter` in `RAWSimO.Core/Helper/` for some output.

## Optional Dependencies

- **GnuPlot** — required for some plotting features; its `bin/` must be on the system PATH
- **Hardware projects** — depend on native DLLs in `Material/Lib/` (Emgu.CV, HidLibrary, Blink1, ZXing.Net); only needed for physical robot integration

---

## RL Gym Extension (This Fork)

This repository extends RAWSim-O with a reinforcement learning interface. The addition consists of:

| Component | Language | Location | Purpose |
|---|---|---|---|
| `RAWSimO.GymServer` | C# | `RAWSimO.GymServer/` | TCP server wrapping simulation; exposes reset/step/close API |
| `rmfs_gym` | Python | `rmfs_gym/` | PettingZoo `ParallelEnv` that communicates with GymServer |
| Instance generator | Python | `scripts/generate_citi_lab.py` | Generates the custom 31×49 citi-lab warehouse layout |

### Running the GymServer

First build, then launch the server with the three config files:

```bash
dotnet run --project RAWSimO.GymServer/RAWSimO.GymServer.csproj -- \
  <instance.xinst> <setting.xsett> <config.xconf> [port] [step_duration] [max_steps] [--baseline]
```

- Default port: `7654` (override via `RMFS_GYM_PORT` env var or 4th positional arg)
- `--baseline`: lets the built-in `AgentAStar` control bot destinations; policy actions are ignored (used for collecting baseline data)
- **Requires** the `.xconf` to use `AgentAStar` as the path planner (`DecentralAStarPathPlanningConfiguration`)

### Python Client / PettingZoo Env

```python
from rmfs_gym.envs import RMFSParallelEnv

env = RMFSParallelEnv(host="localhost", port=7654)
obs, infos = env.reset(seed=42)
obs, rewards, dones, truncs, infos = env.step(actions)
env.close()
```

**Observation space**: Per-bot RGB image (`H×W×3`, default 64×64) rendered from a first-person camera mounted on each AGV.

**Action space**: `MultiDiscrete([5, 3, 3])` — direction (N/S/E/W/STAY), acceleration mode (0–2), deceleration mode (0–2).

**Reward**: `α × orders_completed − β × collisions − γ × idle_ratio` with `α=1.0, β=0.5, γ=0.1`.

**`infos` dict** (per agent each step):
- `action_mask`: `bool[5]` — which directions are physically reachable
- `collision_count`: int — how many other bots share the same waypoint

### TCP Protocol

Wire format: `[4-byte little-endian uint32 JSON length][JSON bytes][raw image bytes]`

Commands sent from Python → C#:
- `{"cmd": "reset", "seed": N}` → server replies with `{"n_bots": N, "image_size": [W, H, C]}` then sends concatenated raw image bytes
- `{"cmd": "step", "actions": {"bot_0": [dir, accel, decel], ...}}` → server replies with JSON `{rewards, dones, truncated, infos}` then image bytes
- `{"cmd": "close"}` → server shuts down

### Generating the Citi-Lab Instance

```bash
python scripts/generate_citi_lab.py
# Outputs: Material/Instances/CoreBenchmark/citi-lab.xinst
```

Layout: 31 rows × 49 cols, 300 pods, 30 robots, 3 output stations, 1 input station. Tile codes mirror the netlogo-rmfs `config.py` conventions.
