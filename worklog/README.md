# Worklog index

บันทึก GimDVR แยกหนึ่งไฟล์ต่อ session/request เวลา Asia/Bangkok (+07:00) อ่าน [agent notes](../AGENT_NOTES.md) และ [กติกา](../AGENTS.md) ใช้ [_template.md](_template.md) สำหรับไฟล์ใหม่

## งานล่าสุด

- [2026-09-28_20-19-59_detection-log-review.md](2026-09-28_20-19-59_detection-log-review.md) — passive NAS log review: GPU confirmed, approximately 3.01x throughput with limited margin

- [2026-09-28_20-10-23_nas-detection-diagnostics.md](2026-09-28_20-10-23_nas-detection-diagnostics.md) — persistent NAS timing/throughput/queue JSONL diagnostics

- [2026-09-28_20-07-24_detection-throughput-boundary.md](2026-09-28_20-07-24_detection-throughput-boundary.md) — three-camera throughput threshold and pending NAS initialization

- [2026-09-28_19-50-36_nas-detection-compact-recordings.md](2026-09-28_19-50-36_nas-detection-compact-recordings.md) — NAS motion/person worker, SQLite results, compact web/app badges

- [2026-09-28_19-47-25_nas-detection-feasibility.md](2026-09-28_19-47-25_nas-detection-feasibility.md) — low-load NAS motion/person detection design and limits

- [2026-09-28_19-20-49_eye4-reverse-engineering.md](2026-09-28_19-20-49_eye4-reverse-engineering.md) — Eye4 static protocol research, local/cloud/SD alarm sources

- [2026-09-28_19-10-18_mobile-recordings-detection-review.md](2026-09-28_19-10-18_mobile-recordings-detection-review.md) — mobile recordings navigation/layout; vendor event capability review

- [2026-09-28_19-08-32_fullscreen-direct-back.md](2026-09-28_19-08-32_fullscreen-direct-back.md) — one-tap fullscreen back to overview on web/app

- [2026-09-28_19-01-36_pan-header-mobile-landscape.md](2026-09-28_19-01-36_pan-header-mobile-landscape.md) — shared pan header/up row; mobile single-view fullscreen landscape

- [2026-09-28_18-59-10_compact-mobile-controls.md](2026-09-28_18-59-10_compact-mobile-controls.md) — compact borderless floating controls on mobile/web/app

- [2026-09-28_18-57-21_minimal-live-status-web-android.md](2026-09-28_18-57-21_minimal-live-status-web-android.md) — live/buffering only on web and Android

- [2026-09-28_18-56-18_mobile-power-icon-svg.md](2026-09-28_18-56-18_mobile-power-icon-svg.md) — SVG viewing switch icon independent of mobile fonts

- [2026-09-28_18-54-37_web-apk-download-link.md](2026-09-28_18-54-37_web-apk-download-link.md) — APK download links on login and signed-in header

- [2026-09-28_18-51-04_android-shared-web-stream.md](2026-09-28_18-51-04_android-shared-web-stream.md) — Android1.0.1 shared web streaming player, signed APK update

- [2026-09-28_18-48-54_preserve-live-layout-sessions.md](2026-09-28_18-48-54_preserve-live-layout-sessions.md) — preserve players and background streams across live layouts

- [2026-09-28_18-42-33_unified-copy-controls-100ms.md](2026-09-28_18-42-33_unified-copy-controls-100ms.md) — shared copy stream for all viewing modes, overview controls,100ms fragments /200ms buffers

- [2026-09-28_18-41-08_overview-buffer-200ms.md](2026-09-28_18-41-08_overview-buffer-200ms.md) — overview startup/rebuffer/live target set to200ms; static deployment

- [2026-09-28_18-39-26_buffer-latency-explanation.md](2026-09-28_18-39-26_buffer-latency-explanation.md) — current buffer settings and latency limits; explanation only, no deployment

- [2026-09-28_18-37-37_copy-stream-empty-avcc-fix.md](2026-09-28_18-37-37_copy-stream-empty-avcc-fix.md) — repair empty H.264 initialization from in-band SPS/PPS, version1.6.1

- [2026-09-28_18-28-23_continuous-copy-fmp4.md](2026-09-28_18-28-23_continuous-copy-fmp4.md) — continuous copy-only fMP4 overview 1.6.0; local functional tests and NAS file deployment

- [2026-09-28_18-12-29_keyframe-start-latency-design.md](2026-09-28_18-12-29_keyframe-start-latency-design.md) — keyframe startup versus steady latency; explanation only

- [2026-09-28_18-11-23_reduce-overview-player-delay.md](2026-09-28_18-11-23_reduce-overview-player-delay.md) — reduce copy-only overview target delay; code tests only

