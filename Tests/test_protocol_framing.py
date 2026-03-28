"""Test TCP framing protocol (Python side)."""
import struct
import json
import numpy as np
import pytest


def encode_message(header: dict, image_data: bytes = b"") -> bytes:
    """Encode a message as [4-byte len][json][image_data]."""
    json_bytes = json.dumps(header).encode("utf-8")
    length = struct.pack("<I", len(json_bytes))
    return length + json_bytes + image_data


def decode_message(data: bytes) -> tuple[dict, bytes]:
    length = struct.unpack("<I", data[:4])[0]
    json_bytes = data[4:4+length]
    rest = data[4+length:]
    return json.loads(json_bytes.decode("utf-8")), rest


class TestFraming:
    def test_reset_cmd_roundtrip(self):
        msg = encode_message({"cmd": "reset", "seed": 42})
        header, rest = decode_message(msg)
        assert header["cmd"] == "reset"
        assert header["seed"] == 42
        assert rest == b""

    def test_step_response_with_images(self):
        img_data = np.random.randint(0, 255, 64 * 64 * 3, dtype=np.uint8).tobytes()
        header = {"rewards": {"bot_0": 0.5}, "dones": {"bot_0": False}}
        msg = encode_message(header, img_data)
        decoded_header, rest = decode_message(msg)
        assert decoded_header["rewards"]["bot_0"] == pytest.approx(0.5)
        assert rest == img_data

    def test_multiple_bots_image_concat(self):
        n_bots = 3
        img_size = 64 * 64 * 3
        images = [np.full(img_size, i, dtype=np.uint8) for i in range(n_bots)]
        all_images = np.concatenate(images).tobytes()
        assert len(all_images) == n_bots * img_size
        # Verify ordering
        for i in range(n_bots):
            chunk = all_images[i * img_size : (i + 1) * img_size]
            assert all(b == i for b in chunk)
