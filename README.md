# GimDVR

Current1.11.1: all three cameras send H.265 at1920x1080. Live uses H.265/H.264 WebRTC without fMP4 fallback or video transcoding. Windows NAS recovers VStarcam IP changes using verified MAC/UID and LAN discovery, with MAC configurable in camera settings. Recordings copy original camera video; historical files remain unchanged.

Live settings1.9.1: defaults150ms fMP4 fragments and300ms startup/rebuffer/live target. Admin can change each value in ระบบและกิจกรรม → ภาพสด · Buffer; SQLite persists settings for web and Android. Changing fragment duration restarts shared readers and may briefly interrupt recording; changing only buffers reconnects viewers. Historical100/200ms values below are superseded.

Remote analysis1.9.0: [MotionService setup and operation](docs/motion-service.md). NAS polls the authenticated HTTPS service every15seconds, prefers NVIDIA CUDA/CPU remote processing when ready, and falls back to NAS OpenVINO on failure. Recording, retention and SQLite stay on NAS.

อ่าน [Agent notes](AGENT_NOTES.md) สำหรับ concept/หลักการปัจจุบัน และ [Worklog index](worklog/README.md) สำหรับประวัติแยกแต่ละงาน ข้อความรุ่นเก่าด้านล่างเป็นประวัติและอาจถูกแทนที่ด้วยการตัดสินใจล่าสุด

.NET 10 / ASP.NET Core application for a Windows NAS, hosted as `/gimdvr` under the existing HTTPS IIS site.

## Using the app

Sign in with the initial administrator from `C:\Users\tatsa\web-data\GimDvr\bootstrap.txt` on the NAS. Change the password with the account button after signing in. The bootstrap file is removed on that password change. Users have admin, operator, or viewer roles. Changing credentials, permissions, or enabled state invalidates existing sessions.

The initial three VStarcam cameras have recording **disabled**, with **no recording folder**. Viewing live video creates only a short, rotating stream cache. To record, choose an absolute NAS-local or UNC folder for each camera and enable recording. Grant the actual web pool identity Modify permission to that folder in web-owner mode; the currently deployed pool is `IIS AppPool\DVR`. With the independent worker installed, grant `NT SERVICE\GimDvrRecorder` Modify instead. A mapped desktop drive letter is not suitable for IIS.

