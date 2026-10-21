"""
Remote HTTP API translation provider compatible with OpenAI/LM Studio endpoints.
"""

from __future__ import annotations
import requests

from core.config import LlmLaunchParams, RemoteApiConfig
from translation.base import TranslationProvider


class RemoteApiProvider(TranslationProvider):
    def __init__(self, api_config: RemoteApiConfig, llm_params: LlmLaunchParams):
        self._api_cfg = api_config
        self._llm_params = llm_params
        self._session = requests.Session()

        if self._api_cfg.api_key:
            self._session.headers.update(
                {"Authorization": f"Bearer {self._api_cfg.api_key}"}
            )

    def generate(self, prompt: str) -> str:
        payload = {
            "model": self._api_cfg.model_name,
            "messages": [{"role": "user", "content": prompt}],
            "temperature": self._llm_params.temperature,
            "max_tokens": self._llm_params.max_tokens,
            "top_k": self._llm_params.top_k,
            "top_p": self._llm_params.top_p,
            "min_p": self._llm_params.min_p,
            "repeat_penalty": self._llm_params.repeat_penalty,
            "stop": self._llm_params.stop_strings,
            "stream": False,
        }

        try:
            with self._session.post(
                self._api_cfg.full_url,
                json=payload,
                timeout=(10, self._api_cfg.timeout_seconds),
            ) as response:
                response.raise_for_status()
                data = response.json()
                return (
                    data.get("choices", [{}])[0]
                    .get("message", {})
                    .get("content", "")
                )
        except requests.RequestException:
            return "(Connection error)"