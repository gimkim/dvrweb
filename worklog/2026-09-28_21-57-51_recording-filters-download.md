# Recording filters and downloads (1.9.2)

Recorded 2026-09-28 21:57:51 +07. User requests Motion/Human checkboxes in recording results and download buttons in both results and clip playback.

Added client-side filters across the entire fetched date range: no selection shows all; either checkbox requires that positive detection; both use OR. Unknown/negative results do not match. Delayed metadata refresh reapplies filters without replacing the video or resetting playback. Play-all and previous/next follow filtered indices, while original records remain stable. Empty filtered results disable play-all. Compact SVG download link per row and a labelled download link in the player point at a new authenticated catalog-ID endpoint. Server returns the original MP4 as an attachment, with range support and a generated timestamp filename; no re-encoding, paths or camera credentials are exposed.

Affected Program.cs, DetectionLog.cs version, app.js, app.css, index.html and recording-ui-checks.cjs. Ten code-only UI checks passed, including OR semantics, unknown exclusion, playlist navigation, selected-clip download and endpoint authorization policy smoke check; all47 existing backend checks passed. Release web and worker publish succeeded. JS syntax and git diff checks passed. No browser/device/production HTTP testing performed. Download endpoint response behavior relies on ASP.NET File result; it was build/policy checked, not exercised with production footage.

Deployed NAS web assets/binaries and worker binaries. Deployment hashes matched, production configuration preserved and app_offline removed. Backup: \\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-detection-20260928-215718. External DB backed up/preserved; MotionService untouched. IIS update can briefly interrupt recording. Existing pages require normal reload; asset hash versioning handles cache updates.

This request changes the website/shared results UI. Existing Android shell receives filters, but native Android download handling is not implemented in APK1.0.1; no APK build or claim of Android download support in this session. Source, notes and worklog committed/pushed to main.
