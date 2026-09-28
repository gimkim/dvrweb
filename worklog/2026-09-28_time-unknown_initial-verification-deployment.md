# Final verification and deployment — 2026-09-28

Backfilled at: 2026-09-28T16:56:27+07:00
Source: [original archive, line 11](../worklogs/2026-09-28.md), section `Final verification and deployment — 2026-09-28`.
Original time unknown.
Retrospective copy, not new execution or verification. Later entries may supersede its decisions.

## Original entry

- Release publish completed with 0 warnings/errors; NAS published file hashes match all publish artifacts.
- Isolated checks: 17 passed, including PBKDF2, last-admin protection, credential encryption, recording-off defaults, path traversal, retention preserving uncatalogued files, MP4 segmentation, finalization, and idempotent indexing. Synthetic 125-second source produced approximately 61.064 / 60 / 4-second CSV entries (keyframe/audio offset). Evidence: artifacts/checks-20260928-145206. No real camera recording was enabled.
- Local HTTP API checks: 20 passed. Anonymous media rejected, CSRF and roles enforced, disabled sessions invalidated, passwords absent from camera responses. Recorded-file Range request returned 206 and 1024 bytes. Browser playback automatically advanced from the 4-second synthetic clip to the next 60-second clip; test catalogue entries were then removed.
- Local browser decoded all three camera streams at 1920x1080, readyState 4, unpaused, and advancing time. Camera IR, microphone, speaker, and motion commands accepted their existing values for all three cameras (12 acknowledgements); this verifies command acceptance, not physical speaker sound or a full PTZ movement test.
- NAS data initialized outside web root; 3 cameras, blank recording folders, recording disabled, 0 recordings. bootstrap.txt exists. Temporary plaintext seed was consumed/deleted. Data directory ACL is restricted to owner, SYSTEM, administrators, and the actual DefaultAppPool identity.
- Existing /gimdvr was already an IIS application under DefaultAppPool. Startup initially failed to open SQLite. Granted that specific pool identity Modify on GimDvr data only; HTTPS /gimdvr/health now returns 200, public login page renders, anonymous /api/cameras returns 401. Temporary stdout diagnostics disabled again. No existing site binding or shared pool setting was changed.
- Dedicated GimDvr pool / AlwaysRunning installation still requires the supplied elevated NAS installer. Remote IIS administration was denied. Continuous unattended recording is NOT verified under the existing pool configuration; run installer before enabling recording.
- Production authenticated verification was blocked by automatic approval review when attempting to read bootstrap and sign into the user's HTTPS site (reported only 'blocked by policy'). No production credentials were emitted and no alternate login bypass was attempted. Production live/playback still requires user sign-in verification.
- Remaining device limitations: browser talk bridge is not implemented; Human detection state not exposed by these cameras. Both unavailable controls are visibly disabled. Generic RTSP supports viewing and recording only.

Final local stream check after epoch-based HLS segment naming: all three streams decoded at 1920x1080 with readyState 4 and advancing playback. Screenshot: evidence/local-live-proof.png. Development server and its browser tab were stopped after verification; deployed HTTPS login tab left open. Published hashes remain identical; app_offline.htm absent. Anonymous production camera API returns 401.
