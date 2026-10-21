"""
Configuration management module for the Python engine.
Parses appsettings.json (supporting JSONC comments) into strongly-typed dataclasses
and resolves model file paths.
"""

from __future__ import annotations
import json
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import List


@dataclass(frozen=True)
class AsrLaunchParams:
    device: str = "cuda"
    compute_type: str = "float16"
    att_context_size: List[int] = field(default_factory=lambda: [70, 13])
    max_speakers: int = 4
    idle_steps_threshold: int = 4


@dataclass(frozen=True)
class DiarizationLaunchParams:
    binary_diar_preds: bool = False
    cache_gating: bool = True
    cache_gating_buffer_size: int = 2


@dataclass(frozen=True)
class LlmLaunchParams:
    n_ctx: int = 2048
    n_gpu_layers: int = 0
    n_threads: int = 4
    max_concurrent_predictions: int = 1
    offload_kqv: bool = False
    enable_thinking: bool = False
    temperature: float = 0.0
    max_tokens: int = 128
    stop_strings: List[str] = field(default_factory=lambda: ["(Note:", "\n"])
    top_k: int = 20
    top_p: float = 1.0
    min_p: float = 0.0
    repeat_penalty: float = 1.0


@dataclass(frozen=True)
class ModelDescriptor:
    key: str
    name: str
    filename: str
    override_path: str
    download_url: str

    def resolve_path(self, root_dir: Path, default_models_dir: str) -> Path:
        if self.override_path.strip():
            custom = Path(self.override_path.strip())
            if not custom.is_absolute():
                custom = root_dir / custom
            if custom.suffix:
                return custom.resolve()
            return (custom / self.filename).resolve()

        base_dir = Path(default_models_dir.strip() or "models")
        if not base_dir.is_absolute():
            base_dir = root_dir / base_dir
        return (base_dir / self.filename).resolve()


@dataclass(frozen=True)
class RemoteApiConfig:
    base_url: str = "http://localhost:1234/v1"
    endpoint: str = "/chat/completions"
    api_key: str = ""
    model_name: str = "tower-plus-2b@q5_k_m"
    timeout_seconds: int = 30

    @property
    def full_url(self) -> str:
        return f"{self.base_url.rstrip('/')}/{self.endpoint.lstrip('/')}"


@dataclass(frozen=True)
class TranslationConfig:
    provider: str = "LlamaCpp"
    source_language: str = "English"
    target_language: str = "Russian"
    prompt_template: str = (
        "Translate the following {source_lang} source text to {target_lang}: {text} \n{target_lang}:"
    )
    remote_api: RemoteApiConfig = field(default_factory=RemoteApiConfig)


