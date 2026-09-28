# Automatic asset URL versioning 1.3.4

- เวลา: 2026-09-28T17:02:13+07:00
- ประเภท: implementation / automated verification / NAS deployment

## คำขอ
ต่อ version ใน URL ของScript/CSS/element resources อัตโนมัติเพื่อไม่ต้องCtrl+F5หลังแก้

## สิ่งที่เปลี่ยน
AssetVersions.cs อ่านindex.htmlและเติม?v=SHA256ของไฟล์ให้local relative src/href/poster ที่มีไฟล์จริง แทนเลขmanual รักษาquery/fragment ไม่แก้external URLs/anchors ตรวจmtimeและlengthเพื่อinvalidate hash cacheเมื่อไฟล์เปลี่ยน
Program.cs ส่งHTMLแบบno-storeก่อนStaticFiles และstatic assets no-cacheเพื่อrevalidate; API no-store ป้องกันภาพนิ่ง/ข้อมูลค้าง ไม่มีversion timestampสุ่มใส่streamsegmentsหรือrecording URLs
index.html เอาเลขmanualออก; app.js โหลดaudio workletผ่านassetUrlที่serverใส่versionให้ ไม่เปลี่ยนHLS/QSV/playback/control behavior
ครอบคลุมresource attributesปัจจุบันทั้งหมดและdynamicworklet; ไม่ใช่ตัวrewriteJSทั่วไปหรือCSSurl/srcsetที่อาจเพิ่มภายหลัง หากเพิ่มdynamicassetใหม่ต้องลงทะเบียนในassetUrl mapping
หน้าที่เปิดค้างไม่ถูกreloadอัตโนมัติ เปิดใหม่หรือrefreshปกติจึงรับHTMLและURLsใหม่

## การตรวจ
33checksผ่าน รวมquery/fragment preservation, content change changes URL without restart, HTML/worklet versioning, externalURLs untouched; JSsyntaxและweb/workerReleasepublishผ่าน
NAShealth200/version1.3.4; HTMLCache-Control=no-store; app.css/vendor-hls/live-controls/app.jsมีhashversionและทุกไฟล์HTTP200/no-cache; publishedhashmismatches0
ไม่ได้ทำauthenticatedbrowserlogin งานนี้ตรวจresponseจากproductionและautomatedfixture ไม่อ้างว่า hot reload หน้าที่เปิดค้าง

## Deployment
สำรองwebDLL/index/app.jsในweb-setup/GimDvr/backup-auto-assets-* ใช้app_offlineระหว่างcopyweb+worker และรักษาappsettings/externaldataทั้งหมด IIS restartอาจทำให้ช่วงบันทึกขาดตามweb-owner modeเดิม

## เอกสารและข้อจำกัด
อัปเดตAGENT_NOTES/README/AGENTSให้เพิ่มdynamicassetผ่านversionhelper ไม่ใช้เลขmanual และบันทึกไฟล์ใหม่ในดัชนี ไม่มีการเปลี่ยนcredentialsหรือrecordingconfiguration
