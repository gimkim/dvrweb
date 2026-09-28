# Current overview buffering and reported latency

Time:2026-09-28 18:39:26 Asia/Bangkok. User confirms deployed version works, estimates slightly over2seconds delay, asks current buffer, possible benefit of reducing it, and largest source of latency.

Read current player, fragment mux arguments and proxy loop. Deployed copy-stream.js SHA256 matches source; deployed DLL matches current published1.6.1 binary. No runtime settings changed, no deployment/restart, no production HTTP/browser testing or new media sampling.

Verified configuration: first playback reserve0.45s, initial/seek target0.5s behind received buffered end, starvation reserve0.4s; catchup1.05x only above0.9s lead; jump when lead exceeds2s. Therefore0.5s is a target at initialization/correction, not a continuously enforced cap. Steady lead can stay above0.5s; a1second excess takes approximately20seconds to shed at1.05x if incoming media proceeds at realtime. Back-buffer deletion keeps about3seconds of already-played media; that is not forward latency. Shared cache128fragments is likewise not a wait-to-fill buffer.

FFmpeg requests200ms fMP4 fragments and flushes them; proxy polls every50ms and flushes each framed packet. Polling interval is a configured wait, not measured worst-case IO/network delay. Actual fragment completion can be affected by source packet arrival, muxing and process scheduling. Keyframe gating affects startup/reconnect, not every subsequent fragment. No extra video encoding in overview.

Reducing a0.5s player target to0.25–0.3s can theoretically remove about0.2–0.25s when the player is actually near its target, with less resilience to arrival jitter. It cannot by itself explain/remove the entire reported2+seconds. Need per-stage timing (arrival/publication/receive/currentTime) to distinguish source/RTSP/mux/cache/proxy delays from browser lead and decoding. None exists in this investigation, so do not label camera, NAS, network or browser the measured largest bottleneck. User timing is an observation, not synchronized instrumentation. Keep the working deployment unchanged for this explanatory request.