Recording uses MP4 files targeting **60 seconds**, without re-encoding video. Cuts follow camera keyframes; the first/last clip and interrupted streams can be shorter, and a boundary can vary by one keyframe interval. Recordings are stored beneath `chosen-folder\gimdvr-camera-id\session\`. Retention deletes only completed, catalogued app recordings. Unlimited retention never deletes recordings by age. The current unfinished minute appears after it is finalized. Playback supports seeking, range requests, and automatic next-clip playback for the same camera.

Live video, audio, snapshots, and recorded files are authenticated and proxied by the server. The browser receives no camera credentials. Camera passwords are encrypted using ASP.NET Data Protection; user passwords use salted PBKDF2. Back up `dvr.db` **and** the `keys` directory together with recordings. Keep the data directory outside the web root.

## Camera support and current limitations

VStarcam: RTSP live video/audio, snapshots, hold-to-pan PTZ with continuous commands and a server-side dead-man stop, IR setting, microphone/speaker volume (0 mutes), and motion detection configuration. Generic RTSP: live and recording. The existing human-setting CGI probe is inconclusive; that camera control stays disabled until supported settings are confirmed. Independent NAS-side motion/person analysis now labels recordings without relying on camera alarm metadata.

**Browser-to-camera talk is not implemented for these cameras.** Their RTSP SDP does not advertise an ONVIF audio backchannel. The official Windows H5 SDK uses a proprietary desktop helper; its published browser interface starts local-helper microphone capture, rather than accepting browser audio at the NAS. This needs a compatible server-side VStarcam audio bridge before it can work. The UI reports this limitation before requesting microphone access. Do not interpret speaker volume control as talk support.

Until the worker service is installed, the recorder runs with the IIS application, including when browsers are closed. The installer configures AlwaysRunning, preload, one worker, no idle timeout, and no scheduled recycle. Reboots, application restarts, camera outages, and forced termination still cause gaps; a forced stop can leave the current MP4 unfinalized. Earlier completed clips remain playable. Automatic disk-full deletion is intentionally not performed: set retention and monitor storage. Camera clocks are not modified; file times use the server clock.

## Build and deploy

Build: `dotnet build src/GimDvr -c Release`.

Publish: `dotnet publish src/GimDvr -c Release -r win-x64 --self-contained false -o artifacts/publish`.

Production paths are in `deployment/appsettings.Production.json`. Publish output goes to `C:\Users\tatsa\web\dvrcam` on GIMKIM-NAS. Run `deployment/Install-GimDvr.ps1` in an elevated PowerShell **on the NAS**, selecting the existing HTTPS site if necessary. It configures only the GimDvr pool/application and ACLs; it does not replace site bindings or other applications. ASP.NET Core 10 Hosting Bundle and FFmpeg/ffprobe are required. Re-running setup preserves the database, keys, users, and camera settings.

For later upgrades, use app_offline.htm during the file copy and preserve all external data/configuration. Do not copy development settings, credentials, evidence, or test recordings into wwwroot. TLS terminates at the existing IIS HTTPS binding. No router camera port-forwarding is required.

## Validation

Run `dotnet run --project tests/GimDvr.Checks` for isolated store/retention/segmentation checks. API and browser checks are separately recorded in `worklogs/`. Build success is not proof that IIS is configured, cameras accept controls, or audio can be heard at the camera.

## Version 1.1: controls and shared recorder

The control panel is modeless, draggable, and resizable. Advanced settings are collapsed so the camera remains visible. Hold an arrow to move; release, close, or leave the page to stop. The server expires a missing heartbeat after 650 ms and rejects stale commands. Network delay and the camera firmware still affect perceived response.

Version 1.2 uses one RTSP reader per camera. It copies video into the original 60-second recording output and a loopback-only MPEG-TS relay. While someone watches, one separate encoder per camera consumes that relay and produces H.264/AAC fragmented MP4 in 0.2-second pieces. Every viewer shares these encoded fragments through an authenticated streaming HTTP response and MediaSource player. No browser connects to the camera. Live height defaults to 1080 (Dvr:LiveHeight can lower it); 15 fps, ultrafast/zerolatency, GOP 3. Recording video remains copy quality.

When the last viewer leaves or hides the tab, the live encoder stops after an 8-second lease grace period. Recording continues with the same reader PID. When neither recording nor viewing is required, the reader stops too. Snapshot requests briefly acquire the same live lease. A new viewer may wait for the camera's next keyframe during startup (about four seconds on these cameras). The player seeks forward if its buffered lead exceeds 0.65 seconds, targets about 0.25 seconds, and reconnects rather than accumulating a long queue. Slow networks, camera buffering, and CPU saturation can still exceed one second end-to-end; a strict one-second guarantee has not been measured. Chrome/Edge with MediaSource are supported. Closing/hiding the page cancels the response; viewer access is revalidated every five seconds.

Implementation references: https://ffmpeg.org/ffmpeg-formats.html (fragmented MP4 output), https://developer.mozilla.org/en-US/docs/Web/API/MediaSource/addSourceBuffer (browser media buffers).

An independent Windows Service is supplied in `src/GimDvr.Worker`. Publish with `dotnet publish src/GimDvr.Worker -c Release -r win-x64 --self-contained false -o artifacts/worker-publish`. It owns the camera readers, recording, retention, and shared live cache. The web only sends watch leases and serves authenticated media when `Dvr:MediaOwner` is `worker`. A process lock prevents concurrent owners.

On GIMKIM-NAS, run elevated PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Users\tatsa\web-setup\GimDvr\Install-RecorderService.ps1"
```

The installer stops the web-owned reader, grants the service access, switches configuration, and verifies a fresh worker heartbeat. It restores web configuration if activation fails. Grant the service Modify on each chosen recording folder. No camera recording setting is changed. Once active, IIS restarts do not stop recording. Service or machine restarts and camera outages still create gaps.

Deployment status on 2026-09-28: version 1.1 web is deployed; worker binaries and installer are staged. Remote Windows service creation returned Access denied (5), so independent-service activation requires the command above on the NAS. Current production remains in web-owner mode.


Version 1.2.1 fixes camera H.264 recordings whose SPS/PPS arrive in-band: the recorder uses `extract_extradata` to populate MP4 codec headers without re-encoding video. Previously damaged finalized recordings were repaired with original copies retained as `.mp4.before-avcc-repair`; these backups are not part of automatic retention. Six catalog entries with missing original files could not be repaired. See the repair report and timestamped worklog for details.

