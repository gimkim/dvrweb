# MotionService activation verification

2026-09-28 21:41:00–21:42 +07. User asks whether the deployed service now works.

Read external installer status and production MotionService/NAS diagnostic logs only. No HTTP test, browser/device interaction, synthetic benchmark, footage read, code/config edit or deployment in this request.

- installation.json reports success at21:39:44 on Default Web Site; IIS backup Before-MotionService-20260928-213936. Reported certificate SHA256 matches the pin provisioned on NAS.
- First IIS model initialization exceeded the60second startup deadline and was killed at21:40:55. Automatic retry started21:41:10 and reported ready=true/device=CUDA at21:41:37. Cause of initial warmup delay was not further established; no timeout tuning was needed to achieve readiness.
- NAS logged remote_status available=true at21:41:41. Existing local job finished before switching; normal production queue jobs then used remote.
- Service logged completed60second clips at21:41:58 and21:42:02, elapsed2.737 and2.684seconds,120sampled frames, CUDA inference and CUDA decode. A60.064second clip at21:42:05 completed in2.821seconds with30person samples.
- NAS logged matching remote_complete plus complete results saved through its normal queue flow; observed total perclip elapsed2.702 and2.870seconds (CUDA/cuda/remote-yolox-tiny-v1). This confirms actual NAS-to-service production analysis, beyond local fixtures. It is a short sample, not sustained capacity or human-accuracy validation. Idle pauses are not included in perclip speed.

The prior pending-UAC activation limitation is now resolved. NAS local fallback operated during warmup and remote polling restored routing without another deployment. Agent notes updated and worklog committed/pushed.
