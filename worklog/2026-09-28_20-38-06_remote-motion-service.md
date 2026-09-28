# Remote CUDA MotionService and NAS failover

Started2026-09-28 20:38:06 +07. User requests an independent service on this machine at C:\Users\tatsa\web\MotionService, exposed through HTTPS192.168.1.30/MotionService, CUDA with CPU fallback, NAS polling/preference for remote and local NAS fallback when unavailable.

## Implementation

- Independent .NET10 MotionService, dedicated IIS pool installer, external private config/runtime/incoming/logs. Bearer authentication on all routes, bounded256MiB/180second upload,110second deadline, one admitted job, generated temporary names and cleanup. No caller paths/URLs/static files served.
- Shared sampled motion pipeline; remote YOLOX-tiny ONNX Runtime1.23.2 CUDA with actual warm inference and CPU startup/runtime fallback. CUDA FFmpeg decode falls back to single-thread CPU on no frames. Remote read pacing removed, NAS4x pacing unchanged. Versions explicitly distinguish remote-yolox-tiny-v1 and nas-person-v1; no claim equal person accuracy.
- NAS RemoteDetection polls15s (3s health timeout), validates ready protocol1, uploads recorded file, accepts only complete covered results. Transport/status/incomplete failure falls back to local in same queue attempt, subsequent poll restores remote. Shutdown cancellation preserved. Local Python startup now lazy; extracted reusable process runner and Windows job ownership.
- External NAS detection-remote.json holds URL/key/exact SHA256 certificate pin, loaded by web and recorder owners. HTTPS only, no redirect or broad certificate bypass. Public certificate read via TLS handshake and verified against this computer's LocalMachine certificate store before configuring pin. No production HTTP test issued.
- Existing keys, recordings, detection rows and appsettings remain intact. Source includes runtime/setup scripts and operating docs. No APK rebuild needed.

## Runtime evidence and validation

- RTX5080 detected. Isolated embedded Python3.11.9 runtime installed outside web root. Slow PyPI wheel transport required ranged mirror download; final ONNX wheel SHA256 verified against official PyPI metadata:054282614c2fc9a4a27d74242afbae706a410f1f63cc35bc72f99709029a5ba4. Official YOLOX model digest verified. Other dependencies installed through pip; source records pinned main dependencies.
- Reused this host's installed CUDA12.1 DLLs and cuDNN9.16 CUDA12.9 DLLs in runtime/cuda; successful real CUDA inference confirmed. No system installation changed. NVIDIA licenses remain in their original installed locations; isolated Python distributions retain package licenses. Extra partially downloaded CUDA wheels remain ignored artifacts, not deployed runtime.
-43 existing backend checks passed;11 remote routing/TLS/auth/cancellation checks;4 actual NAS-local synthetic pipeline/log/ownership checks after refactoring;7 sampled detector checks and4 ONNX preprocessing/class-score/CUDA-failure-fallback checks.
- Actual separate loopback HTTP service fixtures with synthetic60second blank video: CUDA+CUDA decode completed120frames in0.438s; forced CPU inference+software decode completed120frames in0.562s. Auth rejection, invalid duration/type/size, invalid-video unknown labels and temporary cleanup passed. Evidence artifacts/motion-integration-20260928-210248. These small static frames are not real-camera throughput or human-accuracy evidence. No browser/device/private-footage test.
- All web/worker/service Release publishes succeeded; diff checks passed.

## Deployment and remaining activation

- MotionService files published to requested local path; hashes verified. Private API key/config outside web root, no secrets committed.
- NAS1.9.0 web/worker binaries plus external remote config deployed; file/config hashes verified and appsettings hash preserved. Backup: \\gimkim-nas\C\Users\tatsa\web-setup\GimDvr\backup-detection-20260928-210427. app_offline removed. Recorder briefly restarted as part of web-owner deployment.
- Passive NAS logs show PID13316, version1.9.0, remote_status available=false/not_ready at21:05:22, followed by successful local GPU/D3D11VA files at21:05:46,21:06:01 and21:06:17. Failover is operating while remote IIS activation is pending.
- The current Windows account cannot read IIS configuration. Elevated installer was launched via standard UAC; consent.exe remains pending and installation.json has not been produced as of21:07. User was asked to approve the existing UAC prompt. Do not claim production remote routing is active until installer success and NAS remote_complete logs confirm it. No UAC/security bypass attempted. After approval the installer configures only this app/pool using the existing HTTPS binding, and NAS polls activate/warm the service automatically. If installer reports a binding ambiguity, inspect its external installation.json before changing any IIS binding.
