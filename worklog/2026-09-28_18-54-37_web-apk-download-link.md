# APK download links on website

2026-09-28 18:54:37 Asia/Bangkok. User requests an APK download link on the website.

Added versioned GimDVR1.0.1 APK download anchors below login and in the signed-in header. Relative downloads/GimDVR-1.0.1.apk works under the existing /gimdvr/ path, with download attribute. Added compact accessible link styling; hidden inside the installed Android shell where WebView has no APK download handler. Existing fullscreen layout hides header as before.

Code smoke check confirms both anchors and existing NAS APK file. Deployed index.html/app.css only after backup in web-setup/GimDvr/backup-apk-link-*, updated publish staging and verified source/deployed hashes. No restart, recording interruption, credential/config changes or APK rebuild. No browser/production HTTP tests per policy. Normal reload receives versioned assets. Notes/index updated; source prepared for main commit/push.
