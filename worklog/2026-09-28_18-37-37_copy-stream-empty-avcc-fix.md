# Copy stream waits indefinitely: empty AVC initialization — 1.6.1

Recorded 2026-09-28 18:37:37 Asia/Bangkok; deployment verification completed shortly afterwards.

User reports alternating keyframe/stream waiting with no video after1.6.0. Read current notes, source and NAS cache metadata over SMB; no production HTTP/browser requests or camera setting changes.

## Evidence and fix

All three fragment caches were advancing and contained regular keyframe flags, so the wait was not a missing keyframe. Garage init.mp4 had an eight-byte avcC box with no payload. ffprobe could report container dimensions1920x1080 but no extradata. The old JavaScript scanner read bytes from the following stts box and returned invalid codec avc1.000010, then hid the failure behind generic reconnect text. First keyframe mdat contained length-prefixed SPS20bytes/PPS4bytes and IDR slices.

Added FFmpeg delay_moov, per https://ffmpeg.org/ffmpeg-formats.html, to defer header writing. The first deployed candidate still generated empty avcC on these cameras; this alone was not a fix. Final FragmentCache now waits for the first random-access packet before publishing initialization. When avcC is empty, it constructs the AVC configuration from that packet's actual SPS/PPS and updates enclosing MP4 box lengths. Existing nonempty configuration and all encoded video packets are preserved. Missing/malformed parameters fail rather than publish a bogus initialization. Video remains copy-only with200ms fragments and shared reader; focus/recording arguments are unchanged.

JavaScript now checks avcC bounds, configuration version and complete SPS/PPS entries before choosing a codec, and reports the actual reconnect error. Atomic cache-file replacement retries transient Windows UnauthorizedAccessException as well as IOException within the existing bounded retry; a local fixture exposed that sharing race.

## Tests

-19 stream functional checks passed: artifacts/copy-checks-20260928-183706. Includes existing fragmentation/continuity/auth/cache tests, reconstructing deliberately emptied synthetic avcC with identical decoded frame hashes, preserving an existing header, and locally repairing/decoding a private captured camera-cache fragment. The original six-frame comparison was corrected to compare identical encoded packets with/without repaired header because B-frame presentation order crosses the fragment boundary.
-7 JavaScript packet/config checks passed, including the exact empty-avcC/following-stts structure, absent SPS and oversized box rejection.6 existing session lifecycle checks and syntax check passed.
-36 existing backend checks passed before the final header-repair addition: artifacts/checks-20260928-183415. Final stream suite and web/worker Release publish passed after it.
-Private camera media stays only in ignored artifacts; not committed or rendered. No real browser, production HTTP, physical Android or end-to-end latency testing occurred.

## Deployment

Final web/worker deployment used app_offline and backup `\\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-copy-init-fix-20260928-183722`;6 changed files. Earlier delay-only candidate backup183346 remains available. Config hashes preserved, external state/recordings unchanged, offline marker removed. Web DLL/copy-stream.js and both worker DLL hashes matched published files.

After restart, current cache metadata for Garage, Front door and Side all reports H.2641920x1080 with extradata_size35, where the prior cache had none. Recovered Garage codec validates as avc1.4d0028. This confirms complete generated headers, not successful browser playback. Normal reload/reopen loads versioned JS; no APK change. Source/worklog will be committed and pushed to existing main.

User subsequently confirmed in this conversation that video is now working. This is user-reported playback success; no agent browser test or measured latency claim.
