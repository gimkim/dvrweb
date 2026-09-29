# Local-only detection (1.10.5)

User requests removing DVR use of Remote MotionService because local NAS detection is fast enough. DetectionService no longer constructs the remote client, starts its health polling, uploads recordings, or waits for its request gate. Every lane uses the local detector directly. Both web/worker entrypoints stop loading detection-remote.json. Missing local runtime leaves jobs pending. Existing SQLite results, worker/readrate settings and external files preserved. Admin text no longer mentions remote queue behavior. Standalone MotionService installation and historical helper/tests are not uninstalled; DVR has no execution path to them.

Validation:6actual synthetic OpenVINO integration checks include parallel local jobs, dynamic worker settings, result persistence, shutdown/diagnostics and deliberately invalid legacy remote URL/key being ignored with no remote events.3admin UI/code checks pass. Web/worker Release publish succeeded. Deployment uses existing backup/hash/config-preserving procedure; no browser/device tests. Source/notes/worklog committed/pushed.

NAS deployment completed with matching hashes/config preservation; backup-detection-20260929-111511. No remote service uninstall performed.
