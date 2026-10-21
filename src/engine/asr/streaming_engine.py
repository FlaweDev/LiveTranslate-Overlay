"""
Real-time multi-speaker streaming ASR and diarization engine.
"""

from __future__ import annotations
from dataclasses import dataclass
from typing import Callable, Dict
import torch
from omegaconf import OmegaConf

from asr.audio_capture import LoopbackAudioCapture
from asr.model_loader import (
    load_nemo_models,
    resolve_amp_dtype,
    resolve_torch_device,
)
from core.config import EngineConfig
from core.models_manager import ModelsManager


@dataclass(frozen=True)
class TranscriptSegmentEvent:
    speaker_index: int
    start_time: float
    end_time: float
    text: str


SegmentCallback = Callable[[TranscriptSegmentEvent], None]
ResetCallback = Callable[[], None]


class StreamingAsrEngine:
    def __init__(
        self,
        config: EngineConfig,
        models_manager: ModelsManager,
        on_segment: SegmentCallback,
        on_reset: ResetCallback | None = None,
    ):
        self._config = config
        self._models_manager = models_manager
        self._on_segment = on_segment
        self._on_reset = on_reset or (lambda: None)

        self._device = resolve_torch_device(config.asr_params.device)
        self._amp_dtype = resolve_amp_dtype(config.asr_params.compute_type, self._device)
        self._audio_capture = LoopbackAudioCapture(sample_rate=16000)

        self._streamer_cfg = self._build_streamer_config()
        self._diar_model = None
        self._asr_model = None
        self._streamer = None
        self._is_running = False
        self._total_time_offset = 0.0

    def initialize(self) -> None:
        from nemo.collections.asr.parts.utils.multispk_transcribe_utils import SpeakerTaggedASR

        asr_path = self._models_manager.resolve_model_path(self._config.asr_model)
        diar_path = self._models_manager.resolve_model_path(self._config.diar_model)

        self._diar_model, self._asr_model = load_nemo_models(
            asr_path=asr_path,
            diar_path=diar_path,
            device=self._device,
        )
        self._streamer = SpeakerTaggedASR(
            self._streamer_cfg,
            self._asr_model,
            self._diar_model,
        )

    def run(self) -> None:
        if self._asr_model is None:
            self.initialize()

        self._audio_capture.start()
        self._is_running = True

        step_num = 0
        idle_steps = 0
        session_samples = 0
        last_spk_text: Dict[int, str] = {}
        samples = [{"audio_filepath": "live_stream"}]
        idle_threshold = self._config.asr_params.idle_steps_threshold

        try:
            while self._is_running:
                audio_chunk = self._audio_capture.get_chunk(timeout=1.0)
                if audio_chunk is None:
                    continue

                session_samples += len(audio_chunk)
                audio_tensor = torch.from_numpy(audio_chunk).unsqueeze(0).to(self._device)
                lengths_tensor = torch.tensor(
                    [audio_tensor.shape[1]], dtype=torch.int32, device=self._device
                )

                drop_extra = (
                    0
                    if step_num == 0 and not self._streamer_cfg.pad_and_drop_preencoded
                    else self._asr_model.encoder.streaming_cfg.drop_extra_pre_encoded
                )

                with torch.inference_mode(), torch.amp.autocast(
                    self._device.type,
                    dtype=self._amp_dtype,
                    enabled=(self._device.type == "cuda"),
                ):
                    processed_sig, processed_len = self._asr_model.preprocessor(
                        input_signal=audio_tensor,
                        length=lengths_tensor,
                    )
                    self._streamer.perform_parallel_streaming_stt_spk(
                        step_num=step_num,
                        chunk_audio=processed_sig,
                        chunk_lengths=processed_len,
                        is_buffer_empty=False,
                        drop_extra_pre_encoded=drop_extra,
                    )

                self._streamer.generate_seglst_dicts_from_parallel_streaming(samples=samples)
                results = self._streamer.instance_manager.seglst_dict_list

                current_spk_text, last_segment = self._extract_latest_segments(results)
                text_updated = False

                for spk_idx, text in current_spk_text.items():
                    if last_spk_text.get(spk_idx) != text:
                        text_updated = True
                        seg = last_segment[spk_idx]
                        self._on_segment(
                            TranscriptSegmentEvent(
                                speaker_index=spk_idx,
                                start_time=seg["start_time"] + self._total_time_offset,
                                end_time=seg["end_time"] + self._total_time_offset,
                                text=text,
                            )
                        )

                if text_updated:
                    idle_steps = 0
                    last_spk_text = current_spk_text
                else:
                    idle_steps += 1
                    if idle_steps >= idle_threshold:
                        self._on_reset()
                        self._reset_streaming_state()
                        self._total_time_offset += session_samples / float(self._audio_capture.sample_rate)
                        step_num = 0
                        idle_steps = 0
                        session_samples = 0
                        last_spk_text.clear()
                        continue

                step_num += 1
        finally:
            self._audio_capture.stop()

    def stop(self) -> None:
        self._is_running = False
        self._audio_capture.stop()

    def _reset_streaming_state(self) -> None:
        from nemo.collections.asr.parts.utils.multispk_transcribe_utils import SpeakerTaggedASR

        if hasattr(self._asr_model.encoder, "reset_streaming_cache"):
            self._asr_model.encoder.reset_streaming_cache()
        self._streamer = SpeakerTaggedASR(
            self._streamer_cfg,
            self._asr_model,
            self._diar_model,
        )

    def _build_streamer_config(self) -> object:
        asr_p = self._config.asr_params
        diar_p = self._config.diar_params
        return OmegaConf.create({
            "audio_file": "live_stream",
            "manifest_file": None,
            "output_path": "live_output.json",
            "online_normalization": True,
            "pad_and_drop_preencoded": True,
            "att_context_size": list(asr_p.att_context_size),
            "single_speaker_mode": asr_p.max_speakers <= 1,
            "max_num_of_spks": asr_p.max_speakers,
            "binary_diar_preds": diar_p.binary_diar_preds,
            "batch_size": 1,
            "fix_prev_words_count": 5,
            "update_prev_words_sentence": 10,
            "ignored_initial_frame_steps": 2,
            "word_window": 8,
            "verbose": True,
            "cache_gating": diar_p.cache_gating,
            "cache_gating_buffer_size": diar_p.cache_gating_buffer_size,
            "masked_asr": False,
        })

    @staticmethod
    def _extract_latest_segments(results: list) -> tuple[Dict[int, str], Dict[int, dict]]:
        current_spk_text: Dict[int, str] = {}
        last_segment: Dict[int, dict] = {}

        for segment in results:
            speaker_id = segment.get("speaker", "speaker_0")
            spk_idx = int(speaker_id.split("_")[-1]) if "_" in speaker_id else 0
            text = segment.get("words", "").strip()
            if text:
                last_segment[spk_idx] = segment
                current_spk_text[spk_idx] = text

        return current_spk_text, last_segment