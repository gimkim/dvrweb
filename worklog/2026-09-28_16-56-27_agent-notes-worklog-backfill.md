# Agent notes and per-session worklog backfill

- ทำงาน/บันทึกเมื่อ: 2026-09-28T16:56:27+07:00
- ประเภท: documentation
- App version: 1.3.2 unchanged

## คำขอ
บันทึก concept/หลักการลง agent note; สร้าง worklog แยกไฟล์ทุกครั้งที่ทำงาน และบันทึกย้อนหลัง

## สิ่งที่เปลี่ยน
- เพิ่ม AGENT_NOTES.md สรุป HLS/QSV เน้นความไว, on-demand sharing, recording/playback, controls, paths และข้อจำกัด ตรวจค่าหลักกับ source ปัจจุบัน
- แก้ AGENTS.md ให้ทุก session รวมงานวิเคราะห์/เอกสารสร้างไฟล์ใหม่ใน worklog/ อัปเดตดัชนี และอ่าน notes/latest relevant worklog ก่อนเริ่ม
- เพิ่ม template/index; แยก archive ครบ14หัวข้อเป็น14ไฟล์ย้อนหลัง คงเนื้อหาและเวลาเดิม ระบุ provenance/backfill time ไม่สร้างเวลาที่ไม่ทราบขึ้นใหม่
- เก็บ worklogs/2026-09-28.md เดิม ใช้ worklog/ สำหรับงานใหม่ และเพิ่มลิงก์ใน README

## หลักฐานย้อนหลังและขอบเขต
ใช้ archive เดิมและบทสนทนานี้ ไม่เรียกงานเก่าซ้ำหรือสมมติ deployment ใหม่ Intermediate1.3.0/1.3.1อยู่ใน session HLS และระบุในไฟล์1.3.2 รายละเอียดที่ไม่มีหลักฐานไม่สร้างขึ้นใหม่ งาน sysdiag ที่ไม่เกี่ยวกับ GimDVRอยู่นอกขอบเขต

## การตรวจสอบ
ตรวจ14ส่วนกับ14ไฟล์และเนื้อหาตรงต้นฉบับหลัง normalize newline/ขอบ whitespace ตรวจ hash archiveไม่เปลี่ยนและลิงก์ local มีปลายทาง ไม่คัดลอกรหัสผ่าน/DB/keys/ภาพกล้องลงเอกสาร
การสร้างไฟล์ครั้งแรกหยุดเพราะ assertion เกี่ยวกับ CRLF; แก้การ normalize ในไฟล์ที่เพิ่งสร้างและตรวจครบ โดยไม่แก้ archive
Archive SHA256: 389ee4d57d7478d492e62d3ed0ddb70badefa3c1ccb10c9d4b5eeafc6d01dc18

## Deployment
ไม่มีการแก้ application code, build, deploy, restart หรือเปลี่ยนกล้อง/ข้อมูลบันทึก งานนี้เป็นเอกสารใน source project

## งานค้าง
ใช้กติกานี้ทุกครั้งถัดไป ข้อจำกัด service และการยืนยันความลื่นระยะยาวยังคงตาม AGENT_NOTES.md
