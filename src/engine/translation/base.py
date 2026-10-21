"""
Base abstractions for text translation and incremental sentence caching.
Maintains per-phrase translation cache so only the active tail (last 1-2 sentences)
is re-translated on live ASR updates.
"""

from __future__ import annotations
import re
from abc import ABC, abstractmethod
from typing import Dict, List

from core.config import EngineConfig


class TranslationProvider(ABC):
    @abstractmethod
    def generate(self, prompt: str) -> str:
        pass


class PhraseTranslationSession:
    _SENTENCE_SPLIT_REGEX = re.compile(r"(?<=[.?!:])\s+")
    _WHITESPACE_REGEX = re.compile(r"\s+")

    def __init__(self, provider: TranslationProvider, config: EngineConfig):
        self._provider = provider
        self._trans_cfg = config.translation
        self._caches: Dict[int, List[str]] = {}

    def translate_phrase(self, phrase_id: int, full_text: str) -> str:
        cleaned = full_text.strip()
        if not cleaned:
            return ""

        sentences = [s for s in self._SENTENCE_SPLIT_REGEX.split(cleaned) if s]
        total_sentences = len(sentences)

        cache = self._caches.setdefault(phrase_id, [])

        for i in range(len(cache), total_sentences - 2):
            translated_sentence = self._translate_segment(sentences[i])
            cache.append(translated_sentence)

        if total_sentences >= 2:
            active_tail = " ".join(sentences[-2:])
        elif total_sentences == 1:
            active_tail = sentences[0]
        else:
            active_tail = ""

        translated_tail = self._translate_segment(active_tail) if active_tail else ""
        combined = " ".join(cache + [translated_tail])
        return self._WHITESPACE_REGEX.sub(" ", combined).strip()

    def clear_phrase(self, phrase_id: int) -> None:
        self._caches.pop(phrase_id, None)

    def reset_all(self) -> None:
        self._caches.clear()

    def _translate_segment(self, text: str) -> str:
        prompt = self._trans_cfg.prompt_template.format(
            source_lang=self._trans_cfg.source_language,
            target_lang=self._trans_cfg.target_language,
            text=text.strip(),
        )
        raw_output = self._provider.generate(prompt)
        return self._clean_output(raw_output)

    def _clean_output(self, output: str) -> str:
        text = output.strip()
        if not self._trans_cfg or not text:
            return text

        # Strip <think>...</think> blocks if reasoning budget is disabled
        text = re.sub(r"<think>[\s\S]*?</think>", "", text).strip()
        first_line = text.split("\n")[0].strip()
        return first_line