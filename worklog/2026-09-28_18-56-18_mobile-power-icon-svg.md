# Mobile power icon missing glyph

2026-09-28 18:56:18 Asia/Bangkok. User screenshot from mobile Brave shows outlined missing-glyph boxes inside camera viewing toggles. Code used Unicode U+23FB, relying on the device font to draw it.

Replaced that character with inline SVG power-symbol geometry using currentColor. Added fixed-size, nonshrinking centered layout and pointer-events:none on the SVG. Existing button role=switch, aria-checked, camera label, enabled state and click handler remain intact. No new assets/fonts/dependencies or streaming changes.

JavaScript syntax,3 role/fullscreen checks,3 layout ownership checks and6 lifecycle checks passed. Diff whitespace check passed. No browser/physical-device testing performed; screenshot is user-provided evidence, not a new UI test.

Deployed app.js/app.css only after backup under web-setup/GimDvr/backup-power-svg-*; verified matching source/deployed hashes and updated publish staging. No server restart or recording interruption. Normal reload/app reopen loads versioned assets; APK rebuild unnecessary. Notes/index updated and source prepared for commit/push.
