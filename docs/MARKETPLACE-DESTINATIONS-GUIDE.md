# Valliani Data Sync — Marketplace Destinations Guide

Complete context: latest package paths, build/run commands, Destinations workflow, design, and architecture (inventory + products).

---

## 1. Latest updated package (use this on any PC)

| What | Full path |
|------|-----------|
| **Ready ZIP (other PCs)** | `c:\Users\shah\OneDrive\Documents\New folder\rwclone\distribute\VallianiDataSync-Ready.zip` |
| **Server WINDOW ZIP** | `c:\Users\shah\OneDrive\Documents\New folder\rwclone\distribute\VallianiDataSync-Server-WINDOW.zip` |
| **Extracted folder (already built)** | `c:\Users\shah\OneDrive\Documents\New folder\rwclone\distribute\VallianiDataSync\` |
| **Exe (after extract/publish)** | `...\VallianiDataSync\VallianiDataSync.exe` |

Both ZIPs are the same desktop app build. Prefer **VallianiDataSync-Ready.zip** for install on another machine.

### Source code (repo root)

```
c:\Users\shah\OneDrive\Documents\New folder\rwclone\
```

### Key files changed for marketplace sync

| File | Role |
|------|------|
| `src\VallianiDataSync.Infrastructure\Destinations\BuiltInSyncQueries.cs` | Fixed SQL sources: sales, inventory, inv-test, products, products-test, payments |
| `src\VallianiDataSync.Infrastructure\Destinations\MarketplaceCatalogMapper.cs` | Inventory 8-col + products 35-col CSV shaping |
| `src\VallianiDataSync.Infrastructure\Destinations\DestinationSyncService.cs` | Full Sync, single-file CSV upload (`csvFile`), reports |
| `src\VallianiDataSync.Infrastructure\Data\Repositories.cs` | Stream SQL (CTE-safe for products) |
| `src\VallianiDataSync.App\ViewModels\DestinationsViewModel.cs` | Destinations UI: sources, token mask, Save / Full Sync |
| `scripts\build-distribute.bat` | Builds Ready + Server WINDOW zips |
| `BUILD-DISTRIBUTE.bat` | One-click wrapper at repo root |

---

## 2. Commands — build, run, package

Open **PowerShell** or **cmd**. First:

```bat
cd /d "c:\Users\shah\OneDrive\Documents\New folder\rwclone"
```

### A) Restore + build (dev)

```bat
dotnet restore
dotnet build src\VallianiDataSync.App\VallianiDataSync.App.csproj -c Release
```

### B) Run desktop app from source (dev)

```bat
dotnet run --project src\VallianiDataSync.App\VallianiDataSync.App.csproj -c Release
```

Or after build, run the exe under:

```
src\VallianiDataSync.App\bin\Release\net8.0-windows\win-x64\
```

(If that folder is empty, use Debug output or the publish folder below.)

### C) Publish self-contained (same as distribute)

```bat
dotnet publish src\VallianiDataSync.App\VallianiDataSync.App.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishReadyToRun=true ^
  -o distribute\VallianiDataSync
```

### D) Full Ready ZIP (recommended)

```bat
BUILD-DISTRIBUTE.bat
```

or:

```bat
scripts\build-distribute.bat
```

Output:

- `distribute\VallianiDataSync-Ready.zip`
- `distribute\VallianiDataSync-Server-WINDOW.zip`

### E) Install / run on this or another PC

1. Copy `VallianiDataSync-Ready.zip` to the PC (Desktop or `C:\Temp`).
2. Right-click → **Extract All** (do not run from inside the ZIP).
3. Open extracted folder → double-click **`Install.bat`** (or **`START.bat`**).
4. If Windows SmartScreen: **More info → Run anyway**.
5. If blocked: right-click `VallianiDataSync.exe` → Properties → **Unblock** → OK.

Need: **64-bit Windows 10/11**. No separate .NET install (self-contained).

### F) Optional checks

```bat
dotnet --info
```

SQL connectivity helper (inside package folder):

```bat
CHECK-SQL.bat
```

---

## 3. Prerequisites

| Need | Detail |
|------|--------|
| **SQL Server** | Host e.g. `CHAINARY` / IP, DB e.g. `JMDB20R_VILLIANI_LIVE` (or as configured in app) |
| **Auth** | SQL login (e.g. `sa`) — match what Destinations / connection UI expects |
| **Marketplace API** | Base like `https://devbe.vallianimarketplace.com` or production host |
| **API token** | Bearer token — paste once in Destinations → Save (field shows `••••`) |
| **Network** | PC must reach SQL + marketplace HTTPS |

