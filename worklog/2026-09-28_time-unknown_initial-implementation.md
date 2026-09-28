# 2026-09-28 — Initial implementation

Backfilled at: 2026-09-28T16:56:27+07:00
Source: [original archive, line 1](../worklogs/2026-09-28.md), section `2026-09-28 — Initial implementation`.
Original time unknown.
Retrospective copy, not new execution or verification. Later entries may supersede its decisions.

## Original entry

User scope: .NET 10 NAS DVR at /gimdvr, three initial cameras, extensible camera/user management, proxied viewing, per-camera storage/retention, one-minute segments. Recording must remain off until the owner sets a folder.

Implemented source: cookie roles and account management, encrypted camera credentials, SQLite catalogue, VStarcam controls, FFmpeg HLS proxy and segmented MP4 recorder, retention, range playback, Thai responsive UI. Initial camera connection details validated by authenticated RTSP and CGI requests. Three live streams decoded in the local browser; recording count remained zero.

Device boundary: human-detection state is not returned by this firmware. RTSP Require backchannel probe returns only video and camera microphone audio tracks. Official H5 SDK inspected; its browser interface requires a local desktop helper. Talk remains unavailable, explicitly exposed in the UI.

NAS boundary: SMB write access is available; remote service/IIS configuration access is denied. Existing HTTPS /gimdvr returns an IIS 401 before deployment. Installation assets and final verification are in progress. No production recording was started.
