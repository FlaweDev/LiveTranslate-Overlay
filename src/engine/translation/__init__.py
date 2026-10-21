from core.config import EngineConfig
from core.models_manager import ModelsManager
from translation.api_provider import RemoteApiProvider
from translation.base import PhraseTranslationSession, TranslationProvider
from translation.llama_provider import LlamaCppProvider


def create_translation_session(
    config: EngineConfig,
    models_manager: ModelsManager,
) -> PhraseTranslationSession:
    provider_type = config.translation.provider.strip().lower()

    if provider_type == "llamacpp":
        gguf_path = models_manager.resolve_model_path(config.llm_model)
        provider: TranslationProvider = LlamaCppProvider(gguf_path, config.llm_params)
    elif provider_type == "remoteapi":
        provider = RemoteApiProvider(config.translation.remote_api, config.llm_params)
    else:
        raise ValueError(f"Unsupported translation provider: '{config.translation.provider}'")

    return PhraseTranslationSession(provider, config)