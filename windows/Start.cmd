@echo off
setlocal
cd /d "%~dp0"
if exist "CodexBattery.exe" goto launch
call "%~dp0Build.cmd"
if errorlevel 1 exit /b 1
:launch
start "" "%~dp0CodexBattery.exe"
