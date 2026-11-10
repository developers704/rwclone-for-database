@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

set OUT=%~dp0..\distribute\VallianiDataSync
set ZIP=%~dp0..\distribute\VallianiDataSync-Ready.zip

echo.
echo === Building installer-ready package (latest code) ===
echo.

if exist "%OUT%" rmdir /S /Q "%OUT%" 2>nul
mkdir "%OUT%" 2>nul
mkdir "%~dp0..\distribute" 2>nul

dotnet publish "src\VallianiDataSync.App\VallianiDataSync.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishReadyToRun=true ^
  -o "%OUT%"

if errorlevel 1 (
  echo PUBLISH FAILED.
  exit /b 1
)

copy /Y "%~dp0packaging\Install.bat.template" "%OUT%\Install.bat" >nul
copy /Y "%~dp0packaging\START-WINDOW-APP.bat" "%OUT%\START-WINDOW-APP.bat" >nul
copy /Y "%~dp0packaging\CHECK-SQL.bat" "%OUT%\CHECK-SQL.bat" >nul
copy /Y "%~dp0..\distribute\LOCAL-AND-LIVE-SETUP.txt" "%OUT%\LOCAL-AND-LIVE-SETUP.txt" >nul 2>nul

copy /Y "%~dp0packaging\START.bat" "%OUT%\START.bat" >nul
copy /Y "%~dp0packaging\OPEN-APP.bat" "%OUT%\OPEN-APP.bat" >nul
copy /Y "%~dp0packaging\START.bat" "%OUT%\1-OPEN-APP.bat" >nul

(
  echo Valliani Data Sync — Install on any Windows PC
  echo ==============================================
  echo.
  echo IMPORTANT on other PCs:
  echo   1. Copy the FULL ZIP to the PC ^(Desktop or C:\Temp^)
  echo   2. Right-click ZIP -^> Extract All ^(full folder, not open-from-zip^)
  echo   3. Open the extracted folder
  echo   4. Double-click Install.bat ^(recommended^) OR START.bat
  echo.
  echo If Windows says "Windows protected your PC":
  echo   More info -^> Run anyway
  echo.
  echo If nothing opens:
  echo   Right-click VallianiDataSync.exe -^> Properties -^> check Unblock -^> OK
  echo   then run START.bat again
  echo.
  echo Need: 64-bit Windows 10/11. No .NET install needed.
  echo.
  echo Do NOT use the Web Server ZIP for desktop — that is browser-only.
) > "%OUT%\README.txt"

if exist "%ZIP%" del /F /Q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if not exist "%ZIP%" (
  echo ZIP CREATE FAILED for Ready package.
  exit /b 1
)

set ZIPWIN=%~dp0..\distribute\VallianiDataSync-Server-WINDOW.zip
(
echo SERVER — Windows desktop app ^(no browser URL^)
echo ==============================================
echo 1. Copy ZIP to the PC and Extract All
echo 2. Double-click Install.bat
echo 3. Desktop shortcut: Valliani Data Sync
echo.
echo If blocked: Properties on VallianiDataSync.exe -^> Unblock
echo.
echo Do NOT use VallianiDataSync-Web-Server.zip for this — that is browser-only on port 8080.
) > "%OUT%\README-SERVER-WINDOW.txt"

if exist "%ZIPWIN%" del /F /Q "%ZIPWIN%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIPWIN%' -Force"
if not exist "%ZIPWIN%" (
  echo ZIP CREATE FAILED for WINDOW package.
  exit /b 1
)

echo.
echo DONE. SERVER window app:
echo   %ZIPWIN%
echo Other PCs:
echo   %ZIP%
echo.
endlocal
