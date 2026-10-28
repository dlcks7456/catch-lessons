$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$root=(Resolve-Path 'artifacts').Path
$version=([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
$setup=(Resolve-Path ('artifacts/installers/CatchLessons-Windows-x64-'+$version+'.exe')).Path
$existing='HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{C39D01BF-F322-45D5-93AB-4402D96C828A}_is1'
if(Test-Path -LiteralPath $existing){throw 'An existing CatchLessons installation was found; isolated installation verification was not run.'}
$settings=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CaptionGlass/settings.json'
$before=if(Test-Path -LiteralPath $settings){(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash}else{$null}
$results=@()
foreach($task in @('api','whisper')){
 $target=Join-Path $root ('installer-verify-'+[Guid]::NewGuid().ToString('N'))
 $absolute=[IO.Path]::GetFullPath($target)
 if(-not $absolute.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $absolute)){throw 'Invalid or existing verification target'}
 try {
  $tasks=if($task -eq 'api'){'/TASKS=!whisper'}else{'/TASKS=whisper'}
  $log=Join-Path $root ('installer-'+$task+'-'+[Guid]::NewGuid().ToString('N')+'.log')
  $args=@(('/LOG='+$log),'/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOCLOSEAPPLICATIONS',$tasks,('/DIR='+$absolute))
  $install=Start-Process -FilePath $setup -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
  if($install.ExitCode -ne 0){$detail=if(Test-Path -LiteralPath $log){(Get-Content -LiteralPath $log -Tail 15)-join "`n"}else{"No installer log."};throw "Install failed ($task): $($install.ExitCode)`n$detail"}
  $resolved=(Resolve-Path -LiteralPath $absolute).Path
  if($resolved -ne $absolute -or -not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Installed target failed path verification'}
  $marker=Test-Path -LiteralPath (Join-Path $resolved 'whisper-disabled.flag')
  $model=Test-Path -LiteralPath (Join-Path $resolved 'models/ggml-large-v3-turbo-q5_0.bin')
  if(($task -eq 'api' -and (-not $marker -or $model)) -or ($task -eq 'whisper' -and ($marker -or -not $model))){throw "Install selection failed: $task"}
  if($model -and (Get-FileHash -LiteralPath (Join-Path $resolved 'models/ggml-large-v3-turbo-q5_0.bin') -Algorithm SHA256).Hash -ne '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2'){throw 'Installed Whisper checksum mismatch'}
  $diagnostic=Join-Path $root ('install-mode-'+$task+'.json')
  $app=Start-Process -FilePath (Join-Path $resolved 'CatchLessons.exe') -ArgumentList '--install-mode-test',$diagnostic -WorkingDirectory $PWD -WindowStyle Hidden -PassThru -Wait
  if($app.ExitCode -ne 0){throw "Installed app diagnostic failed: $task"}
  $result=Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
  if(($task -eq 'api' -and -not $result.apiOnly) -or ($task -eq 'whisper' -and -not $result.whisperEnabled)){throw "Installed engine setting incorrect: $task"}
  $results+=@{mode=$task;passed=$true;modelIncluded=$model;apiOnly=$result.apiOnly;userSettingsUnchanged=$true;target=$resolved}
 } finally {
  if(Test-Path -LiteralPath $absolute){
   $resolved=(Resolve-Path -LiteralPath $absolute).Path
   if($resolved -ne $absolute -or -not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target failed path verification'}
   $uninstaller=Join-Path $resolved 'unins000.exe'
   if(Test-Path -LiteralPath $uninstaller){$uninstall=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS' -WindowStyle Hidden -PassThru -Wait;if($uninstall.ExitCode -ne 0){throw "Uninstall failed: $task"}}
  }
 }
 $after=if(Test-Path -LiteralPath $settings){(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash}else{$null}
 if($before -ne $after){throw 'User settings changed during installer verification'}
 if(Test-Path -LiteralPath $existing){throw 'Uninstall registry entry remains'}
 if(Test-Path -LiteralPath (Join-Path $absolute 'whisper-disabled.flag')){throw 'Installer-owned marker remains after uninstall'}
}
$results|ConvertTo-Json|Set-Content -Encoding utf8 'artifacts/installer-verification.json'
Get-Content 'artifacts/installer-verification.json'




