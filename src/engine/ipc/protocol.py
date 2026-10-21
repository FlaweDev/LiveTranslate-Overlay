"""
JSON-over-Stdio IPC protocol definitions for communication with the .NET UI.
"""

from __future__ import annotations
import json
import sys
import threading
from typing import Any, Dict, List, TextIO

from core.models_manager import DownloadProgress, MissingModelInfo


class IpcChannel:
    def __init__(self, stdout_stream: TextIO, stdin_stream: TextIO):
        self._stdout = stdout_stream
        self._stdin = stdin_stream
        self._write_lock = threading.Lock()

    def send_event(self, event_type: str, payload: Dict[str, Any] | None = None) -> None:
        message: Dict[str, Any] = {"event": event_type}
        if payload:
            message.update(payload)

        serialized = json.dumps(message, ensure_ascii=False) + "\n"
        with self._write_lock:
            self._stdout.write(serialized)
            self._stdout.flush()

    def send_download_required(self, missing_models: List[MissingModelInfo]) -> None:
        self.send_event(
            "download_required",
            {
                "models": [
                    {
                        "key": m.key,
                        "name": m.name,
                        "target_path": str(m.target_path),
                        "url": m.download_url,
                        "size_bytes": m.size_bytes,
                    }
                    for m in missing_models
                ]
            },
        )

    def send_download_progress(self, progress: DownloadProgress) -> None:
        self.send_event(
            "download_progress",
            {
                "key": progress.model_key,
                "name": progress.model_name,
                "downloaded_bytes": progress.downloaded_bytes,
                "total_bytes": progress.total_bytes,
                "speed_mbps": progress.speed_mbps,
                "percentage": progress.percentage,
            },
        )

    def send_status(self, status: str, message: str) -> None:
        self.send_event("status", {"status": status, "message": message})

    def send_error(self, error_message: str) -> None:
        self.send_event("error", {"message": error_message})

    def wait_for_command(self, expected_command: str) -> Dict[str, Any]:
        while True:
            line = self._stdin.readline()
            if not line:
                sys.exit(0)

            clean_line = line.lstrip("\ufeff").strip()
            if not clean_line:
                continue

            try:
                data = json.loads(clean_line)
                cmd = data.get("command")
                if cmd == expected_command:
                    return data
                if cmd == "cancel":
                    sys.exit(0)
            except json.JSONDecodeError as exc:
                print(f"[IPC Warning] Malformed command JSON: {clean_line} ({exc})", file=sys.stderr)
                continue