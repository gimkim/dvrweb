# Apparent detection queue stall

Request: diagnose and fix why the queue appears to have stopped. Investigation began 2026-09-28 20:25:29 +07:00.

## Evidence and cause

Read the current NAS detection JSONL, current service/scheduler, and recording-results UI. Process 8200 is still running: 35 complete at 20:24:55 versus 20 at 20:20:11 in the previous review. Queue unresolved decreased to 700. Full-minute results continued at 20:25:12 and 20:27:10 (17.381 and 17.609 seconds, GPU/D3D11VA). No worker stall was observed. Six historic short-file errors still retry separately and do not block later work; their specific cause remains unresolved.

The UI only fetched detection metadata during an explicit search. Open search results never reflected subsequently completed analysis, making the queue appear stationary. Large historical backlog and modest processing headroom remain separate issues; this change does not increase worker throughput or claim that all queued files are complete.

## Fix

Shared web/Android app.js now polls the captured recording range every 10 seconds while results contain unfinished analysis. It updates existing badge nodes and a compact completed X/Y count, retaining the playlist, dialog, video source and playback position. Sequential requests avoid overlap. Filter/search changes, navigation and logout invalidate old requests and cancel the timer. Hidden documents skip requests; transient failures retry without disrupting playback. Polling stops when all displayed results are complete. Range boundaries remain those explicitly searched, not a moving time window.

README and agent notes describe the new refresh contract. No backend sampling, scheduler, runtime/model or recording settings were changed. Existing APK uses shared assets and needs no rebuild.

## Validation and deployment

- Code-only recording fixture: 9 checks passed, including delayed-result update, completion stop, playback preservation, cancelled scheduled work, navigation, and stale in-flight response rejection.
- Live-layout 3 checks and live-session 6 checks passed; JavaScript syntax and git diff checks passed.
- Static app.js deployed at 20:27:40 to NAS wwwroot; source/deployed SHA256 matched. Backup: `\\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-detection-refresh-20260928-202740`.
- No IIS/worker restart or database/settings changes. Existing asset URL content-hash versioning detects this change; already-open clients must reload once normally.
- No production HTTP, browser/device UI, or private-footage test performed. Runtime log evidence is passive worker observation, not proof of rendered UI behavior.
