# Keep camera controls hidden on fullscreen entry

User request: entering fullscreen should not immediately display the camera control panel. Changed app.js so single-camera layout only mounts the closed dialog; fullscreen transitions close/reparent it without reopening it. Explicit Control buttons still open it. Shared website code serves Android WebView too; no APK rebuild needed. Camera streaming sessions and fullscreen/orientation logic unchanged.

Code validation: node --check app.js;3 live-layout fixture checks and5 mobile-fullscreen fixture checks passed, covering operator camera switches without auto-opening, retained stream sessions, browser/Android landscape bridge, closed fullscreen transition and explicit reopening. git diff --check passed. No actual browser/device testing per user policy.

Deployed only wwwroot/app.js to NAS. Backup: web-setup/GimDvr/backup-manual-fullscreen-controls-20261003-122737. Source/deployed SHA256 match C57BD9CADE3BC841F61B47336501744CCB1AC0CC46827A7E1447105EBBD757C3. No backend rebuild, app restart, camera/config/database changes. Existing AssetVersions checks file modification/length and computes content hash, so ordinary refresh loads updated script. User-visible browser behavior remains untested.
