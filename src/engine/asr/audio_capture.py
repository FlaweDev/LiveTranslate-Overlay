"""
System loopback audio capture module.
Continuously records system speaker output in a background thread and pushes
normalized chunks into a thread-safe queue.
"""

from __future__ import annotations
import queue
import threading
import numpy as np
import soundcard as sc


class LoopbackAudioCapture:
    def __init__(
        self,
        sample_rate: int = 16000,
        block_size: int = 4096,
        blocks_per_chunk: int = 4,
    ):
        self.sample_rate = sample_rate
        self.block_size = block_size
        self.blocks_per_chunk = blocks_per_chunk
        self._queue: queue.Queue[np.ndarray] = queue.Queue()
        self._is_running = False
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self._is_running:
            return

        try:
            speaker = sc.default_speaker()
            mic = sc.get_microphone(id=speaker.id, include_loopback=True)
        except Exception as exc:
            raise RuntimeError(f"Failed to initialize loopback audio device: {exc}") from exc

        self._is_running = True
        self._thread = threading.Thread(
            target=self._record_loop,
            args=(mic,),
            daemon=True,
        )
        self._thread.start()

    def stop(self) -> None:
        self._is_running = False
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=1.5)

    def get_chunk(self, timeout: float = 1.0) -> np.ndarray | None:
        try:
            return self._queue.get(timeout=timeout)
        except queue.Empty:
            return None

    def _record_loop(self, mic: object) -> None:
        buffer = []
        with mic.recorder(
            samplerate=self.sample_rate,
            channels=1,
            blocksize=self.block_size,
        ) as recorder:
            while self._is_running:
                raw_block = recorder.record(numframes=self.block_size)
                buffer.append(raw_block)
                if len(buffer) >= self.blocks_per_chunk:
                    chunk = np.concatenate(buffer).astype(np.float32).flatten()
                    self._queue.put(chunk)
                    buffer.clear()