App settings live under:

```
%LOCALAPPDATA%\VallianiDataSync\settings.json
```

Secrets (API key) are stored separately under the app’s secrets store (not plain in settings when using credential APIs).

---

## 4. Destinations workflow (day-to-day)

### Fixed sync sources (tick in Destinations)

| Source key | What it sends | Rows |
|------------|---------------|------|
| `sales` | Sales / commission lines | All matching SQL |
| `inventory` | 8-col inventory CSV | **All** on-hand rows |
| `marketplace-inv-test` | Same inventory template | **TOP 10,000** (fast test) |
| `marketplace-products` | 35-col product catalog CSV | **All** clean SKUs |
| `marketplace-products-test` | Same products template | **TOP 3,000** (fast test) |
| `payments` | Payments SQL | All matching |

### Inventory test → full

1. Destinations → project **Enabled**.
2. Tick **only** `marketplace-inv-test`.
3. API URL = sku-inventory / merge import URL (as used for inventory).
4. Paste token → **Save**.
5. **Full Sync** — expect ~10k rows, one CSV, field name `csvFile`.
6. When OK: untick test, tick **Inventory FULL** → Save → Full Sync.

### Products test → full

1. Tick **only** `marketplace-products-test`.
2. API URL example:

   `https://devbe.vallianimarketplace.com/api/v2/bulk/vendor-catalog/import`

3. Token → **Save**.
4. **Full Sync** — ~3,000 products, one CSV, multipart field **`csvFile` only**.
5. When OK: tick **marketplace-products** (FULL), untick test → Save → Full Sync.

### Important rules

- **One marketplace source at a time** while testing (avoids mixing inventory URL with products URL).
- Inventory URL ≠ products URL (sku-inventory vs vendor-catalog).
- **Full Sync** always pushes; auto sync may skip if row fingerprint unchanged.
- Do **not** run the browser-only Web Server ZIP for this desktop Destinations flow.

### Known API contract (multer)

Marketplace import expects multipart form field name:

```text
csvFile
```

Wrong name (`file`) or extra junk fields → `MulterError: Unexpected field` (HTTP 500 HTML). Current build sends **only** `csvFile` + CSV bytes.

---

## 5. Architecture

```
┌─────────────────────┐     SQL (JMDB…)      ┌──────────────────────────┐
│  WPF App            │◄────────────────────►│  SQL Server               │
│  Destinations UI    │                      │  Built-in sync queries   │
└─────────┬───────────┘                      └──────────────────────────┘
          │
          │ Full Sync / Auto
          ▼
┌─────────────────────┐
│ DestinationSyncService
│  • EnsureDefaults (pin SQL)
│  • StreamTableRowsAsync
│  • Map → CSV (ordered headers)
│  • ONE multipart POST (csvFile)
└─────────┬───────────┘
          │ HTTPS Bearer
          ▼
┌─────────────────────┐
│ Marketplace API     │
│  sku-inventory OR   │
│  vendor-catalog/import
└─────────────────────┘
```

### Layers

| Project | Responsibility |
|---------|----------------|
| `VallianiDataSync.App` | WPF UI, ViewModels, OTP/admin gates |
| `VallianiDataSync.Core` | Models (`DestinationProject`, mapping config) |
| `VallianiDataSync.Infrastructure` | SQL repos, Destinations sync, HTTP, settings, credentials |
| `VallianiDataSync.Sync` | Background / scheduled sync orchestration |

