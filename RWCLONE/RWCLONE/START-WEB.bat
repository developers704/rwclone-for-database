@echo off
cd /d "%~dp0"
title Valliani Web Panel
echo Starting Valliani Data Sync Web on http://127.0.0.1:8080 ...
echo Data: LocalAppData (Development) or see appsettings.json
echo.
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\VallianiDataSync.Web -c Release --no-launch-profile --urls http://127.0.0.1:8080
pause
