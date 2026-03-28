"""
Phase 4.3 — Benchmark AgentAStar via Gym
==========================================
Runs N episodes in baseline mode and records:
  - avg throughput (StatNumberOfPickups delta per step)
  - collision rate (collisions per step)
  - avg reward per step

Results are saved as benchmark_results.json — this becomes the
**minimum target** for the world model controller in Plan 3.

Usage:
    # Start GymServer in baseline mode first, then:
    python scripts/benchmark_astar.py --episodes 10 --output benchmark_results.json
"""

import argparse
import json
import os
import sys
import time
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rmfs_gym.envs.rmfs_parallel_env import RMFSParallelEnv
from rmfs_gym.utils.action_utils import encode_action, WAYPOINT_IDX_STAY


def run_episode(env: RMFSParallelEnv, seed: int) -> dict:
    """Run one episode, return per-episode metrics."""
    obs, _ = env.reset(seed=seed)
    agents = list(obs.keys())
    baseline_action = encode_action(WAYPOINT_IDX_STAY, 1, 1)

    rewards_all   = []
    collisions_all = []
    steps = 0

    while env.agents:
        actions = {a: baseline_action.copy() for a in env.agents}
        obs, rewards, dones, truncs, infos = env.step(actions)
        steps += 1

        step_reward = sum(rewards.values())
        rewards_all.append(step_reward / max(len(rewards), 1))

        step_collisions = sum(
            info.get("collision_count", 0)
            for info in infos.values()
            if isinstance(info, dict)
        )
        collisions_all.append(step_collisions)

    return {
        "steps": steps,
        "total_reward": float(np.sum(rewards_all)),
        "avg_reward_per_step": float(np.mean(rewards_all)) if rewards_all else 0.0,
        "total_collisions": int(np.sum(collisions_all)),
        "collision_rate_per_step": float(np.mean(collisions_all)) if collisions_all else 0.0,
    }


def main():
    parser = argparse.ArgumentParser(description="Benchmark AgentAStar baseline via GymServer")
    parser.add_argument("--host",     default="localhost")
    parser.add_argument("--port",     type=int, default=7654)
    parser.add_argument("--episodes", type=int, default=10)
    parser.add_argument("--output",   default="benchmark_results.json")
    parser.add_argument("--seed",     type=int, default=0)
    args = parser.parse_args()

    print(f"Connecting to GymServer at {args.host}:{args.port} ...")
    env = RMFSParallelEnv(host=args.host, port=args.port)
    print("Connected. Running benchmark ...")

    episode_results = []
    for ep in range(args.episodes):
        seed = args.seed + ep
        t0 = time.time()
        result = run_episode(env, seed=seed)
        elapsed = time.time() - t0
        result["episode"] = ep
        result["seed"] = seed
        result["wall_time_s"] = round(elapsed, 2)
        episode_results.append(result)

        print(f"  ep={ep:3d}  steps={result['steps']:4d}  "
              f"avg_rew={result['avg_reward_per_step']:+.4f}  "
              f"collisions/step={result['collision_rate_per_step']:.3f}  "
              f"({elapsed:.1f}s)")

    env.close()

    # Aggregate
    avg_reward     = float(np.mean([r["avg_reward_per_step"]   for r in episode_results]))
    avg_collisions = float(np.mean([r["collision_rate_per_step"] for r in episode_results]))
    avg_steps      = float(np.mean([r["steps"]                  for r in episode_results]))

    summary = {
        "method": "AgentAStar_baseline",
        "n_episodes": args.episodes,
        "avg_reward_per_step": avg_reward,
        "avg_collision_rate_per_step": avg_collisions,
        "avg_steps_per_episode": avg_steps,
        "target_for_plan3": {
            "min_avg_reward_per_step": avg_reward,
            "max_collision_rate_per_step": avg_collisions,
        },
        "episodes": episode_results,
    }

    with open(args.output, "w") as f:
        json.dump(summary, f, indent=2)

    print(f"\n=== AgentAStar Benchmark Results ===")
    print(f"Avg reward/step:      {avg_reward:+.4f}")
    print(f"Avg collisions/step:  {avg_collisions:.4f}")
    print(f"Avg steps/episode:    {avg_steps:.1f}")
    print(f"Saved → {os.path.abspath(args.output)}")
    print(f"\nPlan 3 target: reward/step ≥ {avg_reward:.4f}, collisions/step ≤ {avg_collisions:.4f}")


if __name__ == "__main__":
    main()
