@echo off
setlocal
pushd "%~dp0" || exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4.8 compiler not found. You can use the included CodexBattery.exe.
  exit /b 1
)
set "OUT=CodexBattery.build-%RANDOM%.exe"
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:"%OUT%" /resource:Assets\Pets\codex.png,CodexBattery.Pets.codex.png /resource:Assets\Icons\battery-256.png,CodexBattery.Icons.battery-256.png /resource:Assets\Icons\battery-32.png,CodexBattery.Icons.battery-32.png /resource:Assets\Icons\portrait-256.png,CodexBattery.Icons.portrait-256.png /resource:Assets\Icons\portrait-32.png,CodexBattery.Icons.portrait-32.png /win32icon:Assets\CodexBattery.ico /win32manifest:app.manifest /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll *.cs
if errorlevel 1 exit /b 1
move /Y "%OUT%" "CodexBattery.exe" >nul || exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /platform:anycpu /optimize+ /out:"Tools\CheckUsageClient.exe" /reference:System.dll,System.Core.dll,System.Web.Extensions.dll UsageClient.cs Tools\CheckUsageClient.cs
if errorlevel 1 exit /b 1
echo Build passed.
popd
exit /b 0
