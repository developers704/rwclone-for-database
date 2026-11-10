@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Valliani Data Sync

if not exist "VallianiDataSync.exe" (
  echo ERROR: VallianiDataSync.exe missing in this folder.
  echo.
  echo Fix:
  echo   1. Copy the full VallianiDataSync-Ready.zip to this PC
  echo   2. Right-click ZIP - Extract All
  echo   3. Open the extracted folder
  echo   4. Run START.bat or Install.bat from THERE
  echo.
  echo Do NOT open the EXE from inside the ZIP window.
  pause
  exit /b 1
)

echo Unblocking downloaded files...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

echo Starting VallianiDataSync.exe ...
start "" "%~dp0VallianiDataSync.exe"

:: Wait briefly; if it dies immediately, warn the user
timeout /t 3 /nobreak >nul
tasklist /FI "IMAGENAME eq VallianiDataSync.exe" 2>nul | find /I "VallianiDataSync.exe" >nul
if errorlevel 1 (
  echo.
  echo App closed immediately — it did not stay open.
  echo.
  echo Try these:
  echo   1. Right-click VallianiDataSync.exe - Properties - Unblock - OK
  echo   2. Double-click Install.bat, then use Desktop shortcut
  echo   3. If Windows SmartScreen appears: More info - Run anyway
  echo   4. Check Desktop for VallianiDataSync-crash.txt and send that file
  echo.
  echo Need: Windows 10/11 64-bit ^(not 32-bit^)
  pause
  exit /b 1
)

echo App is running.
endlocal
