#!/usr/bin/env python3
"""
Entry point for the .NET frontend integration.
Manages model verification/downloading handshake, asynchronous translation worker,
and real-time ASR event streaming over stdout/stdin.
"""

from __future__ import annotations
import argparse
import sys
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Dict

# Ensure engine root is on sys.path
ENGINE_ROOT = Path(__file__).resolve().parent.parent
if str(ENGINE_ROOT) not in sys.path:
    sys.path.insert(0, str(ENGINE_ROOT))

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    sys.stdin.reconfigure(encoding="utf-8", errors="replace")

# Redirect standard stdout to stderr to keep IPC channel strictly JSON-only
_ipc_stdout = sys.stdout
sys.stdout = sys.stderr

from asr.model_loader import apply_runtime_patches
apply_runtime_patches(quiet=True)

from asr.streaming_engine import StreamingAsrEngine, TranscriptSegmentEvent
from core.config import EngineConfig
from core.models_manager import ModelsManager
from ipc.protocol import IpcChannel
from translation import create_translation_session
from translation.base import PhraseTranslationSession


@dataclass
class ActivePhraseState:
    phrase_id: int
    speaker_index: int
    start_time: float
    full_text: str


class BridgeOrchestrator:
    def __init__(self, config: EngineConfig, ipc: IpcChannel):
        self._config = config
        self._ipc = ipc
        self._models_manager = ModelsManager(config)
        self._translator: PhraseTranslationSession | None = None
        self._asr_engine: StreamingAsrEngine | None = None

        self._phrases_by_id: Dict[int, ActivePhraseState] = {}
        self._active_phrase_by_spk: Dict[int, int] = {}
        self._next_phrase_id = 1

        self._pending_translations: Dict[int, str] = {}
        self._pending_lock = threading.Lock()
        self._worker_event = threading.Event()
        self._is_running = True

    def start(self) -> None:
        self._ensure_models_ready()

        self._ipc.send_status("loading_models", "Loading translation & ASR models...")
        self._translator = create_translation_session(self._config, self._models_manager)

        worker = threading.Thread(target=self._translation_worker_loop, daemon=True)
        worker.start()

        self._asr_engine = StreamingAsrEngine(
            config=self._config,
            models_manager=self._models_manager,
            on_segment=self._on_asr_segment,
        )
        self._asr_engine.initialize()

        self._ipc.send_status("ready", "System started and listening")
        self._asr_engine.run()

    def stop(self) -> None:
        self._is_running = False
        self._worker_event.set()
        if self._asr_engine:
            self._asr_engine.stop()

    def _ensure_models_ready(self) -> None:
        missing = self._models_manager.find_missing_models()
        if not missing:
            return

        if self._config.require_download_confirmation:
            self._ipc.send_download_required(missing)
            self._ipc.wait_for_command("confirm_download")

        self._ipc.send_status("downloading", "Downloading required models...")
        self._models_manager.download_models(
            missing_models=missing,
            on_progress=self._ipc.send_download_progress,
        )
        self._ipc.send_event("download_completed")

    def _on_asr_segment(self, segment: TranscriptSegmentEvent) -> None:
        spk = segment.speaker_index
        phrase_id = self._active_phrase_by_spk.get(spk)
        state = self._phrases_by_id.get(phrase_id) if phrase_id is not None else None

        if state is None or abs(state.start_time - segment.start_time) > 1e-3:
            phrase_id = self._next_phrase_id
            self._next_phrase_id += 1
            self._active_phrase_by_spk[spk] = phrase_id

            self._phrases_by_id[phrase_id] = ActivePhraseState(
                phrase_id=phrase_id,
                speaker_index=spk,
                start_time=segment.start_time,
                full_text=segment.text,
            )

            self._ipc.send_event(
                "new_text",
                {
                    "phrase_id": phrase_id,
                    "spk_idx": spk,
                    "start_time": segment.start_time,
                    "text": segment.text,
                },
            )
        else:
            previous_text = state.full_text
            delta_text = segment.text[len(previous_text):]
            state.full_text = segment.text

            if delta_text:
                self._ipc.send_event(
                    "add_text",
                    {
                        "phrase_id": phrase_id,
                        "text": delta_text,
                    },
                )

        with self._pending_lock:
            self._pending_translations[phrase_id] = segment.text
        self._worker_event.set()

    def _translation_worker_loop(self) -> None:
        while self._is_running:
            self._worker_event.wait()
            self._worker_event.clear()

            while self._is_running:
                with self._pending_lock:
                    if not self._pending_translations:
                        break
                    phrase_id = next(iter(self._pending_translations))
                    full_text = self._pending_translations.pop(phrase_id)

                if self._translator:
                    translated = self._translator.translate_phrase(phrase_id, full_text)
                    self._ipc.send_event(
                        "translation",
                        {
                            "phrase_id": phrase_id,
                            "translation": translated,
                        },
                    )


def main() -> None:
    parser = argparse.ArgumentParser(description="LiveTranslate-Overlay Python IPC Bridge")
    parser.add_argument(
        "--config",
        type=Path,
        default=ENGINE_ROOT.parent.parent / "config" / "appsettings.json",
        help="Path to base appsettings.json",
    )
    parser.add_argument(
        "--override-config",
        type=Path,
        default=None,
        help="Optional path to appsettings.Development.json override",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=None,
        help="Application root directory for resolving relative paths",
    )
    args = parser.parse_args()

    ipc = IpcChannel(stdout_stream=_ipc_stdout, stdin_stream=sys.stdin)

    try:
        config = EngineConfig.load(
            config_path=args.config,
            root_dir=args.root,
            override_config_path=args.override_config,
        )
        orchestrator = BridgeOrchestrator(config, ipc)
        orchestrator.start()
    except KeyboardInterrupt:
        pass
    except Exception as exc:
        ipc.send_error(str(exc))
        print(f"Critical error: {exc}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()