[CmdletBinding()]
param([string]$Destination='C:\Users\tatsa\web-data\MotionService\runtime',[string]$Ffmpeg=(Get-Command ffmpeg -ErrorAction Stop).Source,[string]$CudaBin,[string]$CudnnBin)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path "$Destination\models","$Destination\tools" | Out-Null
Invoke-WebRequest 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile "$Destination\python.zip"
Expand-Archive -LiteralPath "$Destination\python.zip" -DestinationPath $Destination -Force
$package=if($CudaBin -and $CudnnBin){'onnxruntime-gpu==1.23.2'}else{'onnxruntime-gpu[cuda,cudnn]==1.23.2'}
py -3.10 -m pip install --index-url https://pypi.org/simple --target "$Destination/Lib/site-packages" --platform win_amd64 --python-version 3.11 --only-binary=:all: $package numpy==2.2.6 opencv-python-headless==4.12.0.88
if($LASTEXITCODE -ne 0){throw 'Motion runtime installation failed'}
Set-Content -LiteralPath "$Destination/python311._pth" -Value "python311.zip`n.`nLib/site-packages`nimport site"
Invoke-WebRequest 'https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_tiny.onnx' -OutFile "$Destination/models/yolox_tiny.onnx"
if((Get-FileHash "$Destination/models/yolox_tiny.onnx").Hash -ne '427CC366D34E27FF7A03E2899B5E3671425C262EA2291F88BB942BC1CC70B0F7'){throw 'Model hash mismatch'}
Invoke-WebRequest 'https://raw.githubusercontent.com/Megvii-BaseDetection/YOLOX/main/LICENSE' -OutFile "$Destination/models/LICENSE-YOLOX.txt"
Copy-Item -LiteralPath $Ffmpeg -Destination "$Destination/tools/ffmpeg.exe"
if($CudaBin -and $CudnnBin){
 New-Item -ItemType Directory -Force -Path "$Destination/cuda"|Out-Null
 Get-ChildItem -LiteralPath $CudaBin -File | Where-Object Name -match '^(cublas.*12|cudart.*12|cufft.*11|curand.*10|nvrtc.*|nvJitLink.*)\.dll$' | Copy-Item -Destination "$Destination/cuda"
 Get-ChildItem -LiteralPath $CudnnBin -File -Filter '*64_9.dll' | Copy-Item -Destination "$Destination/cuda"
 Write-Output "Set Motion:CudaDllDirectory to $Destination\cuda. Preserve the installed NVIDIA license notices."
}
