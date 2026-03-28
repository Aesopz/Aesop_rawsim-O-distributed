"""
Phase 4 — Baseline Data Collection
====================================
Connects to a running GymServer in --baseline mode and collects
N episodes of (obs, action, reward, done) per bot.

Usage:
    # 1. Start GymServer in another terminal (baseline mode):
    #    dotnet run --project RAWSimO.GymServer -- <inst> <sett> <conf> 7654 1.0 500 --baseline
    #
    # 2. Run this script:
    #    python scripts/collect_baseline.py --episodes 10 --output dataset/

Dataset format per episode:
    dataset/
      episode_0000/
        bot_0.npz  →  {obs: (T,64,64,3) uint8, actions: (T,3) int32,
                        rewards: (T,) float32, dones: (T,) bool}
        bot_1.npz
        ...
"""

import argparse
import os
import sys
import time
import numpy as np

# Allow running from repo root without installing rmfs_gym
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rmfs_gym.envs.rmfs_parallel_env import RMFSParallelEnv
from rmfs_gym.utils.action_utils import encode_action, WAYPOINT_IDX_STAY


def collect_episode(env: RMFSParallelEnv, episode_idx: int, output_dir: str, seed: int):
    """Run one episode in baseline mode, save data."""
    obs, _ = env.reset(seed=seed)
    agents = list(obs.keys())
    n_bots = len(agents)

    # Buffers per agent
    obs_buf   = {a: [] for a in agents}
    act_buf   = {a: [] for a in agents}
    rew_buf   = {a: [] for a in agents}
    done_buf  = {a: [] for a in agents}

    # Dummy action for baseline mode (server ignores it)
    baseline_action = encode_action(WAYPOINT_IDX_STAY, 1, 1)  # [8, 1, 1]

    step = 0
    while env.agents:
        # Record pre-step observation
        for a in env.agents:
            obs_buf[a].append(obs[a].copy())

        # Send dummy actions — baseline mode server ignores them
        actions = {a: baseline_action.copy() for a in env.agents}
        for a in env.agents:
            act_buf[a].append(baseline_action.copy())

        obs, rewards, dones, truncs, infos = env.step(actions)

        for a in list(rewards.keys()):
            rew_buf[a].append(float(rewards[a]))
            done_buf[a].append(bool(dones[a]) or bool(truncs[a]))

        step += 1
        if step % 50 == 0:
            print(f"  Episode {episode_idx:04d} step {step:4d} | "
                  f"avg_reward={np.mean([rew_buf[a][-1] for a in agents if rew_buf[a]]):.4f}")

    # Save .npz per bot
    ep_dir = os.path.join(output_dir, f"episode_{episode_idx:04d}")
    os.makedirs(ep_dir, exist_ok=True)

    for a in agents:
        if not obs_buf[a]:
            continue
        path = os.path.join(ep_dir, f"{a}.npz")
        np.savez(
            path,
            obs=np.array(obs_buf[a],   dtype=np.uint8),    # (T, 64, 64, 3)
            actions=np.array(act_buf[a], dtype=np.int32),   # (T, 3)
            rewards=np.array(rew_buf[a], dtype=np.float32), # (T,)
            dones=np.array(done_buf[a],  dtype=np.bool_),   # (T,)
        )

    total_steps = len(rew_buf[agents[0]]) if agents else 0
    total_reward = sum(np.sum(rew_buf[a]) for a in agents if rew_buf[a])
    print(f"  Episode {episode_idx:04d} done | steps={total_steps} | "
          f"total_reward={total_reward:.2f} | saved → {ep_dir}")
    return total_steps, total_reward


def main():
    parser = argparse.ArgumentParser(description="Collect baseline AgentAStar dataset")
    parser.add_argument("--host",     default="localhost")
    parser.add_argument("--port",     type=int, default=7654)
    parser.add_argument("--episodes", type=int, default=10, help="Number of episodes to collect")
    parser.add_argument("--output",   default="dataset", help="Output directory for .npz files")
    parser.add_argument("--seed",     type=int, default=0,  help="Base random seed")
    args = parser.parse_args()

    print(f"Connecting to GymServer at {args.host}:{args.port} ...")
    env = RMFSParallelEnv(host=args.host, port=args.port)
    print("Connected.")

    os.makedirs(args.output, exist_ok=True)

    all_steps  = []
    all_reward = []

    for ep in range(args.episodes):
        seed = args.seed + ep
        print(f"\nEpisode {ep+1}/{args.episodes} (seed={seed})")
        t0 = time.time()
        steps, reward = collect_episode(env, ep, args.output, seed=seed)
        elapsed = time.time() - t0
        all_steps.append(steps)
        all_reward.append(reward)
        print(f"  Time: {elapsed:.1f}s")

    env.close()

    print("\n=== Collection Summary ===")
    print(f"Episodes:    {args.episodes}")
    print(f"Avg steps:   {np.mean(all_steps):.1f}")
    print(f"Avg reward:  {np.mean(all_reward):.4f}")
    print(f"Dataset dir: {os.path.abspath(args.output)}")


if __name__ == "__main__":
    main()
