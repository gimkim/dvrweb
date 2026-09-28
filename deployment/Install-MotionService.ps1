#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$WebRoot='C:\Users\tatsa\web\MotionService',[string]$DataRoot='C:\Users\tatsa\web-data\MotionService')
$ErrorActionPreference='Stop'
Import-Module WebAdministration
$result=Join-Path $DataRoot 'installation.json'
try {
 if(!(Test-Path -LiteralPath (Join-Path $WebRoot 'MotionService.dll'))){throw 'Publish MotionService first'}
 if(!(Test-Path -LiteralPath (Join-Path $DataRoot 'service.json'))){throw 'Prepare private service configuration first'}
 $backup='Before-MotionService-'+(Get-Date -Format yyyyMMdd-HHmmss)
 Backup-WebConfiguration -Name $backup
 $existing=@(Get-WebApplication | Where-Object path -eq '/MotionService')
 if($existing.Count -gt 1){throw 'Multiple MotionService applications; select site explicitly'}
 $bindings=@(Get-Website | ForEach-Object {$site=$_;foreach($b in $_.Bindings.Collection){if($b.protocol -eq 'https' -and $b.bindingInformation -in @('*:443:','192.168.1.30:443:')){[pscustomobject]@{Site=$site.Name;Binding=$b}}}})
 if($bindings.Count -ne 1){throw 'Expected one existing HTTPS binding for 192.168.1.30; no binding changed'}
 $siteName=$bindings[0].Site
 if($existing.Count -eq 1 -and !(Get-WebApplication -Site $siteName -Name MotionService)){throw 'Existing MotionService belongs to a different IIS site'}
 if($existing.Count -eq 1 -and [IO.Path]::GetFullPath($existing[0].PhysicalPath).TrimEnd('\') -ne $WebRoot){throw 'Existing app uses a different directory'}
 $pool='MotionService'
 if(@(Get-WebApplication | Where-Object {$_.applicationPool -eq $pool -and $_.path -ne '/MotionService'}).Count){throw 'Pool is shared'}
 if(!(Test-Path "IIS:\AppPools\$pool")){New-WebAppPool -Name $pool | Out-Null}
 Set-ItemProperty "IIS:\AppPools\$pool" managedRuntimeVersion ''
 Set-ItemProperty "IIS:\AppPools\$pool" processModel.identityType ApplicationPoolIdentity
 Set-ItemProperty "IIS:\AppPools\$pool" processModel.loadUserProfile $true
 Set-ItemProperty "IIS:\AppPools\$pool" processModel.idleTimeout ([TimeSpan]::Zero)
 Set-ItemProperty "IIS:\AppPools\$pool" processModel.maxProcesses 1
 Set-ItemProperty "IIS:\AppPools\$pool" startMode AlwaysRunning
 Set-ItemProperty "IIS:\AppPools\$pool" recycling.periodicRestart.time ([TimeSpan]::Zero)
 if(Test-Path "IIS:\Sites\$siteName\MotionService") {Set-ItemProperty "IIS:\Sites\$siteName\MotionService" applicationPool $pool}
 else {New-WebApplication -Site $siteName -Name MotionService -PhysicalPath $WebRoot -ApplicationPool $pool | Out-Null}
 Set-ItemProperty "IIS:\Sites\$siteName\MotionService" preloadEnabled $true
 $location="$siteName/MotionService"
 Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
 Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $location -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name userName -Value ''
 # All service endpoints additionally require a secret bearer key; do not enable directory/static serving.
 foreach($path in @($WebRoot,(Join-Path $DataRoot 'runtime'),(Join-Path $DataRoot 'service.json'))){
  $acl=Get-Acl -LiteralPath $path
  if((Get-Item -LiteralPath $path).PSIsContainer){$rule=New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$pool",'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')}
  else{$rule=New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$pool",'Read','Allow')}
  $acl.SetAccessRule($rule);Set-Acl -LiteralPath $path -AclObject $acl
 }
 foreach($name in @('incoming','logs')){
  $path=Join-Path $DataRoot $name;New-Item -ItemType Directory -Force -Path $path|Out-Null
  $acl=Get-Acl -LiteralPath $path;$acl.SetAccessRule((New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$pool",'Modify','ContainerInherit,ObjectInherit','None','Allow')));Set-Acl -LiteralPath $path -AclObject $acl
 }
 $binding=Get-WebBinding -Name $siteName -Protocol https | Where-Object bindingInformation -eq $bindings[0].Binding.bindingInformation
 $thumb=$binding.certificateHash
 if($thumb -is [byte[]]){$thumb=([BitConverter]::ToString($thumb)).Replace('-','')}
 $store=$binding.certificateStoreName;if(!$store){$store='My'}
 $cert=Get-Item "Cert:\LocalMachine\$store\$thumb"
 $sha=[Security.Cryptography.SHA256]::Create();$pin=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','');$sha.Dispose()
 Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
 @{success=$true;site=$siteName;certificateSha256=$pin;backup=$backup;time=(Get-Date -Format o)}|ConvertTo-Json|Set-Content -LiteralPath $result -Encoding UTF8
}catch{
 @{success=$false;error=$_.Exception.Message;time=(Get-Date -Format o)}|ConvertTo-Json|Set-Content -LiteralPath $result -Encoding UTF8
 throw
}
