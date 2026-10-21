"""
Model verification and downloading manager.
Checks local paths for required models, queries remote file sizes,
and performs chunked HTTP downloads with throttled progress reporting.
"""

from __future__ import annotations
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, List
import requests

from core.config import EngineConfig, ModelDescriptor


@dataclass(frozen=True)
class MissingModelInfo:
    key: str
    name: str
    target_path: Path
    download_url: str
    size_bytes: int


@dataclass(frozen=True)
class DownloadProgress:
    model_key: str
    model_name: str
    downloaded_bytes: int
    total_bytes: int
    speed_mbps: float
    percentage: float


ProgressCallback = Callable[[DownloadProgress], None]


class ModelsManager:
    def __init__(self, config: EngineConfig):
        self._config = config

    def get_required_models(self) -> List[ModelDescriptor]:
        models = [self._config.asr_model, self._config.diar_model]
        if self._config.translation.provider.lower() == "llamacpp":
            models.append(self._config.llm_model)
        return models

    def resolve_model_path(self, descriptor: ModelDescriptor) -> Path:
        return descriptor.resolve_path(
            self._config.root_dir,
            self._config.default_models_dir
        )

    def find_missing_models(self) -> List[MissingModelInfo]:
        missing: List[MissingModelInfo] = []

        for descriptor in self.get_required_models():
            target_path = self.resolve_model_path(descriptor)
            if not target_path.exists() or target_path.stat().st_size == 0:
                size_bytes = self._fetch_remote_file_size(descriptor.download_url)
                missing.append(
                    MissingModelInfo(
                        key=descriptor.key,
                        name=descriptor.name,
                        target_path=target_path,
                        download_url=descriptor.download_url,
                        size_bytes=size_bytes,
                    )
                )

        return missing

    def download_models(
        self,
        missing_models: List[MissingModelInfo],
        on_progress: ProgressCallback | None = None,
    ) -> None:
        for model in missing_models:
            self._download_single_model(model, on_progress)

    def _download_single_model(
        self,
        model: MissingModelInfo,
        on_progress: ProgressCallback | None,
    ) -> None:
        if not model.download_url:
            raise ValueError(f"Download URL is empty for model '{model.name}'.")

        model.target_path.parent.mkdir(parents=True, exist_ok=True)
        temp_path = model.target_path.with_suffix(model.target_path.suffix + ".part")

        with requests.get(model.download_url, stream=True, timeout=(10, 60)) as response:
            response.raise_for_status()
            total_bytes = int(response.headers.get("Content-Length", model.size_bytes))

            downloaded = 0
            start_time = time.monotonic()
            last_emit_time = 0.0

            with open(temp_path, "wb") as file:
                for chunk in response.iter_content(chunk_size=1024 * 256):
                    if not chunk:
                        continue

                    file.write(chunk)
                    downloaded += len(chunk)

                    now = time.monotonic()
                    if on_progress and (now - last_emit_time >= 0.25 or downloaded == total_bytes):
                        elapsed = max(now - start_time, 0.001)
                        speed_mbps = (downloaded / (1024 * 1024)) / elapsed
                        percentage = (downloaded / total_bytes * 100.0) if total_bytes > 0 else 0.0

                        on_progress(
                            DownloadProgress(
                                model_key=model.key,
                                model_name=model.name,
                                downloaded_bytes=downloaded,
                                total_bytes=total_bytes,
                                speed_mbps=round(speed_mbps, 2),
                                percentage=round(percentage, 1),
                            )
                        )
                        last_emit_time = now

        temp_path.replace(model.target_path)

    @staticmethod
    def _fetch_remote_file_size(url: str) -> int:
        if not url:
            return 0
        try:
            response = requests.head(url, allow_redirects=True, timeout=10)
            if response.ok:
                return int(response.headers.get("Content-Length", 0))
        except requests.RequestException:
            pass
        return 0