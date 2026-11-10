@echo off
setlocal
cd /d "%~dp0.."

set OUT=C:\VallianiDataSync\Web
echo Publishing Valliani Data Sync Web to %OUT% ...
dotnet publish "src\VallianiDataSync.Web\VallianiDataSync.Web.csproj" -c Release -o "%OUT%"
if errorlevel 1 (
  echo Publish failed.
  exit /b 1
)

echo.
echo Published. Next (Administrator CMD):
echo   scripts\install-web-service.bat
echo.
echo Then open: http://SERVER-IP:8080
endlocal
