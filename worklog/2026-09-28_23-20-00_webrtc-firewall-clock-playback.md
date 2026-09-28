# WebRTC startup, firewall and playback investigation (1.10.1)

Recorded2026-09-28 23:20+07; investigation began earlier this session. User explicitly requested real web testing for slow startup/stutter, reported router8189 forwarded, and confirmed successful elevated NAS firewall script execution. This is a request-specific exception to code-only testing policy.

## Findings and changes

- NAS firewall previously blocked peer media. Following user script execution, TCP8189 was reachable and Brave actually negotiated UDP WebRTC. WAN/mobile routing was not tested.
- Actual fallback player produced roughly89,508 waiting events in40s: live-controls pause/canplay autoplay fought the copy player's rebuffer pause. Removed those listeners and mute-triggered play; player alone owns playback. Regression fixture now binds real controls to player.
- All three camera sources delivered about15fps while original video PTS advanced30fps. Around6s wall time produced only3s media, exhausting buffers. Confirmed H264Main1080p/level40, no B-frames. Added optional external live-clock.json camera allowlist, enabled garage/front-door/side. Arrival wallclock applies to shared input/new recordings; other cameras retain original +genpts. No existing files rewritten. After fix6.7–7.2s wall produced12–14half-second fragments, versus6 before.
- MediaMTX loopback UDP read capacity4MiB and packet write queue2048 resolved connected-but-undecodable streams in the observed sample. No video re-encoding or additional camera connections.
- Added bounded gateway warning state and browser DOM transport/startup/frame/jitter/freeze counters. Offer18s, ICE4s and no-decoded-frame10s timeouts; network failure has60s shared retry cooldown.
- WebRTC400ms receiver jitter target where supported.200ms reduced stutter but still measured multiple freezes;400ms improved visible cameras. Browser remains adaptive; this is not a hard end-to-end latency limit. Preserved owner's fallback settings500/800/800/800ms.

## Validation and deployment

56 backend checks,7 WebRTC lifecycle checks,6 copy playback checks including controls interaction,3 layout checks; Release web/worker builds/publish and JS syntax/diff checks passed. Repeated affected fixtures after final static edits passed. NAS deployed1.10.1, appsettings/data preserved and DB backed up; backups backup-detection-20260928-230855 and231129 in NAS web-setup/GimDvr. Recorder interruption occurred during DLL deployment. Final3player asset SHA256 match deployment. External live-clock.json is private deployment config, not Git.

Real authenticated Brave desktop test: all3actual WebRTC/UDP, no packetsLost, RTT~1ms. First400ms-target sample started Side1.528s, Garage3.211s, Front door4.200s (next-keyframe wait varies). At~52s Garage/Front door zero freezes/drops; offscreen Side3freezes totaling0.971s, zero drops.200ms prior sample at97s had12/6/16freezes for Garage/Front/Side. These are bounded samples, not camera-to-screen delay measurements or indefinite smoothness proof. Overview/Garage/back preserved original sessions/times; Side single-view test follows below.

New completed MP4 for each camera decoded end-to-end with FFmpeg passthrough/demux timebase, exit0/noerrors; lengths59.989/59.997/59.958s. Initial null-mux default output timebase emitted rounded DTS warnings; rerun with source timebase removed these output-mux warnings. Source recordings preserved.

Physical Android and external cellular clients untested; APK1.0.1 receives same hosted JS. Private footage/diagnostic artifacts remain ignored. Firewall opening does not address camera timing or player event-loop bugs by itself.

## Follow-up within the same investigation,23:23+07

400ms arrival-clock-only sample at~125s still had2/3/17freezes (0.703/1.045/5.956s) for Garage/Front/Side; Side was actually visible in single view, so this is not explained solely by offscreen rendering. Added optional WebRtcClockFps=15 on the verified camera allowlist to regularize only WebRTC output packet timestamps via setts, preserving compressed video and recording output. Synthetic remux of captured sample produced exact66.667ms packet spacing. This fixed-rate override must be revalidated if camera FPS changes.57backend checks passed. Republished both web/worker and deployed with matching hashes/config preservation, backup-detection-20260928-232309. Side control capability read timed out while video kept playing; no claim made that this unrelated camera control endpoint is fixed.

Final deployed clock sample: after initial warmup all3advanced720frames in48s with no additional freezes/drops/loss. A normal reload began at3.300/3.337/3.413s, with early startup freezes still present (Garage2/1.780s, Front3/1.131s, Side3/3.363s at~35s; Side17dropped). Therefore do not claim completely eliminated startup stutter. Sources still require the next camera keyframe; first-picture time varies0.8â€“4.2s in these tests. Remaining brief startup decode/jitter recovery is explicitly reported. No evidence supports bandwidth saturation on the client/NAS path: measured UDP loss0 and RTT~1ms. Router forwarding itself was user-reported, not verified from outside LAN.
At~65s in the reload sample Garage rose to3freezes/2.438s and Front to5/2.263s; Side stayed3/3.363s with17drops. Thus residual stutter is not exclusively startup. All3remain connected on WebRTC with0reported packet loss. Fixes improve the identified faults but do not establish fully smooth playback; further input pacing/decoder investigation remains.
