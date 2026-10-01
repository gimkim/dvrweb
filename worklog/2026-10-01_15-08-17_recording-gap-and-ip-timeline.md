# Recording gaps and IP timeline

User asks when IP changed and how long recordings were missing. Read completed manifest CSV intervals and file metadata only; no liveSQLite access. All times Bangkok2026-10-01. Filename timestamps represent NAS-local recording starts; end derived from finalized manifest duration. Exact DHCP lease-change time is NOT available, so recording cessation is not presented as proven IP change time.

- garage: longest absent interval 2026-10-01 04:35:11.836989 to 2026-10-01 14:49:18, 36846.16seconds. Last finalized end at sampling 2026-10-01 15:05:50.999978.
- front-door: longest absent interval 2026-10-01 04:27:58.312978 to 2026-10-01 14:49:30, 37291.69seconds. Last finalized end at sampling 2026-10-01 15:05:58.979756.
- side: longest absent interval 2026-10-01 12:25:06.265067 to 2026-10-01 14:49:18, 8651.73seconds. Last finalized end at sampling 2026-10-01 15:06:31.418134.

Checked actual session directories during these large gaps: no nonzero or zero MP4 files beginning inside the missing intervals; no hidden uncatalogued recording found by this scan. Files before the boundary may still start before the interval; no recovery attempted.

Old .36/.37/.38 had changed by initialOct1address diagnostic at~14:47; UID-verified .43/.42/.46 found. Long gap endpoints alone cannot establish DHCP cause. Later userDHCPreservations .20/.21/.22 were confirmed/recovered15:00. Controlled appstop/update14:52:34 to14:59:47–55 added~7m12–21seconds per camera;Side reboot/leasehandoff added~28seconds15:00:02–30. Do not conflate planned maintenance with overnight loss. No source/config/deployment or browser/device tests. Worklog only.
