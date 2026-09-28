# Minimal live status on web and Android

2026-09-28 18:57:21 Asia/Bangkok. User requests only `live` and `buffering` rather than verbose streaming/encoding/buffering descriptions; explicitly applies to web and app.

Updated shared app.js/copy-stream.js status output: playing=live; startup/wait/retry=buffering. Disabled viewing shows no status overlay. Removed verbose single-mode subtitle and overview transport explanation. Added empty-overlay hiding. Other controls, recording badges and error messages outside streaming overlays remain unchanged. No streaming parameters or lifecycle changes. Android consumes the same server player, so no APK rebuild needed.

JavaScript syntax,6 session lifecycle,3 layout ownership,3 role/fullscreen and7 packet/codec checks passed. No real browser/device/HTTP testing. Deployed app.js/app.css/copy-stream.js after timestamped backup under web-setup/GimDvr/backup-short-live-status-*; matching deployed/source hashes verified and publish staging updated. No restart or recording interruption. Normal browser refresh or app restart loads versioned assets. Notes/index updated and source prepared for main commit/push.