### Design choices (marketplace)

1. **Fixed sources, not raw table list** — only the named queries above (multi-table ready; sales/payments included).
2. **Template headers pinned** — inventory = 8 cols; products = 35 cols (SQL aliases = CSV headers).
3. **Single-file push** for inventory/products — load all rows for that source, one CSV upload (no chunk workers).
4. **CTE products SQL** runs as-is (cannot wrap `WITH` in a derived table); fingerprint uses a lighter `SelectedItems` count.
5. **Warehouse** = `WLocationCode` (e.g. `VJ-PB`), not GUID / full name.
6. **Token** — paste once; UI shows mask `••••`; Save keeps existing key if mask left unchanged.

### Data flow (one Full Sync)

1. User ticks source(s) → Save → `SyncSources` + `FormatFields` persisted.
2. Sync loads mapping → pins inventory/products SQL.
3. `StreamTableRowsAsync` executes query → batches into memory for single-file path.
4. Mapper keeps only template columns / drops bad inventory warehouses.
5. `ProductFormatExporter.ToCsvOrdered` writes header order exactly.
6. HTTP POST multipart `csvFile` → API; report shows OK / Failed / rows / API body snippet.

---

## 6. CSV templates

### Inventory (8)

`Sku`, `Barcode`, `Warehouse`, `Quantity`, `lastRestocked`, `P-Cost`, `Tag Price`, `99 Price`

### Products (35)

`Sku`, `Vendor-Model`, `Description-Name`, `Tag Price`, `99-Price`, `Category`, `Subcategory-Department`, `Style`, `Brand-Design`, `Metal-Color`, `Metal-Type`, `Size`, `Gender`, `Extent-Width`, `AvgWeight`, `Stone Type`, `Center-Stone`, `Center-Carat`, `Center-Shape`, `Center-Color`, `Center-Clarity`, `Side-Stone`, `Side-Carat`, `Side-Shape`, `Side-Color`, `Side-Clarity`, `Dial`, `Year`, `p-cost`, `Model-No`, `Featureimages_Link`, `Galleryimage_Link`, `Video_Link`, `JewelMate-Created-At`, `Vendor`

---

## 7. Troubleshooting

| Symptom | Fix |
|---------|-----|
| `MulterError: Unexpected field` | Use latest Ready.zip; must send `csvFile` only (current build). |
| HTTP 401 / 403 | Re-paste API token → Save (don’t leave empty). |
| 0 rows / slow forever | Use `*-test` source first; check SQL connectivity. |
| Warehouse GUID / wrong store | Inventory SQL must use `WLocationCode`. |
| Token “gone” after Save | Leave `••••` or paste new token; Save again. |
| SmartScreen / won’t start | Unblock exe; Extract All; use Install.bat. |
| Wrong API for products | Use `.../vendor-catalog/import`, not sku-inventory. |

Logs (if present):

```
%LOCALAPPDATA%\VallianiDataSync\logs\
```

---

## 8. Quick checklist (copy)

```text
[ ] cd repo root
[ ] BUILD-DISTRIBUTE.bat   → Ready.zip updated
[ ] Extract ZIP on target PC
[ ] Install.bat / START.bat
[ ] Configure SQL connection in app
[ ] Destinations: Enabled + API URL + token → Save
[ ] Tick marketplace-products-test (or inv-test) only
[ ] Full Sync → read sync report
[ ] On success → switch to FULL source → Full Sync
```

---

## 9. Related docs in repo

- `distribute\LOCAL-AND-LIVE-SETUP.txt` — local vs live notes  
- `docs\API_RUN_GUIDE.md` / `docs\API_SPECIFICATION.md` — general API notes  
- `BUILD-DISTRIBUTE.bat` — package entry point  

This guide is the Destinations / marketplace sync source of truth for the Oct 2026 inventory + products work.
