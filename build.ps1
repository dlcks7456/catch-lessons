$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& $sdk publish CatchLessons.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
# App-local VC runtime for this personal Windows build; no system installer is run.
foreach ($runtimeName in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','concrt140.dll')) {
    $runtimeSource = Join-Path ([Environment]::SystemDirectory) $runtimeName
    if (Test-Path -LiteralPath $runtimeSource) { Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $PSScriptRoot 'artifacts/app') -Force }
}
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut((Join-Path $PSScriptRoot 'CatchLessons.lnk'))
$shortcut.TargetPath = Join-Path $PSScriptRoot 'artifacts/app/CatchLessons.exe'
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = (Join-Path $PSScriptRoot 'Assets/app.ico') + ',0'
$shortcut.Description = 'CatchLessons - 시스템 오디오 자막 및 번역'
$shortcut.Save()
