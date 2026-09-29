@echo off
cd /d "%~dp0frontend"
if not exist node_modules (
  call npm ci || exit /b 1
)
call npm start