@dataclass(frozen=True)
class EngineConfig:
    root_dir: Path
    default_models_dir: str
    require_download_confirmation: bool
    asr_model: ModelDescriptor
    asr_params: AsrLaunchParams
    diar_model: ModelDescriptor
    diar_params: DiarizationLaunchParams
    llm_model: ModelDescriptor
    llm_params: LlmLaunchParams
    translation: TranslationConfig

    @classmethod
    def load(
        cls,
        config_path: Path,
        root_dir: Path | None = None,
        override_config_path: Path | None = None,
    ) -> EngineConfig:
        resolved_root = (root_dir or config_path.parent).resolve()
        data = cls._load_jsonc_file(config_path)

        if override_config_path and override_config_path.exists():
            override_data = cls._load_jsonc_file(override_config_path)
            data = cls._deep_merge_dicts(data, override_data)

        models_sec = data.get("models", {})
        asr_sec = models_sec.get("asr", {})
        asr_lp = asr_sec.get("launchParameters", {})

        diar_sec = models_sec.get("diarization", {})
        diar_lp = diar_sec.get("launchParameters", {})

        llm_sec = models_sec.get("llm", {})
        llm_lp = llm_sec.get("launchParameters", {})

        trans_sec = data.get("translation", {})
        api_sec = trans_sec.get("remoteApi", {})

        return cls(
            root_dir=resolved_root,
            default_models_dir=models_sec.get("defaultDirectory", "models"),
            require_download_confirmation=models_sec.get("requireDownloadConfirmation", True),
            asr_model=ModelDescriptor(
                key="asr",
                name=asr_sec.get("name", "ASR Model"),
                filename=asr_sec.get("filename", "asr.nemo"),
                override_path=asr_sec.get("overridePath", ""),
                download_url=asr_sec.get("downloadUrl", ""),
            ),
            asr_params=AsrLaunchParams(
                device=asr_lp.get("device", "cuda"),
                compute_type=asr_lp.get("computeType", "float16"),
                att_context_size=asr_lp.get("attContextSize", [70, 13]),
                max_speakers=asr_lp.get("maxSpeakers", 4),
                idle_steps_threshold=asr_lp.get("idleStepsThreshold", 4),
            ),
            diar_model=ModelDescriptor(
                key="diarization",
                name=diar_sec.get("name", "Diarization Model"),
                filename=diar_sec.get("filename", "diar.nemo"),
                override_path=diar_sec.get("overridePath", ""),
                download_url=diar_sec.get("downloadUrl", ""),
            ),
            diar_params=DiarizationLaunchParams(
                binary_diar_preds=diar_lp.get("binaryDiarPreds", False),
                cache_gating=diar_lp.get("cacheGating", True),
                cache_gating_buffer_size=diar_lp.get("cacheGatingBufferSize", 2),
            ),
            llm_model=ModelDescriptor(
                key="llm",
                name=llm_sec.get("name", "LLM Model"),
                filename=llm_sec.get("filename", "model.gguf"),
                override_path=llm_sec.get("overridePath", ""),
                download_url=llm_sec.get("downloadUrl", ""),
            ),
            llm_params=LlmLaunchParams(
                n_ctx=llm_lp.get("nCtx", 2048),
                n_gpu_layers=llm_lp.get("nGpuLayers", 0),
                n_threads=llm_lp.get("nThreads", 4),
                max_concurrent_predictions=llm_lp.get("maxConcurrentPredictions", 1),
                offload_kqv=llm_lp.get("offloadKqv", False),
                enable_thinking=llm_lp.get("enableThinking", False),
                temperature=llm_lp.get("temperature", 0.0),
                max_tokens=llm_lp.get("maxTokens", 128),
                stop_strings=llm_lp.get("stopStrings", ["(Note:", "\n"]),
                top_k=llm_lp.get("topK", 20),
                top_p=llm_lp.get("topP", 1.0),
                min_p=llm_lp.get("minP", 0.0),
                repeat_penalty=llm_lp.get("repeatPenalty", 1.0),
            ),
            translation=TranslationConfig(
                provider=trans_sec.get("provider", "LlamaCpp"),
                source_language=trans_sec.get("sourceLanguage", "English"),
                target_language=trans_sec.get("targetLanguage", "Russian"),
                prompt_template=trans_sec.get(
                    "promptTemplate",
                    "Translate the following {source_lang} source text to {target_lang}: {text} \n{target_lang}:",
                ),
                remote_api=RemoteApiConfig(
                    base_url=api_sec.get("baseUrl", "http://localhost:1234/v1"),
                    endpoint=api_sec.get("endpoint", "/chat/completions"),
                    api_key=api_sec.get("apiKey", ""),
                    model_name=api_sec.get("modelName", "tower-plus-2b@q5_k_m"),
                    timeout_seconds=api_sec.get("timeoutSeconds", 30),
                ),
            ),
        )

    @classmethod
    def _load_jsonc_file(cls, path: Path) -> Dict[str, Any]:
        raw_text = path.read_text(encoding="utf-8")
        clean_json = cls._strip_json_comments(raw_text)
        return json.loads(clean_json)

    @classmethod
    def _deep_merge_dicts(
        cls,
        base: Dict[str, Any],
        override: Dict[str, Any],
    ) -> Dict[str, Any]:
        merged = dict(base)
        for key, value in override.items():
            if (
                key in merged
                and isinstance(merged[key], dict)
                and isinstance(value, dict)
            ):
                merged[key] = cls._deep_merge_dicts(merged[key], value)
            else:
                merged[key] = value
        return merged

    @staticmethod
    def _strip_json_comments(text: str) -> str:
        pattern = r'("(?:\\.|[^"\\])*")|//.*?$|/\*[\s\S]*?\*/'
        return re.sub(
            pattern,
            lambda m: m.group(1) if m.group(1) else "",
            text,
            flags=re.MULTILINE,
        )