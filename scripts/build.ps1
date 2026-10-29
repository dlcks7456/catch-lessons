param([string]$OutputFolder = "artifacts/app")
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools/home'
$env:NUGET_PACKAGES = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& $sdk publish src/CatchLessons.Windows/CatchLessons.csproj -c Release -r win-x64 --self-contained true -o $OutputFolder
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
# App-local VC runtime for this personal Windows build; no system installer is run.
foreach ($runtimeName in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','msvcp140_codecvt_ids.dll','concrt140.dll')) {
    $runtimeSource = Join-Path ([Environment]::SystemDirectory) $runtimeName
    if (Test-Path -LiteralPath $runtimeSource) { Copy-Item -LiteralPath $runtimeSource -Destination (Join-Path $PWD $OutputFolder) -Force }
}
if ($OutputFolder -ne 'artifacts/app') { return }
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcut = $shortcutShell.CreateShortcut((Join-Path (Split-Path $PSScriptRoot -Parent) 'CatchLessons.lnk'))
$shortcut.TargetPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/app/CatchLessons.exe'
$shortcut.WorkingDirectory = (Split-Path $PSScriptRoot -Parent)
$shortcut.IconLocation = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Assets/app.ico') + ',0'
$shortcut.Description = 'CatchLessons - 시스템 오디오 자막 및 번역'
$shortcut.Save()
