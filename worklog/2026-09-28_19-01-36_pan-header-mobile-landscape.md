# Shared pan top row and mobile single-camera fullscreen

2026-09-28 19:01:36 Asia/Bangkok. User requests camera title/close button on the up-arrow row and automatic fullscreen landscape when entering single camera on mobile web and app.

CSS overlays the draggable title and close action into the left/right cells of the first pad row, keeping the up-arrow middle cell unobstructed via pointer-events. Removes the separate header row's vertical space while retaining44px touch height, name wrapping/truncation, dragging and PTZ bindings. Only applies once dpad exists; loading/error panel keeps its normal header.

openSingle now requests fullscreen synchronously after its in-place layout change on coarse-pointer devices or Android shell. Preserves transient click activation and existing video/session/buffer. Browser path requests fullscreen then screen.orientation.lock('landscape'); unlocks on fullscreen exit. Failed/unsupported orientation lock preserves fullscreen and provides manual-rotation button hint. Android uses existing native bridge, which hides bars and requests SensorLandscape; exit restores portrait. Existing installed APK loads shared JS; no native rebuild needed. Open control panel is restored inside the fullscreen subtree instead of silently closing.

Limit: browsers may reject fullscreen or orientation locking, especially outside a user gesture or on unsupported platforms. Full-tab single view remains available; no promise of forced landscape where the browser disallows it. Consulted primary API references: https://developer.mozilla.org/en-US/docs/Web/API/Element/requestFullscreen and https://developer.mozilla.org/en-US/docs/Web/API/ScreenOrientation/lock . Direct deep links/history are not fabricated tap gestures; automatic request is bound to camera-name tap.

Validation:4 new code-only tests cover synchronous tap ordering, browser fullscreen-before-lock/unlock, rejected lock fallback and native landscape/portrait bridge.3 role/fullscreen,3 layout/session ownership,6 session lifecycle checks and JS syntax/diff checks pass. No browser, emulator, device or production HTTP tests performed.

Deployed app.js/app.css/live-controls.js with timestamped backup under web-setup/GimDvr/backup-mobile-landscape-*, verified source/deployed hashes and updated publish staging. No server restart, recording interruption or stream parameter changes. Notes/index updated; source prepared for existing main commit/push. Normal reload/app reopen receives assets.
