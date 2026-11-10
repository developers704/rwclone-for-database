# Valliani Data Sync API — Run Guide

Ye server API desktop app se data receive karti hai aur apne database mein save karti hai.

## Flow

```
Aapka ERP SQL (iTransactDB etc.)
        │
        ▼
Valliani Data Sync (desktop)
        │  HTTPS + API key
        ▼
Valliani Data Sync API  ← yeh project
        │
        ▼
VallianiSync DB (SQLite local / SQL Server)
+ product images on disk
```

## Local chalana (development)

Terminal 1 — API:

```powershell
cd "c:\Users\shah\OneDrive\Documents\New folder\rwclone"
dotnet run --project src\VallianiDataSync.Api --launch-profile https
```

Browser: `https://localhost:7188/swagger`

Default API key:

```
valliani-dev-api-key-change-me
```

## Desktop app mein settings

**Server / API:**

| Field | Value |
|-------|--------|
| API Base URL | `https://localhost:7188` |
| API Key | `valliani-dev-api-key-change-me` |
| Timeout | 60 |
| Retry | 3 |

Phir **Test API** → **Save**.

## SQL Server use karna (optional)

`src/VallianiDataSync.Api/appsettings.json`:

```json
"VallianiApi": {
  "DatabaseProvider": "sqlserver",
  "ApiKey": "apni-strong-key"
},
"ConnectionStrings": {
  "SqlServer": "Server=YOUR_SERVER;Database=VallianiSyncDb;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

**Note:** Yeh **naya** `VallianiSyncDb` hai (synced catalog). Aapka ERP `iTransactDB` source rehta hai — usko overwrite nahi karte.

## Endpoints (RWLink-style sync)

| Method | Path | Kaam |
|--------|------|------|
| POST | `/api/v1/auth/validate` | API key check |
| GET | `/api/v1/stores` | Stores list |
| POST | `/api/v1/stores` | Store add/update |
| POST | `/api/v1/products/sync` | Products upsert (+ replace) |
| POST | `/api/v1/inventory/sync` | Qty update |
| POST | `/api/v1/prices/sync` | Price update |
| POST | `/api/v1/images/upload` | Image upload |
| POST | `/api/v1/sync/complete` | Sync run finish |
| GET | `/api/v1/sync/status` | Sync status |
| GET | `/api/v1/products` | Browse synced products |
| GET | `/api/v1/activity` | Hub-like activity log |

RFID / Irys license / Tiara cloud **is API mein nahi** — woh vendor tools the.

## Production

1. Strong `ApiKey` set karo  
2. HTTPS certificate + public URL `PublicBaseUrl`  
3. `DatabaseProvider=sqlserver`  
4. Firewall sirf trusted PCs ko API allow kare  
