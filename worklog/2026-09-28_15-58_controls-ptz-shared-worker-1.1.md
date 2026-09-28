# 2026-09-28 15:58 +07 — version 1.1 controls, PTZ, shared media worker

Backfilled at: 2026-09-28T16:56:27+07:00
Source: [original archive, line 25](../worklogs/2026-09-28.md), section `2026-09-28 15:58 +07 — version 1.1 controls, PTZ, shared media worker`.
Original timestamp has minute precision only.
Retrospective copy, not new execution or verification. Later entries may supersede its decisions.

## Original entry

Request: floating camera controls, responsive hold-to-pan, independent background recording with one reused camera input.

Implemented modeless draggable/resizable control panel, collapsed advanced settings, viewport bounds, stale-open guard, pointer and keyboard hold/release handling. PTZ uses continuous onestep=0, latest desired direction, a 650 ms renewable lease, closed-token rejection, axis stop before direction changes, and shutdown/failure stops. This avoids queued delayed movements.

Live playback now uses one-second independent H.264 keyframes from the same FFmpeg input that records original video. Recording remains stream-copy with 60-second target segments. A trial using copied split_by_time segments caused black/stalled players and was rejected. Default live encoding is 1080p/15fps ultrafast; LiveHeight is configurable to lower CPU cost. Browser buffer lead measured about 1.25–2.25 seconds, not a measured end-to-end latency claim.

Added GimDvr.Worker Windows Service host, shared watch leases, atomic runtime status/heartbeat, web proxy-only mode, process ownership lock, and install/rollback script. Recorder uses one RTSP input for recording plus every viewer. Recording settings were not enabled or changed on real cameras.

Validation: 22 checks passed, including five PTZ lease/stop checks and four recorder lifetime/sharing checks using a fake FFmpeg process. 21 local API checks passed. Three actual local camera streams each had one established RTSP connection and played at 1920x1080, readyState 4. Browser verified modeless panel drag with all three streams visible; screenshot evidence/floating-controls-1.1.png. Actual perceived physical pan smoothness and production authenticated playback still require user observation. No production bootstrap login was attempted; earlier session automatic approval rejected that action.

Deployment: web version 1.1.0 published to the named NAS directory; production configuration preserved, published file hash mismatches=0. Backup: web-setup/GimDvr/backup-1.0-20260928-154645. Initial HTTP 500.30 was diagnosed from a fresh stdout log: actual application pool is now DVR, while data ACL granted DefaultAppPool. Resolved DVR SID using remote LookupAccountName, added only that pool Modify access using access-only ACL persistence, and restarted app via app_offline. HTTPS health now 200/version 1.1.0; anonymous /api/me returns 401. Diagnostic stdout disabled again. No database, user, credential, key, or camera setting replacement.

Worker binaries staged at C:/Users/tatsa/web-workers/GimDvr and installer at C:/Users/tatsa/web-setup/GimDvr/Install-RecorderService.ps1 on NAS. Remote service creation returned Access denied (5). Service is NOT installed or active; production configuration deliberately remains web-owner mode. User must run staged installer once in elevated PowerShell on NAS for IIS-independent recording. Installer file ACL handling and PowerShell syntax checked. Local web and worker test processes stopped at completion.
