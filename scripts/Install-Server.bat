@echo off
:: Valliani Data Sync Web — one-click install on Windows Server
:: Right-click → Run as administrator  OR  double-click (will ask UAC)
setlocal EnableExtensions
cd /d "%~dp0"

:: Elevate to Administrator
net session >nul 2>&1
if %errorlevel% NEQ 0 (
  echo Requesting Administrator rights...
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b 0
)

set SRC=%~dp0
set DEST=C:\VallianiDataSync\Web
set DATA=C:\ProgramData\VallianiDataSync
set SVC=VallianiDataSyncWeb
set EXE=%DEST%\VallianiDataSync.Web.exe

echo.
echo ============================================
echo  Valliani Data Sync — Server Install
echo ============================================
echo.

if not exist "%SRC%VallianiDataSync.Web.exe" (
  echo ERROR: VallianiDataSync.Web.exe not found next to Install.bat
  echo Unzip the FULL zip first, then run Install.bat from inside that folder.
  pause
  exit /b 1
)

echo [1/5] Stopping old service if present...
sc stop %SVC% >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete %SVC% >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/5] Copying files to %DEST% ...
mkdir "%DEST%" 2>nul
mkdir "%DATA%" 2>nul
mkdir "%DATA%\secrets" 2>nul
mkdir "%DATA%\data" 2>nul
mkdir "%DATA%\logs" 2>nul

:: Copy everything except this installer note noise is fine
robocopy "%SRC%." "%DEST%" /E /IS /IT /NFL /NDL /NJH /NJS /XD >nul
if not exist "%EXE%" (
  echo ERROR: Copy failed — %EXE% missing.
  pause
  exit /b 1
)

echo [3/5] Creating Windows service %SVC% ...
sc create %SVC% binPath= "\"%EXE%\"" start= auto DisplayName= "Valliani Data Sync Web"
if errorlevel 1 (
  echo sc create failed — trying config if already exists...
  sc config %SVC% binPath= "\"%EXE%\"" start= auto
)
sc description %SVC% "Valliani Data Sync browser panel + multi-table sync"
sc failure %SVC% reset= 0 actions= restart/5000
sc config %SVC% start= auto

echo [4/5] Firewall TCP 8080 ...
netsh advfirewall firewall delete rule name="Valliani Data Sync Web 8080" >nul 2>&1
netsh advfirewall firewall add rule name="Valliani Data Sync Web 8080" dir=in action=allow protocol=TCP localport=8080

echo [5/5] Starting service...
sc start %SVC%
timeout /t 3 /nobreak >nul

sc query %SVC% | find "RUNNING" >nul
if errorlevel 1 (
  echo.
  echo WARNING: Service not RUNNING yet. Check Event Viewer or:
  echo   %DEST%\logs
  echo   %DATA%\logs
  echo.
) else (
  echo Service is RUNNING.
)

echo.
echo ============================================
echo  DONE
echo  Open:  http://localhost:8080
echo  Or:    http://THIS-SERVER-IP:8080
echo  Data:  %DATA%
echo ============================================
echo.
echo Next in browser:
echo   1. Database  - connect SQL
echo   2. Data Mapping - Sync ALL tables - Save
echo   3. Destinations - API URL - Sync Now
echo.

start "" "http://localhost:8080"
pause
endlocal
