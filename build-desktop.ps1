$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.tools/home'
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.tools/nuget'
$sdk=Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if(-not(Test-Path $sdk)){$sdk='dotnet'}
& $sdk publish Desktop/CatchLessons.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/desktop-win-x64
if($LASTEXITCODE -ne 0){throw 'Desktop build failed'}
foreach($name in @('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll','msvcp140_1.dll','msvcp140_2.dll','msvcp140_atomic_wait.dll','concrt140.dll')){
 $source=Join-Path ([Environment]::SystemDirectory) $name
 if(Test-Path $source){Copy-Item -LiteralPath $source -Destination artifacts/desktop-win-x64 -Force}
}
