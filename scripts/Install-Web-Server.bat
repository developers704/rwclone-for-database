@echo off
:: Valliani Data Sync Web — one-click server install
:: Unzip package → double-click Install.bat (Admin prompt OK)
setlocal EnableExtensions
cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
  echo Requesting Administrator rights...
  powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

set SRC=%~dp0
set DEST=C:\VallianiDataSync\Web
set DATA=C:\ProgramData\VallianiDataSync
set SVC=VallianiDataSyncWeb
set EXE=%DEST%\VallianiDataSync.Web.exe

echo.
echo ========================================
echo  Valliani Data Sync — Server Install
echo ========================================
echo.

if not exist "%SRC%VallianiDataSync.Web.exe" (
  echo ERROR: VallianiDataSync.Web.exe not found in this folder.
  echo Unzip the FULL package first, then run Install.bat from inside that folder.
  pause
  exit /b 1
)

sc query %SVC% >nul 2>&1
if %errorlevel%==0 (
  echo Stopping existing service...
  sc stop %SVC% >nul 2>&1
  timeout /t 4 /nobreak >nul
)

echo Installing files to %DEST% ...
mkdir "%DEST%" 2>nul
mkdir "%DATA%" 2>nul
mkdir "%DATA%\secrets" 2>nul
mkdir "%DATA%\data" 2>nul
mkdir "%DATA%\logs" 2>nul

xcopy /E /I /Y /Q "%SRC%*" "%DEST%\" >nul
if errorlevel 1 (
  echo ERROR: Copy failed.
  pause
  exit /b 1
)

if not exist "%EXE%" (
  echo ERROR: %EXE% missing after copy.
  pause
  exit /b 1
)

sc query %SVC% >nul 2>&1
if %errorlevel%==0 (
  sc delete %SVC% >nul 2>&1
  timeout /t 2 /nobreak >nul
)

echo Creating Windows service %SVC% ...
sc create %SVC% binPath= "\"%EXE%\"" start= auto DisplayName= "Valliani Data Sync Web"
if errorlevel 1 (
  echo ERROR: sc create failed.
  pause
  exit /b 1
)
sc description %SVC% "Valliani Data Sync browser panel + background sync"
sc failure %SVC% reset= 0 actions= restart/5000
sc config %SVC% start= auto

echo Firewall rule TCP 8080 ...
netsh advfirewall firewall delete rule name="Valliani Data Sync Web 8080" >nul 2>&1
netsh advfirewall firewall add rule name="Valliani Data Sync Web 8080" dir=in action=allow protocol=TCP localport=8080

:: Keep OTP disabled for now (settings under ProgramData)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$p='C:\ProgramData\VallianiDataSync\settings.json'; if(Test-Path $p){ try{ $j=Get-Content $p -Raw | ConvertFrom-Json; if($j.security){ $j.security.otpEnabled=$false; $j | ConvertTo-Json -Depth 30 | Set-Content $p -Encoding UTF8 } } catch {} }"

echo Starting service...
sc start %SVC%
timeout /t 3 /nobreak >nul

sc query %SVC% | findstr /I "RUNNING" >nul
if %errorlevel%==0 (
  echo.
  echo SUCCESS — service is RUNNING.
) else (
  echo.
  echo WARNING: Service did not start. Install .NET 8 Hosting Bundle:
  echo   https://dotnet.microsoft.com/download/dotnet/8.0
  echo Then run: sc start %SVC%
)

echo.
echo Open: http://localhost:8080
echo From other PC: http://THIS-SERVER-IP:8080
echo.
echo Next:
echo   1. Database — connect + Save
echo   2. Data Mapping — Sync ALL ON + Save
echo   3. Destinations — API URL + Sync Now
echo.
echo For the desktop window app, use VallianiDataSync-Server-WINDOW.zip instead of this web package.
echo Web panel: http://localhost:8080
pause
endlocal
