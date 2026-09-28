# Eye4 static reverse engineering

2026-09-28 19:20:49 Asia/Bangkok onward. User requested reverse engineering Eye4 after the mobile playback/detection investigation.

Read project guidance and recent worklog. Inspected installed Windows Eye4 binaries without executing; located alarm CGI and status fields and confirmed command-dispatch xrefs. Acquired partial Android5.9.5 mirror download after the vendor-linked download returned401. Recovered three complete DEX files; internal SHA1/Adler32 checks pass. JADX decompiled relevant Java; failures elsewhere and missing complete APK publisher verification are explicitly documented. No credential files inspected, no production/browser/camera tests, no app login or camera setting changes.

Deliverable: docs/eye4-protocol-findings.md, evidence hashes, concrete method paths, event source separation and integration requirements. Identified local sensor logs, cloud event history and SD filename motion markers as separate paths. Confirmed cloud code18 motion/41 human agrees with vendor public push docs. Found additional human capability query paths2017/2126/2127, so existing2106-only probe cannot establish absence of human support. Did not deploy speculative event parsing or silently enable unsupported controls.

Research files remain ignored under artifacts/eye4-re; no vendor binary or decompiled source goes to public Git. Only authored report/worklog/agent note/index committed. Runtime source unchanged; no deployment. Full APK signature and actual installed camera responses remain unverified. User's code-only testing boundary remains in effect.
