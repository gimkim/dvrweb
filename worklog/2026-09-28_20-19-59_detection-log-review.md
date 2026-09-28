# NAS detection log review

Request: inspect the NAS logs. Session started 2026-09-28 20:19:59 +07:00.

Read-only inspection of external detection JSONL logs and restricted detection/recording metadata in SQLite. No application code change, deployment, browser/API test, footage decoding, or new benchmark occurred.

## Observed evidence

- NAS process 8200, log `detection-20260928-8200-15a97c61-0000.jsonl`, started 20:13:55 +07.
- At 20:20:11: 20 complete clip results, all reporting OpenVINO GPU and D3D11VA decoding; 6 error results with `analysis_failed`, zero frames, roughly 0.01 second duration of work. No worker_error, clip_error, or runtime_missing events in the inspected snapshot.
- Recent full-minute clips took 16.75–18.79 seconds. Most had 120 sampled frames and 12 person inferences, consistent with idle five-second inference rather than sustained motion-heavy work.
- Excluding the first startup minute, 20:14:55–20:19:55: 902.63 video seconds completed in 299.9865 wall seconds = 3.0089x aggregate throughput. Three continuously recording cameras require approximately 3x, so observed headroom is minimal. This five-minute sample does not establish sustained capacity, especially with motion on all cameras.
- In that window unresolved files declined 707 to 703; unresolved video declined 180.26 seconds. Actual arrivals differed from the ideal continuous three-camera rate; do not extrapolate this into a guaranteed backlog drain rate.
- Latest heartbeat at 20:19:55: 19 complete / 941.62 video seconds; 6 errors / 31.92 seconds; 696 pending / 39764.33 seconds; 1 processing / 60 seconds. Total unresolved 703 files / 39856.25 seconds (664.27 video minutes), zero retry-exhausted files. Lifetime throughput including startup was 2.614x.
- Subsequent SQLite metadata check: six errors were short clips of 1.53–7.70 seconds, attempts 1–2, all generic analysis_failed. Their stored paths use NAS-local O:. Client-side path mappings could not verify existence even for six successful control records, so missing files are NOT established as the cause. No database rows/files were modified. More precise error categorization or NAS-local existence checks would be needed to resolve these six failures.

## Conclusion and limits

GPU/hardware decode are confirmed by actual NAS logs. Current observed throughput approximately keeps pace with three cameras but has little safety margin, and historical backlog remains large. The FFmpeg input is explicitly paced with readrate=4, with serial clip scheduling and an inter-clip pause; these can limit throughput independently of available compute, but no change was requested or made. User-visible accuracy and sustained motion-heavy capacity remain unverified.
