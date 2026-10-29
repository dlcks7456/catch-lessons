$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$version=([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid version'}
& ./scripts/build.ps1 -OutputFolder artifacts/installer-app
$models=Join-Path $PWD 'artifacts/installer-app/models';New-Item -ItemType Directory -Force $models | Out-Null
$items=@(
 @{Name='ggml-large-v3-turbo-q5_0.bin';Repo='ggerganov/whisper.cpp';Hash='394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2'},
 @{Name='ggml-silero-v6.2.0.bin';Repo='ggml-org/whisper-vad';Hash='2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987'}
)
foreach($item in $items){$path=Join-Path $models $item.Name;$existing=Join-Path $PWD ('artifacts/app/models/'+$item.Name);if(-not(Test-Path -LiteralPath $path) -and (Test-Path -LiteralPath $existing) -and (Get-FileHash -LiteralPath $existing -Algorithm SHA256).Hash -eq $item.Hash){Copy-Item -LiteralPath $existing -Destination $path};if(-not(Test-Path -LiteralPath $path)){$temp=$path+'.download-'+[Guid]::NewGuid().ToString('N');Invoke-WebRequest -Uri ("https://huggingface.co/"+$item.Repo+"/resolve/main/"+$item.Name) -OutFile $temp;if((Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash -ne $item.Hash){throw "Model checksum failed: $($item.Name)"};Move-Item -LiteralPath $temp -Destination $path};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $item.Hash){throw "Existing model checksum failed: $path"}}
$compiler=Join-Path $PWD '.tools/inno-portable/ISCC.exe';if(-not(Test-Path -LiteralPath $compiler)){$candidate='C:/Program Files (x86)/Inno Setup 6/ISCC.exe';if(Test-Path $candidate){$compiler=$candidate}else{throw 'Install Inno Setup 6 or provide .tools/inno-portable/ISCC.exe.'}}
& $compiler '/Q' ('/DMyAppVersion='+$version) 'installer/CatchLessons.iss'
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}


