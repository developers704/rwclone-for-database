@echo off
cd /d "%~dp0"
title Valliani Desktop
echo Stopping old desktop if running...
taskkill /F /IM VallianiDataSync.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo Starting Valliani Data Sync desktop app...
echo.
dotnet run --project src\VallianiDataSync.App -c Release
pause
