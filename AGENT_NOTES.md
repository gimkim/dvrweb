# GimDVR — Agent notes

ปรับปรุง: 2026-09-28 (Asia/Bangkok) — สถานะออกแบบล่าสุด 1.8.0 (copy-only fMP4 ทุกโหมด, 100ms fragments / 200ms buffer)

เอกสารนี้สรุป concept และหลักการปัจจุบัน ต้องอ่านคู่กับ [AGENTS.md](AGENTS.md) และ [ดัชนี worklog](worklog/README.md) รายละเอียดการทดลองเก่าไม่ใช่ข้อกำหนดปัจจุบัน เมื่อผู้ใช้เปลี่ยนแนวทางให้แก้สรุปนี้และสร้าง worklog ไฟล์ใหม่

## เป้าหมายและขอบเขต

- เว็บ .NET 10 บน Windows NAS / IIS ที่ `https://gimgim.ddns.net/gimdvr/` เครื่อง NAS เป็น Intel N100 และผู้ใช้ดูผ่าน LAN 2.5 Gbps
- กล้อง VStarcam เริ่มต้น Garage, Front door, Side; เพิ่มกล้องในอนาคตได้ กล้องและรหัสผ่านต้องอยู่หลัง server proxy ห้ามให้ browser ติดต่อกล้องโดยตรง
- จัดการผู้ใช้ admin/operator/viewer เองได้ รหัส admin เริ่มต้นอยู่ใน bootstrap.txt นอก web root; ห้ามคัดลอกรหัสจริงลง note/log/repository ไม่มีข้อบังคับความยาวรหัสผ่าน แต่ต้องไม่ว่าง ยังมี hashing, login throttling และการยกเลิก session เมื่อสิทธิ์เปลี่ยน
- ทุกกล้องเริ่มต้นไม่บันทึกจนผู้ใช้กำหนด folder และเปิดบันทึกเอง ปัจจุบันตามการตรวจครั้งล่าสุดผู้ใช้เปิดบันทึกทั้งสามกล้องแล้ว ห้ามนำ default ไปทับค่าของผู้ใช้

## ภาพสด: แบบที่ผู้ใช้เลือกสุดท้าย (1.7.0)

```text
Camera RTSP → reader หนึ่งตัวต่อกล้อง → บันทึก MP4 (video copy)
                                  → fMP4 100ms (video copy, ไม่มีเสียง) → authenticated continuous proxy
                                  → loopback MPEG-TS relay
                                      → legacy QSV/HLS endpoints (UI ปัจจุบันไม่ใช้)
                                          → short-segment HLS → authenticated web proxy
```

