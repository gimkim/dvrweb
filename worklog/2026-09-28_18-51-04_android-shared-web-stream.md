# Android1.0.1: same server-owned streaming player as web

Started2026-09-28 18:51:04 Asia/Bangkok. User requests updating the Android app to use the same streaming system as the website.

Inspection found the app already loads the fixed HTTPS GimDVR site in Android WebView; it contains no independent native/HLS player or bundled stale JavaScript. The current server player therefore supplies shared copy-only100ms fMP4/200ms buffers in both modes, floating controls and in-place layout switching that preserves other camera sessions. Do not misdescribe this as replacing a separate native stream implementation.

For the requested app update, set WebView CacheMode.NoCache so server-owned player assets are loaded without local cache reuse. Retain cookies, DOM storage, user-agent Android identification, autoplay policy, origin restrictions and TLS validation. No cookie/preference clearing. Bump ApplicationVersion to2, ApplicationDisplayVersion to1.0.1 and UA suffix to1.0.1. Background app suspend still releases sessions and resume rebuilds them; live layout/fullscreen transitions keep the shared web behavior. No new camera connections or encoder implementation in the APK. Live copy stream remains video-only, as on the current website.

## Validation and artifact

- Release Android publish using existing Build-Android.ps1 succeeded for arm64-v8a/armeabi-v7a/x86_64, Android8+.
- apksigner v2/v3 verification passed; signing certificate SHA256 matches previous1.0.0 APK:992eba9d4164190f04f805a60e30770c16e29987628ae667a08cde9f230a341b. Supports in-place upgrade under normal Android package rules.
- zipalign16KiB check passed; aapt confirms net.gimgim.gimdvr/versionCode2/versionName1.0.1/minSDK26/targetSDK36.
-6 stream lifecycle,3 layout ownership and7 packet/codec code checks passed. git diff check performed. No real browser, emulator or physical-device tests, no production HTTP calls. Device playback, rotation and installed-session continuity remain unverified.
- Signed artifact: artifacts/android/GimDVR-1.0.1.apk. SHA256:0CD154FDBF9874E9B09FBB4532BFCDE5757151524206F842C4FAD0717DC3C8D8.
- Copied new versioned APK to NAS wwwroot/downloads/GimDVR-1.0.1.apk and verified SHA256 via SMB. Existing1.0.0 download preserved. No web binary restart, recording interruption or external configuration changes. APK/keystore/passwords remain ignored and uncommitted.

Download location: https://gimgim.ddns.net/gimdvr/downloads/GimDVR-1.0.1.apk . Install over the existing app; do not uninstall if retaining its local state. Notes/README/index updated and source prepared for commit/push.
