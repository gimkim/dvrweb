"""Synthetic frames/fixtures only. No private footage or cameras."""
import importlib.util
import pathlib
import unittest
import numpy as np

spec = importlib.util.spec_from_file_location("detect", pathlib.Path(__file__).parents[1] / "src/GimDvr/Detection/detect.py")
d = importlib.util.module_from_spec(spec)
spec.loader.exec_module(d)


class FakePerson:
    device = "fixture"
    error = None
    compiled = True

    def __init__(self, scores):
        self.scores = iter(scores)

    def score(self, frame):
        return next(self.scores, 0)


class Checks(unittest.TestCase):
    def setUp(self):
        self.black = np.zeros((d.HEIGHT, d.WIDTH, 3), np.uint8)

    def test_static_and_exposure_not_motion(self):
        motion = d.Motion()
        for level in [0, 0, 30, 30, 120, 120]:
            self.assertFalse(motion.update(np.full_like(self.black, level)))

    def test_local_motion_persists(self):
        summary = d.Summary(FakePerson([]))
        summary.update(self.black)
        for x in range(30, 130, 10):
            frame = self.black.copy()
            frame[60:140, x:x+40] = 255
            summary.update(frame)
        self.assertTrue(summary.motion)
        self.assertFalse(summary.human)

    def test_person_needs_repeated_evidence_including_idle(self):
        summary = d.Summary(FakePerson([0.8, 0.9]))
        summary.update(self.black)
        self.assertFalse(summary.human)
        summary.update(self.black)
        summary.update(self.black)
        self.assertTrue(summary.human)
        self.assertFalse(summary.motion)

    def test_one_person_false_positive_not_confirmed(self):
        summary = d.Summary(FakePerson([0.8, 0.0]))
        for _ in range(10): summary.update(self.black)
        self.assertFalse(summary.human)

    def test_truncated_or_unavailable_never_false_negative(self):
        summary = d.Summary(FakePerson([]))
        summary.update(self.black)
        result = summary.result(60, "fixture", False)
        self.assertIsNone(result["motion"])
        self.assertIsNone(result["human"])
        unavailable = FakePerson([])
        unavailable.compiled = None
        unavailable.error = "model_unavailable"
        summary = d.Summary(unavailable)
        for _ in range(20): summary.update(self.black)
        result = summary.result(10, "fixture", True)
        self.assertEqual(result["state"], "partial")
        self.assertFalse(result["motion"])
        self.assertIsNone(result["human"])

    def test_scene_change_suppression(self):
        rng = np.random.default_rng(1)
        old = np.repeat(rng.integers(0, 2, (32, 54, 1), dtype=np.uint8)*255, 3, axis=2)
        old = d.cv2.resize(old, (d.WIDTH,d.HEIGHT), interpolation=d.cv2.INTER_NEAREST)
        motion = d.Motion()
        motion.update(old)
        for _ in range(3): self.assertFalse(motion.update(255-old))

    def test_complete_sample_negative(self):
        summary = d.Summary(FakePerson([]))
        for _ in range(20): summary.update(self.black)
        result = summary.result(10, "fixture", True)
        self.assertEqual(result["state"], "complete")
        self.assertFalse(result["motion"])
        self.assertFalse(result["human"])
        self.assertEqual(result["humanSamples"], 2)


if __name__ == "__main__": unittest.main()
