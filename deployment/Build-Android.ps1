[CmdletBinding()]
param(
 [string]$AndroidSdk='C:\Program Files (x86)\Android\android-sdk',
 [string]$JavaSdk='C:\Program Files (x86)\Android\openjdk\jdk-17.0.14',
 [string]$SigningDirectory,
 [string]$BuildToolsVersion='36.0.0'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(!$SigningDirectory){$SigningDirectory=Join-Path $repo '.local-data\android-signing'}
$key=Join-Path $SigningDirectory 'gimdvr.keystore'
$pass=Join-Path $SigningDirectory 'store.pass'
if(!(Test-Path -LiteralPath $key) -or !(Test-Path -LiteralPath $pass)){throw 'Restore the private release keystore and store.pass before building an update. Do not replace the signing identity.'}
$project=Join-Path $repo 'src\GimDvr.Android\GimDvr.Android.csproj'
[xml]$xml=Get-Content -LiteralPath $project
$version=$xml.Project.PropertyGroup.ApplicationDisplayVersion
$outDir=Join-Path $repo 'artifacts\android'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
& dotnet publish $project -c Release "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$JavaSdk" -p:AndroidKeyStore=false -o $outDir
if($LASTEXITCODE -ne 0){throw 'Android publish failed'}
$apk=Join-Path $outDir "GimDVR-$version.apk"
$signer=Join-Path $AndroidSdk "build-tools\$BuildToolsVersion\apksigner.bat"
$oldJavaHome=$env:JAVA_HOME
try{
 $env:JAVA_HOME=$JavaSdk
 # PKCS12 uses one password. Passing the same file as both store/key password
 # makes apksigner read a nonexistent second line; omit --key-pass.
 & $signer sign --ks $key --ks-key-alias gimdvr --ks-pass "file:$pass" --out $apk (Join-Path $outDir 'net.gimgim.gimdvr.apk')
 if($LASTEXITCODE -ne 0){throw 'APK signing failed'}
 & $signer verify --verbose --print-certs $apk
 if($LASTEXITCODE -ne 0){throw 'APK signature verification failed'}
}finally{$env:JAVA_HOME=$oldJavaHome}
Get-FileHash -LiteralPath $apk
