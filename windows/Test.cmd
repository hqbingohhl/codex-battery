@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "REPORT=%TEMP%\CodexBattery-check-%RANDOM%-%RANDOM%.txt"
start "" /wait ".\CodexBattery.exe" --self-test "%REPORT%"
if errorlevel 1 goto failed
if not exist "%REPORT%" goto failed
findstr /c:"PASS --self-test" "%REPORT%" >nul || goto failed
type "%REPORT%"
start "" /wait ".\CodexBattery.exe" --ui-test "%REPORT%"
if errorlevel 1 goto failed
if not exist "%REPORT%" goto failed
findstr /c:"PASS --ui-test" "%REPORT%" >nul || goto failed
type "%REPORT%"
".\Tools\CheckUsageClient.exe"
if errorlevel 1 goto failed
del "%REPORT%" 2>nul
echo All checks passed.
if /I not "%~1"=="--quiet" pause
popd
exit /b 0
:failed
if exist "%REPORT%" type "%REPORT%"
echo A check failed.
if /I not "%~1"=="--quiet" pause
popd
exit /b 1
