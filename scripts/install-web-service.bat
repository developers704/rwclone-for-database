@echo off
:: Run as Administrator on Windows Server
setlocal
set SVC=VallianiDataSyncWeb
set BIN=C:\VallianiDataSync\Web\VallianiDataSync.Web.exe
set DATA=C:\ProgramData\VallianiDataSync

if not exist "%BIN%" (
  echo Missing %BIN%
  echo Run scripts\publish-web.bat first.
  exit /b 1
)

mkdir "%DATA%" 2>nul
mkdir "%DATA%\secrets" 2>nul
mkdir "%DATA%\data" 2>nul
mkdir "%DATA%\logs" 2>nul

sc query %SVC% >nul 2>&1
if %errorlevel%==0 (
  echo Stopping existing service...
  sc stop %SVC% >nul 2>&1
  timeout /t 3 /nobreak >nul
  sc delete %SVC% >nul 2>&1
  timeout /t 2 /nobreak >nul
)

echo Creating service %SVC% ...
sc create %SVC% binPath= "\"%BIN%\"" start= auto DisplayName= "Valliani Data Sync Web"
sc description %SVC% "Valliani Data Sync browser panel + background destination sync"
sc failure %SVC% reset= 0 actions= restart/5000
sc config %SVC% start= auto

echo Opening firewall TCP 8080 ...
netsh advfirewall firewall delete rule name="Valliani Data Sync Web 8080" >nul 2>&1
netsh advfirewall firewall add rule name="Valliani Data Sync Web 8080" dir=in action=allow protocol=TCP localport=8080

echo Starting service...
sc start %SVC%
echo.
echo Done. Browse: http://THIS-SERVER-IP:8080
echo Data folder: %DATA%
echo Configure Security/OTP + Database in the browser after first login.
echo.
echo Optional dedicated service account later:
echo   1. Create local user e.g. VallianiSyncSvc
echo   2. Grant Modify on %DATA%
echo   3. sc config %SVC% obj= ".\VallianiSyncSvc" password= "YOUR_PASSWORD"
echo   4. Re-enter SMTP/SQL passwords in UI (DPAPI is per Windows user)
endlocal
