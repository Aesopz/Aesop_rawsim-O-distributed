import socket
import struct
import json
import numpy as np
from typing import Dict, List


class GymClient:
    """TCP client for RAWSimO GymServer. Length-prefixed JSON + raw image bytes."""

    def __init__(self, host: str = "localhost", port: int = 7654):
        self._host = host
        self._port = port
        self._sock: socket.socket | None = None
        self._connect()

    def _connect(self):
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.connect((self._host, self._port))

    def disconnect(self):
        if self._sock:
            self._sock.close()
            self._sock = None

    def _send_message(self, header: dict, image_data: bytes | None = None):
        json_bytes = json.dumps(header).encode("utf-8")
        length = struct.pack("<I", len(json_bytes))  # little-endian uint32
        self._sock.sendall(length + json_bytes)
        if image_data:
            self._sock.sendall(image_data)

    def _recv_exact(self, n: int) -> bytes:
        buf = b""
        while len(buf) < n:
            chunk = self._sock.recv(n - len(buf))
            if not chunk:
                raise ConnectionError("Server closed connection")
            buf += chunk
        return buf

    def _recv_message(self) -> dict:
        length_bytes = self._recv_exact(4)
        length = struct.unpack("<I", length_bytes)[0]
        json_bytes = self._recv_exact(length)
        return json.loads(json_bytes.decode("utf-8"))

    def send_reset(self, seed: int = 0) -> dict:
        self._send_message({"cmd": "reset", "seed": seed})
        return self._recv_message()

    def send_step(self, actions: Dict[str, np.ndarray]) -> dict:
        serialized = {k: v.tolist() if hasattr(v, "tolist") else list(v)
                      for k, v in actions.items()}
        self._send_message({"cmd": "step", "actions": serialized})
        return self._recv_message()

    def send_close(self):
        self._send_message({"cmd": "close"})

    def recv_images(self, n_bots: int, h: int, w: int, c: int) -> List[np.ndarray]:
        total = n_bots * h * w * c
        raw = self._recv_exact(total)
        images = []
        img_size = h * w * c
        for i in range(n_bots):
            chunk = raw[i * img_size : (i + 1) * img_size]
            images.append(np.frombuffer(chunk, dtype=np.uint8).reshape(h, w, c).copy())
        return images
