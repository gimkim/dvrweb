# Garage source timeouts and WebRTC recovery

User reports Garage still frozen. User confirms it remains on downstairs AP with stronger signal than other cameras; RSSI alone is not a root cause. Source probes show intermittent TCP10554 timeouts. A requested65s direct RTSP copy ended after5.2s of HEVC1080p video with demux timeout. FFmpeg exit0 is not evidence of completing the requested duration. NAS manifests repeatedly contain approximately17-22s clips. Temporarily paused only Garage Enabled flag for an exclusive bounded probe, backed up its JSON privately, restored original Enabled in finally with revisions; single receiver attempt also timed out. No codec rollback, bitrate change, AP change or recording deletion.

Fixed shared web/Android HEVC player: failed signaling, lost source/lease, and stalled decoded frames release old peer/session and reconnect with3/6/12/15s capped backoff. Successful decoded frames reset backoff. Unsupported HEVC and401/403 stop. Destroy cancels retry and late offers are still released. No fMP4/transcode fallback. Tests cover actual reconnection, backoff, destroy and terminal auth failures, plus existing lifecycle cases.

Validation:12WebRTC code-fixture checks and5snapshot checks pass. Static webrtc-stream.js deployed and SHA256 matched; backup-webrtc-reconnect-20260930-164350. Existing asset hashes change automatically on page reload. No browser/device test. No APK rebuild needed. No backend binary change.

Recorder heartbeat later remained stuck at09:43:08Z while WebRTC status continued. Performed app_offline recycle of GimDVR only: recorder lock released, offline removed in finally, health returned1.11.0/ok. This interrupts all camera readers briefly. Fresh heartbeat/source evidence to follow. Old diagnostic PID appeared in CIM but Stop-Process reported nonexistent; do not claim it was killed. Root cause of source timeout and stalled supervisor remains unproven; restart does not establish repair.

Follow-up16:50:31Bangkok: recorder heartbeat fresh after recycle, but Garage restarted again(PID13364 to2816); finalized post-recycle clip20.310111s. Supervisor recovery confirmed, stable camera stream NOT confirmed.
