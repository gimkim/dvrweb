# Shared copy stream in every viewing mode; controls restored;100ms fragments

Session started2026-09-28 18:42:33 Asia/Bangkok. User requests the working copy stream for overview and single-camera mode, plus camera controls back in overview. During implementation user additionally requests100ms fMP4 fragments, keeping200ms player buffers.

## Changes

- Both viewing modes use startCopyStream and overview watch leases. Single-camera viewing no longer starts the focus QSV/CPU encoder. Backend legacy focus routes remain for older clients; legacy leases expire normally after clients reload/leave.
- Overview restores a Control button for each enabled camera for operator/admin roles, bound to the existing draggable PTZ/advanced-settings panel. Viewer permissions, per-device viewing preferences and camera-name single-view navigation remain. No overview fullscreen button added.
- Single mode retains full-tab layout, fullscreen and automatic floating controls. Copy transport is video-only, so removed single-mode mute button and made fullscreen bindings handle its absence safely. Explained silent transport to user during implementation. Recording audio/settings are unchanged.
- FragmentCache uses100000us fragment duration instead of200000us, without re-encoding. All three player buffer thresholds remain200ms. Empty-avcC repair, startup keyframe gating, shared reader/cache and existing catchup policy remain.
- Version1.7.0. Updated current agent notes/README and this worklog. Existing APK receives changes via server assets; no rebuild.

## Verification

19 stream checks passed: artifacts/copy-checks-20260928-184408. Synthetic10seconds/GOP4seconds yields100fragments. Full and joined decoded frames match source; simultaneous recording/HLS/copy outputs, header repair, auth revocation, cancellation and cache bounds pass. Adapted next-keyframe handler fixture to derive publication sequence from actual keyframe rather than assuming200ms fragments.

6 lifecycle checks pass including identical single/overview copy endpoints and no focus-encoder watch requests even when overview preference is off.3 new pure-JavaScript rendering/binding checks pass for role-specific overview controls and fullscreen cleanup with no mute button.7 packet/codec checks and JavaScript syntax checks pass. These are local code fixtures, not browser harnesses. No production HTTP, real UI/device or latency tests.

Web and worker Release publish succeeded. Deploy completed18changed files with app_offline and preserved configuration hashes. Backup: `\\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-unified-copy-100ms-20260928-184425`. First hash check ran while file-copy retry was still in progress and failed; after deployment completion all web DLL/app.js/live-controls.js/copy-stream.js and worker DLL hashes matched. Offline marker removed. External data and recording configuration preserved. Web-owner restart can briefly interrupt recording; independent service activation not changed.

Normal reload/reopen loads versioned assets. Actual100ms publication cadence depends on camera frame timing and process scheduling;200ms buffer is not a guaranteed end-to-end latency. Source prepared for existing main commit/push.
