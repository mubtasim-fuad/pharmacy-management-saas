@echo off
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK, then run this file again.
  pause
  exit /b 1
)
where npm >nul 2>nul
if errorlevel 1 (
  echo Install Node.js 24, then run this file again.
  pause
  exit /b 1
)
start "MedLedger API" /D "%~dp0backend" cmd /k "dotnet run"
start "MedLedger Web" /D "%~dp0frontend" cmd /k call "%~dp0run-web.bat"
echo Two windows opened. When the web window is ready, visit http://localhost:4200
echo Keep both windows open while using MedLedger.
pause
