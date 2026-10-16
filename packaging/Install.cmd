@echo off
rem Use the standard Windows location first, so this works even if PATH does not contain powershell (issue 635).
set "PS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%PS%" set "PS=powershell"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Ask
pause