Version 1.2.2 uses Intel Quick Sync H.264 encoding when available (`Dvr:LiveEncoder=auto`, default). A bounded three-frame hardware encode probe runs under the actual media owner's Windows identity on first live use. Encoder settings retain 15fps/GOP3/0.2-second fragments, disable B frames/lookahead, use async_depth=1 and ICQ24. Decoding and resizing remain on CPU; only video encoding is offloaded. Set LiveEncoder=cpu to force libx264. A failed probe or failed/stalled QSV camera encoder falls back to CPU; the camera card reports Quick Sync or CPU. Per-camera runtime status and runtime/qsv-status.json report the actual path. One shared encoder per watched camera and 8-second idle stop remain unchanged; original recording video uses copy plus extract_extradata. N100 NAS validation confirmed h264_qsv fragments from all three cameras decoded successfully. No before/after CPU percentage benchmark was performed.
Version 1.2.3 playback requires choosing a camera before start/end datetimes. Default range is the last hour in browser local time. Results include overlapping clips, oldest first, with pagination beyond 2000 files. Play all automatically advances files and respects the selected boundaries; there may be a brief load between files.

Version 1.2.4 fixes Quick Sync browser startup: the live encoder now encodes mono AAC at 16kHz/48kbps so AudioSpecificConfig exists before fMP4 initialization is written. AAC stream-copy from the relay could omit this configuration with fast QSV startup, which FFmpeg tolerated but browser MediaSource rejected. Video still uses shared on-demand QSV; recording settings and video copy are unchanged. Actual NAS QSV output from all three cameras was played in a local browser validation page using the production player; this is separate from authenticated production-site testing.

Version 1.2.5 adds QSV CBR 2.5Mbps, 500kbit VBV, and low_delay_brc in place of unrestricted ICQ. This is not a verified cure for frame pacing. Production NAS LiveEncoder is explicitly cpu following the user's QSV stutter report. The CPU path remains libx264 ultrafast/zerolatency; software decode/scale plus QSV upload and async_depth1 remain investigation targets. Do not claim GPU saturation or LAN bandwidth exhaustion from bitrate alone.

Version 1.2.6 replaces aggressive live-edge chasing: wait for 1.5 seconds of buffered media before starting, use normal playback speed, and on starvation pause to accumulate a reserve that grows in 0.5-second steps up to3seconds. Seek only across a missing buffered range or more than6seconds behind the received edge. This deliberately trades additional delay for resilience to bursty delivery; it is not a camera-to-screen latency guarantee. CPU remains configured on production while pacing is evaluated. Recording and encoder lifetime are unchanged.

Version 1.3.2 was the streaming design before 1.5.0 (supersedes the low-latency fMP4 player above). Live video uses hls.js with one-second MPEG-TS segments, two-segment live target, max latency six segments, and a five-second maximum buffer, as in1.1.2. Only the player/protocol was restored; the final user request retains on-demand shared encoding. One RTSP reader continues recording original video and supplies a loopback relay. One encoder per watched camera produces HLS shared by all viewers and stops after an8-second lease grace period; watch heartbeats are every3seconds. Quick Sync uses veryfast, async_depth4, no lookahead/B-frames, GOP15/15fps,1080p, CBR target4Mbps/VBV4Mbit/low_delay_brc. This prioritizes speed over ICQ quality mode; actual bitrate can vary. CPU fallback remains available. Snapshot extracts JPEG from the latest completed TS segment; recording extract_extradata and playback range/queue functionality remain. Production LiveEncoder=auto. Archived realtime.js is no longer loaded; the framed live-stream endpoint is removed.

Version 1.3.4 automatically adds content-hash versions to HTML local asset URLs and the dynamic audio worklet. HTML is no-store and static files revalidate. Normal reload obtains new assets after deployment; already-open pages do not auto-reload.

## Source repository and development setup

Public source: https://github.com/gimkim/dvrweb. With .NET 10 and FFmpeg installed, copy src/GimDvr/appsettings.Development.example.json to appsettings.Development.json and adjust local paths, then run dotnet run --project src/GimDvr. The real development config and all database/keys/bootstrap/media/evidence files are excluded from Git. Historical worklogs reference local evidence that is not included in the public repository. Deployment/repair scripts contain NAS-specific paths and must be reviewed before use on another machine.


## Android app and per-camera viewing

