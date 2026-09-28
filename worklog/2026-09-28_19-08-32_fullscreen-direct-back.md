# Direct overview navigation from fullscreen

2026-09-28 19:08:32 Asia/Bangkok. User confirms mobile single view becomes fullscreen landscape, but returning requires exiting fullscreen first then tapping the header Back. Requests eliminating this redundant step.

Added a Back to all cameras button inside each video-wrap, visible only in native-browser fullscreen or Android app-fullscreen. This subtree remains visible when the external card header is excluded by fullscreen. Uses the existing data-back-overview binding/showOverview navigation: exit fullscreen/unlock native orientation, restore overview and preserve existing video sessions in one action. Existing fullscreen icon is hidden while fullscreen is active. Normal non-fullscreen desktop control remains available. Back button uses safe-area offsets and44px minimum touch height.

JavaScript syntax,4 rendering/binding tests (including common header/fullscreen back navigation),3 layout/session preservation tests and4 mobile fullscreen/lock/native bridge checks passed. No real browser/device/production HTTP testing. Actual user report supplies evidence of prior mobile entry behavior, not verification of the new Back action.

Deployed only app.js/app.css after timestamped backup under web-setup/GimDvr/backup-fullscreen-back-*, verified source/deployed hashes and updated publish staging. No binary restart, recording interruption or APK rebuild. Refresh/reopen loads shared assets for web and Android. Notes/index updated; source prepared for main commit/push.
