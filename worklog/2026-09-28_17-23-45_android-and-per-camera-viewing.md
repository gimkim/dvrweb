# Per-camera viewing and Android 1.0.0 / web 1.4.0

- Time: 2026-09-28T17:23:45.005106+07:00
- Request: a viewing toggle before each camera name, persisted across sessions; a fixed-server Android APK with persistent login, vertically stacked cameras, independent viewing toggles, landscape fullscreen and movable controls.

## Implementation and decisions

Web viewing preference is localStorage `gimdvr.viewing.<user-id>` keyed by camera ID, per user and device/browser. Missing entries default on; disabled camera stays disabled. A viewing toggle destroys only that camera's HLS player, pending retry, event listeners and watch heartbeat. It never edits camera Enabled/RecordingEnabled. Last-viewer encoder shutdown still follows the existing server grace period; other viewers and recording readers are unaffected. Cancellation during a pending watch request cannot resurrect the stream; late failures after cancellation are ignored.

Fullscreen uses the existing video wrapper. Operators/admins have an additional control-panel button within fullscreen. The same movable modeless dialog is reparented into the fullscreen wrapper and restored to body before page cleanup. Native live timeline/play/pause remain absent. HLS encoding, QSV settings, quality/bitrate and recording pipeline are unchanged.

Android is a .NET 10 Android Activity with a WebView client using the same authenticated web UI/API, pinned to https://gimgim.ddns.net/gimdvr/. There is no server selector. It allows only that HTTPS origin/path for navigation, rejects TLS errors and cleartext, disables file/content/third-party-cookie access and backups, and exposes only fullscreen/cookie-flush bridge methods. The APK contains no camera or user credentials. Portrait view is one camera per row; app fullscreen uses CSS plus native sensor-landscape and immersive system bars, restoring portrait on exit/back. Insets protect controls from system bars/display cutouts. Pause/background stops web live sessions, resume restarts only selected cameras. Screen remains awake while Activity is visible.

Android login requests RememberDevice: a persistent HttpOnly server cookie with a ten-year server ticket; web login remains 14-day sliding. CookieManager flushes login/logout and lifecycle state. No password is saved. Session survives normal app closes; logout clears it, and password/role/account changes invalidate it through the existing stamp check. Platform cookie expiration, clearing app data, uninstall, lost server keys or account revocation can still require login; this is not literally an eternal session. Viewing preferences survive logout and are separated by account. Failed logout shows an error instead of pretending server logout succeeded.

## APK / build

Package net.gimgim.gimdvr, version 1.0.0 (code 1), Android 8+ (min 26), target 36, arm64-v8a/armeabi-v7a/x86_64. Release APK size 10,955,723 bytes.
SHA256: 1185371B424F06BCECC6A1C16AC06396F9D3789527EED5B37C06C32452766C2F
Signing certificate SHA256: 992eba9d4164190f04f805a60e30770c16e29987628ae667a08cde9f230a341b

Private signing material remains in ignored .local-data/android-signing (gimdvr.keystore and store.pass); reuse it for future updates and never commit it. deployment/Build-Android.ps1 publishes then signs with apksigner, verifies signature and prints hash. Initial MSBuild signing failed because apksigner read the same password file twice and reached EOF; a subsequent incremental publish incorrectly skipped signing. The final build script explicitly signs and verifies the final APK, omitting duplicate --key-pass for PKCS12. Do not treat a successful incremental publish as signature evidence.

## Verification

- 33 existing .NET checks passed; web and worker Release publish passed.
- 4 Node lifecycle checks passed: cancellation while watch pending, late failure after stop, isolated stop, replacement/shutdown cleanup.
- JavaScript syntax checks passed.
- Local isolated backend checks: web cookie about 14 days, Android ticket about 3653 days; cookie restored in a new HTTP client authenticated; logout cleared it and /me returned 401. No production bootstrap credentials were read.
- Browser tested production JS/CSS with a local fixture API and synthetic HLS: Garage switch off survived reload and navigation; Garage watch count stayed 2 and manifest count 4 while Front/Side watch counts rose to 40. Browser fullscreen control panel appeared and dragged from x1695 to x305. Mobile layout at 412x892 stacked camera cards at y72/421/770 with x12; simulated native bridge at 892x412 showed fullscreen control and dragged to x129/y27. This tests web UI/lifecycle, not Android native orientation or sustained camera playback. The synthetic fixture recycles short segments and is not a smoothness benchmark.
- Release APK verified with apksigner v2/v3; zipalign 16-KiB check passed; aapt confirmed package/version/minSDK/3 ABIs/Internet permission.
- No Android device or emulator image was available. Installation, native rotation/back/pause-resume and WebView cookie persistence on a physical device remain unverified.

## Deployment

Deployed web + worker binaries to the established NAS paths under app_offline. Existing appsettings hashes were preserved; external data/configuration was not changed. Backup: web-setup/GimDvr/backup-android-views-20260928-172111. Web-owner mode still means an update can briefly interrupt recording. After deployment all three runtime states reported Recording=true with fresh timestamps.

Health returned HTTP 200/version 1.4.0; deployed DLL/app.js/app.css/live-controls.js hashes matched local publish; HTML no-store and 5 versioned URLs; unauthenticated camera API remained 401. APK HTTPS download returned 200/application/vnd.android.package-archive and the exact SHA256 above.
Download: https://gimgim.ddns.net/gimdvr/downloads/GimDVR-1.0.0.apk
No authenticated production-browser or physical Android test was performed.

## Files

src/GimDvr.Android/, deployment/Build-Android.ps1, Models.cs, Program.cs, wwwroot/app.js/app.css/live-controls.js, tests/live-session-checks.cjs, .gitignore, README.md, AGENT_NOTES.md and this worklog/index. No camera credentials, signing key, APK, private footage or test data belongs in Git.
