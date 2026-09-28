# Progressive recording search

Recorded 2026-09-28_22-03-05 +07. User requests camera selection before revealing date/time selection.

Shared web/Android recordings UI initially shows only camera selection. Time fields, last-hour shortcut, search button, time hint and results area are hidden until a camera is selected. Selection reveals the next step with the existing last-hour defaults; clearing selection hides/disables it again. Existing search/reset logic remains. Explicit hidden CSS overrides mobile grid and toolbar display rules.

Updated app.js, app.css and existing recording UI fixture. JS syntax, ten code functional/smoke checks and diff checks passed. NAS static files backed up at \\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-recording-steps-2026-09-28_22-03-05 and replaced with matching SHA256. No server restart, APK build, browser/device or production HTTP testing. Normal reload picks up asset versions. Notes and worklog committed/pushed.
