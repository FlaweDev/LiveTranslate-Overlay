"""
Local translation provider powered by llama-cpp-python.
Enforces concurrency limits and applies exact sampling/offload parameters from config.
"""

from __future__ import annotations
import threading
from pathlib import Path
from llama_cpp import Llama

from core.config import LlmLaunchParams
from translation.base import TranslationProvider


class LlamaCppProvider(TranslationProvider):
    def __init__(self, model_path: Path, params: LlmLaunchParams):
        if not model_path.exists():
            raise FileNotFoundError(f"GGUF model file not found: {model_path}")

        self._params = params
        self._semaphore = threading.BoundedSemaphore(
            value=max(1, params.max_concurrent_predictions)
        )

        self._llm = Llama(
            model_path=str(model_path),
            n_ctx=params.n_ctx,
            n_gpu_layers=params.n_gpu_layers,
            n_threads=params.n_threads,
            offload_kqv=params.offload_kqv,
            verbose=False,
        )

    def generate(self, prompt: str) -> str:
        with self._semaphore:
            response = self._llm.create_chat_completion(
                messages=[{"role": "user", "content": prompt}],
                max_tokens=self._params.max_tokens,
                temperature=self._params.temperature,
                top_k=self._params.top_k,
                top_p=self._params.top_p,
                min_p=self._params.min_p,
                repeat_penalty=self._params.repeat_penalty,
                stop=self._params.stop_strings if self._params.stop_strings else None,
            )

            choices = response.get("choices", [])
            if not choices:
                return ""
            return choices[0].get("message", {}).get("content", "")