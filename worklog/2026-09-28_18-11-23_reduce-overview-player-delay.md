# Reduce overview player delay (static patch on 1.5.0)

Time: 2026-09-28T18:11:23.213255+07:00

## Request and evidence
User reports copy-only overview latency greater than ten seconds and asks to reduce it. Followed the code-only test policy: no browser, browser harness or production HTTP test was performed. Read the existing runtime Overview paths and existing generated playlist text through SMB for diagnosis only. All three playlists declared TARGETDURATION4 and six EXTINF4.000000 entries. No new camera connection or test viewer was started.

Source overview player had liveSyncDurationCount2 (8 seconds for these actual segments), liveMaxLatencyDurationCount6 (24 seconds), maxLiveSyncPlaybackRate1 (no speed catch-up), maxBufferLength8. Waiting for the next completed4-second segment adds publication delay. This explains a plausible source of the reported >10 seconds but is not a measured camera-to-screen breakdown. Bundled hls.js liveSyncPosition code clamps to at least one target-duration behind the published edge; simply setting a subsecond liveSyncDuration would not overcome these4-second segments.

## Change
Overview now liveSyncDurationCount1 (4-second target with these playlists), maxLatencyDurationCount2, liveSyncOnStallIncrease0, maxLiveSyncPlaybackRate1.1, maxBufferLength4, backBufferLength4. The player can catch up gently and no longer grows its target latency after stalls. Kept one-segment reserve to avoid forcing a1-second reserve against bursts only every4seconds, which would cause repeated starvation.

No encoder, camera GOP/configuration, muxer, recording, focus-stream settings, or APK changes. Video is still stream-copy. This removes four seconds from configured overview target delay but does not prove actual end-to-end delay fell by exactly four seconds. Conventional complete-segment HLS still has a4-second boundary and publication delay; it cannot promise sub1-2second end-to-end delay with this pipeline. Further reductions would need shorter source GOP or a different incremental/real-time delivery path, with separate implementation and validation.

## Tests and deployment
node --check passed. Six local Node lifecycle/config checks passed, including the4-second segment regression case, no automatic stall-target growth, isolated stop/reconnect, and unchanged0.7-second focus configuration. git diff --check passed. No UI or live-web test occurred.

Static app.js deployed only; no app_offline/restart or recording interruption requested. Backup: web-setup/GimDvr/backup-overview-buffer-20260928-181034. Source/publish/NAS file SHA256: 420F30169E417D287028FAF00C71A88381455F77EAEF81EF8A0CAE9E0E3E7C4F. Existing asset hash versioning makes a normal reload/reopen pick it up; an already-open page retains its existing player until reloaded. Server health version remains1.5.0 because no backend binary changed.

Updated AGENT_NOTES/README/worklog index; private runtime data was not committed. Actual perceived latency remains for user validation, not claimed tested.
