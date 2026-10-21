#!/usr/bin/env python3
"""
CLI utility for offline audio file transcription and speaker diarization.
Reuses core configuration, model management, and NeMo loader modules.
"""

from __future__ import annotations
import argparse
import os
import sys
import tempfile
from pathlib import Path
import numpy as np
import soundfile as sf
import torch
from omegaconf import OmegaConf

from asr.model_loader import apply_runtime_patches, load_nemo_models, resolve_torch_device
from core.config import EngineConfig
from core.models_manager import ModelsManager


def preprocess_audio_to_16k_mono(audio_path: Path) -> tuple[Path, bool]:
    data, sample_rate = sf.read(str(audio_path))
    modified = False

    if data.ndim > 1 and data.shape[1] > 1:
        data = np.mean(data, axis=1)
        modified = True

    if sample_rate != 16000:
        import resampy
        data = resampy.resample(data, sample_rate, 16000)
        modified = True

    if modified:
        tmp = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
        sf.write(tmp.name, data.astype(np.float32), 16000)
        return Path(tmp.name), True

    return audio_path, False


def transcribe_file(
    audio_path: Path,
    config: EngineConfig,
    models_manager: ModelsManager,
    output_json: Path,
) -> list[dict]:
    from nemo.collections.asr.parts.utils.multispk_transcribe_utils import SpeakerTaggedASR
    from nemo.collections.asr.parts.utils.streaming_utils import CacheAwareStreamingAudioBuffer

    device = resolve_torch_device(config.asr_params.device)
    diar_model, asr_model = load_nemo_models(
        asr_path=models_manager.resolve_model_path(config.asr_model),
        diar_path=models_manager.resolve_model_path(config.diar_model),
        device=device,
    )

    cfg = OmegaConf.create({
        "audio_file": str(audio_path),
        "manifest_file": None,
        "output_path": str(output_json),
        "online_normalization": True,
        "pad_and_drop_preencoded": True,
        "att_context_size": list(config.asr_params.att_context_size),
        "single_speaker_mode": False,
        "max_num_of_spks": config.asr_params.max_speakers,
        "binary_diar_preds": config.diar_params.binary_diar_preds,
        "batch_size": 1,
        "fix_prev_words_count": 5,
        "update_prev_words_sentence": 10,
        "ignored_initial_frame_steps": 2,
        "word_window": 8,
        "verbose": False,
        "cache_gating": config.diar_params.cache_gating,
        "cache_gating_buffer_size": config.diar_params.cache_gating_buffer_size,
        "masked_asr": False,
    })

    buffer = CacheAwareStreamingAudioBuffer(
        model=asr_model,
        online_normalization=cfg.online_normalization,
        pad_and_drop_preencoded=cfg.pad_and_drop_preencoded,
    )
    buffer.append_audio_file(audio_filepath=str(audio_path), stream_id=-1)
    streamer = SpeakerTaggedASR(cfg, asr_model, diar_model)

    for step_num, (chunk_audio, chunk_lengths) in enumerate(buffer):
        drop_extra = (
            0
            if step_num == 0 and not cfg.pad_and_drop_preencoded
            else asr_model.encoder.streaming_cfg.drop_extra_pre_encoded
        )
        with torch.inference_mode(), torch.amp.autocast(device.type, enabled=(device.type == "cuda")):
            streamer.perform_parallel_streaming_stt_spk(
                step_num=step_num,
                chunk_audio=chunk_audio,
                chunk_lengths=chunk_lengths,
                is_buffer_empty=buffer.is_buffer_empty(),
                drop_extra_pre_encoded=drop_extra,
            )

    streamer.generate_seglst_dicts_from_parallel_streaming(
        samples=[{"audio_filepath": str(audio_path)}]
    )
    return streamer.instance_manager.seglst_dict_list


def main() -> None:
    parser = argparse.ArgumentParser(description="Offline Multi-Speaker Audio Transcription CLI")
    parser.add_argument("--audio", "-a", type=Path, required=True, help="Input audio file")
    parser.add_argument("--config", "-c", type=Path, default=Path("../../config/appsettings.json"))
    parser.add_argument("--output", "-o", type=Path, default=Path("output_transcription.json"))
    parser.add_argument("--quiet", "-q", action="store_true", help="Suppress NeMo logs")
    args = parser.parse_args()

    apply_runtime_patches(quiet=args.quiet)

    if not args.audio.exists():
        print(f"Audio file not found: {args.audio}", file=sys.stderr)
        sys.exit(1)

    config = EngineConfig.load(args.config)
    models_mgr = ModelsManager(config)

    missing = models_mgr.find_missing_models()
    if missing:
        print("Downloading missing models...")
        models_mgr.download_models(missing)

    processed_path, is_temp = preprocess_audio_to_16k_mono(args.audio)
    try:
        segments = transcribe_file(processed_path, config, models_mgr, args.output)
        for seg in segments:
            spk = seg.get("speaker", "speaker_0")
            start = seg.get("start_time", 0.0)
            end = seg.get("end_time", 0.0)
            words = seg.get("words", "")
            print(f"[{spk}] ({start:.2f}s - {end:.2f}s): {words}")
    finally:
        if is_temp and processed_path.exists():
            os.unlink(processed_path)


if __name__ == "__main__":
    main()