# Valliani Data Sync

Windows desktop application that reads product/inventory data from your local Microsoft SQL Server database and synchronizes it to **your own** remote REST API — including product images.

This is a clean-room replacement for the business workflow previously handled by RWLink. It does **not** connect to Irys/Tiara/Rareworks vendor infrastructure.

## Solution layout

| Project | Role |
|---------|------|
| `VallianiDataSync.App` | WPF UI (MVVM) |
| `VallianiDataSync.Web` | Browser panel (Blazor Server) + Windows Service for server |
| `VallianiDataSync.Api` | Your server API (receives sync + stores data) |
| `VallianiDataSync.Core` | Models, options, interfaces |
| `VallianiDataSync.Infrastructure` | SQL Server, HTTP API client, DPAPI secrets, SQLite sync state, Serilog |
| `VallianiDataSync.Sync` | Sync engine, image sync, scheduler |
| `VallianiDataSync.Tests` | Unit tests |

## Prerequisites

- Windows 10/11
- .NET 8 Desktop Runtime (or SDK to build)
- SQL Server reachable from the PC
- Your HTTPS API endpoint

## Build

```powershell
dotnet restore VallianiDataSync.sln
dotnet build VallianiDataSync.sln -c Release
dotnet test VallianiDataSync.sln -c Release
```

Run desktop:

```powershell
dotnet run --project src/VallianiDataSync.App -c Release
```

Run **web panel** locally (browser, no download):

```powershell
START-WEB.bat
```

Open `http://127.0.0.1:8080`. For Windows Server (auto-start service, share link): [docs/WEB_SERVER_DEPLOY.md](docs/WEB_SERVER_DEPLOY.md).

Run API (alag terminal):

```powershell
dotnet run --project src/VallianiDataSync.Api --launch-profile https
```

- Swagger: `https://localhost:7188/swagger`
- Default API key: `valliani-dev-api-key-change-me`
- Details: [docs/API_RUN_GUIDE.md](docs/API_RUN_GUIDE.md)

## First-run setup

1. Start **API** (command above) — keep it running.
2. Start **desktop app**.
3. **Database** — your local ERP SQL (source). Password stored with DPAPI.
4. **Data Mapping** — map ERP columns to SKU/name/price/qty/image.
5. **Stores** — store codes (e.g. Vallani_Jewelers).
6. **Server/API** — Base URL `https://localhost:7188`, API key `valliani-dev-api-key-change-me` → Test API → Save.
7. **Images** — local/UNC picture folders.
8. Dashboard → **Sync Now** or **Full Sync**.

## Security

- Secrets never go in `appsettings.json` or git.
- SQL password and API key use DPAPI (`%LocalAppData%\VallianiDataSync\secrets`).
- HTTPS required for API calls.
- Passwords/API keys are not written to log files.

## Docs

- [RWLink analysis](docs/RWLINK_ANALYSIS.md)
- [API specification](docs/API_SPECIFICATION.md)
- [API run guide](docs/API_RUN_GUIDE.md)
- [Database mapping](docs/DATABASE_MAPPING.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Web panel on Windows Server](docs/WEB_SERVER_DEPLOY.md)

## License / vendor notice

Do not point this app at `*.tiara.jewelry`, `*.irysgroup.com`, or any original vendor hosts. Use only infrastructure you control.
