param([string]$PreviousInstaller='', [string]$PreviousSha256='')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$root=(Resolve-Path 'artifacts').Path
$version=([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
$setup=(Resolve-Path ('artifacts/installers/CatchLessons-Windows-x64-'+$version+'.exe')).Path
$existing='HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{C39D01BF-F322-45D5-93AB-4402D96C828A}_is1'
if(Test-Path -LiteralPath $existing){throw 'An existing CatchLessons installation was found; isolated installation verification was not run.'}
$settings=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CaptionGlass/settings.json'
$before=if(Test-Path -LiteralPath $settings){(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash}else{$null}
$expectedSettings=$before
$modes=@('api','whisper','reuse')
if($PreviousInstaller){
 if($before){throw 'Upgrade verification requires a fresh profile without user settings.'}
 $PreviousInstaller=(Resolve-Path -LiteralPath $PreviousInstaller).Path
 if($PreviousSha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $PreviousInstaller -Algorithm SHA256).Hash -ne $PreviousSha256){throw 'Previous installer checksum mismatch'}
 $modes+='upgrade'
}
$results=@()
foreach($task in $modes){
 $target=Join-Path $root ('installer-verify-'+[Guid]::NewGuid().ToString('N'))
 $absolute=[IO.Path]::GetFullPath($target)
 if(-not $absolute.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $absolute)){throw 'Invalid or existing verification target'}
 try {
  if($task -eq 'upgrade'){
   $oldArgs=@('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOCLOSEAPPLICATIONS','/TASKS=whisper',('/DIR='+$absolute))
   $old=Start-Process -FilePath $PreviousInstaller -ArgumentList $oldArgs -WindowStyle Hidden -PassThru -Wait
   if($old.ExitCode -ne 0){throw 'Previous installer failed'}
   $oldVersion=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $absolute 'CatchLessons.dll')).Version
   if($oldVersion -ge [version]$version){throw 'Upgrade fixture is not an older app version'}
   $recording=Join-Path $absolute 'recording-fixture.txt';[IO.File]::WriteAllText($recording,'recording must remain')
   $modelPath=Join-Path $absolute 'models/ggml-large-v3-turbo-q5_0.bin';$modelBefore=(Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash
   [Reflection.Assembly]::Load('System.Security.Cryptography.ProtectedData') | Out-Null
   $protected=[Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes('installer-upgrade-test-key'),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
   New-Item -ItemType Directory -Path (Split-Path $settings -Parent) -Force | Out-Null
   [IO.File]::WriteAllText($settings,(@{SpeechEngine='whisper';ProtectedKey=$protected;OutputFolder=$absolute;OutputFolderMigrated=$true;Topic='Upgrade fixture'}|ConvertTo-Json))
   $expectedSettings=(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash
  }
  $tasks=if($task -in @('api','upgrade')){'/TASKS=!whisper'}else{'/TASKS=whisper'}
  $log=Join-Path $root ('installer-'+$task+'-'+[Guid]::NewGuid().ToString('N')+'.log')
  $args=@(('/LOG='+$log),'/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOCLOSEAPPLICATIONS',$tasks,('/DIR='+$absolute))
  $install=Start-Process -FilePath $setup -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
  if($install.ExitCode -ne 0){$detail=if(Test-Path -LiteralPath $log){(Get-Content -LiteralPath $log -Tail 15)-join "`n"}else{"No installer log."};throw "Install failed ($task): $($install.ExitCode)`n$detail"}
  $resolved=(Resolve-Path -LiteralPath $absolute).Path
  if($resolved -ne $absolute -or -not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Installed target failed path verification'}
  if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $resolved 'CatchLessons.dll')).Version.ToString(3) -ne $version){throw 'Installed assembly version mismatch'}
  if($task -eq 'upgrade'){
   if((Get-Content -LiteralPath $recording -Raw) -ne 'recording must remain' -or (Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $modelBefore){throw 'Upgrade changed recording or model fixture'}
   $saved=Get-Content -LiteralPath $settings -Raw|ConvertFrom-Json
   $restored=[Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($saved.ProtectedKey),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
   if($restored -ne 'installer-upgrade-test-key'){throw 'Upgrade changed the protected API key'}
  }
  if($task -eq 'reuse'){
   $recording=Join-Path $resolved 'recording-fixture.txt';[IO.File]::WriteAllText($recording,'recording must remain')
   $fixtureSettings=Join-Path $resolved 'settings-fixture.json';[IO.File]::WriteAllText($fixtureSettings,'{"speechEngine":"whisper"}')
   $modelPath=Join-Path $resolved 'models/ggml-large-v3-turbo-q5_0.bin';$modelBefore=(Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash
   $reinstallArgs=@('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/NOCLOSEAPPLICATIONS','/TASKS=!whisper',('/DIR='+$absolute))
   $reinstall=Start-Process -FilePath $setup -ArgumentList $reinstallArgs -WindowStyle Hidden -PassThru -Wait
   if($reinstall.ExitCode -ne 0){throw 'Reinstall without Whisper failed'}
   if((Get-Content -LiteralPath $recording -Raw) -ne 'recording must remain' -or (Get-Content -LiteralPath $fixtureSettings -Raw) -ne '{"speechEngine":"whisper"}'){throw 'Reinstall changed recording or settings fixture'}
   if((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $modelBefore){throw 'Reinstall changed existing Whisper model'}
  }
  $marker=Test-Path -LiteralPath (Join-Path $resolved 'whisper-disabled.flag')
  $model=Test-Path -LiteralPath (Join-Path $resolved 'models/ggml-large-v3-turbo-q5_0.bin')
  if(($task -eq 'api' -and (-not $marker -or $model)) -or ($task -ne 'api' -and ($marker -or -not $model))){throw "Install selection failed: $task"}
  if($model -and (Get-FileHash -LiteralPath (Join-Path $resolved 'models/ggml-large-v3-turbo-q5_0.bin') -Algorithm SHA256).Hash -ne '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2'){throw 'Installed Whisper checksum mismatch'}
  $diagnostic=Join-Path $root ('install-mode-'+$task+'.json')
  $app=Start-Process -FilePath (Join-Path $resolved 'CatchLessons.exe') -ArgumentList '--install-mode-test',$diagnostic -WorkingDirectory $PWD -WindowStyle Hidden -PassThru -Wait
  if($app.ExitCode -ne 0){throw "Installed app diagnostic failed: $task"}
  $result=Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
  if(($task -eq 'api' -and -not $result.apiOnly) -or ($task -ne 'api' -and -not $result.whisperEnabled)){throw "Installed engine setting incorrect: $task"}
  $results+=@{mode=$task;passed=$true;modelIncluded=$model;apiOnly=$result.apiOnly;userSettingsUnchanged=$true;target=$resolved;version=$version;previousVersion=if($task -eq 'upgrade'){$oldVersion.ToString(3)}else{$null}}
 } finally {
  if(Test-Path -LiteralPath $absolute){
   $resolved=(Resolve-Path -LiteralPath $absolute).Path
   if($resolved -ne $absolute -or -not $resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target failed path verification'}
   $uninstaller=Join-Path $resolved 'unins000.exe'
   if(Test-Path -LiteralPath $uninstaller){$uninstall=Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOCLOSEAPPLICATIONS' -WindowStyle Hidden -PassThru -Wait;if($uninstall.ExitCode -ne 0){throw "Uninstall failed: $task"}}
  }
 }
 $after=if(Test-Path -LiteralPath $settings){(Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash}else{$null}
 if($expectedSettings -ne $after){throw 'User settings changed during installer verification'}
 if(Test-Path -LiteralPath $existing){throw 'Uninstall registry entry remains'}
 if(Test-Path -LiteralPath (Join-Path $absolute 'whisper-disabled.flag')){throw 'Installer-owned marker remains after uninstall'}
}
$results|ConvertTo-Json|Set-Content -Encoding utf8 'artifacts/installer-verification.json'
Get-Content 'artifacts/installer-verification.json'




