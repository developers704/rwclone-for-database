@echo off
:: Run on the PC that has SQL Server — shows what is installed
setlocal
echo === SQL Server services ===
sc query state= all | findstr /I "SQL"
echo.
echo === Listening 1433 ===
netstat -an | findstr ":1433"
echo.
echo === Tip ===
echo Open SSMS → Connect → copy the Server name exactly.
echo Paste that into Valliani Data Sync → Database → Hostname.
echo Port = 0 , Windows Auth ON  (or sa + password if that is how SSMS connects)
echo Database = exact name from SSMS Object Explorer
echo.
pause
endlocal
