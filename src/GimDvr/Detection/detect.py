"""Bounded offline clip analysis. JSON lines on stdin/stdout; never contacts cameras."""
import argparse
import json
import os
import subprocess
import sys
import time
import contextlib

os.environ.setdefault("OMP_NUM_THREADS", "1")
import cv2
import numpy as np

cv2.setNumThreads(1)
WIDTH, HEIGHT, FPS = 544, 320, 2


class Motion:
    def __init__(self):
        self.background = None
        self.hits = 0
        self.cooldown = 0

    def update(self, frame):
        gray = cv2.cvtColor(cv2.resize(frame, (160, 96)), cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (5, 5), 0).astype(np.float32)
        if self.background is None:
            self.background = gray
            return False
        delta = gray - self.background
        # Ignore uniform exposure/IR brightness shifts, reset large scene changes (including PTZ).
        delta -= np.median(delta)
        mask = (np.abs(delta) > 18).astype(np.uint8)
        if mask.mean() > 0.45:
            self.background = gray
            self.cooldown = 3
            self.hits = 0
            return False
        cv2.accumulateWeighted(gray, self.background, 0.12)
        if self.cooldown:
            self.cooldown -= 1
            return False
        count, _, stats, _ = cv2.connectedComponentsWithStats(mask)
        active = count > 1 and int(stats[1:, cv2.CC_STAT_AREA].max()) >= 18
        self.hits = self.hits + 1 if active else 0
        return self.hits >= 2


class Person:
    def __init__(self, model, device):
        self.compiled = None
        self.device = None
        self.error = None
        try:
            import openvino as ov
            core = ov.Core()
            for target in dict.fromkeys([device, "CPU"]):
                try:
                    options = {"PERFORMANCE_HINT": "LATENCY", "NUM_STREAMS": "1"}
                    if target == "CPU":
                        options["INFERENCE_NUM_THREADS"] = 1
                    self.compiled = core.compile_model(model, target, options)
                    self.device = target
                    break
                except Exception:
                    pass
            if self.compiled is None:
                self.error = "model_unavailable"
        except Exception:
            self.error = "runtime_unavailable"

    def score(self, frame):
        tensor = frame.transpose(2, 0, 1)[None].astype(np.float32)
        rows = self.compiled([tensor])[self.compiled.output(0)].reshape(-1, 7)
        people = rows[(rows[:, 0] >= 0) & (rows[:, 1] == 1)]
        return float(people[:, 2].max()) if len(people) else 0.0


class Summary:
    def __init__(self, person):
        self.detector = Motion()
        self.person = person
        self.frames = self.samples = self.person_hits = 0
        self.motion = self.human = False
        self.confidence = 0.0
        self.last_person_frame = -100
        self.recent_person = False
        self.human_error = person.error

    def update(self, frame):
        moving = self.detector.update(frame)
        self.motion |= moving
        self.frames += 1
        # 1fps when moving/recently occupied; idle scan every5s, including the first frame.
        interval = FPS if moving or self.recent_person else FPS * 5
        if self.person.compiled is not None and not self.human_error and self.frames - self.last_person_frame >= interval:
            self.last_person_frame = self.frames
            try:
                score = self.person.score(frame)
                self.samples += 1
                self.confidence = max(self.confidence, score)
                self.person_hits = self.person_hits + 1 if score >= 0.60 else 0
                self.recent_person = score >= 0.45
                self.human |= self.person_hits >= 2
            except Exception:
                self.human_error = "inference_failed"

    def result(self, duration, decoder, decode_ok):
        # Accept rounding by at most one sample; missing/truncated tails must never become negatives.
        complete = decode_ok and self.frames >= max(2, int(duration * FPS) - 1)
        human_complete = complete and self.samples >= 2 and not self.human_error
        state = "complete" if complete and human_complete else "partial" if self.frames else "error"
        return dict(state=state, motion=True if self.motion else False if complete else None,
                    human=True if self.human else False if human_complete else None,
                    frames=self.frames, humanSamples=self.samples, confidence=round(self.confidence, 4),
                    device=self.person.device, decoder=decoder,
                    error=self.human_error or (None if complete else "decode_incomplete"), version=getattr(self.person, "version", "nas-person-v1"))


def decode(ffmpeg, path, duration, person, hardware, decoder="d3d11va", readrate=4):
    args = [ffmpeg, "-hide_banner", "-loglevel", "error", "-xerror", "-nostdin", "-threads", "1", "-filter_threads", "1"]
    if readrate > 0:
        args += ["-readrate", str(readrate)]
    if hardware:
        args += ["-hwaccel", decoder]
    args += ["-i", path, "-t", str(min(duration + 1, 180)), "-map", "0:v:0", "-an", "-sn", "-dn",
             "-vf", f"fps={FPS},scale={WIDTH}:{HEIGHT}:flags=fast_bilinear", "-pix_fmt", "bgr24", "-threads", "1", "-f", "rawvideo", "pipe:1"]
    flags = subprocess.CREATE_NO_WINDOW | subprocess.BELOW_NORMAL_PRIORITY_CLASS if os.name == "nt" else 0
    summary = Summary(person)
    process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, creationflags=flags)
    try:
        frame_bytes = WIDTH * HEIGHT * 3
        while True:
            data = process.stdout.read(frame_bytes)
            if len(data) != frame_bytes:
                break
            summary.update(np.frombuffer(data, dtype=np.uint8).reshape(HEIGHT, WIDTH, 3))
        code = process.wait(timeout=15)
        return summary.result(duration, decoder if hardware else "cpu-1thread", code == 0)
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()
        process.stdout.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--ffmpeg", required=True)
    parser.add_argument("--device", default="GPU")
    parser.add_argument("--backend", choices=["openvino", "onnx"], default="openvino")
    parser.add_argument("--decoder", choices=["d3d11va", "cuda", "cpu"], default="d3d11va")
    parser.add_argument("--readrate", type=float, default=4)
    options = parser.parse_args()
    with contextlib.redirect_stdout(sys.stderr):
        if options.backend == "onnx":
            sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
            from onnx_person import OnnxPerson
            person = OnnxPerson(options.model, options.device)
        else:
            person = Person(options.model, options.device)
    hardware = os.name == "nt" and options.decoder != "cpu"
    for line in sys.stdin:
        try:
            request = json.loads(line)
            if request.get("kind") == "health":
                if person.compiled is not None and not person.error:
                    with contextlib.redirect_stdout(sys.stderr):
                        person.score(np.zeros((HEIGHT, WIDTH, 3), np.uint8))
                print(json.dumps(dict(ready=person.compiled is not None and not person.error, device=person.device)), flush=True)
                continue
            duration = float(request["duration"])
            if not 0 < duration <= 180:
                print(json.dumps(dict(state="error", error="invalid_duration")), flush=True)
                continue
            if not os.path.isfile(request["path"]):
                print(json.dumps(dict(state="error", error="file_unavailable")), flush=True)
                continue
            result = decode(options.ffmpeg, request["path"], duration, person, hardware, options.decoder, request.get("readrate", options.readrate))
            if hardware and result["frames"] == 0:
                hardware = False  # Do not repeatedly initialize a broken device for every clip.
                result = decode(options.ffmpeg, request["path"], duration, person, False, options.decoder, request.get("readrate", options.readrate))
        except Exception:
            result = dict(state="error", error="analysis_failed")
        print(json.dumps(result, allow_nan=False), flush=True)


if __name__ == "__main__":
    main()
