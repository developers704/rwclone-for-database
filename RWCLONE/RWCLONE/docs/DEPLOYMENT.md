# Deployment Guide

## Build (developer machine)

```powershell
cd <repo-root>
dotnet restore VallianiDataSync.sln
dotnet build VallianiDataSync.sln -c Release
dotnet test VallianiDataSync.sln -c Release
```

## Publish installer-ready folder

```powershell
dotnet publish src/VallianiDataSync.App/VallianiDataSync.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o .\publish\VallianiDataSync
```

For offline PCs without a shared runtime:

```powershell
dotnet publish src/VallianiDataSync.App/VallianiDataSync.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o .\publish\VallianiDataSync-SelfContained
```

## Runtime prerequisites (framework-dependent)

- Windows 10 1809+ / Windows 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Network access to SQL Server and your HTTPS API
- NTFS write access to `%LocalAppData%\VallianiDataSync`

## Data locations

| Path | Purpose |
|------|---------|
| `%LocalAppData%\VallianiDataSync\settings.json` | Non-secret settings |
| `%LocalAppData%\VallianiDataSync\secrets\*.dpapi` | DPAPI-protected SQL password / API key |
| `%LocalAppData%\VallianiDataSync\data\sync-state.db` | SQLite queue, hashes, logs |
| `<app>\logs\app-YYYYMMDD.log` | Serilog rolling files |

## First store rollout checklist

1. Install runtime + publish folder
2. Configure Database → Test Connection
3. Configure mapping against a read-only view/table
4. Add stores
5. Configure API → Test API
6. Set image roots
7. Run **Sync Now** on one store
8. Verify server records + images
9. Enable automatic schedule only after a clean manual run

## Firewall

Allow outbound:

- TCP 1433 (or your SQL port) to SQL Server
- TCP 443 to your API host

Deny / do not configure any Irys/Tiara domains.

## Updates

Replace the publish folder with a newer build. Settings/secrets persist under LocalAppData and are not overwritten.
