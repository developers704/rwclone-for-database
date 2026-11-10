# Valliani Data Sync — Web panel on Windows Server

Browser UI (no client download) + auto-start Windows Service.

## Install (one click)

1. Copy `dist\VallianiDataSync-Web-Server.zip` to the server
2. Unzip to any folder (e.g. Desktop)
3. Double-click **`Install.bat`** → Allow Admin
4. Browser opens: `http://localhost:8080`  
   From other PCs: `http://SERVER-IP:8080`

Rebuild ZIP on a build PC:

```bat
scripts\pack-web-server.bat
```

## After install

1. **Database** — SQL connect → Save  
2. **Data Mapping** — Sync ALL database tables ON → Save  
3. **Destinations** — API URL (+ key) → Sync Now  

Skip **Server / API** for multi-table sync.

## Requirements

- Windows Server (or Win 10/11)
- [.NET 8 ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0) if service fails to start
- Admin rights (Install.bat elevates itself)

## Data location

| Path | Purpose |
|------|---------|
| `C:\VallianiDataSync\Web\` | App files |
| `C:\ProgramData\VallianiDataSync\settings.json` | Settings |
| `C:\ProgramData\VallianiDataSync\secrets\` | DPAPI passwords |
| `C:\ProgramData\VallianiDataSync\data\` | SQLite sync state |
| `C:\ProgramData\VallianiDataSync\logs\` | Logs |

## Uninstall

```bat
sc stop VallianiDataSyncWeb
sc delete VallianiDataSyncWeb
netsh advfirewall firewall delete rule name="Valliani Data Sync Web 8080"
```
