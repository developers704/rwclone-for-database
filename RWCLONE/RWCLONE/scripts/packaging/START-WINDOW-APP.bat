@echo off
:: Open desktop window app on server (stops web service on 8080)
setlocal
cd /d "%~dp0"

echo Stopping web service (port 8080) ...
sc stop VallianiDataSyncWeb >nul 2>&1

set APP=%LOCALAPPDATA%\VallianiDataSync\App\VallianiDataSync.exe
if exist "%APP%" (
  echo Starting desktop app...
  start "" "%APP%"
  exit /b 0
)

if exist "%~dp0VallianiDataSync.exe" (
  echo Running from this folder...
  start "" "%~dp0VallianiDataSync.exe"
  exit /b 0
)

echo ERROR: Run Install.bat first (from VallianiDataSync-Server-WINDOW.zip).
pause
endlocal
