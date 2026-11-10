@echo off
:: Rebuild web + ZIP with one-click Install.bat
setlocal
cd /d "%~dp0.."

set OUT=%CD%\dist\server-deploy
set ZIP=%CD%\dist\VallianiDataSync-Web-Server.zip

echo Building web to %OUT% ...
dotnet publish "src\VallianiDataSync.Web\VallianiDataSync.Web.csproj" -c Release -o "%OUT%"
if errorlevel 1 (
  echo Publish failed.
  exit /b 1
)

copy /Y "%CD%\scripts\Install-Web-Server.bat" "%OUT%\Install-Web-8080.bat" >nul
if exist "%OUT%\Install.bat" del /F /Q "%OUT%\Install.bat"
copy /Y "%CD%\scripts\install-web-service.bat" "%OUT%\install-web-service.bat" >nul

(
echo BROWSER panel only ^(URL http://SERVER:8080^)
echo =============================================
echo Run: Install-Web-8080.bat  ^(Admin^)
echo NOT for desktop window — use VallianiDataSync-Server-WINDOW.zip for that.
) > "%OUT%\README-INSTALL.txt"

echo Creating ZIP...
if exist "%ZIP%" del /F /Q "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
  echo ZIP failed.
  exit /b 1
)

echo.
echo Done.
echo ZIP: %ZIP%
echo On server: unzip → Install-Web-8080.bat (browser only)
endlocal
