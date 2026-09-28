"""YOLOX-tiny COCO person score with ONNX Runtime CUDA and CPU fallback.

Only presence scores are used: objectness * COCO class0 probability. Box decoding
and NMS cannot change the maximum class score and are unnecessary for this label.
Model/input contract: official YOLOX 0.1.1rc0 tiny, BGR 0..255, CHW 416x416.
"""
import contextlib
import sys
import os
import cv2
import numpy as np


def tensor_for(frame):
    h, w = frame.shape[:2]
    ratio = min(416 / h, 416 / w)
    resized = cv2.resize(frame, (int(w * ratio), int(h * ratio)))
    padded = np.full((416, 416, 3), 114, dtype=np.uint8)
    padded[:resized.shape[0], :resized.shape[1]] = resized
    return np.ascontiguousarray(padded.transpose(2, 0, 1)[None], dtype=np.float32)


def person_score(output):
    rows = np.asarray(output).reshape(-1, 85)
    scores = rows[:, 4] * rows[:, 5]
    if not np.isfinite(scores).all():
        raise ValueError("invalid scores")
    return float(np.clip(scores.max(initial=0), 0, 1))


class OnnxPerson:
    version = "remote-yolox-tiny-v1"

    def __init__(self, model, device):
        self.compiled = None
        self.device = None
        self.error = None
        self.model = model
        try:
            import onnxruntime as ort
            self.ort = ort
            self.options = ort.SessionOptions()
            self.options.intra_op_num_threads = 2
            self.options.inter_op_num_threads = 1
            self.options.log_severity_level = 3
            if device.upper() != "CPU":
                try:
                    with contextlib.redirect_stdout(sys.stderr):
                        directory = os.environ.get("MOTION_CUDA_DLL_DIRECTORY", "")
                        if directory and os.name == "nt":
                            self.dll_directory = os.add_dll_directory(directory)
                        ort.preload_dlls(directory=directory)
                    self.compiled = ort.InferenceSession(model, sess_options=self.options,
                        providers=[("CUDAExecutionProvider", {"device_id": 0, "gpu_mem_limit": 1073741824, "cudnn_conv_algo_search": "HEURISTIC"}), "CPUExecutionProvider"])
                    self.device = "CUDA" if "CUDAExecutionProvider" in self.compiled.get_providers() else "CPU"
                    self.score(np.zeros((320, 544, 3), np.uint8))
                except Exception:
                    self.compiled = None
            if self.compiled is None:
                self.cpu()
        except Exception:
            self.error = "model_unavailable"
            self.compiled = None

    def cpu(self):
        self.compiled = self.ort.InferenceSession(self.model, sess_options=self.options, providers=["CPUExecutionProvider"])
        self.device = "CPU"

    def score(self, frame):
        tensor = tensor_for(frame)
        try:
            output = self.compiled.run(None, {self.compiled.get_inputs()[0].name: tensor})[0]
        except Exception:
            if self.device != "CUDA":
                raise
            self.cpu()
            output = self.compiled.run(None, {self.compiled.get_inputs()[0].name: tensor})[0]
        return person_score(output)
