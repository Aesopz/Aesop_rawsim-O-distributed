# Plan: Gymnasium Environment Wrapper for RMFS

**Version:** 1.4 — Resolution changed to 64×64 (Ha & Schmidhuber paper standard)
**Date:** 2026-03-26
**Status:** Phase 1 ✅ DONE | Phase 2 ✅ DONE | Phase 3 ✅ DONE | Phase 4 ✅ DONE
**Scope:** (1) C# GymServer dengan WPF offscreen rendering, (2) Python PettingZoo ParallelEnv wrapper,
(3) Waypoint+acceleration/deceleration action space, (4) Per-bot onboard camera observation

---

## Table of Contents

1. [Objective](#1-objective)
2. [Synchronization dengan Plan 1](#2-synchronization-dengan-plan-1)
3. [Architecture Overview](#3-architecture-overview)
4. [Observation Space](#4-observation-space)
5. [Action Space](#5-action-space)
6. [Reward Function](#6-reward-function)
7. [IPC Protocol — TCP Socket](#7-ipc-protocol--tcp-socket)
8. [C# GymServer — RAWSimO.GymServer](#8-c-gymserver--rawsimogymserver)
9. [Python rmfs_gym Package — PettingZoo](#9-python-rmfs_gym-package--pettingzoo)
10. [Implementation Phases](#10-implementation-phases)
11. [Risk Matrix](#11-risk-matrix)
12. [File Index](#12-file-index)

---

## 1. Objective

Membungkus simulasi RAWSim-O ke dalam **PettingZoo ParallelEnv** (multi-agent Gymnasium extension)
sehingga dapat digunakan untuk melatih controller berbasis reinforcement learning dan world model.

Setiap robot:

1. Menerima **observasi** berupa pixel image 64×64×3 RGB dari onboard camera 3D (WPF offscreen render)
2. Mengeksekusi **action** berupa pemilihan waypoint tujuan + profil kecepatan (acceleration × deceleration)
3. Menerima **reward** berbasis throughput orders, collision penalty, dan idle penalty

**Platform:** Windows only (WPF RenderTargetBitmap)
**Baseline preserved:** AgentAStarPathManager dari Plan 1 tetap dipakai untuk LOCAL path execution.

---

## 2. Synchronization dengan Plan 1

### 2.1 Dependensi Keras pada Plan 1

| Komponen Plan 1                       | Dipakai di Plan 2                                                                | Cara Pakai                                                    |
| ------------------------------------- | -------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `virtual bool RegisterNextWaypoint()` | Wajib — Plan 2 menggunakan `GymPathManager` yang extends `AgentAStarPathManager` | Override tetap return `true`                                  |
| `AgentAStarPathManager`               | Base class dari `GymPathManager`                                                 | LOCAL navigation ke waypoint yang di-set gym                  |
| `AgentAStarPathPlanningConfiguration` | Dipakai oleh `GymPathManager` constructor                                        | Path planning config (ReplanInterval, RuntimeLimitPerAgentMs) |

### 2.2 Alur Kontrol: Plan 1 vs Plan 2

```
Plan 1 (standalone):                    Plan 2 (gym-controlled):
  BotManager → DestinationWaypoint        Python Policy → action (waypoint_idx, accel, decel)
       ↓                                        ↓
  AgentAStarPathManager.TryReplan()       GymServer translates action
       ↓                                        ↓ sets bot.DestinationWaypoint
  SpaceAStar → bot.Path                   GymPathManager.TryReplan() ← same A* logic
       ↓                                        ↓
  BotNormal executes path                 BotNormal executes path
```

**Kunci:** Gym hanya mengontrol DESTINATION (macro decision). A\* dari Plan 1 tetap menangani LOCAL path execution ke waypoint tersebut. Collision avoidance antar bot **tidak** ditangani di Plan 1 — ini adalah tanggung jawab policy yang berjalan di Plan 2/3.

### 2.3 Plan 1 Output yang Dipakai Plan 2 sebagai Baseline

Ketika gym pertama kali dijalankan, `GymPathManager` dapat di-toggle ke mode `BaselineAgentAStar`:

- `_useBaselineMode = true` → `SetGymDestination()` tidak mengubah `bot.DestinationWaypoint`
- Destination tetap diset oleh **BotManager** (bagian dari `Instance.Controller`, aktif seperti biasa)
- AgentAStar menangani LOCAL path execution ke destination yang dipilih BotManager
- Gym env tetap berjalan (render + collect data), tapi Python policy tidak mengontrol apapun
- Digunakan untuk **data collection awal** dan **baseline benchmark**

---

## 3. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│  Python Side (rmfs_gym package)                                  │
│                                                                  │
│  RMFSParallelEnv(ParallelEnv)                                    │
│    agents = ["bot_0", ..., "bot_N"]                              │
│    observation_space = Box(64, 64, 3, uint8)   per agent         │
│    action_space      = MultiDiscrete([9, 3, 3]) per agent        │
│                                                                  │
│    reset()  → send RESET cmd → recv obs dict                     │
│    step(a)  → send STEP+actions → recv obs, rewards, dones       │
│    close()  → send CLOSE cmd                                     │
└─────────────────────────┬────────────────────────────────────────┘
                          │  TCP Socket (localhost:7654)
                          │  Protocol: length-prefixed JSON + raw bytes
┌─────────────────────────▼────────────────────────────────────────┐
│  C# Side (RAWSimO.GymServer — new project)                       │
│                                                                  │
│  TcpGymServer                                                    │
│    → GymSimulationHost                                           │
│         → Instance (RAWSimO.Core)                                │
│         → GymPathManager (extends AgentAStarPathManager)         │
│         → BotCameraRenderer (WPF offscreen, STA thread)          │
│         → RewardCalculator                                       │
│         → WaypointCandidateCache (K nearest per bot, per step)   │
└──────────────────────────────────────────────────────────────────┘
```

**Platform constraint:** `RAWSimO.GymServer.csproj` targets `net6.0-windows`
karena menggunakan `PresentationFramework` (WPF) untuk offscreen rendering.

---

## 4. Observation Space

### 4.1 Format

```python
observation_space = Box(
    low=0, high=255,
    shape=(64, 64, 3),   # H=64, W=64, RGB
    dtype=np.uint8
)
```

### 4.2 WPF Offscreen Rendering — BotCameraRenderer

Setiap bot punya sudut pandang kamera yang dihitung dari posisi dan orientasinya:

```csharp
// BotCameraRenderer.cs — runs on WPF STA thread
internal class BotCameraRenderer
{
    private readonly HelixViewport3D _viewport;
    private readonly SimulationAnimation3D _animation;
    private readonly int _imageWidth = 64;
    private readonly int _imageHeight = 64;

    // Height of the bot's "eye" above the tier floor (meters)
    private const double BOT_EYE_HEIGHT = 0.5;

    // Dipanggil setiap gym step untuk setiap bot
    public byte[] RenderBot(BotNormal bot)
    {
        // 1. Hitung posisi kamera dari bot position + orientation
        double heading = bot.GetTargetOrientation() + Math.PI / 2;
        var position  = new Point3D(bot.X, bot.Y, bot.Tier.Z + BOT_EYE_HEIGHT);
        var lookDir   = new Vector3D(Math.Cos(heading), Math.Sin(heading), 0);
        var upDir     = new Vector3D(0, 0, 1);

        // 2. Set viewport camera (no animation — direct assignment)
        _viewport.Dispatcher.Invoke(() => {
            var cam = (PerspectiveCamera)_viewport.Camera;
            cam.Position        = position;
            cam.LookDirection   = lookDir;
            cam.UpDirection     = upDir;
            cam.FieldOfView     = 90.0;

            // 3. Force layout + render
            _viewport.Measure(new Size(_imageWidth, _imageHeight));
            _viewport.Arrange(new Rect(0, 0, _imageWidth, _imageHeight));
            _viewport.UpdateLayout();

            // 4. Capture ke RenderTargetBitmap
            var rtb = new RenderTargetBitmap(
                _imageWidth, _imageHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_viewport);

            // 5. Encode ke raw RGB bytes
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            // → convert BGRA → RGB, store to byte[]
        });
        return _rgbBuffer;
    }
}
```

**Catatan:** `HelixViewport3D` tidak memerlukan window yang terlihat untuk render — WPF bisa
render offscreen selama ada `Application` instance aktif di STA thread. Tidak ada window yang
ditampilkan ke user saat GymServer berjalan.

### 4.3 GymCameraConfiguration

Parameter kamera rendering didefinisikan di Plan 2 (bukan Plan 1) sebagai konfigurasi GymServer:

```csharp
// RAWSimO.GymServer/GymCameraConfiguration.cs
public class GymCameraConfiguration
{
    /// <summary>Rendered image width in pixels.</summary>
    public int ImageWidth = 64;

    /// <summary>Rendered image height in pixels.</summary>
    public int ImageHeight = 64;

    /// <summary>Camera field of view in degrees (horizontal).</summary>
    public double FieldOfViewDeg = 90.0;

    /// <summary>Height of the bot's eye above tier floor (meters).</summary>
    public double BotEyeHeight = 0.5;
}
```

### 4.4 Isi Visual Observation

Setiap frame 64×64×3 berisi (persis seperti snapshot yang ada):

- Pod-pod (biru) sebagai kolom rak yang membentuk koridor
- Bot lain yang masuk dalam FoV kamera (jika visible)
- Lantai dan langit-langit warehouse
- Cahaya dari area station di ujung koridor

---

## 5. Action Space

### 5.1 Format

```python
action_space = MultiDiscrete([9, 3, 3])
# [waypoint_idx, accel_mode, decel_mode]
# 9 = 8 candidate waypoints + 1 STAY
# 3 accel_mode: 0=low(0.5×), 1=normal(1.0×), 2=high(1.5×)
# 3 decel_mode: 0=gentle(0.5×), 1=normal(1.0×), 2=hard(1.5×)
# Total kombinasi: 9 × 3 × 3 = 81
```

### 5.2 Waypoint Candidates — K=8

Setiap step, GymServer menghitung ulang **8 waypoint kandidat** per bot:

```csharp
// WaypointCandidateCache.cs
internal Waypoint[] GetCandidates(BotNormal bot)
{
    // BFS depth=2 dari current waypoint di Graph
    // Ambil 8 waypoint terdekat (Euclidean) yang dapat dicapai
    // Index 0..7 = kandidat, index 8 = STAY (current waypoint)
    // ⚠️ IMPLEMENTASI TODO: Graph beroperasi pada node IDs (int), bukan Waypoint objects.
    // `GetNeighborNodes(Waypoint, depth)` kemungkinan besar TIDAK ADA di Graph API.
    // Verifikasi Graph API saat implementasi, lalu adaptasi:
    //   int startNodeId = _waypointIds[bot.CurrentWaypoint];  // atau NextWaypoint
    //   var neighborIds = BFS dari startNodeId di graph, depth=2
    //   var candidates  = neighborIds.Select(id => _waypointIds.Reverse[id])
    //                                .OrderBy(wp => EuclideanDistance(bot, wp))
    //                                .Take(8).ToArray();
    var candidates = _graph
        .GetNeighborNodes(bot.CurrentWaypoint, depth: 2)  // adaptasi sesuai actual Graph API
        .OrderBy(wp => EuclideanDistance(bot, wp))
        .Take(8)
        .ToArray();
    _cache[bot] = candidates;
    return candidates;
}
```

**Konsistensi dengan Plan 3:** Urutan kandidat diurutkan by distance, bukan by direction.
World model mempelajari relasi spasial dari pixel observation, bukan dari ordering kandidat.

### 5.3 Acceleration & Deceleration Multipliers

```csharp
// PhysicsMultipliers.cs
internal static class SpeedMode
{
    public static readonly double[] AccelFactors = { 0.5, 1.0, 1.5 };
    public static readonly double[] DecelFactors = { 0.5, 1.0, 1.5 };

    // Contoh kombinasi:
    // accel=2 + decel=0 → mulai bergerak cepat, berhenti perlahan (overshoot risk)
    // accel=0 + decel=2 → mulai lambat, berhenti cepat (untuk area padat)
    // accel=1 + decel=1 → default behavior (baseline AgentAStar)
}
```

### 5.4 Action Translation di GymServer

```csharp
// GymSimulationHost.cs
internal void ApplyAction(BotNormal bot, int[] action)
{
    int waypointIdx = action[0];
    int accelMode   = action[1];
    int decelMode   = action[2];

    // 1. Set destination waypoint
    Waypoint target = waypointIdx == 8
        ? bot.CurrentWaypoint                    // STAY
        : _waypointCache.GetCandidates(bot)[waypointIdx];

    bot.DestinationWaypoint = target;

    // 2. Set physics multipliers (stored per-bot, applied during movement)
    bot.AccelerationMultiplier = SpeedMode.AccelFactors[accelMode];
    bot.DecelerationMultiplier = SpeedMode.DecelFactors[decelMode];
    // NOTE: Requires adding AccelerationMultiplier + DecelerationMultiplier
    //       nullable doubles to BotNormal.cs (see File Index)
}
```

---

## 6. Reward Function

### 6.1 Per-Bot Reward

```csharp
// RewardCalculator.cs
internal double ComputeReward(BotNormal bot,
                               double prevTime, double currTime,
                               int collisions)
{
    double dt = currTime - prevTime;

    // Throughput delta: orders handled via this bot's station visits
    double ordersCompleted = CountOrdersCompletedInInterval(bot, prevTime, currTime);

    // Idle penalty: fraction of step time bot was stationary without task
    double idleRatio = (bot.IdleTime - _prevIdleTime[bot]) / dt;

    // Collision count: two bots at same waypoint simultaneously
    // Detected by GymServer comparing bot CurrentWaypoint positions each step
    double collisionPenalty = collisions;

    const double ALPHA = 1.0;   // throughput weight
    const double BETA  = 0.5;   // collision weight
    const double GAMMA = 0.1;   // idle weight

    return ALPHA * ordersCompleted
         - BETA  * collisionPenalty
         - GAMMA * idleRatio;
}
```

**Konsistensi dengan Plan 3:** Konstanta ALPHA, BETA, GAMMA identik di Plan 2 dan Plan 3
(reward model di world model mempelajari reward function yang sama).

### 6.2 Episode Done Condition

```python
# Per bot: done = True jika episode selesai (simulation reach endTime)
# Truncated = True jika max_steps tercapai (default: 1000 steps per episode)
dones     = {"bot_i": simulation_finished}
truncated = {"bot_i": step_count >= max_steps}
```

---

## 7. IPC Protocol — TCP Socket

### 7.1 Koneksi

```
Port default: 7654 (configurable via env var RMFS_GYM_PORT)
Protocol: TCP, localhost
Byte order: little-endian
```

### 7.2 Message Format

Setiap message terdiri dari:

```
[4 bytes: JSON header length (uint32)]
[N bytes: JSON header (UTF-8)]
[M bytes: raw image data (hanya untuk response yang mengandung obs)]
```

### 7.3 Command: RESET

Python → C#:

```json
{ "cmd": "reset", "seed": 42 }
```

C# → Python response:

```json
{ "n_bots": 5, "image_size": [64, 64, 3], "candidates_k": 8 }
```

Diikuti `n_bots × 64 × 64 × 3` bytes raw RGB (bot_0_image | bot_1_image | ...).

### 7.4 Command: STEP

Python → C#:

```json
{
  "cmd": "step",
  "actions": {
    "bot_0": [3, 1, 1],
    "bot_1": [7, 2, 0],
    "bot_2": [8, 1, 1]
  }
}
```

C# → Python response:

```json
{
  "rewards": { "bot_0": 0.5, "bot_1": -0.1, "bot_2": 0.0 },
  "dones": { "bot_0": false, "bot_1": false, "bot_2": false },
  "truncated": { "bot_0": false, "bot_1": false, "bot_2": false },
  "infos": {
    "bot_0": { "waypoints_available": 6, "collision_count": 0 },
    "bot_1": { "waypoints_available": 8, "collision_count": 1 }
  }
}
```

Diikuti `n_bots × 64 × 64 × 3` bytes raw RGB images (sama urutan dengan RESET).

### 7.5 Command: CLOSE

```json
{ "cmd": "close" }
```

GymServer shutdown simulation dan tutup connection.

### 7.6 Step Duration

Setiap STEP command mengadvance simulasi sebesar `step_duration` detik (default: **1.0 detik**).
GymServer menjalankan `Instance.Update()` loop sampai `currentTime >= targetTime`, lalu pause.

---

## 8. C# GymServer — RAWSimO.GymServer

### 8.1 Project Structure

```
RAWSimO.GymServer/
├── RAWSimO.GymServer.csproj         ← net6.0-windows, ref RAWSimO.Core + PresentationFramework
├── Program.cs                        ← entry point, parse args, start TcpGymServer
├── TcpGymServer.cs                   ← TCP listener, message framing, dispatch to Host
├── GymSimulationHost.cs              ← owns Instance, GymPathManager, step loop
├── BotCameraRenderer.cs              ← WPF STA thread, RenderTargetBitmap per bot
├── WaypointCandidateCache.cs         ← K=8 candidates per bot, refreshed per step
├── RewardCalculator.cs               ← ALPHA/BETA/GAMMA reward computation
├── PhysicsMultipliers.cs             ← accel/decel factor constants + application
└── GymProtocol.cs                    ← JSON serialization helpers, message framing
```

### 8.2 GymPathManager

```csharp
// Extends AgentAStarPathManager dari Plan 1
// Perbedaan: destination waypoint di-set oleh GymSimulationHost, bukan BotManager
internal class GymPathManager : AgentAStarPathManager
{
    private bool _useBaselineMode = false;  // true = pakai AgentAStar decision, false = gym policy

    public void SetGymDestination(BotNormal bot, Waypoint target)
    {
        if (!_useBaselineMode)
            bot.DestinationWaypoint = target;
        // jika baseline mode: GymServer tidak mengubah DestinationWaypoint
        // → BotManager (bagian dari Instance.Controller) yang menentukan destination seperti biasa
    }

    public void EnableBaselineMode() => _useBaselineMode = true;
    public void DisableBaselineMode() => _useBaselineMode = false;
}
```

### 8.3 GymSimulationHost — Step Loop

```csharp
// GymSimulationHost.cs
internal GymStepResult Step(Dictionary<string, int[]> actions, double stepDuration)
{
    double targetTime = _instance.CurrentTime + stepDuration;

    // 1. Apply actions ke semua bot
    foreach (var (botId, action) in actions)
    {
        BotNormal bot = _botById[botId];
        ApplyAction(bot, action);
    }

    // 2. Advance simulation sampai targetTime
    while (_instance.CurrentTime < targetTime)
        _instance.Update(_instance.CurrentTime, Math.Min(targetTime, _instance.NextEventTime));

    // 3. Render semua bot (sequential, satu viewport)
    var observations = _bots.ToDictionary(
        b => b.GetInfoId().ToString(),
        b => _renderer.RenderBot(b));

    // 4. Compute rewards
    double prevTime = targetTime - stepDuration;
    var rewards = _bots.ToDictionary(
        b => b.GetInfoId().ToString(),
        b => _rewardCalc.ComputeReward(b, prevTime, targetTime, GetCollisionCount(b)));

    // 5. Refresh waypoint candidates untuk step berikutnya
    foreach (var bot in _bots)
        _waypointCache.GetCandidates(bot);

    return new GymStepResult(observations, rewards, IsDone(), IsTerminated());
}
```

---

## 9. Python rmfs_gym Package — PettingZoo

### 9.1 Package Structure

```
rmfs_gym/
├── __init__.py
├── envs/
│   ├── __init__.py
│   └── rmfs_parallel_env.py     ← RMFSParallelEnv(ParallelEnv)
├── connection/
│   ├── __init__.py
│   └── gym_client.py            ← TCP client, message framing
└── utils/
    ├── action_utils.py          ← encode/decode MultiDiscrete actions
    └── obs_utils.py             ← decode raw bytes → numpy array
```

### 9.2 RMFSParallelEnv

```python
# rmfs_gym/envs/rmfs_parallel_env.py
from pettingzoo import ParallelEnv
from gymnasium.spaces import Box, MultiDiscrete
import numpy as np
from ..connection.gym_client import GymClient

class RMFSParallelEnv(ParallelEnv):
    metadata = {"render_modes": ["human"], "name": "rmfs_v1"}

    def __init__(self, host="localhost", port=7654,
                 image_size=(64, 64, 3), k_waypoints=8):
        self._client = GymClient(host, port)
        self._img_h, self._img_w, self._img_c = image_size
        self._k = k_waypoints

    def reset(self, seed=None, options=None):
        info = self._client.send_reset(seed or 0)
        self.agents = [f"bot_{i}" for i in range(info["n_bots"])]
        self.possible_agents = list(self.agents)
        raw_images = self._client.recv_images(len(self.agents),
                                              self._img_h, self._img_w, self._img_c)
        obs = {a: img for a, img in zip(self.agents, raw_images)}
        return obs, {a: {} for a in self.agents}

    def step(self, actions):
        # actions: dict[agent_id, np.array([waypoint_idx, accel, decel])]
        resp = self._client.send_step(actions)
        raw_images = self._client.recv_images(len(self.agents),
                                              self._img_h, self._img_w, self._img_c)
        obs     = {a: img for a, img in zip(self.agents, raw_images)}
        rewards = resp["rewards"]
        dones   = resp["dones"]
        truncs  = resp["truncated"]
        infos   = resp["infos"]
        if all(dones.values()):
            self.agents = []
        return obs, rewards, dones, truncs, infos

    def observation_space(self, agent):
        return Box(0, 255, shape=(self._img_h, self._img_w, self._img_c), dtype=np.uint8)

    def action_space(self, agent):
        # [waypoint_idx (0..K), accel_mode (0..2), decel_mode (0..2)]
        return MultiDiscrete([self._k + 1, 3, 3])

    def close(self):
        self._client.send_close()
        self._client.disconnect()
```

---

## 10. Implementation Phases

### Phase 1: GymServer Core (3–4 hari)

**Step 1.1 — RAWSimO.GymServer.csproj**

- Buat project baru `net6.0-windows`
- Reference: `RAWSimO.Core`, `PresentationFramework`, `System.Text.Json`

**Step 1.2 — GymPathManager**

- File: `RAWSimO.GymServer/GymPathManager.cs`
- Extends `AgentAStarPathManager` dari Plan 1
- Tambah `SetGymDestination()` dan baseline mode toggle

**Step 1.3 — PhysicsMultipliers + BotNormal extension**

- File: `RAWSimO.Core/Elements/BotNormal.cs` — tambah `AccelerationMultiplier` + `DecelerationMultiplier` (nullable double, default null = no override)
- Modifikasi movement computation untuk menggunakan multiplier jika set

**Step 1.4 — WaypointCandidateCache**

- File: `RAWSimO.GymServer/WaypointCandidateCache.cs`
- BFS depth=2 dari Graph, sort by distance, cache per bot

**Step 1.5 — RewardCalculator**

- File: `RAWSimO.GymServer/RewardCalculator.cs`
- Implementasi ALPHA/BETA/GAMMA reward computation

**Step 1.6 — GymSimulationHost + step loop**

- File: `RAWSimO.GymServer/GymSimulationHost.cs`
- Load `.xinst` + `.xsett` + GymPathManager config
- Step loop: apply actions → advance sim → render → rewards

---

### Phase 2: WPF Offscreen Renderer (2–3 hari)

**Step 2.1 — WPF STA Thread Setup**

- File: `RAWSimO.GymServer/BotCameraRenderer.cs`
- Buat `Application` tanpa main window di STA thread
- Init `HelixViewport3D` + `SimulationAnimation3D` headless

**Step 2.2 — Per-Bot Render**

- Implementasi `RenderBot(BotNormal bot)`:
  - Compute camera pos/dir dari `bot.X`, `bot.Y`, `bot.GetTargetOrientation()`
  - Set `PerspectiveCamera` langsung (no animation)
  - `RenderTargetBitmap.Render(viewport)` → convert BGRA→RGB → `byte[]`

**Step 2.3 — Validasi visual**

- Jalankan GymServer dengan 1 bot, request 5 step
- Bandingkan frame yang dihasilkan dengan snapshot manual dari Visualization
- Verifikasi koridor dan pod terlihat dengan benar

---

### Phase 3: TCP Protocol + Python Client (2 hari)

**Step 3.1 — TcpGymServer + GymProtocol (C#)**

- File: `RAWSimO.GymServer/TcpGymServer.cs`, `GymProtocol.cs`
- Length-prefixed JSON + raw bytes (spesifikasi §7)
- Handle: RESET, STEP, CLOSE

**Step 3.2 — GymClient (Python)**

- File: `rmfs_gym/connection/gym_client.py`
- `send_reset()`, `send_step()`, `send_close()`, `recv_images()`
- Length-prefixed read/write matching C# framing

**Step 3.3 — RMFSParallelEnv (Python)**

- File: `rmfs_gym/envs/rmfs_parallel_env.py`
- PettingZoo compliance test: `pettingzoo.test.parallel_api_test(env)`

---

### Phase 4: Baseline Data Collection (1–2 hari)

**Step 4.1 — Enable Baseline Mode**

- Jalankan env dengan `GymPathManager.EnableBaselineMode()`
- Biarkan AgentAStar Plan 1 menjalankan simulasi
- Kumpulkan dataset: `(obs_t, action_t, reward_t, obs_{t+1})` untuk setiap bot

**Step 4.2 — Dataset Format (konsisten dengan Plan 3)**

```python
# Setiap episode tersimpan sebagai:
# dataset/episode_XXXX/
#   bot_0.npz: {obs: (T,64,64,3), actions: (T,3), rewards: (T,), dones: (T,)}
#   bot_1.npz: ...

# Explicit dtype schema (canonical — Plan 3 menggunakan schema ini):
#   obs:     np.ndarray, shape=(T,64,64,3), dtype=np.uint8
#   actions: np.ndarray, shape=(T,3),       dtype=np.int32
#   rewards: np.ndarray, shape=(T,),        dtype=np.float32
#   dones:   np.ndarray, shape=(T,),        dtype=np.bool_

# Contoh save:
np.savez(filepath,
    obs=obs_buf.astype(np.uint8),
    actions=action_buf.astype(np.int32),
    rewards=reward_buf.astype(np.float32),
    dones=done_buf.astype(np.bool_))
```

**Step 4.3 — Benchmark AgentAStar via Gym**

- Jalankan N=10 episode dengan AgentAStar baseline
- Record: throughput, collision rate, avg reward per step
- Nilai ini menjadi **target minimum** untuk world model controller di Plan 3

---

## 11. Risk Matrix

| Risk                                                                     | Likelihood | Impact | Mitigation                                                                                                  |
| ------------------------------------------------------------------------ | ---------- | ------ | ----------------------------------------------------------------------------------------------------------- |
| WPF headless render tidak menghasilkan visual yang benar (blank frame)   | Medium     | High   | Pastikan WPF `Application` ada di STA thread sebelum create viewport; force `UpdateLayout()` sebelum render |
| `RenderTargetBitmap` terlalu lambat untuk N bot × M step                 | Medium     | Medium | Resolusi 64×64 sesuai paper Ha & Schmidhuber; render sequential bukan parallel                                     |
| TCP framing mismatch antara C# dan Python                                | Low        | High   | Unit test round-trip: Python kirim cmd, C# echo back; verifikasi byte count                                 |
| `BotNormal.AccelerationMultiplier` mempengaruhi non-gym simulation modes | Low        | Medium | Set multiplier ke `null` by default; hanya GymServer yang set nilai ini                                     |
| WaypointCandidateCache stale saat bot bergerak cepat                     | Medium     | Low    | Refresh cache setiap step (bukan cache lama); kandidat selalu up-to-date                                    |
| Step duration terlalu pendek: bot belum bergerak saat step selesai       | Medium     | Medium | Default 1.0s; configurable via GymSimulationHost constructor param                                          |
| PettingZoo API mismatch (agents list empty saat reset)                   | Low        | Medium | Jalankan `parallel_api_test()` sebelum training                                                             |
| GymPathManager baseline mode menginterferensi gym actions                | Low        | High   | Flag `_useBaselineMode` adalah mutual exclusive; test dengan single bot                                     |

---

## 12. File Index

### Files to Create (New)

| File                                          | Purpose                                                               |
| --------------------------------------------- | --------------------------------------------------------------------- |
| `RAWSimO.GymServer/RAWSimO.GymServer.csproj`  | New project, net6.0-windows                                           |
| `RAWSimO.GymServer/Program.cs`                | Entry point: parse port, start TcpGymServer                           |
| `RAWSimO.GymServer/TcpGymServer.cs`           | TCP listener, connection handler                                      |
| `RAWSimO.GymServer/GymProtocol.cs`            | JSON serialization, length-prefixed framing                           |
| `RAWSimO.GymServer/GymSimulationHost.cs`      | Instance lifecycle, step loop                                         |
| `RAWSimO.GymServer/GymPathManager.cs`         | Extends AgentAStarPathManager; gym-controlled destination             |
| `RAWSimO.GymServer/BotCameraRenderer.cs`      | WPF STA thread, RenderTargetBitmap per bot                            |
| `RAWSimO.GymServer/GymCameraConfiguration.cs` | Camera config: ImageWidth/Height, FoV, BotEyeHeight                   |
| `RAWSimO.GymServer/WaypointCandidateCache.cs` | K=8 BFS candidates per bot                                            |
| `RAWSimO.GymServer/RewardCalculator.cs`       | ALPHA=1.0/BETA=0.5/GAMMA=0.1 reward per bot                           |
| `RAWSimO.GymServer/PhysicsMultipliers.cs`     | Accel/decel factor constants                                          |
| `rmfs_gym/__init__.py`                        | Python package init                                                   |
| `rmfs_gym/envs/rmfs_parallel_env.py`          | RMFSParallelEnv(ParallelEnv)                                          |
| `rmfs_gym/connection/gym_client.py`           | TCP client, length-prefixed protocol                                  |
| `rmfs_gym/utils/action_utils.py`              | **CANONICAL** — MultiDiscrete encode/decode; Plan 3 imports from here |
| `rmfs_gym/utils/obs_utils.py`                 | Raw bytes → numpy (64,64,3) uint8                                     |
| `rmfs_gym/setup.py`                           | Package install config                                                |

### Files to Modify (Existing)

| File                                 | Change                                                                                          |
| ------------------------------------ | ----------------------------------------------------------------------------------------------- |
| `RAWSimO.Core/Elements/BotNormal.cs` | Add `AccelerationMultiplier` + `DecelerationMultiplier` nullable double; apply in movement calc |
| `RAWSimO.sln`                        | Add `RAWSimO.GymServer` project reference                                                       |

### Files NOT Modified

| File                                                                             | Reason                                                                     |
| -------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| `RAWSimO.Core/Control/Defaults/PathPlanning/AgentAStar/AgentAStarPathManager.cs` | GymPathManager extends it; no change to Plan 1 files                       |
| `RAWSimO.Core/Control/PathManager.cs`                                            | `virtual` sudah ditambahkan di Plan 1 — cukup                              |
| `RAWSimO.Visualization/`                                                         | Gym rendering pakai BotCameraRenderer sendiri, bukan Visualization project |
| Semua `.xinst` / `.xsett` / `.xconf`                                             | Gym load file yang sama; format tidak berubah                              |
