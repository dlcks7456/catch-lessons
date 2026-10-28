@echo off
cd /d "%~dp0"
if exist "artifacts\app\CatchLessons.exe" (
  start "" "artifacts\app\CatchLessons.exe"
) else (
  echo Run build.ps1 first.
  pause
)
