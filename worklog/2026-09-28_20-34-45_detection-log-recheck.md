# Detection log recheck

2026-09-28 20:34:45–20:35 +07. User requested another log inspection.

Read current NAS detection JSONL only. Process8200 continues without worker_error events. Latest read has69 complete results, versus35 at20:24:55 during the previous investigation. Recent60second clips take approximately15.06–15.13seconds, GPU/D3D11VA. One recent front-door result reports motion=true, human=false; this is model output, not independently verified accuracy.

Heartbeats20:29:55–20:34:55 cover300seconds, aggregate completed-video/wall throughput3.6134x. Unresolved queue696->691; this remains above the3x requirement for three continuous cameras in this observed window, but is not sustained motion-heavy capacity proof. Six unique historic errors have now exhausted three retries (18 failed attempts total); they remain in unresolved totals and are not actively retried. Their generic analysis_failed cause remains unconfirmed. No evidence that these failures stop the worker.

No code/configuration/database changes, deployment, browser/API tests, or footage access. Documentation-only update committed/pushed. UI refresh behavior was not tested live.
