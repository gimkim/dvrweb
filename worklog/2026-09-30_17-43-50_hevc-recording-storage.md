# HEVC recording storage measurement

User asks file size after HEVC switch. Read completed manifest CSVs and file sizes only; did not open live SQLite over SMB. Sample19completed clips/~19recorded minutes per camera, today17:23–17:42Bangkok. First/last sampled clip per camera ffprobe confirms HEVC1920x1080. DecimalMB/GB. Normalize bytes by actual manifest duration, not wall gaps. No source/config/deployment change, camera connection or browser test.

- garage: 38.92MB total, 2.048MB/min, 2.95GB/day, 88.49GB/30days.
- front-door: 89.42MB total, 4.707MB/min, 6.777GB/day, 203.32GB/30days.
- side: 181.11MB total, 9.526MB/min, 13.717GB/day, 411.51GB/30days.

Total23.444GB/day,703.32GB/30days. Earlier H26424h estimate31.115GB/day,933.45GB/30days; about25percent lower overall in this short daytime sample. Side13.717GB/day is slightly above earlier13.281GB/day. Different scene/time windows mean this is not a controlled codec efficiency comparison and not a full-day HEVC measurement.
