#Requires -RunAsAdministrator
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'GIMKIM-NAS'){throw 'Run on GIMKIM-NAS only.'}
$exe='C:\Users\tatsa\web-tools\mediamtx.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Install the verified MediaMTX binary first.'}
foreach($protocol in @('UDP','TCP')){
 $name="GimDvr-WebRTC-$protocol"
 $rule=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
 if($rule){Remove-NetFirewallRule -Name $name}
 New-NetFirewallRule -Name $name -DisplayName "GimDVR WebRTC $protocol 8189" -Direction Inbound -Action Allow -Protocol $protocol -LocalPort 8189 -Program $exe -Profile Any | Out-Null
}
Get-NetFirewallRule -Name 'GimDvr-WebRTC-*' | Select-Object Name,Enabled,Action
Write-Output 'NAS firewall configured. For remote Internet use, forward TCP+UDP 8189 on the router to this NAS. Never expose ports18889 or19997.'
