# Mobile recordings and detection capability review

2026-09-28 19:10:18–19:15 Asia/Bangkok. Request: enable mobile recorded playback on web and Android; investigate motion/human indicators per recording.

## Changes

Android shell hid the entire sidebar, including the only recordings link. Added shared app-only Live/Recordings navigation through existing data-page routing. Mobile website keeps its existing navigation. Recording search uses a single-column mobile layout, labelled file cards instead of a wide table, full-width touch actions, and a viewport-sized playback dialog with previous/next and automatic continuation. Existing camera-first selection, last-hour default, date range search, authenticated MP4 range playback and playsinline remain intact. Installed APK 1.0.1 receives shared assets; no native rebuild needed.

Affected: wwwroot/index.html, app.js, app.css; tests/recording-ui-checks.cjs; notes, README and worklog index.

## Detection findings

Current Store recordings schema and Recording model contain identity/path/time/duration/bytes, no event timeline or detection flags. Recorder maps video/audio only. CameraClient reads motion enable/sensitivity and optional HumanoidDetection configuration; configuration is not evidence that an event happened. No alarm ingestion currently links events to files. Existing clips therefore have unknown detection status, not confirmed no-motion.

Official https://www.vstarcam.com/business/sdk-download describes camera-to-cloud alarm integration and ONVIF as supported on some cameras. Current linked https://www.vstarcam.com/business/wp-content/uploads/2023/08/Alarm-push-interface-EN.pdf documents multipart alarm POST (vuid,type,picture,optional video), motion 0x12, humanoid 0x29, and get_alarm_push_cfg.cgi / set_alarm_push_cfg.cgi. This proves a vendor integration protocol exists, not that these three cameras support it. The previously attempted URL without /business/ returns404.

Historical initial worklog says the installed firmware did not return human-detection state. That historical result was not re-probed this session and does not prove lack of hardware human detection. No camera settings or alarm destinations were changed; no live camera/API/browser tests were performed. Further integration requires confirming each model/firmware's event interface, preserving existing push destinations, receiving events securely and associating timestamps/uncertainty with recording intervals. Push document has no event timestamp field, so receipt time/delivery delay must not be treated as exact camera event time. Historical files cannot acquire true camera-event labels without historical event data. No speculative badges or server AI added.

## Validation and deployment

Four new code-only fixture checks pass: camera-first/default hour; camera/time query and first-file seek; sequential/individual playback/end boundary; app navigation/inline video contract. Existing4 live-mode,3 layout and6 session checks pass; JS syntax and diff whitespace checks pass. No browser, emulator, device or production HTTP test.

Static deployment completed19:15:16, three source/NAS SHA256 pairs matched; publish staging synchronized. Backup: \\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-mobile-recordings-20260928-191516. No service restart or recording interruption. Normal reload/app reopen required. Mobile visual/playback behavior remains unverified on real devices. Source committed/pushed through normal main workflow.
