from pettingzoo import ParallelEnv
from gymnasium.spaces import Box, MultiDiscrete
import numpy as np
from rmfs_gym.connection.gym_client import GymClient


class RMFSParallelEnv(ParallelEnv):
    metadata = {"render_modes": ["human"], "name": "rmfs_v1"}

    def __init__(self, host="localhost", port=7654, n_directions=5):
        self._client = GymClient(host, port)
        self._img_h = None  # set on first reset from server
        self._img_w = None
        self._img_c = None
        self._n_dirs = n_directions
        self.possible_agents = []
        self.agents = []

    def reset(self, seed=None, options=None):
        info = self._client.send_reset(seed or 0)
        self.agents = [f"bot_{i}" for i in range(info["n_bots"])]
        self.possible_agents = list(self.agents)
        w, h, c = info["image_size"]  # auto-detect from server (GymCameraConfiguration is source of truth)
        self._img_h, self._img_w, self._img_c = h, w, c
        raw_images = self._client.recv_images(len(self.agents), self._img_h, self._img_w, self._img_c)
        obs = {a: img for a, img in zip(self.agents, raw_images)}
        return obs, {a: {} for a in self.agents}

    def step(self, actions):
        resp = self._client.send_step(actions)
        raw_images = self._client.recv_images(len(self.agents), self._img_h, self._img_w, self._img_c)
        obs     = {a: img for a, img in zip(self.agents, raw_images)}
        rewards = {a: float(resp["rewards"].get(a, 0.0)) for a in self.agents}
        dones   = {a: bool(resp["dones"].get(a, False))  for a in self.agents}
        truncs  = {a: bool(resp["truncated"].get(a, False)) for a in self.agents}
        infos   = {a: resp.get("infos", {}).get(a, {})   for a in self.agents}
        if all(dones.values()) or all(truncs.values()):
            self.agents = []
        return obs, rewards, dones, truncs, infos

    def observation_space(self, agent):
        return Box(0, 255, shape=(self._img_h, self._img_w, self._img_c), dtype=np.uint8)

    def action_space(self, agent):
        return MultiDiscrete([self._n_dirs, 3, 3])

    def close(self):
        try:
            self._client.send_close()
            self._client.disconnect()
        except Exception:
            pass
