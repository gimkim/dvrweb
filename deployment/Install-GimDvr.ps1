#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$SiteName)
$ErrorActionPreference='Stop'
if ($env:COMPUTERNAME -ne 'GIMKIM-NAS') { throw 'Run this script ON GIMKIM-NAS in an elevated Windows PowerShell.' }
Import-Module WebAdministration
$appPath='C:\Users\tatsa\web\dvrcam'
$dataPath='C:\Users\tatsa\web-data\GimDvr'
$pool='GimDvr'
if (!(Test-Path -LiteralPath "$appPath\GimDvr.dll")) { throw 'Publish files are missing.' }
if (!(Test-Path -LiteralPath 'C:\Users\tatsa\web-tools\ffmpeg.exe')) { throw 'FFmpeg is missing.' }
if (!$SiteName) {
    $matching=@(Get-Website | Where-Object { @($_.Bindings.Collection | Where-Object { $_.bindingInformation -match ':gimgim\.ddns\.net$' }).Count -gt 0 })
    if ($matching.Count -eq 1) { $SiteName=$matching[0].Name }
    else {
        $httpsSites=@(Get-Website | Where-Object { @($_.Bindings.Collection | Where-Object { $_.protocol -eq 'https' }).Count -gt 0 })
        if($httpsSites.Count -eq 1){$SiteName=$httpsSites[0].Name}
        else{throw 'Specify -SiteName with the existing IIS site that serves https://gimgim.ddns.net. No site was changed.'}
    }
}
if (!(Test-Path "IIS:\Sites\$SiteName")) { throw "IIS site does not exist: $SiteName" }
if (@(Get-WebApplication | Where-Object { $_.applicationPool -eq $pool -and $_.path -ne '/gimdvr' }).Count -gt 0) { throw 'GimDvr pool is shared by another application; refusing to change it.' }
$preexisting=Get-WebApplication -Site $SiteName -Name gimdvr
if ($preexisting -and [IO.Path]::GetFullPath($preexisting.PhysicalPath).TrimEnd('\') -ne $appPath) { throw 'Existing /gimdvr points elsewhere. Resolve this explicitly in IIS.' }
# Preserve a server configuration backup before changing only this application.
$backupName='Before-GimDvr-'+(Get-Date -Format yyyyMMdd-HHmmss)
Backup-WebConfiguration -Name $backupName
New-Item -ItemType Directory -Force -Path $dataPath | Out-Null
if (!(Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
Set-ItemProperty "IIS:\AppPools\$pool" managedRuntimeVersion ''
Set-ItemProperty "IIS:\AppPools\$pool" managedPipelineMode Integrated
Set-ItemProperty "IIS:\AppPools\$pool" startMode AlwaysRunning
Set-ItemProperty "IIS:\AppPools\$pool" processModel.identityType ApplicationPoolIdentity
Set-ItemProperty "IIS:\AppPools\$pool" processModel.loadUserProfile $true
Set-ItemProperty "IIS:\AppPools\$pool" processModel.idleTimeout ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$pool" processModel.maxProcesses 1
Set-ItemProperty "IIS:\AppPools\$pool" recycling.periodicRestart.time ([TimeSpan]::Zero)
Clear-ItemProperty "IIS:\AppPools\$pool" recycling.periodicRestart.schedule
if (Test-Path "IIS:\Sites\$SiteName\gimdvr") {
    $existing=Get-WebApplication -Site $SiteName -Name gimdvr
    if (!$existing) { throw 'An existing virtual directory named gimdvr must be converted to an application in IIS first.' }
    if ([IO.Path]::GetFullPath($existing.PhysicalPath).TrimEnd('\') -ne $appPath) { throw 'Existing /gimdvr points elsewhere. Resolve this explicitly in IIS.' }
    Set-ItemProperty "IIS:\Sites\$SiteName\gimdvr" applicationPool $pool
} else { New-WebApplication -Site $SiteName -Name gimdvr -PhysicalPath $appPath -ApplicationPool $pool | Out-Null }
Set-ItemProperty "IIS:\Sites\$SiteName\gimdvr" preloadEnabled $true
$location="$SiteName/gimdvr"
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name userName -Value ''
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $false
$feature=Get-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit
if ($feature.State -ne 'Enabled') { Enable-WindowsOptionalFeature -Online -FeatureName IIS-ApplicationInit -All -NoRestart | Out-Null }
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/applicationInitialization' -Name doAppInitAfterRestart -Value $true
# Only this application's data. No other app pools receive access to credentials.
$owner=(Get-Acl -LiteralPath $dataPath).GetOwner([Security.Principal.SecurityIdentifier])
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetOwner($owner)
$acl.SetAccessRuleProtection($true,$false)
foreach($sid in @($owner.Value,'S-1-5-18','S-1-5-32-544')) {
    $identity=New-Object Security.Principal.SecurityIdentifier($sid)
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
}
$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$pool",'Modify','ContainerInherit,ObjectInherit','None','Allow')))
if(Get-Service GimDvrRecorder -ErrorAction SilentlyContinue){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('NT SERVICE\GimDvrRecorder','Modify','ContainerInherit,ObjectInherit','None','Allow')))}
Set-Acl -LiteralPath $dataPath -AclObject $acl
& icacls.exe $appPath /grant "IIS AppPool\${pool}:(OI)(CI)RX" /T /Q
if($LASTEXITCODE -ne 0){throw 'Application ACL failed'}
& icacls.exe 'C:\Users\tatsa\web-tools\ffmpeg.exe' /grant "IIS AppPool\${pool}:RX" /Q
if($LASTEXITCODE -ne 0){throw 'FFmpeg ACL failed'}
& icacls.exe 'C:\Users\tatsa\web-tools\ffprobe.exe' /grant "IIS AppPool\${pool}:RX" /Q
if($LASTEXITCODE -ne 0){throw 'FFprobe ACL failed'}
if((Get-WebAppPoolState $pool).Value -eq 'Started'){Restart-WebAppPool $pool}else{Start-WebAppPool $pool}
Write-Host "Installed /gimdvr on $SiteName. IIS backup: $backupName"
Write-Host 'Open https://gimgim.ddns.net/gimdvr/ to initialize the app.'
Write-Host "Read $dataPath\bootstrap.txt for the initial admin password. Recording remains disabled until configured."
