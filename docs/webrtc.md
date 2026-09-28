# WebRTC live transport

GimDVR1.10.0 uses MediaMTX1.21.1 for WebRTC-first live views. Web and the existing Android WebView share the same versioned player; no APK update required. Recording/playback/download formats stay unchanged and live video remains silent, matching the existing copy player.

## Data path

One FFmpeg camera reader still handles recording and existing previews. Its additional video-only MPEG-TS output uses `-c:v copy -an` to a dedicated loopback UDP port. MediaMTX reads that output on demand and shares it among viewers. Neither an extra camera RTSP connection nor a video encoder is started per viewer. The UDP source bypasses the fMP4 fragment cache. Camera H264 must be WebRTC-compatible (notably no B-frames); incompatible media or an unreachable peer falls back to the existing fMP4 player.

## Installation and network

Place the official Windows amd64 MediaMTX executable beside configured FFmpeg, normally `C:\Users\tatsa\web-tools\mediamtx.exe`. It is owned/restarted by the web process and Windows Job Object. Version1.21.1 archive:
https://github.com/bluenviron/mediamtx/releases/download/v1.21.1/mediamtx_v1.21.1_windows_amd64.zip

Published archive SHA256: `faa97974861eb75a68b5aa326c78e7e7a6f670b5ef191bace78e715130381f23`.
Executable SHA256: `7de0b5c1060f716cc813c6fab302c938f1fe7faccbd92eba6567f3bf18c9054e`.
Keep its supplied MIT license alongside it. Binary/config/runtime files are not committed.

The app generates MediaMTX config in external DataRoot/runtime. WHEP signaling binds127.0.0.1:18889 and control API binds127.0.0.1:19997; only loopback read/API permissions are granted. RTSP/RTMP/HLS/SRT/MoQ gateway listeners are disabled. Never forward the internal HTTP ports. Encrypted peer media uses TCP and UDP8189, with NAS interface IPs and gimgim.ddns.net candidates.

Run `deployment/Enable-WebRtcFirewall.ps1` elevated ON GIMKIM-NAS. Deployed copy: `C:\Users\tatsa\web-setup\GimDvr\Enable-WebRtcFirewall.ps1`. Internet use also needs router TCP+UDP8189 forwarded to the NAS; this change does not configure the router or TURN. If unreachable, clients automatically fall back. HTTPS443 alone is insufficient for the media connection.

## Session policy and lifecycle

Authenticated same-origin POST `/api/cameras/{id}/webrtc` accepts bounded receive-only SDP and returns an answer plus opaque app session ID. The server never accepts client-selected gateway/source URLs. Three-second authenticated PUT heartbeats retain a session. Server checks account stamp/enabled, camera enabled/source generation and12s heartbeat expiry every2s, deleting the WHEP session when invalid. DELETE is owner-scoped. Failed deletion stops the owned gateway to fail closed (other peers then reconnect/fallback). At most64 pending/active sessions. Gateway addresses, internal WHEP resource secrets and camera credentials are not returned.

Player cancels/releases on per-camera stop/navigation/logout/suspend; overview/single layout changes preserve existing sessions. It falls back once per viewing session after failed negotiation, failed peer, expired lease, initial25s timeout or10s without decoded frames after an answer. It does not repeatedly alternate transports. Existing manual buffer settings now apply to fMP4 fallback only; WebRTC manages its own jitter buffer. Live-only visible status remains.

Gateway state: DataRoot/runtime/webrtc-state.json (timestamp, readiness, active sessions, process ID, pinned version). Admin `/api/system` includes readiness/session count. Browser video.dataset.transport identifies webrtc/fmp4 for diagnostics without adding a status overlay.

## Validation boundary

`node tests/webrtc-player-checks.cjs`, `node tests/live-session-checks.cjs`, `node tests/live-layout-checks.cjs`, and `dotnet run --project tests/GimDvr.Checks -c Release` are code-only checks. `py -3.10 tests/mediamtx-smoke.py artifacts/mediamtx` runs local MediaMTX plus synthetic FFmpeg input: startup, two WHEP offers sharing one source, and DELETE. Requires extracted pinned binary and FFmpeg on PATH. It does not perform a browser/DTLS decode or production camera test. User policy forbids unsolicited real web/device testing; latency, camera codec compatibility and external routing remain unverified.

Primary references: https://mediamtx.org/docs/read/webrtc and https://mediamtx.org/docs/features/webrtc-specific-features ; pinned release config and WHEP HTTP implementation were inspected for the source/API contracts.
