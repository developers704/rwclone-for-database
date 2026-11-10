@echo off
cd /d "%~dp0"
echo Starting SQL connection test page at http://127.0.0.1:5055
echo.
dotnet run
pause
