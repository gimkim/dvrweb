# Overview video copy and focused low-latency QSV — 1.5.0

Time: 2026-09-28T18:04:54.886549+07:00

## Request
Separate multi-camera overview from single-camera viewing. Overview should proxy original video without CPU-heavy encoding, with no camera controls/settings/fullscreen. Selecting a camera opens a full-tab view with PTZ and short-buffer Quick Sync streaming.

## Design and changes
The existing single RTSP reader now emits a second, video-only HLS output using -c:v copy -an. This is container remuxing, preserving source video rather than transcoding it. Overview has no audio, camera settings, snapshot or fullscreen controls; it retains the persistent per-camera viewing toggle and a camera-name entry button. Camera management remains available through the normal application navigation.

Overview shares its reader with recording and the existing loopback relay. HLS remux output lives in a separate Overview directory; it remains a bounded rolling cache while the reader runs, including during recording. It does not create a new camera connection or video encoder. Existing recorder/relay audio encoding still uses CPU; this is not a zero-CPU claim.

The single-camera page stops all overview players/heartbeats for that tab, fills the tab viewport, opens the draggable PTZ panel for operator/admin, and supplies Back to all cameras, sound and fullscreen. Viewer role has no PTZ controls. URL hash/history supports reload/back. Android uses the same updated web UI and retains native fullscreen landscape through the existing bridge; resume now handles either viewing mode. APK 1.0.0 is unchanged and receives this server-side UI after reopening/reloading.

Focus watch leases and /api/focus media endpoints are distinct from overview /api/live. Only focus demand starts the shared QSV encoder; overview demand never keeps it alive. One encoder per selected camera is shared across viewers, with the existing eight-second idle grace. Different users/tabs may deliberately focus different cameras simultaneously; no global one-camera lock was added. A switch can briefly overlap encoders during grace. Camera reader/recording lifetime is based on either viewing mode or recording.

Focus retains 1080p/15fps and 4Mbps target/max, changes QSV async_depth 4 to1 and VBV4Mbit to1Mbit, disables lookahead/B-frames and uses GOP8 (~0.533s). HLS target0.5 seconds, list12; actual NAS segments were0.533333s with target duration1. Player liveSyncDuration0.7s, maxLiveLatency2s, maxBufferLength1.5s, backBuffer1s and catch-up ceiling1.1x. QSV probe/failure CPU fallback remain; the single-view heading reports actual QSV/CPU when returned by heartbeat. This is short-segment classic HLS, not LL-HLS partial-segment delivery and not a guaranteed subsecond camera-to-screen result. Overview cuts at camera keyframes with target2s/list6. Reference: https://ffmpeg.org/ffmpeg-formats.html (HLS keyframe-aligned segmentation).

Snapshot now reuses completed overview TS and does not require starting the focus encoder. Authentication and media path validation cover both endpoints. Shared worker state exposes both cache paths, and cache cleanup preserves both active directories. Credentials, paths/retention/recording flags and source recording arguments are unchanged.

## Validation
- 36 .NET checks passed, including recording alone/25 overview viewers not starting an encoder; focused viewers sharing one encoder; encoder stops while overview watches continue; reader PID persists; worker proxy; retention/PTZ/asset tests.
- 5 Node lifecycle tests passed, including focus mode using its distinct stream even when that camera's overview preference is off. JS syntax and Release web/worker publish passed with no compiler errors.
- NAS-backed local browser harness used production JS/CSS with a fixture user/camera-control API and actual NAS HLS cache. It did not read production bootstrap credentials or log into production. No physical PTZ movement was requested by tests.
- Overview all3: H.2641920x1080, ffprobe avg_frame_rate30/1, no audio stream. Browser readyState4/paused=false on all3; currentTime around21-22s. No per-camera settings/control/fullscreen UI. NAS encoder=none for all3 while recording=true.
- Front door focus: NAS h264_qsv PID13536, reader PID4952 unchanged. Browser readyState4/paused=false with time progressing13.27 to38.98s; buffered-ahead snapshots0.59s and1.02s. No browser error entries during this check. These are buffer observations, not measured end-to-end latency or indefinite smoothness.
- Back to overview: focus encoder stopped, all3 recording readers unchanged (Garage13392/Front4952/Side10400), recording=true. A new completed Front door MP4 (20260928T180053.mp4) decoded3seconds successfully.
- Production health200/version1.5.0; deployed DLL/JS/CSS hashes checked. Anonymous overview and focus media endpoints both401. Configuration hashes preserved.
- Physical Android install/rotation and authenticated production-browser behavior remain unverified. Local harness evidence must not be presented as those checks.

## Deployment and source
Web and worker published to established NAS paths with app_offline. Backup: web-setup/GimDvr/backup-overview-focus-20260928-175935. As before, web-owner mode means deployment can briefly interrupt recording; all3 resumed. Final actual-encoder-label JS update copied as a static-only change; content hashes provide cache versioning. No APK rebuild was needed.

Files: MediaService.cs, MediaService.Live.cs, MediaService.Qsv.cs, Program.cs, app.js/app.css, .NET/Node checks, AGENT_NOTES.md, README.md and this new worklog/index. Private validation files remain ignored under artifacts. Existing worklogs preserved.
