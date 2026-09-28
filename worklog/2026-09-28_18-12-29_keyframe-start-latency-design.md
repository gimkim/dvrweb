# Keyframe-gated startup and small-buffer feasibility

Time: 2026-09-28T18:12:29.924230+07:00

User asks whether waiting for a keyframe before starting and keeping a small buffer should allow low-latency copy-only video.

Yes, provided transport publishes the keyframe and subsequent dependent frames incrementally. A long GOP primarily delays initial acquisition/recovery in such a transport; it does not inherently impose the same steady-state latency. Need decoder configuration (SPS/PPS) plus a decodable random-access frame/IDR, contiguous timestamps and subsequent frames in order. A few-hundred-millisecond player reserve is a starting tuning target, not a measured end-to-end guarantee.

Current overview instead publishes completed HLS TS files: waiting for the first keyframe does not remove the wait to finalize that file at a later boundary. Existing playlists were observed at4seconds in the prior worklog; no new production measurement occurred. Reducing the player reserve alone cannot remove publication batching. Incrementally delivered TS or short fMP4 fragments can preserve original encoded video without re-encoding, but require a corresponding proxy/player change and handling of joining viewers/reconnect/keyframe gating. This is a feasibility explanation, not an implemented transport change.

Read existing notes/current code context and official FFmpeg muxer documentation https://ffmpeg.org/ffmpeg-formats.html (HLS keyframe segmentation and MP4 fragmentation). No code changes, deployment, tests, browser use, live-site calls or camera configuration changes occurred. Added this worklog and index only. Current streaming behavior remains1.5.0 plus the previous overview-buffer static patch.
