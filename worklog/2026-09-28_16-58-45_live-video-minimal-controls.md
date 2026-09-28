# ภาพสดมีเฉพาะเสียงและเต็มจอ

- เวลา: 2026-09-28T16:58:45+07:00
- ประเภท: UI implementation / browser verification / static deployment
- Backend health version: 1.3.2 unchanged; UI assets cache revision: 1.3.3

## คำขอ
เอา progress bar, play/pause และ speed menu ออกจากภาพสด เหลือ mute/unmute และ fullscreen เล่นต่อเนื่องเมื่อเปิดหน้าภาพสด

## สิ่งที่เปลี่ยน
- app.js: เอา native controls ออกจากเฉพาะ live video เพิ่มสองปุ่มและผูก lifecycle ตัวควบคุม
- live-controls.js: mute/unmute พร้อม accessible labels, fullscreen ที่ wrapper เพื่อคง custom controls, ปิด PiP/remote controls/context menu และ resume บน pause/canplay ขณะยัง active
- destroy ถอด event listeners ก่อน stopLive เพื่อไม่ให้ resume หลังออกจากหน้า
- app.css: จัดสองปุ่มมุมล่างขวาและ fullscreen layout/status ไม่ทับปุ่ม
- index.html: โหลด helper ก่อน app.js และเปลี่ยน cache revision
- ระบบย้อนหลังยังมี native controls; HLS/QSV/recording settings ไม่เปลี่ยน

## การตรวจ
node --check ทั้งapp.js/live-controls.js ผ่าน; Release web publish ผ่าน
Browser local harness ใช้ HLS จาก NAS จริงและ helper/CSS production: ทั้ง3controls=false, paused=false,เวลาเดิน; mute button เปลี่ยนmuted false/true; AX fullscreen เหลือสองปุ่มและออกกลับได้ ไม่มี slider/play/pause/speed UI
Screenshot: ../evidence/live-controls-1.3.3.png
ไม่ได้ login หน้า production; การทดสอบเป็น local harness ไม่ใช่ authenticated production UI และไม่รับรอง browser/OS จะไม่ suspend tab

## Deployment
สำรอง static assets ลงweb-setup/GimDvr/backup-live-controls-* แล้วอัปเดตwwwrootและstatic manifest บนNASเท่านั้น ไม่เปลี่ยนbinary/config/data ไม่รีสตาร์ตIISหรือrecorder
Static hashesตรงpublishทุกไฟล์; live-controls.js HTTP200; health200/version1.3.2

## เอกสารและงานค้าง
เพิ่มหลักการUIลงAGENT_NOTES.md และentryนี้ในดัชนี ผู้ใช้refreshเพื่อโหลดassetsใหม่
