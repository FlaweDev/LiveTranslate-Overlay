"""
NeMo model loader and runtime environment patcher.
Handles logging suppression, Windows/Python compatibility patches,
CUDA graph disabling, and loading local .nemo archives.
"""

from __future__ import annotations
import datetime
import logging
import os
import sys
import tarfile
import warnings
from pathlib import Path
from typing import Tuple
import torch


def apply_runtime_patches(quiet: bool = True) -> None:
    if quiet:
        os.environ["NEMO_LOGGING_LEVEL"] = "ERROR"
        os.environ["HYDRA_FULL_ERROR"] = "0"
        os.environ["TOKENIZERS_PARALLELISM"] = "false"
        logging.getLogger().setLevel(logging.CRITICAL)
        logging.disable(logging.WARNING)
        warnings.filterwarnings("ignore")

        for name in [
            "nemo",
            "nemo.collections",
            "nemo_logger",
            "pytorch_lightning",
            "omegaconf",
            "hydra",
            "transformers",
        ]:
            logger = logging.getLogger(name)
            logger.setLevel(logging.CRITICAL)
            logger.disabled = True

    if sys.version_info < (3, 12):
        old_extract = tarfile.TarFile.extract

        def safe_extract(self, member, path="", set_attrs=True, *, filter=None):
            return old_extract(self, member, path, set_attrs=set_attrs)

        tarfile.TarFile.extract = safe_extract


def patch_nemo_windows_datetime() -> None:
    import nemo.collections.asr.parts.utils.diarization_utils as diar_utils

    class SafeDatetime(datetime.datetime):
        @classmethod
        def fromtimestamp(cls, timestamp, tz=None):
            return super().fromtimestamp(max(0.0, timestamp), tz)

    diar_utils.datetime = SafeDatetime


def resolve_torch_device(device_pref: str) -> torch.device:
    if device_pref.lower() == "cuda" and torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def resolve_amp_dtype(compute_type: str, device: torch.device) -> torch.dtype:
    if device.type != "cuda":
        return torch.float32
    mapping = {
        "float16": torch.float16,
        "bfloat16": torch.bfloat16,
        "float32": torch.float32,
    }
    return mapping.get(compute_type.lower(), torch.float16)


def load_nemo_models(
    asr_path: Path,
    diar_path: Path,
    device: torch.device,
) -> Tuple[object, object]:
    patch_nemo_windows_datetime()
    from nemo.collections.asr.models import ASRModel, SortformerEncLabelModel

    if not diar_path.exists():
        raise FileNotFoundError(f"Diarization model not found at: {diar_path}")
    if not asr_path.exists():
        raise FileNotFoundError(f"ASR model not found at: {asr_path}")

    diar_model = SortformerEncLabelModel.restore_from(
        restore_path=str(diar_path),
        map_location=device,
    ).eval()

    asr_model = ASRModel.restore_from(
        restore_path=str(asr_path),
        map_location=device,
    ).eval()

    disable_cuda_graphs(asr_model)
    return diar_model, asr_model


def disable_cuda_graphs(asr_model: object) -> None:
    from omegaconf import open_dict

    if hasattr(asr_model, "cfg") and hasattr(asr_model.cfg, "decoding"):
        if hasattr(asr_model.cfg.decoding, "greedy"):
            with open_dict(asr_model.cfg.decoding.greedy):
                asr_model.cfg.decoding.greedy.use_cuda_graph_decoder = False

    def _disable_on_decoder(decoder_container: object) -> None:
        if not hasattr(decoder_container, "decoding"):
            return
        dc = getattr(decoder_container.decoding, "decoding", None)
        if dc is None:
            return
        if hasattr(dc, "use_cuda_graph_decoder"):
            dc.use_cuda_graph_decoder = False
        dcomp = getattr(dc, "decoding_computer", None)
        if dcomp is not None:
            if hasattr(dcomp, "allow_cuda_graphs"):
                dcomp.allow_cuda_graphs = False
            if hasattr(dcomp, "disable_cuda_graphs"):
                dcomp.disable_cuda_graphs()
            if hasattr(dcomp, "cuda_graphs_mode"):
                dcomp.cuda_graphs_mode = None

    _disable_on_decoder(asr_model)
    for attr in ["joint", "joint_0", "joint_1", "joint_2", "joint_3"]:
        if hasattr(asr_model, attr):
            _disable_on_decoder(getattr(asr_model, attr))