# Eye4 alarm and recording interoperability investigation

Static analysis, 2026-09-28. This is a protocol research report, not deployed detection support.

## Evidence and limits

- Installed Windows Eye4: `925/P2PAPI.dll` SHA256 `B4CFF45231993FBD6F0F3B1960A87069D9E9D00508285B2FDDE66B8F7AEFA712`. Read PE strings and x86 command-dispatch references without loading/executing the DLL.
- Android sample advertised as Eye45.9.5, code218, package `vstc.vscam.client`, from Aptoide metadata. Vendor-linked5.8.2 download returned401. Mirror download repeatedly stalled/truncated; complete APK signature could not be verified. Treat Android findings as provisional pending a signed complete APK from the owner/vendor.
- All three complete DEX entries recovered from ZIP local headers pass their embedded SHA1 and Adler32 checks. This verifies internal completeness, not publisher authenticity. JADX1.5.6 completed with23 errors for classes.dex and7 for classes2/3; relevant readable methods below were inspected, not assumed correct from search hits alone.
- DEX SHA256: classes.dex `9862db79f8b82ad7316bde8dc62889998bf7b518a312ac69ce85315ccdc4109c`; classes2.dex `4fec59ede56273f4af59230e9c05c49682ebd38c54f893f27779bde66af3b64e`; classes3.dex `7e35476c0df7c6c92989bb66ff82f0fe312d51c3b86b5f8d627d09eee931528f`.
- Local research artifacts/decompilation are ignored under `artifacts/eye4-re/`. No vendor source/binary, credentials or camera data are published with this report. No app execution, live camera request, cloud login, browser test or deployment occurred.

## Three separate event paths

### Camera-local sensor alarm history

Android `vstc/vscam/client/camerset/SensorAlrmLogActivity.java:72` sends `get_alarmlog.cgi?logid=0&sensorid=0` through `NativeCaller.TransferMessage`, with normal camera authentication. Callback `AlrmLogBack` at114 maps device name, armtype, dvstype, actiontype, time, index and count into UI fields. This is sensor-log functionality; it is not proof that every camera records motion/human in this log.

Installed Windows P2PAPI independently contains `get_alarmlog.cgi?`. PE VA0x10222c6c has command-dispatch references at0x10007774 and0x1000aea4, calling the transport object's virtual method. It also parses `alarm_status=` and `alarmstatus=`. Android BridgeService `CallBack_AlarmNotify` uses a different namespace (1,2,24), not cloud codes18/41. Do not conflate them. Native P2P transport does not itself prove the identical CGI is available through HTTP on the installed cameras.

### Cloud message history

`com/vstc/msg_center/httpRequst/DayLogFormNet.java` requests JSON via VscamApi. `vstc2/net/okhttp/HttpConstants.java:71-74` includes `/push/log/summary/show`, `/push/log/show`, `/push/D1/log/show` at api.eye4.cn. `AlamLogRequestBean` requires userid/authkey/date/uid; camera admin credentials are not cloud account credentials.

`MsgDzNormal.java:54-55,102-103` maps decimal18 to motion and41 to human. Other device categories also map to human-related labels; keep original codes/source instead of assuming all devices share one enumeration. `sort/ResultSort.java:126+` reads date, dz, uid, fileid, tfcard and cover, including nested payloads. These are separate event records, not detection read from video pixels.

Vendor public alarm-push documentation corroborates18/0x12 motion and41/0x29 humanoid: https://www.vstarcam.com/business/wp-content/uploads/2023/08/Alarm-push-interface-EN.pdf . Its push payload differs from cloud history. Do not mix their time/schema assumptions. No cloud endpoints were called.

### SD recording list markers

`vstc/vscam/activity/ITFPlayActivity.java:2773+` receives filenames/size via `CallBack_RecordFileSearchResult` and pages with PPPPGetSDCardRecordFileList. `adapter/TFPlayListViewAdapter.java:67-76,108-132` takes first14 filename characters as date/time and suffix between underscore and dot as flags. Zero-based flag positions1='1',2='0' produce model2 and a motion icon; position3='1' overrides to model4. Other outcomes draw a generic playback icon in this inspected adapter. This is a filename convention for camera-created SD files, not MP4 metadata. No human-specific icon is proven in that adapter.

GimDVR creates its own timestamp/ID filenames. Its continuous RTSP recordings do not inherit these SD suffixes. Copying Eye4's filename parser onto NAS recordings would give false results.

## Human capability correction

The current GimDVR probe checks cmd2106/command3 for HumanoidDetection. Eye4 has additional independent paths in `vstc2/nativecaller/Native_CGI.java`:

| Reader | Command parameters | Meaning observed in caller |
|---|---|---|
| getDevicePlan | cmd2017, command11, type2 | Human alarm screen's schedule query |
| getHumanSensitivity / gethumanFramed | cmd2126, command1 | Sensitivity and human-frame configuration |
| getHumanTracking | cmd2127, command1 | Tracking configuration |
| getDevicePlanfaceDetect | cmd2017, command11, type8, status0 | Separate face-detection schedule |

Therefore the prior failure to obtain HumanoidDetection from2106 is insufficient evidence of absent human detection. These commands still need model/firmware-specific validation. Do not enable controls from a200 response alone or write settings without complete original configuration.

## Integration direction

Prefer local event history/notification if the actual devices expose it. Confirm schema, event meaning, timestamp timezone and retention first with a controlled known event. Store camera ID, source namespace, original code, event time, observed time and dedup key; preserve unknown/unsupported states and collection gaps. Match events to NAS recording intervals with explicit clock uncertainty. SD markers are only a secondary source when SD files actually exist. Cloud history is an alternative requiring the owner's Eye4 account session, not automatic reuse of camera credentials.

Existing NAS clips may be backfilled only if matching historical events still exist. No-event is meaningful only during confirmed event-collection coverage. No server-side AI or extra video decoding is required for genuine camera-reported event labels.