- ทั้งหน้ารวมและโหมดเดี่ยวใช้ต้นฉบับ H.264 remux เป็น fMP4 ชิ้นประมาณ100ms โดย -c:v copy -an ไม่ encode video/resize ลดงาน NAS; browser ยังต้อง decode ภาพเอง งานบันทึก/relay ยังมี AAC encode เดิม ไม่อ้างว่าCPUเป็นศูนย์
- หน้ารวมมีปุ่มดูเปิด/ปิด ชื่อกล้องเปิดโหมดเดี่ยว และปุ่มควบคุมรายกล้องสำหรับoperator/admin เปิดแผงลอยPTZ/advancedsettings; ไม่มีfullscreenในหน้ารวม การตั้งค่าการเชื่อมต่อ/บันทึกอยู่หน้า management
- ตั้งแต่1.6.1 ต้องมีavcC/SPS/PPSครบก่อนpublishinit: delay_moovอย่างเดียวไม่พอกับVStarcamชุดนี้ จึงเติมavcCว่างจากSPS/PPSในkeyframeแรก โดยไม่แก้encodedframes; JSต้องตรวจขอบเขตและSPS/PPSก่อนสร้างcodec string ([worklog](worklog/2026-09-28_18-37-37_copy-stream-empty-avcc-fix.md))
- Readerเดิมสร้างfMP4บนstdoutและcacheร่วมกัน ขอบเขต128fragments/64MiB; ไม่เปิดRTSPหรือencoderใหม่ต่อviewer ส่วนHLScopyยังคงไว้สำหรับsnapshot
- ผู้ชมใหม่รอkeyframeถัดไป แล้วส่งfragmentต่อเนื่องรวมdependentframes ไม่รอครบGOP; MediaSource target0.2s/start0.2s/rebuffer0.2s (ปรับตามผู้ใช้2026-09-28; worklog/2026-09-28_18-41-08_overview-buffer-200ms.md), catchup1.05x, seekเมื่อเกิน2s ตรวจสิทธิ์ซ้ำทุก2sและยกเลิกfetchเมื่อหยุดดู ค่าเหล่านี้ไม่ใช่การรับประกันend-to-end latency ([worklog](worklog/2026-09-28_18-28-23_continuous-copy-fmp4.md))
- โหมดเดี่ยวเต็มพื้นที่tab มี Back/fullscreen และcontrolลอยอัตโนมัติสำหรับoperator/admin; ขยายvideoelementเดิมด้วยCSS ไม่stop/reconnectสตรีมตอนสลับโหมด กล้องอื่นคงsessionเบื้องหลัง; กล้องที่ปิดดูไว้จะเปิดชั่วคราวเมื่อเลือกเดี่ยวและหยุดเฉพาะตัวนั้นเมื่อออก ไม่แก้ค่าการดูที่จำไว้ ทุกโหมดส่งภาพเท่านั้นจึงไม่มีปุ่มเสียงสด
- UIทั้งสองโหมดส่งoverviewleaseและใช้/api/cameras/{id}/copy-streamเดียวกัน ไม่เริ่มQSV/CPUencoder; legacyfocusendpointยังอยู่สำหรับclientเก่าและหยุดencoderเมื่อleaseหมด8s
- Legacy QSV (UIปัจจุบันไม่ใช้): veryfast, async_depth1, lookahead0, Bframes0, 1080p15fps, GOP8 (~0.533s), target/max4Mbps, VBV1Mbit, low_delay_brc1; decode/scaleยังCPU; probeและCPUfallbackultrafast/zerolatencyยังอยู่และหน้าเดี่ยวบอกencoderจริง
- Legacy Focus HLS (UIปัจจุบันไม่ใช้): target0.5s/list12 (NASจริง0.533333s); player liveSync0.7s, maxLatency2s, maxBuffer1.5s, backBuffer1s, catchup≤1.1x เป็นclassicHLSsegmentสั้น ไม่ใช่LL-HLS partial segments และไม่รับประกันcamera-to-screen<1s
- Legacy clients: หนึ่งencoderต่อกล้องที่ถูกfocus ไม่ใช่global one-camera lock; คนละtab/userเลือกคนละกล้องได้ และswitchอาจoverlapช่วงgrace
- Snapshotใช้overviewTSล่าสุดแล้วJPEG ไม่เริ่มfocusencoderเพิ่ม; recordingargumentsเดิมคงไว้
- Source/FFmpeg checksและbrowserผ่านlocalNAS-cache harnessแล้ว ไม่ใช่authenticatedproductionloginหรือphysicalAndroidtest รายละเอียด: [worklog](worklog/2026-09-28_18-04-54_overview-copy-and-focus-qsv.md)

## การบันทึกและดูย้อนหลัง

- บันทึกเป็น background media task; video stream-copy พร้อม `extract_extradata` เพื่อให้ MP4 มี SPS/PPS ที่ถูกต้อง เสียง AAC 16kHz mono 48kbps
- เป้าหมายไฟล์ละ 60 วินาที แต่ตัดตาม keyframe จึงอาจคลาดเคลื่อนหนึ่งช่วง keyframe; ตอนเริ่ม/หยุดหรือขาดการเชื่อมต่ออาจสั้นกว่า ห้ามอ้างว่าได้ 60.000 วินาทีทุกไฟล์
- แต่ละกล้องเลือก folder และ retention เป็นจำนวนวันหรือไม่จำกัดได้ เก็บแยก `gimdvr-<camera-id>/<session>/`; ลบเฉพาะไฟล์เสร็จแล้วที่ระบบลง catalog ไว้ ห้ามลบ root ของผู้ใช้แบบ recursive
- ดูย้อนหลังต้องเลือกกล้องก่อน จากนั้นวันเวลาเริ่ม/สิ้นสุด default ย้อนหลัง 1 ชั่วโมง ค้นหาแบบช่วงเวลาทับซ้อน เรียงเก่าไปใหม่ มี pagination และเล่นทั้งหมดต่อไฟล์อัตโนมัติ ไม่อ้างว่า gapless
- การซ่อม MP4 เก่ามีต้นฉบับ `.mp4.before-avcc-repair` เก็บไว้ ห้ามลบทิ้งตามอายุโดยอัตโนมัติ และห้ามแก้ไฟล์ที่กำลังเขียน

## กล้องและ UI

