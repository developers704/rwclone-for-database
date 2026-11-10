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

(
  echo @echo off
  echo cd /d "%%~dp0"
  echo start "" "VallianiDataSync.exe"
) > "%OUT%\START.bat"

(
  echo Valliani Data Sync — Install on any Windows PC
  echo ==============================================
  echo.
  echo OPTION A — Install ^(recommended^):
  echo   1. Unzip this folder
  echo   2. Double-click Install.bat
  echo   3. Desktop shortcut opens the app
  echo.
  echo OPTION B — Run without install:
  echo   Double-click START.bat or VallianiDataSync.exe
  echo.
  echo No .NET install needed.
  echo.
  echo If locked on login:
  echo   Click "Can't unlock? Recover access on this PC" then Unlock.
  echo.
  echo After code changes on the build PC, run BUILD-DISTRIBUTE.bat again
  echo and send the new ZIP.
) > "%OUT%\README.txt"

if exist "%ZIP%" del /F /Q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"

set ZIPWIN=%~dp0..\distribute\VallianiDataSync-Server-WINDOW.zip
(
echo SERVER — Windows desktop app ^(no browser URL^)
echo ==============================================
echo 1. Unzip
echo 2. Double-click Install.bat
echo 3. Desktop shortcut: Valliani Data Sync
echo.
echo Do NOT use VallianiDataSync-Web-Server.zip for this — that is browser-only on port 8080.
) > "%OUT%\README-SERVER-WINDOW.txt"

if exist "%ZIPWIN%" del /F /Q "%ZIPWIN%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIPWIN%' -Force"

echo.
echo DONE. SERVER window app:
echo   %ZIPWIN%
echo Other PCs:
echo   %ZIP%
echo.
endlocal
