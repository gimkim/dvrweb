# NAS motion/person detection and compact recording results1.8.0

2026-09-28 19:50:36–20:06 Asia/Bangkok. User requests implementing low-load NAS detection, marking each recording motion/human in SQLite and compact per-file icons on web/Android.

## Implementation

DetectionService runs one persistent Python/OpenVINO process with one clip at a time. Analyses only finalized catalogued files, so no additional camera connections or encoding, and live transport unchanged. This deliberately uses completed files instead of live frame fanout: results lag file finalization and the work queue. One owner file lock handles IIS overlap/external worker mode; interrupted jobs reset pending. No external recorder service activation performed.

Background2fps small-frame motion plus motion-gated1fps person model/5s idle scan. Consecutive evidence required; exposure compensation/large scene-change reset. Intel person-detection-retail-0013 FP16 with GPU preference, CPU1thread fallback. D3D11VA decode attempt, CPU1thread fallback. Readrate4 limits file consumption; below-normal priorities. No claimed CPU ceiling/real-time throughput on NAS. Samples may miss brief/small people; motion may react to weather/small pans. Scene suppression is heuristic, not exact PTZ-state integration.

SQLite detections table stores nullable independent flags, state, frames/samples/confidence, model version/device/decoder/error plus attempt/update metadata. Bulk result lookup on authenticated recording APIs adds no private paths. Retry partial/errors3attempts/5minute cooldown; newest-first with each fourth scheduling slot reserved for oldest. Existing catalogue backfills gradually. Removal cleans detection row; cannot insert result after recording deleted. No-result/partial/model failure remains unknown rather than fabricated negative.

Shared web/app display inline SVG activity/person icons with positive/negative/unknown classes, accessible labels and concise legend. Compact rows replace wide table/mobile multi-row cards; search form compact on narrow screens. Existing camera-first/hour default/play-all unchanged. Search again to update results. APK1.0.1 uses same assets, no native rebuild.

Files: new DetectionService.cs, Store.Detection.cs, Detection/detect.py, prepare-runtime script and docs/nas-detection.md; Models/Store/API/service registrations/csproj; shared app.js/app.css; backend, Python, integrated runtime and JS tests; notes/README/index.

## Tests

- 43 backend checks including independent SQLite flags, null/pending, restart recovery, idempotent schema, retention cleanup and missing-row race; existing auth, PTZ, shared media ownership and date-range checks retained.
- 7 synthetic Python checks: static/exposure, localized motion, repeated person evidence including idle, single-hit rejection, incomplete/model-unavailable state, large scene reset, complete sampled negative.
- Actual local FFmpeg/OpenVINO GPU/D3D11VA6second blank-video integration -> Python JSON -> hosted service -> SQLite -> API projection:12frames/2inferences/complete/false/false; clean shutdown/lock release. Evidence artifacts/detection-integration-20260928-200542. Positive classifier/threshold behavior is fixture-tested; real-person model accuracy not established.
- 5 recording UI code fixtures;4 existing live-mode,3 layout,6 session checks. JS syntax/diff checks; web and worker Release build/publish successful.
- An initial backend command incorrectly passed --nologo through dotnet run as an FFmpeg path; corrected invocation passed. No real browser/mobile, camera, production HTTP or private-footage test. No NAS GPU/CPU benchmark; local GPU success must not be represented as NAS validation.

## Runtime/deployment

Isolated Python3.11.9, OpenVINO2025.4.1, NumPy2.2.6, OpenCV headless4.12.0.88, supporting packages/model/license copied outside web root to \\gimkim-nas\C\Users\tatsa\web-data\GimDvrDetection. Runtime/model hashes verified. Model official URL and SHA256 pinned in preparation script. No binaries/model/footage/credentials in public Git. Primary implementation references: https://docs.openvino.ai/2024/notebooks/gpu-device-with-output.html and https://github.com/openvinotoolkit/open_model_zoo/blob/master/models/intel/person-detection-retail-0013/model.yml .

First deployment attempt found IIS still holding DLL after recorder released; no new application deployed then. Retried with an explicit assembly-release wait. Successful web/worker deployment20:04:52, web affected-file hashes matched and production config hash unchanged. Backup including pre-migration database under \\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-detection-20260928-200452. app_offline removed; web-owner restart can briefly interrupt recording. External users/camera settings/keys and video files preserved. Runtime activation/results on NAS not probed; installed worker begins with normal app startup. No health/browser requests.

An initial tool command to generate deployment script via a Python shell heredoc was blocked by automatic policy; used apply_patch to write a fixed PowerShell script and native PowerShell file operations, which were accepted. No request for expanded permissions or workaround shell deletion.

Updated current notes and README, source committed/pushed to main via normal workflow. Remaining validation: real footage recall, mobile presentation and sustained NAS decoder/inference load, only when user authorizes those tests.
