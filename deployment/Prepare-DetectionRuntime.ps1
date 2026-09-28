[CmdletBinding()]
param([string]$Destination=(Join-Path $PSScriptRoot '../artifacts/detection-runtime'))
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path "$Destination/models" -Force | Out-Null
# Isolated Python, no system installation and no camera/cloud credentials.
Invoke-WebRequest 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile "$Destination/python.zip"
Expand-Archive -LiteralPath "$Destination/python.zip" -DestinationPath $Destination -Force
py -3.10 -m pip install --target "$Destination/Lib/site-packages" --platform win_amd64 --python-version 3.11 --only-binary=:all: openvino==2025.4.1 numpy==2.2.6 opencv-python-headless==4.12.0.88 openvino-telemetry==2025.2.0 packaging==26.3
if($LASTEXITCODE -ne 0){throw 'Detection dependencies could not be prepared'}
Set-Content -LiteralPath "$Destination/python311._pth" -Value "python311.zip`n.`nLib/site-packages`nimport site"
$base='https://storage.openvinotoolkit.org/repositories/open_model_zoo/2023.0/models_bin/1/person-detection-retail-0013/FP16/person-detection-retail-0013'
$hashes=@{xml='19556695D2B18255FE5593D388AE69BA7F9A2F5C3F986BDE8356741D0DB26E89';bin='CF4A1F2F252D229966001E15454C5BA02A7ACE047B181B235E3A2C4AEBCF3BA7'}
foreach($extension in @('xml','bin')){
 Invoke-WebRequest "$base.$extension" -OutFile "$Destination/models/person.$extension"
 if((Get-FileHash "$Destination/models/person.$extension").Hash -ne $hashes[$extension]){throw 'Model digest mismatch'}
}
Invoke-WebRequest 'https://raw.githubusercontent.com/openvinotoolkit/open_model_zoo/master/LICENSE' -OutFile "$Destination/models/LICENSE.txt"
Write-Output "Runtime prepared: $Destination. Deploy outside the web root; keep third-party licenses."
