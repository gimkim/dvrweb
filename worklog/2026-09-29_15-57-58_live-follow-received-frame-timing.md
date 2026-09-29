# Live output follows received frame timing (1.10.7)

User requests streaming at the FPS received before addressing camera-to-AP wireless interruptions. Remove fixed-rate setts bitstream filters from both WebRTC MPEG-TS relay and fMP4 outputs, remove clockFps method parameters and the WebRtcClockFps setting accessor. Existing external WebRtcClockFps=15 is now ignored, preventing stale configuration from reinstating forced timing. Preserve the input arrival-clock allowlist because camera timestamps previously ran at the wrong clock rate. Thus current cameras follow NAS arrival timestamps, not nominal codec FPS; other cameras retain their existing source timestamp policy. No frame generation, drop, re-encode, resolution/bitrate change, or camera configuration change. Buffer settings and recording path remain unchanged.

Functional validation:31stream checks with real FFmpeg. New fixtures at15fps,30fps,and15->30fps preserve all decoded frame hashes and interframe timestamps within2ticks of a90kHz clock in BOTH output formats. Existing fragmented media/keyframe joins, B-frame preservation and authentication checks pass.71main checks pass. These synthetic checks validate transport timing, not actual Wi-Fi smoothness or camera-to-display latency. No browser/device test performed.

Web/worker Release publish successful. Deployment uses existing offline/backup/hash procedure, preserves production config and data. Live sessions restart during deployment; user may need refresh. Source, tests, agent notes and worklog pushed. Wireless gaps remain visible; this change removes the artificial15fps timeline, not the underlying camera/AP interruption.

NAS deployment verified: backup-detection-20260929-155720; published web hashes match, production config preserved.