- ภาพสดเล่นอัตโนมัติ ปิดเสียงเริ่มต้น ไม่มี native timeline/play/pause/speed/PiP; หน้ารวมมีview toggle/ชื่อกล้อง/ปุ่มcontrol โหมดเดี่ยวมีfullscreen/control ทั้งสองโหมดภาพสดไม่มีเสียง ตัวควบคุมถอด listeners เมื่อออกจากหน้า ส่วนดูย้อนหลังยังมี controls ตามเดิม ([worklog](worklog/2026-09-28_16-58-45_live-video-minimal-controls.md))

- แผง control ลอย ลากและปรับขนาดได้ ไม่บังภาพทั้งจอ PTZ กดค้าง/ปล่อยเพื่อหยุด มี heartbeat, ป้องกันคำสั่งเก่า และ server stop timeout 650 ms
- รองรับ IR, microphone/speaker volume และ motion ตามความสามารถจริง คำสั่งตอบรับไม่ใช่หลักฐานว่ามอเตอร์/ลำโพงทำงานจริง
- Human detect ยังไม่ถูกเปิดเผยโดย firmware และ browser talk-back ยังไม่มี server-side VStarcam bridge; ต้องแสดงข้อจำกัด ห้ามแสดงว่าทำงานครบ

## Path และขอบเขต deploy

- Source: `C:\Users\tatsa\Documents\ChatGPT\sysdiag\dvrcam`
- Web: `\\gimkim-nas\C\Users\tatsa\web\dvrcam`; IIS pool ที่ใช้งานคือ `DVR`
- External data: `\\gimkim-nas\C\Users\tatsa\web-data\GimDvr` เก็บ DB/users/keys/config/runtime นอก web root ต้องรักษาระหว่าง deploy
- Worker artifacts: `\\gimkim-nas\C\Users\tatsa\web-workers\GimDvr`
- Installer/backups: `\\gimkim-nas\C\Users\tatsa\web-setup\GimDvr`
- พื้นที่บันทึก NAS `O:\DVR` ตรงกับ `\\gimkim-nas\Music\DVR` ตามการตรวจ catalog ไม่ควรเดา mapping จากชื่อ drive
- ณ การตรวจล่าสุด Windows Service ยังไม่ได้ activate; production เป็น web-owner mode แม้มี worker binaries แล้ว IIS restart จึงอาจทำให้บันทึกขาดช่วง ต้องแยกคำว่า background task ออกจาก independent service
- Deploy ด้วย app_offline ระหว่างเปลี่ยน binary, สำรองก่อน, รักษา appsettings และ external data, ตรวจ hashes/health/auth boundary ห้าม deploy .local-data, bootstrap, private evidence ไป wwwroot

## หลักการวิเคราะห์และตรวจสอบ

- ไม่สรุป bandwidth ตันจาก bitrate สูงอย่างเดียว โดยเฉพาะ LAN 2.5 Gbps; ไม่สรุป GPU/CPU ทำไม่ทันจากภาพกระตุกโดยไม่มี timing
- แยก encoder throughput, การออก segment เป็นชุด, proxy delivery, player buffer starvation และการ seek ไล่ภาพสด การวัดผ่าน SMB ไม่ใช่เวลา encode GPU โดยตรง
- FFmpeg decode ผ่านหรือ probe encode 3 เฟรมผ่าน ไม่ยืนยันว่า browser จะรับ container หรือเล่นลื่นต่อเนื่อง ต้องทดสอบ browser จริงด้วย
- ล่าสุด 1.3.2: browser หน้าทดสอบ local รับ HLS จาก NAS ทั้งสามกล้องเล่นต่อได้ในช่วงที่ตรวจ; หลังปิด viewer encoder หยุด แต่ reader PID และการบันทึกยังอยู่ ไม่ใช่การรับรอง latency end-to-end หรือความลื่นระยะยาว และไม่ใช่ authenticated production-page verification
- ผล automatic approval review ที่เคยบล็อกการอ่าน bootstrap/login production ต้องไม่แก้โดยเข้าทางอ้อม ระบุขอบเขตการตรวจตามจริง

## การบันทึกงานทุกครั้ง

สร้าง `worklog/YYYY-MM-DD_HH-mm-ss_topic.md` ใหม่ทุก session/request แม้เป็นงานเอกสารหรือวิเคราะห์อย่างเดียว ใช้ [template](worklog/_template.md), เพิ่มในดัชนี และบันทึกสิ่งที่ยังไม่ยืนยันอย่างตรงไปตรงมา

ประวัติหลัก: [งาน HLS/QSV 1.3.2](worklog/2026-09-28_16-51-40_hls-on-demand-qsv-1.3.2.md) และ [archive เดิม](worklogs/2026-09-28.md)