- [2026-09-28_18-05-36_code-tests-only-policy.md](2026-09-28_18-05-36_code-tests-only-policy.md) — user policy: code smoke/functional tests only

- [2026-09-28_18-04-54_overview-copy-and-focus-qsv.md](2026-09-28_18-04-54_overview-copy-and-focus-qsv.md) — overview video copy and single-camera low-latency QSV 1.5.0

- [2026-09-28_17-23-45_android-and-per-camera-viewing.md](2026-09-28_17-23-45_android-and-per-camera-viewing.md) — Android APK 1.0.0 and per-camera viewing, web 1.4.0

- [2026-09-28_17-04-19_public-github-dvrweb.md](2026-09-28_17-04-19_public-github-dvrweb.md) — public GitHub repository
- [2026-09-28_17-02-13_automatic-asset-url-versioning.md](2026-09-28_17-02-13_automatic-asset-url-versioning.md) — automatic asset URL versioning 1.3.4
- [2026-09-28_16-58-45_live-video-minimal-controls.md](2026-09-28_16-58-45_live-video-minimal-controls.md) — live UI controls; static deployment
- [2026-09-28_16-56-27_agent-notes-worklog-backfill.md](2026-09-28_16-56-27_agent-notes-worklog-backfill.md) — documentation; ไม่มี deploy

## บันทึกย้อนหลัง 2026-09-28

คัดแยกจาก [archive เดิม](../worklogs/2026-09-28.md) เมื่อ 2026-09-28T16:56:27+07:00 ครบ14หัวข้อ ไม่แก้ archive รายการเรียงตามต้นฉบับซึ่งเวลาอาจไม่เรียงและบางงานไม่ระบุเวลา ไม่ใช่การตรวจยืนยันใหม่

- [2026-09-28 — Initial implementation](2026-09-28_time-unknown_initial-implementation.md)
- [Final verification and deployment — 2026-09-28](2026-09-28_time-unknown_initial-verification-deployment.md)
- [2026-09-28 15:58 +07 — version 1.1 controls, PTZ, shared media worker](2026-09-28_15-58_controls-ptz-shared-worker-1.1.md)
- [2026-09-28 15:55:58 +07 — remove password length policy](2026-09-28_15-55-58_password-policy-1.1.1.md)
- [2026-09-28 15:59:30 +07 — snapshot fix 1.1.2](2026-09-28_15-59-30_snapshot-1.1.2.md)
- [2026-09-28 16:01:39 +07 — live video stream copy 1.1.3](2026-09-28_16-01-39_video-copy-1.1.3.md)
- [2026-09-28 16:11:39 +07 — on-demand shared low-latency video 1.2.0](2026-09-28_16-11-39_low-latency-1.2.0.md)
- [2026-09-28 16:18:32 +07 — damaged recorded MP4 repair 1.2.1](2026-09-28_16-18-32_recording-repair-1.2.1.md)
- [2026-09-28 16:22:03 +07 — Intel Quick Sync live encoding 1.2.2](2026-09-28_16-22-03_quick-sync-1.2.2.md)
- [2026-09-28 16:26:19 +07 — camera/time-range playback 1.2.3](2026-09-28_16-26-19_playback-range-1.2.3.md)
- [2026-09-28 16:33:25 +07:00 — Quick Sync browser initialization fix 1.2.4](2026-09-28_16-33-25_aac-browser-fix-1.2.4.md)
- [2026-09-28 16:39:38 +07:00 — QSV pacing investigation and CPU mitigation 1.2.5](2026-09-28_16-39-38_pacing-cpu-mitigation-1.2.5.md)
- [2026-09-28 16:44:15 +07:00 — buffering instead of aggressive latency chasing 1.2.6](2026-09-28_16-44-15_player-buffer-1.2.6.md)
- [2026-09-28 16:51:40 +07:00 — restore HLS with on-demand speed-focused Quick Sync 1.3.2](2026-09-28_16-51-40_hls-on-demand-qsv-1.3.2.md)

## วิธีเพิ่มงานครั้งต่อไป

สร้าง YYYY-MM-DD_HH-mm-ss_topic.md ใหม่ทุกงาน ไม่รวมเป็นไฟล์รายวัน เพิ่มลิงก์ในงานล่าสุด บันทึกคำขอ/การแก้ไข หลักฐาน deployment และสิ่งที่ยังไม่ยืนยัน การแก้ข้อมูลเก่าเป็น entry ใหม่อ้างไฟล์เดิม

- [2026-09-28_19-14-56-camera-stream-options.md](2026-09-28_19-14-56-camera-stream-options.md) — camera main/substream options and current copy-stream clarification
