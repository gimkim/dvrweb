# Live transport advice

Recorded 2026-09-28_22-26-56 +07. User asks whether a better alternative to fMP4 exists. Advisory research only; no application code/config changes or deployment.

Recommended evaluating WebRTC for interactive camera viewing, with NAS media gateway and existing shared input; retain original MP4 recording and fMP4 fallback. Forward H264 without video re-encoding only if the camera profile/packetization and absence of B-frames are compatible with browser WebRTC. Current camera compatibility and actual latency were not measured in this session. HTTPS reverse proxy alone does not route WebRTC media; ICE/UDP/TCP or TURN reachability needs design. A transport change cannot fix upstream camera timestamp/network problems automatically. fMP4 is a container, not itself proof of the prior stutter cause.

Primary sources reviewed: https://mediamtx.org/docs/features/webrtc-specific-features (codecs, B-frames, media channel and NAT/TURN); https://go2rtc.org/ (RTSP/WebRTC/MSE gateway capability). No browser/device/production HTTP tests. No decision to migrate or implementation authorization inferred from this question.
