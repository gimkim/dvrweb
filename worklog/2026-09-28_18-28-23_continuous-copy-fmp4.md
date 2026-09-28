# Continuous copy-only overview — 1.6.0

Recorded: 2026-09-28 18:28:23 Asia/Bangkok (+07:00), after implementation and deployment.

Request: deliver TS or short fMP4 incrementally instead of waiting for the entire keyframe interval. Supersedes the overview HLS buffer patch and implements the design discussed in [previous entry](2026-09-28_18-12-29_keyframe-start-latency-design.md).

## Implementation

- Existing RTSP reader now also writes video-copy, silent fragmented MP4 to stdout, with extract_extradata and 200000us fragment duration. No new camera input or video encoder. HLS copy remains for snapshots; recording and focus QSV arguments remain unchanged.
- FragmentCache parses init and moof/mdat packets, identifies random-access fragments, and atomically publishes a shared disk cache bounded to128fragments/64MiB. Failed parsing/writing drains stdout to protect the existing recorder from a blocked pipe.
- Authenticated copy-stream endpoint uses length-prefixed packets and flushes after each packet with response buffering disabled. New viewers wait for the next future keyframe, then receive contiguous dependent fragments. Slow/missing/restarted streams reconnect. User enabled state/stamp and camera enabled state are checked every2seconds.
- New copy-stream.js consumes fetch streaming with MediaSource. Initial reserve0.45s, live target0.5s, starvation reserve0.4s, catchup1.05x, seek only beyond2s or a missing range. Stop/navigation aborts fetch and pending work. Overview has no added controls. Focus remains existing QSV/HLS.
- New script is loaded through automatic content-hash asset versioning. Existing Android WebView app receives it on reopen; no APK rebuild.
- Updated Program version, reader lifecycle, app.js routing, index.html, AGENT_NOTES.md and README.md. Added stream and packet tests; retained lifecycle tests.

## Validation

- Existing .NET functional suite:36checks passed, artifacts/checks-20260928-182351.
- New stream suite:16checks passed, artifacts/copy-checks-20260928-182519. Synthetic H.264 TS10seconds/GOP4seconds/Bframes2 generated50fragments. Gated byte reads proved first2fragments publish before the next keyframe/EOF. Full and midstream-keyframe decoded frame hashes match the original. Simultaneous overview HLS, fMP4 and recording outputs preserve decoded recording frames. Cache bounds, malformed input, cancellation, framing, future-keyframe gating and direct-handler auth-stamp revocation checked.
- Initial MP4-input fixture was corrected to Annex-B TS to match the camera transport expected by extract_extradata. Atomic cache replacement retries transient file-sharing errors; fixture wait allows completion of index publication.
- JavaScript packet/codec checks4passed; session lifecycle checks6passed. Syntax checks passed. Web and worker Release publish succeeded.
- No production HTTP calls, real web UI, browser harness or physical Android testing. Functional fixtures are not evidence of real camera-to-screen delay or prolonged playback smoothness.

## Deployment

Deployed19changed files to the existing NAS web and worker artifact folders using app_offline. Backup: `\\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-copy-fragments-20260928-182805`. Preserved appsettings hashes and external DB/keys/camera settings; app_offline removed. Verified source/published versus deployed SHA256 for web DLL, app.js, index.html, copy-stream.js and worker DLLs. Independent service was not activated; current web-owner restart can interrupt recording briefly.

No actual latency guarantee: source B-frame/camera buffering, IIS/proxy transport and browser decoding can add delay. Startup still waits for a camera keyframe. H.264 and MediaSource support are required. Disk-cache IO and real playback performance have not been benchmarked. Normal reload/reopen loads this release. Source is prepared for commit/push to the existing public dvrweb repository; private runtime artifacts and fixtures remain ignored.
