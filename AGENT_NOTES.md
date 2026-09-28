# GimDVR — Agent notes

ปรับปรุง: 2026-09-28 (Asia/Bangkok) — สถานะออกแบบล่าสุด 1.3.4 (streaming settings จาก 1.3.2)

เอกสารนี้สรุป concept และหลักการปัจจุบัน ต้องอ่านคู่กับ [AGENTS.md](AGENTS.md) และ [ดัชนี worklog](worklog/README.md) รายละเอียดการทดลองเก่าไม่ใช่ข้อกำหนดปัจจุบัน เมื่อผู้ใช้เปลี่ยนแนวทางให้แก้สรุปนี้และสร้าง worklog ไฟล์ใหม่

## เป้าหมายและขอบเขต

- เว็บ .NET 10 บน Windows NAS / IIS ที่ `https://gimgim.ddns.net/gimdvr/` เครื่อง NAS เป็น Intel N100 และผู้ใช้ดูผ่าน LAN 2.5 Gbps
- กล้อง VStarcam เริ่มต้น Garage, Front door, Side; เพิ่มกล้องในอนาคตได้ กล้องและรหัสผ่านต้องอยู่หลัง server proxy ห้ามให้ browser ติดต่อกล้องโดยตรง
- จัดการผู้ใช้ admin/operator/viewer เองได้ รหัส admin เริ่มต้นอยู่ใน bootstrap.txt นอก web root; ห้ามคัดลอกรหัสจริงลง note/log/repository ไม่มีข้อบังคับความยาวรหัสผ่าน แต่ต้องไม่ว่าง ยังมี hashing, login throttling และการยกเลิก session เมื่อสิทธิ์เปลี่ยน
- ทุกกล้องเริ่มต้นไม่บันทึกจนผู้ใช้กำหนด folder และเปิดบันทึกเอง ปัจจุบันตามการตรวจครั้งล่าสุดผู้ใช้เปิดบันทึกทั้งสามกล้องแล้ว ห้ามนำ default ไปทับค่าของผู้ใช้

## ภาพสด: แบบที่ผู้ใช้เลือกสุดท้าย

```text
Camera RTSP → reader หนึ่งตัวต่อกล้อง → บันทึก MP4 (video copy)
                                  → loopback MPEG-TS relay
                                      → shared encoder เฉพาะตอนมีคนดู
                                          → HLS TS → authenticated web proxy → hls.js
```

- ใช้ HLS และ hls.js แบบรุ่น 1.1.2: segment ประมาณ 1 วินาที, playlist 8 ชิ้น, independent keyframes, ชื่อ segment ไม่ซ้ำเมื่อ restart
- Player: liveSyncDurationCount=2, liveMaxLatencyDurationCount=6, maxBufferLength=5, backBufferLength=4, maxLiveSyncPlaybackRate=1.25 ตามการตั้งค่า HLS เดิม
- คง on-demand encoding ตามคำสั่งภายหลัง: หนึ่ง encoder ต่อกล้องแชร์ให้ทุกคนดู ไม่เปิด camera connection เพิ่มต่อ viewer; heartbeat ทุก 3 วินาที และหยุด encoder หลังไม่มี lease เกิน 8 วินาที งานบันทึก/reader ทำต่อได้
- Quick Sync เน้นความไว: h264_qsv, preset veryfast, async_depth=4, lookahead=0, B-frames=0, 1080p/15fps, GOP=15, CBR เป้าหมาย 4 Mbps, maxrate=4 Mbps, VBV=4 Mbit, low_delay_brc=1 อัตราข้อมูลจริงรวมเสียงและ container อาจต่างจากเป้าหมาย
- ใช้ auto selection พร้อม probe บนเครื่องเจ้าของ media จริง และ CPU fallback เมื่อ QSV ล้มเหลว/หยุดออกข้อมูล CPU fallback คือ libx264 ultrafast/zerolatency/CRF24; ค่า CRF กับ ICQ ไม่เทียบกันตรงตัว
- มีเพียง video encode ที่ใช้ QSV; decode/scale ยังทำบน CPU ห้ามอ้างว่าเป็น hardware pipeline ทั้งหมด
- ไม่ใช้ custom framed fMP4 player / realtime.js แล้ว และไม่กลับไปบังคับบัฟเฟอร์ 0.25 วินาทีเพื่อไล่เป้าหมายต่ำกว่า 1 วินาที ความลื่นสำคัญกว่า latency ที่ต่ำแต่กระตุก
- Snapshot อ่าน TS ที่เสร็จล่าสุดแล้วแปลงเป็น JPEG มี concurrency limit และ timeout ไม่เรียก CGI snapshot ของกล้องที่ส่ง HTTP header ผิดรูปแบบ และไม่เปิด upstream connection ใหม่

