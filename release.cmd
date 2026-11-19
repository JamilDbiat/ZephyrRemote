@echo off
set /p v=New version number (for example 1.0.1): 
set /p n=What changed: 
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version %v% -Notes "%n%"
pause