## Asset URL versioning

HTML ถูก render ด้วย SHA256 ?v= ของ local src/href/poster และส่ง no-store; static assets revalidate ด้วย no-cache จึงใช้ refresh ปกติหลังแก้ได้ ไม่ต้องเปลี่ยนเลขmanual Dynamic workletใช้ assetUrl helper; เพิ่ม dynamic resource ใหม่ต้องลงทะเบียน mapping หรือใช้กลไก versioning เดียวกัน ไม่เติม versionสุ่มในHLS/recording URLs หน้าเปิดค้างไม่hot reload ([worklog](worklog/2026-09-28_17-02-13_automatic-asset-url-versioning.md))

## Source repository

Public repository: https://github.com/gimkim/dvrweb — source root คือโฟลเดอร์ dvrcam ที่มี .git ของตัวเอง ไม่ใช่ parent sysdiag ห้ามเพิ่ม artifacts/evidence/credentials/database/keys เข้าประวัติ Git ([worklog](worklog/2026-09-28_17-04-19_public-github-dvrweb.md))

## Android และการเปิด/ปิดดูแต่ละกล้อง

- Web 1.7.0 / APK 1.0.1: ปุ่มก่อนชื่อกล้องเปิด/ปิดเฉพาะการดูของอุปกรณ์นี้ จำ localStorage แยก user ID/camera ID ไม่เปลี่ยน recording หรือ enabled ของกล้อง และไม่กระทบ viewer อื่น
- หนึ่ง live session ต่อกล้องเป็นเจ้าของ copy-stream/fetch/retry/listeners/heartbeat ต้องยกเลิกได้แม้ watch request ยังรออยู่; background Android หยุด session และ resume เฉพาะกล้องที่เลือก
- Android เป็น .NET10 WebView ที่ fix HTTPS server /gimdvr/; CookieManager เก็บ cookie ไม่มี password ใน APK, RememberDevice ticket อายุ10ปีตาม server แต่ platform expiration/ล้างข้อมูล/เปลี่ยนรหัสหรือสิทธิ์ยังทำให้ต้อง login ใหม่
- แนวตั้งเรียงกล้องลงมา; fullscreen เป็น native landscape + CSS wrapper มี control dialog ลากได้ ต้องย้าย dialog กลับ body ก่อนลบ card เสมอ
- APK net.gimgim.gimdvr, Android8+, arm64/arm/x64; ใช้ deployment/Build-Android.ps1 กับ signing key เดิมที่ .local-data/android-signing ห้าม commit key/pass/APK และต้อง apksigner verify ทุก build
- Web/backend fixture, NAS health/hash และ APK signature ผ่าน; ยังไม่มีการติดตั้ง/หมุนจอ/ทดสอบ cookie บนอุปกรณ์ Android จริง ([worklog](worklog/2026-09-28_17-23-45_android-and-per-camera-viewing.md))

## ขอบเขตการทดสอบตามคำสั่งผู้ใช้ล่าสุด

ตั้งแต่ 2026-09-28T18:05:36.704868+07:00 ทดสอบเฉพาะ smoke test / functional test ของโค้ด ไม่เปิดหน้าเว็บจริง ไม่ทดสอบbrowserหรือbrowser harness เว้นแต่ผู้ใช้สั่งให้ทดสอบโดยชัดเจน ใช้localcode/backendfixtures และตรวจไฟล์/hashการdeployได้ ผลbrowserก่อนหน้านี้เป็นประวัติ ไม่ใช่สิทธิ์ให้ทดสอบซ้ำ ([worklog](worklog/2026-09-28_18-05-36_code-tests-only-policy.md))

การเปลี่ยนล่าสุด: [ทุกโหมดใช้copy100msและคืนcontrolหน้ารวม](worklog/2026-09-28_18-42-33_unified-copy-controls-100ms.md). Bufferทั้งสาม200ms; ทดสอบเฉพาะโค้ดและไฟล์deploymentตามกติกาเดิม.

สลับlayoutต้องรักษาDOM/MediaSource/sessionเดิม ใช้stopLiveเฉพาะออกจากหน้าภาพสด/logout/suspendหรือreloadจริง ([worklog](worklog/2026-09-28_18-48-54_preserve-live-layout-sessions.md)).

APK1.0.1/versionCode2 ใช้server-ownedplayerเดียวกับเว็บ ตั้งWebView CacheMode.NoCacheโดยไม่ล้างCookie/DOMstorage;100msfragments/200msbuffer/สลับlayoutไม่reconnectมาจากเว็บ ไม่มีnativeencoderเพิ่ม ([worklog](worklog/2026-09-28_18-51-04_android-shared-web-stream.md)).

