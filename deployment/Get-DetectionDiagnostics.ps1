[CmdletBinding()]
param(
 [string]$LogDirectory='\\gimkim-nas\C\Users\tatsa\web-data\GimDvr\logs\detection',
 [ValidateRange(1,20160)][int]$Minutes=30
)
$ErrorActionPreference='Stop'
$cutoff=[DateTimeOffset]::UtcNow.AddMinutes(-$Minutes)
if(!(Test-Path -LiteralPath $LogDirectory)){Write-Output 'No detection log directory yet; application may not have started.';return}
$rows=@(Get-ChildItem -LiteralPath $LogDirectory -File -Filter 'detection-*.jsonl' |
 Where-Object LastWriteTimeUtc -ge $cutoff.UtcDateTime |
 ForEach-Object {Get-Content -LiteralPath $_.FullName | ForEach-Object {
  try{$row=$_|ConvertFrom-Json;if([DateTimeOffset]$row.timeUtc -ge $cutoff){$row}}catch{}
 }} | Sort-Object {[DateTimeOffset]$_.timeUtc})
if(!$rows.Count){Write-Output 'No recent log entries. This does not establish worker health.';return}
$rows | Where-Object kind -eq 'summary' | Select-Object -Last 20 | ForEach-Object {
 [pscustomobject]@{
  Time=$_.timeUtc;Process=$_.pid;State=$_.data.stats.workerState;Rate=$_.data.stats.completedVideoSecondsPerWallSecond
  Cameras=$_.data.queue.configuredRecordingCameras;Unresolved=$_.data.queue.unresolvedFiles
  BacklogMinutes=[Math]::Round($_.data.queue.unresolvedVideoSeconds/60,1)
  OldestMinutes=[Math]::Round($_.data.queue.oldestUnresolvedAgeSeconds/60,1)
  Exhausted=$_.data.queue.retryExhaustedFiles
 }
} | Format-Table -AutoSize
$rows | Where-Object kind -in @('worker_error','runtime_missing','clip_error','service_skipped') |
 Select-Object -Last 10 timeUtc,kind,@{Name='Details';Expression={$_.data|ConvertTo-Json -Compress}} | Format-Table -AutoSize
Write-Output 'Rate includes idle/retry time and counts complete clips only. For 3 continuously recording cameras, sustained rate must exceed 3 to drain backlog. Compare multiple intervals and queue trend; do not infer capacity from a single minute.'
