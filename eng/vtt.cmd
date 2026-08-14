@echo off
setlocal EnableExtensions EnableDelayedExpansion

where.exe pwsh.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
  pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0vtt.ps1" %*
  exit /b !ERRORLEVEL!
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0vtt.ps1" %*
exit /b %ERRORLEVEL%
