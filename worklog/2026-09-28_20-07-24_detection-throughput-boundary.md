# Detection throughput clarification

2026-09-28 20:07:24 Asia/Bangkok. User asks whether analysis outruns three real-time cameras so the backlog cannot grow forever.

Verified deployed design in source: one worker, FFmpeg readrate4,1second between files. For three60second files arriving each minute, sustained average wall time must be below20seconds per file including all overhead; equivalently effective aggregate video rate>3x. Idealized4x input reading gives45seconds for180seconds video plus3seconds inter-file delay=48seconds/minute before process startup/inference/I/O/retries. readrate is a pacing cap, not guaranteed measured throughput. Worst-case motion raises model calls substantially. Thus sufficiency has not been established and an unbounded persistent backlog is possible if processing falls behind. Existing historical backlog adds further load.

Read-only SQLite diagnostic (no camera/video/UI/HTTP requests): current NAS database had707 recordings and no detections table. This is consistent with the newly deployed program not yet initializing that database after deployment; cannot claim worker is processing or infer NAS throughput. Runtime directory snapshot also had no detection.lock. Did not start application via unsolicited production request. Need actual elapsed-time/completion rate and queue-age trend under all3 cameras to substantiate a guarantee. Previous synthetic blank clip on development machine does not establish this.

No code change or deployment this session. Documentation-only clarification, no fabricated performance number. Existing testing restriction retained.
