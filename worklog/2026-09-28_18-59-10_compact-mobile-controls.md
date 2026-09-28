# Compact floating camera controls

2026-09-28 18:59:10 Asia/Bangkok. User reports excessive borders and wasted space in controls on mobile.

Scoped CSS changes to #controls: remove outer/nested pad-button borders, reduce panel padding14→8px and gaps, soften shadow, use a single translucent surface and transparent directional buttons. Default collapsed width240px; coarse-pointer/mobile216px capped to viewport minus16px. Opening settings expands to310px desktop/300px touch. Remove old280px minimum; disable resize affordance on touch. Keep48x44px directional hit areas, pressed feedback and keyboard focus rings. Title text is camera name only, hint shortened, settings summary shortened. Empty control error block consumes no space. Dragging, hold/release/stop semantics and advanced settings remain.

Applies to shared web and Android content. JavaScript syntax plus3 role/fullscreen and3 layout ownership functional checks pass; diff check passes. No new tests for reversible CSS/text changes; no browser/physical-device testing. Visual layout remains unverified on hardware per user's policy.

Static app.js/app.css deployed after backup under web-setup/GimDvr/backup-compact-controls-*, source/deployed hashes verified and publish staging updated. No restart, recording interruption, stream change or APK rebuild. Normal refresh/reopen loads versioned assets. Notes/index updated and source prepared for main commit/push.