เว็บมีลิงก์ดาวน์โหลดAPK1.0.1บนหน้าloginและheaderหลังlogin;ซ่อนลิงก์ในAndroidshell ต้องอัปเดตปลายทางเมื่อออกAPKใหม่ ([worklog](worklog/2026-09-28_18-54-37_web-apk-download-link.md)).

ปุ่มเปิด/ปิดการดูใช้inlineSVGแทนUnicode power glyph เพื่อไม่ขึ้นmissing-glyphบนมือถือ คงrole=switch/aria-checkedและlabelเดิม ([worklog](worklog/2026-09-28_18-56-18_mobile-power-icon-svg.md)).

สถานะภาพสดทั้งเว็บ/Androidแสดงเฉพาะliveหรือbuffering ปิดดูซ่อนสถานะ ไม่แสดงคำอธิบายencode/transportยาวๆบนภาพหรือท้ายหน้ารวม ([worklog](worklog/2026-09-28_18-57-21_minimal-live-status-web-android.md)).

แผงcontrolใช้พื้นผิวเดียวไม่มีกรอบซ้อน ปุ่มแพน48x44px;ยุบกว้าง216pxบนtouch/240pxdesktop ตั้งค่าขยาย300/310px มีfocus/pressedfeedbackและลากได้ ([worklog](worklog/2026-09-28_18-59-10_compact-mobile-controls.md)).

ชื่อกล้อง/ขึ้น/ปิดอยู่แถวเดียวในcontrol กดชื่อกล้องบนmobileขอfullscreenทันทีจากgestureแล้วlocklandscape(Webถ้ารองรับ);Androidใช้nativebridgeเดิม คงplayerและเปิดcontrolในfullscreen;ออกแล้วunlock/portrait ([worklog](worklog/2026-09-28_19-01-36_pan-header-mobile-landscape.md)).

Fullscreenต้องมีปุ่มกลับหน้ารวมภายในvideo-wrap และซ่อนปุ่มfullscreenซ้ำซ้อน กดกลับครั้งเดียวออกfullscreen/คืนแนวจอ/กลับoverviewโดยคงsession ([worklog](worklog/2026-09-28_19-08-32_fullscreen-direct-back.md)).

Mobile recordings: Android shell exposes Live/Recordings tabs; web retains sidebar navigation. Mobile search/cards/player fit narrow screens. Detection configuration is not an event: current recordings have no event timeline, so absence of metadata means unknown. Vendor alarm push exists on some models, but these cameras remain unverified ([worklog](worklog/2026-09-28_19-10-18_mobile-recordings-detection-review.md)).

Eye4 static RE found separate local sensor logs, cloud events and SD filename motion markers; never infer event labels from NAS-generated filenames. Human capability uses additional2017/2126/2127 queries beyond current2106 probe; prior missing2106 data is inconclusive. Android sample authenticity remains unverified. See [research](docs/eye4-protocol-findings.md) and [worklog](worklog/2026-09-28_19-20-49_eye4-reverse-engineering.md).

## NAS recording detection (1.8.0)

วิเคราะห์ไฟล์ MP4 ที่บันทึกเสร็จแล้วใน background ไม่เปิด RTSP เพิ่มและไม่ encode วิดีโอ One persistent Python/OpenVINO worker, one clip globally, hardware decode D3D11VA with single-thread CPU fallback;2fps small-frame motion,1fps person inference when active/5s idle scan. GPU preferred, CPU inference1thread fallback. ผลตามหลังไฟล์ปิด+queue;ทยอยbackfillคลิปเก่าโดยไม่แก้ไฟล์วิดีโอ แยกtrue/false/nullและcoverageในSQLite detections;failed/partialไม่แปลว่าไม่มีคน Scene-change suppressionเป็นheuristic ยังมีfalsepositive/negativeได้

Default runtime outside web root: C:\Users\tatsa\web-data\GimDvrDetection. App1.8.0 and worker register DetectionService with file owner lock; web proxy skips when external worker configured. Shared web/Android results usecompactrows and SVG Motion/Human badges;APK1.0.1ไม่ต้องbuildใหม่. Camera-event metadata from Eye4 remains a separate unimplemented source; these labels now come from NAS sampled analysis. [Design](docs/nas-detection.md), [worklog](worklog/2026-09-28_19-50-36_nas-detection-compact-recordings.md).