Install [GimDVR 1.0.1 APK](https://gimgim.ddns.net/gimdvr/downloads/GimDVR-1.0.1.apk) on Android 8 or newer. The app opens the fixed server https://gimgim.ddns.net/gimdvr/ and uses your existing GimDVR login. It is a .NET Android WebView client, with no camera credentials or server-selection screen. Cameras stack vertically; fullscreen switches to landscape with a draggable camera-control panel. Android physical-device validation is still pending.

The power switch before each camera name controls viewing on this device only. Preferences survive reopening and logout, separately per user/browser/app. It does not turn off recording or the camera. Android keeps a persistent login cookie (long-lived server ticket); explicit logout clears it. Clearing app data, reinstalling, platform expiration or server-side account/password changes can require login again.

Build a signed update with `deployment/Build-Android.ps1`. Requires .NET 10 Android workload, SDK/build-tools 36 and JDK 17. Restore the original private `.local-data/android-signing/gimdvr.keystore` and `store.pass` first; never generate a different signing identity for an update. Output is `artifacts/android/GimDVR-<version>.apk`; script verifies its signature. Keep private signing material backed up outside Git. Increment ApplicationVersion/ApplicationDisplayVersion in the Android csproj for updates. APK artifacts and credentials are ignored by Git.

Run web lifecycle checks with `node tests/live-session-checks.cjs` and backend checks with `dotnet run --project tests/GimDvr.Checks -c Release`.


## Viewing modes (1.7.0)

Overview and single-camera view both proxy original H.264 through the same copy-only continuous fMP4 stream. Fragments target100ms; startup reserve, rebuffer reserve and initial/seek live target are all200ms. Both modes use overview watch leases and do not start a video encoder. Legacy focus/QSV endpoints remain for older clients. Existing recording settings and output are unchanged.

Overview retains per-camera viewing switches and clickable names, and restores a per-camera Control button for operators/admins. It opens the existing draggable PTZ/advanced-settings panel. Single-camera mode retains full-tab viewing, fullscreen and its floating panel. Video is silent in both modes; there is no inactive mute toggle. Connection/recording configuration remains on the camera-management page. The existing Android APK receives these versioned web assets on reopen.

The shared fragment cache remains bounded to128fragments/64MiB per camera. New viewers wait for the next keyframe, then receive dependent frames continuously. Empty AVC initialization is repaired from in-band SPS/PPS before publishing. HLS copy remains for snapshots.200ms is not a measured camera-to-screen latency guarantee or a hard cap; existing1.05x catchup above900ms and hard seek above2s remain.

Local checks: `node tests/live-session-checks.cjs`, `node tests/live-mode-checks.cjs`, `node tests/copy-stream-checks.cjs`, and `dotnet run --project tests/GimDvr.StreamChecks -c Release`. The100ms synthetic fixture produces100fragments over10seconds with matching decoded frames. No real browser/device testing was performed for this change under the user's code-only testing policy.

Switching overview/single camera now expands the existing video in place and keeps other selected camera streams running. Returning to overview reuses those same players and buffers. A camera disabled in overview is started temporarily when selected alone, then stopped on return without changing the saved preference. Leaving live views or suspending the app still releases sessions. Layout lifecycle checks: `node tests/live-layout-checks.cjs`.

Android1.0.1 loads the same server-owned copy-stream player as the website with WebView CacheMode.NoCache. Cookies and DOM storage remain enabled; no stored login/preferences are cleared. Install the signed update over1.0.0 to retain app data. Both modes inherit100ms fragments,200ms buffers and in-place layout switching; streams stop on app background/suspend as before. Physical-device playback has not been tested.

Mobile playback: open ดูย้อนหลัง (Android has ภาพสด/ดูย้อนหลัง tabs), select a camera, select start/end (default last hour), search and play one file or the whole range. Existing APK 1.0.1 loads the updated shared UI when reopened. Version1.8.0 adds independent NAS Motion/Human badges and compact recording rows. Results arrive after each file finishes and background analysis runs; older files are queued too. Green check=detected, grey dash=not detected in sampled frames, amber question=pending/incomplete/unavailable. Detection badges and the completed count refresh every 10 seconds while this results page is open, without restarting playback. See [NAS detection setup and limits](docs/nas-detection.md).

Recording search offers Motion/Human checkboxes (both selected means either detection). Play-all follows filtered results. Download original MP4 from each result or the clip player in the web browser. Pending/unknown detections do not match positive filters.

Live buffering now pauses to accumulate the configured rebuffer reserve before resuming. Startup and recovery preserve their configured reserve even when live target is smaller; normal fragment appends do not restart playback. Shared web/Android player, normal reload/reopen to update.

## WebRTC live (1.10.0)

Web and Android now prefer shared copy-only WebRTC via NAS MediaMTX, with automatic fMP4 fallback. Existing recordings are unchanged. See [network setup, security, tests and limits](docs/webrtc.md). NAS firewall TCP+UDP8189 and router forwarding for Internet use must be configured; HTTPS alone does not carry peer media.

1.10.1 corrects verified camera clock pacing through an optional external allowlist, removes conflicting live-control autoplay, and requests400ms WebRTC jitter reserve. See docs/webrtc.md and the latest worklog for measured playback and remaining validation limits.
