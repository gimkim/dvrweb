# 2026-09-28 16:39:38 +07:00 — QSV pacing investigation and CPU mitigation 1.2.5

Backfilled at: 2026-09-28T16:56:27+07:00
Source: [original archive, line 90](../worklogs/2026-09-28.md), section `2026-09-28 16:39:38 +07:00 — QSV pacing investigation and CPU mitigation 1.2.5`.
Original timestamp retained; source entries may not be chronological.
Retrospective copy, not new execution or verification. Later entries may supersede its decisions.

## Original entry

User reported alternating stutter across QSV cameras while CPU looked smoother; clarified viewing on2.5Gbps LAN. Initial suggestion of network bandwidth bottleneck was unsupported and corrected. Read actual NAS playlists and fragment sizes over SMB: unrestricted QSV about7.42/10.15/21.11Mbps, observed maximum playlist arrival gaps1.44/1.71/0.51sec. Sampling through SMB includes publication and observation delays, not isolated GPU encode timing.
Published/deployed1.2.5 using QSV CBR2.5Mbps, VBV500kbit, low_delay_brc1. Hardware probe supported and all3 usedh264_qsv. Warm sampled output27.2/27.2/27.4sec over26.9sec, bitrate2.21/2.28/2.31Mbps, max gaps0.86/0.69/1.02sec. Still insufficient pacing for a0.25sec player buffer; no claim bitrate cap fixes user's LAN symptom. Browser local NAS bridge showed all3readyState4 and lead0.14-0.34sec at snapshot, screenshot evidence/qsv-bitrate-1.2.5.png, but that is not sustained smoothness proof.
A/B CPU trial by explicit production Dvr:LiveEncoder=cpu: actual runtime libx264, recordings remain true. CPU bitrate11.19/13.73/24.11Mbps exceeds prior QSV, reinforcing that bitrate alone cannot explain user report. Observed max gaps0.69/0.95/0.61sec; startup/backlog affects media-time totals, no rigorous throughput comparison claimed. Left production on CPU as practical mitigation based on user's smoother CPU experience. Backed up original production config at local ignored artifacts/qsv-browser-test/production-before-cpu.json. Only added encoder selection; preserved other settings and external data. Decode/filter CPU then GPU transfer and async_depth1 are hypotheses requiring instrumented encode timing; actual root cause beyond observed delivery jitter remains unresolved.
29 checks passed; web/worker Release publish succeeded; NAShealth200/version1.2.5; anonymous stream401; deployed web hash mismatches0. Temporary browser test closed and loopback bridge stopped. Original recording encoder unchanged. IIS restart can cause brief recording gaps, standalone worker remains unactivated.
