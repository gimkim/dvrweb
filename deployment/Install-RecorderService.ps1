#Requires -RunAsAdministrator
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'GIMKIM-NAS'){throw 'Run on GIMKIM-NAS in elevated Windows PowerShell.'}
$service='GimDvrRecorder'
$worker='C:\Users\tatsa\web-workers\GimDvr'
$web='C:\Users\tatsa\web\dvrcam'
$data='C:\Users\tatsa\web-data\GimDvr'
$exe=Join-Path $worker 'GimDvr.Worker.exe'
$configFile=Join-Path $web 'appsettings.Production.json'
if(!(Test-Path -LiteralPath $exe)){throw 'Worker publish files are missing.'}
if(!(Test-Path -LiteralPath $configFile)){throw 'Web production configuration is missing.'}
if(Test-Path -LiteralPath "$web\app_offline.htm"){throw 'Web deployment is already in progress.'}
$config=Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
$original=Get-Content -LiteralPath $configFile -Raw
$backup="$configFile.before-worker-$(Get-Date -Format yyyyMMdd-HHmmss)"
Copy-Item -LiteralPath $configFile -Destination $backup
$binary='"'+$exe+'"'
$existing=Get-CimInstance Win32_Service -Filter "Name='$service'"
if($existing -and $existing.PathName -ne $binary){throw 'Existing service uses another executable; refusing to replace it.'}
if(!$existing){
    & sc.exe create $service binPath= $binary start= delayed-auto obj= "NT SERVICE\$service" DisplayName= 'GimDVR background recorder'
    if($LASTEXITCODE -ne 0){throw 'Service creation failed.'}
}elseif($existing.State -ne 'Stopped'){
    Stop-Service $service
    (Get-Service $service).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))
}
foreach($path in @($worker,'C:\Users\tatsa\web-tools\ffmpeg.exe','C:\Users\tatsa\web-tools\ffprobe.exe')){
    $rights=if(Test-Path -LiteralPath $path -PathType Container){"(OI)(CI)RX"}else{"RX"}
    & icacls.exe $path /grant "NT SERVICE\${service}:$rights" /Q
    if($LASTEXITCODE -ne 0){throw "Cannot grant read/execute on $path"}
}
& icacls.exe $data /grant "NT SERVICE\${service}:(OI)(CI)M" /Q
if($LASTEXITCODE -ne 0){throw 'Worker data permissions failed.'}
& sc.exe failure $service reset= 86400 actions= restart/5000/restart/15000/restart/60000
if($LASTEXITCODE -ne 0){throw 'Cannot configure service recovery.'}
$config.Dvr | Add-Member -NotePropertyName MediaOwner -NotePropertyValue worker -Force
$workerConfig=$config | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText((Join-Path $worker 'appsettings.json'),$workerConfig)
# Let the web-owned reader finish before starting the service. The process lock
# additionally prevents any two owners from opening duplicate camera streams.
Set-Content -LiteralPath "$web\app_offline.htm" -Value 'GimDVR switching recorder to Windows Service.'
try{
    $released=$false
    for($attempt=0;$attempt -lt 45;$attempt++){
        try{$lock=[IO.File]::Open((Join-Path $data 'recorder.lock'),'OpenOrCreate','ReadWrite','None');$lock.Dispose();$released=$true;break}
        catch{Start-Sleep -Seconds 1}
    }
    if(!$released){throw 'Previous recorder has not stopped.'}
    [IO.File]::WriteAllText($configFile,$workerConfig)
    $startedAt=[DateTime]::UtcNow
    Start-Service $service
    (Get-Service $service).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    # A running SCM state alone does not prove the worker owns its lock.
    $ready=$false
    for($attempt=0;$attempt -lt 20;$attempt++){
        if(Test-Path -LiteralPath "$data\runtime\worker-heartbeat.txt"){
            if((Get-Item -LiteralPath "$data\runtime\worker-heartbeat.txt").LastWriteTimeUtc -gt $startedAt){$ready=$true;break}
        }
        Start-Sleep -Seconds 1
    }
    if(!$ready){throw 'Worker heartbeat not ready.'}
    Write-Host 'Background recorder active. Closing or recycling IIS no longer stops recordings.'
    Write-Host 'One camera input is shared by recording and every browser viewer.'
    Write-Host 'Grant NT SERVICE\GimDvrRecorder Modify on each selected recording folder. No camera recording setting was changed.'
}catch{
    Stop-Service $service -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText($configFile,$original)
    throw
}finally{Remove-Item -LiteralPath "$web\app_offline.htm" -ErrorAction SilentlyContinue}

