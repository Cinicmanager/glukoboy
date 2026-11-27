@echo off
setlocal DisableDelayedExpansion
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0startup.ps1"
set "GLUKOBOY_RESULT=%ERRORLEVEL%"
pause
exit /b %GLUKOBOY_RESULT%