## การบันทึกและดูย้อนหลัง

- บันทึกเป็น background media task; video stream-copy พร้อม `extract_extradata` เพื่อให้ MP4 มี SPS/PPS ที่ถูกต้อง เสียง AAC 16kHz mono 48kbps
- เป้าหมายไฟล์ละ 60 วินาที แต่ตัดตาม keyframe จึงอาจคลาดเคลื่อนหนึ่งช่วง keyframe; ตอนเริ่ม/หยุดหรือขาดการเชื่อมต่ออาจสั้นกว่า ห้ามอ้างว่าได้ 60.000 วินาทีทุกไฟล์
- แต่ละกล้องเลือก folder และ retention เป็นจำนวนวันหรือไม่จำกัดได้ เก็บแยก `gimdvr-<camera-id>/<session>/`; ลบเฉพาะไฟล์เสร็จแล้วที่ระบบลง catalog ไว้ ห้ามลบ root ของผู้ใช้แบบ recursive
- ดูย้อนหลังต้องเลือกกล้องก่อน จากนั้นวันเวลาเริ่ม/สิ้นสุด default ย้อนหลัง 1 ชั่วโมง ค้นหาแบบช่วงเวลาทับซ้อน เรียงเก่าไปใหม่ มี pagination และเล่นทั้งหมดต่อไฟล์อัตโนมัติ ไม่อ้างว่า gapless
- การซ่อม MP4 เก่ามีต้นฉบับ `.mp4.before-avcc-repair` เก็บไว้ ห้ามลบทิ้งตามอายุโดยอัตโนมัติ และห้ามแก้ไฟล์ที่กำลังเขียน

## กล้องและ UI

- ภาพสดเล่นอัตโนมัติ ปิดเสียงเริ่มต้น และมีเฉพาะปุ่มเปิด/ปิดเสียงกับเต็มจอ ไม่มี native timeline/play/pause/speed/PiP; fullscreen ใช้ wrapper เพื่อคงสองปุ่ม ตัวควบคุมถอด listeners เมื่อออกจากหน้า ส่วนดูย้อนหลังยังมี controls ตามเดิม ([worklog](worklog/2026-09-28_16-58-45_live-video-minimal-controls.md))

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

- Web 1.4.0 / APK 1.0.0: ปุ่มก่อนชื่อกล้องเปิด/ปิดเฉพาะการดูของอุปกรณ์นี้ จำ localStorage แยก user ID/camera ID ไม่เปลี่ยน recording หรือ enabled ของกล้อง และไม่กระทบ viewer อื่น
- หนึ่ง live session ต่อกล้องเป็นเจ้าของ HLS/retry/listeners/heartbeat ต้องยกเลิกได้แม้ watch request ยังรออยู่; background Android หยุด session และ resume เฉพาะกล้องที่เลือก
- Android เป็น .NET10 WebView ที่ fix HTTPS server /gimdvr/; CookieManager เก็บ cookie ไม่มี password ใน APK, RememberDevice ticket อายุ10ปีตาม server แต่ platform expiration/ล้างข้อมูล/เปลี่ยนรหัสหรือสิทธิ์ยังทำให้ต้อง login ใหม่
- แนวตั้งเรียงกล้องลงมา; fullscreen เป็น native landscape + CSS wrapper มี control dialog ลากได้ ต้องย้าย dialog กลับ body ก่อนลบ card เสมอ
- APK net.gimgim.gimdvr, Android8+, arm64/arm/x64; ใช้ deployment/Build-Android.ps1 กับ signing key เดิมที่ .local-data/android-signing ห้าม commit key/pass/APK และต้อง apksigner verify ทุก build
- Web/backend fixture, NAS health/hash และ APK signature ผ่าน; ยังไม่มีการติดตั้ง/หมุนจอ/ทดสอบ cookie บนอุปกรณ์ Android จริง ([worklog](worklog/2026-09-28_17-23-45_android-and-per-camera-viewing.md))
