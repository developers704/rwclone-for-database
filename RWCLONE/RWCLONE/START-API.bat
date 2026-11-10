@echo off
cd /d "%~dp0"
title Valliani API

echo Stopping any old API...
taskkill /F /IM VallianiDataSync.Api.exe >nul 2>&1
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":7188" ^| findstr "LISTENING"') do taskkill /F /PID %%a >nul 2>&1
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":5188" ^| findstr "LISTENING"') do taskkill /F /PID %%a >nul 2>&1
timeout /t 1 /nobreak >nul

echo.
echo Starting API on https://localhost:7188 ...
echo Keep this window OPEN.
echo.
dotnet run --project src\VallianiDataSync.Api --launch-profile https -c Release
echo.
pause
