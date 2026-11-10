# RWLink Reverse-Engineering Analysis

**Analysis date:** 2026-09-26  
**Artifact inspected:** `RWLink.zip` → compiled deployment (no source tree)  
**Application:** RWLink / RareworksLink  
**Binary version:** 5.7.0.3 (`RWLink.exe`)  
**Publisher:** IRYS Pte. Ltd / Irys Group  
**Observed customer profile in settings:** Vallani Jewelers  

> **Scope note:** This package is a **compiled WinForms ClickOnce-style deployment**, not a Visual Studio source project. Analysis is based on assembly reflection (297 types), Unicode string extraction, configuration files, and runtime logs. Method bodies were not fully decompiled (no .NET SDK / ILSpy available in this environment).

---

## 1. Current application architecture

### 1.1 High-level summary

RWLink is a **.NET Framework 4.8 Windows Forms** desktop application that combines:

1. **RFID / scanner integration** (handheld and fixed readers)
2. **Local ERP/POS data upload** (SQL Server, CSV/XLSX files, or third-party web services)
3. **Image upload** to Irys/Tiara cloud storage (primarily **AWS S3**)
4. **Cloud hub coordination** (REST APIs + SignalR on-demand sync)
5. **Licensing / updater** tied to Irys infrastructure

The product name in logs and namespaces is `RareworksLink`; the executable and UI brand as **RWLink**.

### 1.2 Runtime / packaging

| Item | Value |
|------|--------|
| Framework | .NET Framework 4.8 (`supportedRuntime v4.0`, sku `.NETFramework,Version=v4.8`) |
| UI toolkit | Windows Forms (`WorkBench : Form`) |
| Entry | `RareworksLink.Program` |
| Logging | log4net (rolling file + console) |
| JSON | Newtonsoft.Json |
| HTTP | `System.Net.Http.HttpClient` |
| Realtime | Microsoft.AspNetCore.SignalR.Client |
| Images / FTP / cloud | AWSSDK.S3, FluentFTP, SixLabors.ImageSharp, Renci.SshNet |
| Excel/CSV | Aspose.Cells, NPOI, CsvHelper |
| RFID SDKs | ATID, Impinj Octane, CAEN, BLE APIs, UHF native DLLs, NConnect |
| Deploy | ClickOnce manifests (`RWLink.application`, `RWLink.exe.manifest`) |
| Companion | `RWLink_Updater.exe` |

### 1.3 Major modules (from type map)

| Module | Types / role |
|--------|----------------|
| UI shell | `RareworksLink.WorkBench` — main form / tab host |
| Sync engine | `RareworksLink.DataAndImageSync` — data read, hash, zip, upload, timers |
| Image sync | `RareworksLink.Service.ImageSyncService` — local/FTP/Google Drive → S3 |
| Image matchers | `ImageSync.ParallelImageMatcher`, `ParallelFTPImageMatcher` |
| API validation | `Service.ApiValidationService` |
| On-demand sync | `Sync.SyncManager` (SignalR) |
| Scanner server | `Manager.ServerApiClient` — local HTTP + cloud weblink for RFID |
| DB helpers | `Helpers.DatabaseHelper`, RFID `EncryptionHelper` |
| Config POJO | `Pojo.DataAndImageSyncPojo` (persisted under `SettingsInfo.txt`) |
| Dialogs | `CustomComp.*` (license, PIN, config, update, assistance) |
| Logging | `Logs.AppMetadataLogger`, `Logs.LogBootstrapper` |

### 1.4 Local folder layout (observed)

```
RWLink/
  RWLink.exe (+ .config, .pdb, .manifest, .application)
  SettingsInfo.txt          # full runtime settings (incl. secrets — see Security)
  VersionInfo.txt           # 5.7.0.3
  UpdaterConfig.txt
  loginInfoDetails.txt
  debug.txt
  logs/rwlink_<Machine>.logDD.MM.YYYY.log
  dataFile/<Merchant>_<profile>/
  Image Sync Logs/<Merchant>_<profile>/
  ScanFolder/
  FileUploadFolder/
  + many vendor DLLs
```

---

## 2. Screen list

Primary UI is a single `WorkBench` form with `MainTabControl` pages and nested panels.

### 2.1 Main tabs

| Tab / page | Control name | Purpose |
|------------|--------------|---------|
| Scan | `scanPage` | Start/stop RFID scan, scanner list, scan counts, save list |
| Search | `searchPage` | Tag search (manual ID or uploaded list) |
| Settings | `settingPage` | Add scanners, local/cloud server ports, tag broadcast options, file output, notifications, auto-start |
| Data Upload / Sync | `dataUploadPage` | API key, merchant/profile/store, DB/file/webservice upload, image upload, intervals |
| Config / Hub | `configPage` | Mode selection (Integration / Link / Hub Sync), hub config |
| About | `aboutPage` | Version, license, update, request assistance, registered PCs (UDID) |

### 2.2 Data Upload / Sync sub-panels

| Panel | Purpose |
|-------|---------|
| API key management | `apiKeyTxt`, verify / add / delete API key |
| Merchant / profile / store | `merchantcomboBox`, `profilecomboBox`, `storecomboBox` |
| Data source radios | Database / File / Webservice |
| Database panel | Host, port, DB name, user, password, store code, ERP name, Test, Sync |
| File panel | Folder path, file name prefix, watcher / manual sync |
| Webservice panel | Auth URL, data URL, credentials, custom fields |
| Sync frequency | Auto data sync checkbox, interval combo, manual sync checkbox, replace-data checkbox |
| Image upload | Local folder **or** FTP; auto image sync; interval; replace images; S3 subfolders; Google Drive option |
| Push stocktake | Optional push of stocktake results to external ERP API |

### 2.3 Dialogs / secondary windows

- `LicensingKeyDialogBox` — serial key entry  
- `UdidDisplayPanel` — machine UDID / registered PCs  
- `PinForm`, `HiddenConfig`, `ConfigPanel`  
- `UpdateDialog`, `RequestAssistanceDialogBox`, `CustomDialogBox`  

### 2.4 Modes (product capabilities gated by license)

Observed radio modes: **Integration Mode**, **Link Mode**, **Hub Sync**.  
RFID features further gated by serial key / UDID registration against Irys license services.

---

## 3. Workflow diagram

```text
┌──────────────────────────────────────────────────────────────────┐
│                         RWLink WorkBench                         │
│  Scan | Search | Settings | Data Upload | Config | About         │
└───────────────┬───────────────────────────────┬──────────────────┘
                │                               │
                ▼                               ▼
     ┌──────────────────┐            ┌──────────────────────┐
     │ RFID / Link path │            │ Data + Image Sync    │
     │ ServerApiClient  │            │ DataAndImageSync     │
     │ local HTTP/WS or │            │ ImageSyncService     │
     │ Irys cloud link  │            │ SyncManager(SignalR) │
     └──────────────────┘            └──────────┬───────────┘
                                                │
                ┌───────────────────────────────┼───────────────────────────────┐
                ▼                               ▼                               ▼
     ┌──────────────────┐            ┌──────────────────┐            ┌──────────────────┐
     │ Local SQL Server │            │ CSV/XLSX folder  │            │ External WS API  │
     │ (ERP/POS DB)     │            │ FileSystemWatcher│            │ (auth + paging)  │
     └────────┬─────────┘            └────────┬─────────┘            └────────┬─────────┘
              │                               │                               │
              └───────────────────────────────┼───────────────────────────────┘
                                              ▼
                                   Normalize → CSV/zip
                                   SHA compare vs previous
                                              │
                                              ▼
                         POST multipart to Tiara import API
                         (batch / sid / replace / lastBatch)
                                              │
                                              ▼
                         Image names from import payload
                         Match local/FTP/Drive files
                                              │
                                              ▼
                                   Upload binaries to AWS S3
                                   Verify via /v1/merchant/image/verify
                                              │
                                              ▼
                                   Hub activity + Image Sync Logs
```

---

## 4. Database workflow

### 4.1 Connection model

Built as a classic SQL Server connection string from UI fields:

- `Data Source=` + host (and port)
- `Initial Catalog=` + database name
- `User ID=` + username
- Password appended as `;Password=`
- Uses `System.Data.SqlClient.SqlConnection`

**Configured values observed for this deployment** (non-secret facts only):

| Setting | Observed value |
|---------|----------------|
| Host | `CHAINARY` |
| Port | `1433` |
| Database | `iTransactDB` |
| Username | `sa` |
| Auth | SQL authentication (password present in settings file — **not reproduced here**) |
| Store filter field | `DBStoreCode` (empty in sample) |
| ERP label field | `DBERP` (empty in sample) |

### 4.2 How products are read

Database sync does **not** appear to issue ad-hoc `SELECT` against arbitrary ERP tables. Instead it depends on **vendor-provided stored procedures** expected to exist in the local SQL Server database:

| Stored procedure | Role (from strings / error messages) |
|------------------|--------------------------------------|
| `SP_GetIrysItemCountForMergebyRow` | Count items available for sync |
| `SP_GetIrysItemDetailsForMergebyRow` | Fetch paged/merged item detail rows |
| `SP_InsertTempProductDataToFinalTable` | Temp → final merge helper (local staging) |

Related local object name observed: `item_master_temp`.

Flow inside `DataAndImageSync`:

1. `DatabaseDataSync(...)` stores connection parameters and starts timer/manual path  
2. `ReadDatabaseData()` opens SQL connection  
3. Executes Irys SPs (row/merge style)  
4. `ParseDBDataAsPerRequest(rowCount, DataTable)` converts rows to upload format  
5. Data written under `dataFile/<Merchant>_<profile>/` (CSV/zip path)  
6. `SendDataFileToServer(...)` uploads to Tiara import endpoints  

**Concurrency note:** logs/messages indicate the SP can fail if multiple processes hit it simultaneously.

### 4.3 Change detection (data)

- Computes a **SHA hash** of the prepared data set (`sha256_hash_DB`, `fileSHACode`)  
- If previous and current SHA match → skip upload (“No change found…”, “Previous and new data's SHA-Code is same.”)  
- Same pattern for file and webservice paths  

### 4.4 Implication for Valliani Data Sync

RWLink assumes the **ERP database already contains Irys bridge procedures** that perform field mapping server-side. The desktop app is largely a **scheduler + transporter**, not a full mapping designer.

Valliani Data Sync must therefore provide its **own configurable mapping layer** against the customer’s real tables/views (e.g. iTransact schema), **without** requiring Irys SPs — unless the customer later explicitly chooses to keep those SPs.

---

## 5. API workflow

### 5.1 Vendor domains / endpoints (DO NOT reuse)

| Purpose | Observed URL / path |
|---------|---------------------|
| Core / API key validation | `https://core-api.tiara.jewelry` |
| Staging core | `https://staging-core-api.tiara.jewelry` |
| Global data upload | `https://api-nxt.tiara.jewelry` |
| Regional hubs | `https://us-api-nxt.tiara.jewelry`, `sg-`, `in-`, `me-`, `brinks-`, staging variants |
| Stores for API key | `GET /v1/merchant/store?min=true` |
| Product import | `POST /v1/merchant/products/import?...` (`count`, `id`, `sid`, `replace`, `lastBatch`) |
| Image verify | `/v1/merchant/image/verify` |
| Stocktake fetch | `/v1/merchant/stocktake(s)/get?sid=` |
| Audit trail | `/v1/merchant/audit-trail/` |
| Hub UI mapping | `https://hub.tiara.jewelry/config/data-mapping` |
| Irys web link (RFID cloud) | `https://link.irysgroup.com:8443/irys-web-link/irys/link` |
| Updater / control assets | `https://tiara-control-centre.s3.ap-southeast-1.amazonaws.com` |
| Alternate image fetch (Titan) | `https://jewbridge.titanjew.in/CatalogImages/api/ImageFetch/...` |

Auth style observed:

- API key validated via `ApiValidationService.ValidateApiKeyAsync(baseUrl, apiKey)`  
- Successful validation loads merchant/store list into UI  
- Bearer token usage appears for some RFID/stocktake APIs (`/RFIDAPI/access_token`)  
- Import responses include structured status: `{ status, message, count, sid, lastBatch, replace, id }`  
- Batch size example in strings: **5000 products** per import initialization message  

### 5.2 SignalR on-demand sync

`Sync.SyncManager`:

- Builds a hub URL from the connection API key  
- Listens for `OnDemandSyncRequest`  
- Calls `ExecuteLocalSync(merchantId, profile, storeCode)`  

This is how the **cloud hub can remotely trigger** a store sync on the PC.

### 5.3 Hub activity logging

`AddHubActivity(activityModule, activity, activityDescription)` posts operational breadcrumbs back to the vendor hub (sync start/fail/image done, etc.).

---

## 6. Synchronization lifecycle

```text
Save Sync Settings
        │
        ├─ Validate API key (core API)
        ├─ Persist hub URL, API key, store list, DB/file/WS settings
        └─ Optionally start timers (data interval, image interval)
                │
                ▼
        Data timer tick / Manual Sync / On-demand SignalR
                │
                ▼
        Acquire sync lock (SemaphoreSlim) — skip if already running
                │
                ▼
        Read source (DB SP / file / webservice pages)
                │
                ▼
        Build upload artifact (CSV → optional zip)
                │
                ▼
        Compare SHA to previous upload
           ├─ same → skip (“no change”)
           └─ different → continue
                │
                ▼
        POST product import (batched; replace flag; sid; lastBatch)
                │
                ▼
        Record progress / hub activity / local dataFile artifacts
                │
                ▼
        (Separate schedule) Image sync:
           get image name list from server/import context
           match files locally or via FTP / Google Drive
           upload to S3
           verify
           write Image Sync Logs
```

### 6.1 Sync modes observed

| Mode | Behavior |
|------|----------|
| Manual | `manualSyncCB` / manual buttons; one-shot upload |
| Auto data | `IsDataAutoSync` + `DataUploadTimeInterval` (minutes; sample = `5`) |
| Replace on server | `ReplaceDataOnServer` / `ReplaceDataCB` — full replace semantics on import |
| Incremental-ish | SHA skip when payload unchanged (not field-level delta) |
| File watcher | Folder watch for files starting with configured prefix (e.g. `DataImport`) |
| On-demand | SignalR remote trigger |

### 6.2 Multi-store

- API key returns multiple stores (`Vallani_Jewelers`, `Great_Mall`, `Valley_Fair` in this profile)  
- Selected store used for upload (`DataUploadSelectedStoreCode/Name`)  
- Some paths iterate stores (`ProcessStoresAsync`, `ExportStores_StartAmountAsync`)  

Merchant identity key format on disk: **`{MerchantID}_{profile}`** → e.g. `Vallani Jewelers_default`.

---

## 7. Image synchronization workflow

### 7.1 Sources

| Type | Setting | Behavior |
|------|---------|----------|
| Local files | `ImageUploadType = localfile`, `ImageUploadLocalPath` | Recursive directory search (`DirSearch`), match by image name from product payload |
| FTP | host/port/user/password/path | FluentFTP list + download/match |
| Google Drive | `IsToTakeImageFromDrive` | Download via Drive URL / id fields, then push to S3 |

### 7.2 Matching logic (from log strings)

Product rows carry image-related fields such as:

- `productCode`, `imageName`
- `image_url`, `google_drive_image_id`
- `image_name_new`, `merged_image_name`
- `rfid_tag_new`

Matcher builds:

1. Root folder + partial path  
2. Full path with/without extension  
3. Checks file existence; logs missing images  
4. Parallel matchers for local and FTP sets  

### 7.3 Upload target

- AWS S3 via `AmazonS3Client` + `TransferUtility`  
- Bucket/region from settings (sample: `us-api-nxt-bucket`, `us-east-1`)  
- Optional “make sub folder on S3”  
- Optional replace images on server  
- After upload: `/v1/merchant/image/verify`  
- Completion: `SendImageSyncDone()` + hub activity  
- Local audit: `Image Sync Logs\<Merchant_profile>\ImageSyncLog.txt`

### 7.4 Scheduling

- `StartScheduledImageSync(intervalMinutes)`  
- Sample default interval: **240 minutes**  
- Independent of data sync interval  

---

## 8. Configuration settings

### 8.1 Files

| File | Contents |
|------|----------|
| `RWLink.exe.config` | log4net, binding redirects, userSettings (`SerialKey`, `UDID`, `FirstRun`, version) |
| `SettingsInfo.txt` | **Primary runtime config JSON** (RFID, sync, DB, API, AWS, FTP, stocktake) |
| `UpdaterConfig.txt` | `{ applicationType, dotNetFrameWork, processorArchitecture }` |
| `VersionInfo.txt` | `5.7.0.3` |

### 8.2 Important `SettingsInfo.txt` keys (sync-related)

- Global: `coreHubApiUrl`, `DataUploadWebUrl`, `DataUploadServer`, `ClientID`, `WebLink`, ports  
- Per API key map `DataAndImageSyncDetails[<apiKey>]`:  
  - Identity: `apiKey`, `merchantID`, `profile`, store lists  
  - Hub: `hubApiUrl`, `importServer`  
  - Data source: `DataUploadType` (`Database` \| file \| webservice)  
  - DB: `DBHostName`, `DBPort`, `DBName`, `DBUsername`, `DBPassword`, `DBStoreCode`, `DBERP`  
  - Schedule: `IsDataAutoSync`, `DataUploadTimeInterval`, `manualDataSyncOnServer`, `ReplaceDataOnServer`  
  - Images: local/FTP paths, `IsImageAutoSync`, `ImageUploadTimeInterval`, AWS bucket/region  
  - Stocktake push: URL/credentials/batch size  

### 8.3 Logging configuration

- Pattern: `.\logs\rwlink_%property{DeviceName}.log` + date `dd.MM.yyyy`  
- Levels: Info+ to file/console  
- Startup metadata: app name, version, machine, user, OS, framework  

---

## 9. Security weaknesses

| Issue | Detail |
|-------|--------|
| Secrets in plaintext file | `SettingsInfo.txt` stores SQL password, API key, FTP password in clear JSON |
| Secrets as dictionary keys | API key used as object key under `DataAndImageSyncDetails` |
| Over-privileged SQL login | Sample uses `sa` |
| License / host binding | Serial key + UDID registration phones home to vendor |
| Hard-coded vendor trust | Fixed Tiara/Irys hosts; updater from vendor S3 |
| Certificate callbacks | Image sync contains custom remote certificate validation callbacks (risk of weak TLS validation) |
| Logging sensitivity | Status logs generally avoid dumping passwords, but config file itself is unrestricted on disk |
| ClickOnce unrestricted permission set | Manifest requests unrestricted permissions |

**Policy for replacement:** never copy observed credentials into Valliani Data Sync source, configs, docs, or samples. Rotate any credentials that were exposed in this zip if they are still live.

---

## 10. Features that should be recreated

Business-capable equivalents (against **your** API and DB only):

1. Connect to local SQL Server (host/port/db/user or Windows auth)  
2. Multi-store selection and mapping  
3. Read products/inventory/prices from local DB  
4. Configurable field mapping (do **not** hard-depend on Irys SPs)  
5. Manual full sync and incremental/changed-only sync  
6. Automatic scheduled sync (interval and/or daily)  
7. Product / inventory / price / image sync channels  
8. Batch upload with retry + exponential backoff  
9. Durable pending queue when API offline  
10. Image discovery from local/UNC paths; checksum/timestamp skip  
11. Dashboard status, counts, last/next sync  
12. Structured local logs with filter/export  
13. Secure secret storage (DPAPI / Credential Manager)  
14. Per-record failure isolation + Retry Failed  

---

## 11. Features that must NOT be copied (vendor-dependent)

| Feature | Why exclude |
|---------|-------------|
| Tiara / Irys / irysgroup.com / tiara.jewelry APIs | Vendor infrastructure |
| API keys / buckets / hub URLs from this install | Vendor credentials & tenancy |
| SignalR on-demand hub to Tiara | Vendor control channel |
| AWS S3 upload using vendor buckets/keys | Vendor storage |
| License serial / UDID registration / PC license counts | Vendor DRM |
| `RWLink_Updater` + control-centre S3 updates | Vendor update channel |
| SES email to Irys support (`SesEmailSender`) | Vendor support tooling |
| RFID scanner stack, local RFID HTTP server, cloud weblink | Out of scope for data sync replacement |
| Stocktake push into vendor RFID APIs | Vendor workflow |
| Dependence on `SP_GetIrys*` procedures as the only mapping path | Couples to Irys DB objects (optional later only if customer owns those SPs) |
| Hub-hosted data-mapping UI (`hub.tiara.jewelry/config/data-mapping`) | Vendor mapping service |
| Google Drive → vendor S3 pipeline as designed | Tied to vendor image pipeline |
| Any bypass of licensing / auth | Explicitly forbidden |

---

## 12. Data mapping findings

### 12.1 Where mapping lives today

- **Cloud-side / SP-side**, not a rich desktop mapping designer.  
- Desktop expects Irys SPs (or CSV columns already in Tiara import shape).  
- Hub URL references an external mapping configuration page.  

### 12.2 Fields appearing in upload/image pipelines

From payload/log fragments, the transport layer cares about at least:

- Store code  
- Product / stock number / productCode  
- RFID tag fields (`rfid_tag_new`)  
- Quantity  
- Image name / URL / Google Drive id / merged image name  
- Description (activity + some product paths)  
- Barcode (present in string table / RFID contexts)  

Jewelry-specific attributes (metal, stone, shape, weight, brand, vendor, cost, retail/sale price) are **not clearly hardcoded in the desktop binary strings**. They are likely produced by the **ERP SP / hub mapping** before or during import. Valliani Data Sync should expose these as **configurable mappings**.

### 12.3 Primary identity recommendation

Use **SKU / item number** as the durable product identity for the new system (as requested), with store code as the partition key. RWLink additionally tracks RFID tag values for scanner workflows — recreate RFID only if explicitly needed later.

---

## 13. Exception handling & logging (observed)

- log4net with per-machine rolling logs  
- Sync failures logged with store prefix (`Vallani Jewelers_default: ...`)  
- UI status line + dedicated image sync log files  
- Sync lock prevents overlapping runs  
- Individual network errors (DNS, SQL host unresolved) surface to UI without necessarily killing the process  
- Image missing / oversized cases collected into string builders for reporting  

Gaps vs target product:

- No first-class durable offline queue visible in local SQLite  
- Secrets not encrypted at rest  
- Limited structured “per SKU / endpoint / response” grid like the requested Logs screen  

---

## 14. Observed runtime evidence (this package)

From `logs/rwlink_ROHIT.log19.08.2026.log`:

1. App started on machine `ROHIT`, framework 4.8  
2. First API validation failed: DNS for `core-api.tiara.jewelry`  
3. Later validation succeeded; stores loaded for Vallani Jewelers  
4. SignalR connection failed (network)  
5. SQL connection to configured host failed (“No such host is known”)  
6. Image folder empty validation error  
7. User stopped RWLink  

This confirms the intended path: **API key → store list → SQL read → sync**, with image path required for image sync.

---

## 15. Assumptions for the replacement (Phase 1 decisions)

These will guide Phases 2–10 unless the customer overrides them:

1. **Rebuild from scratch** as **Valliani Data Sync** (.NET 8 WPF MVVM) — no rebrand of RWLink binaries.  
2. **Own REST API** as specified in upcoming `docs/API_SPECIFICATION.md` — zero calls to Tiara/Irys hosts.  
3. **Configurable SQL mapping** to customer tables/views; do not require `SP_GetIrys*` unless customer later provides and approves them.  
4. **Default identity:** `storeCode + sku` (item number as alternate key).  
5. **Incremental sync:** content hash of mapped fields + optional modified timestamp column when mapped.  
6. **Images:** local/UNC roots only in v1; FTP/S3 direct can be added later against **customer** storage.  
7. **Out of scope for v1:** RFID scanning, license server, stocktake push, vendor updater.  
8. **Secrets:** Windows DPAPI for SQL password and API key; never write them into `appsettings.json` or git.  
9. **Local sync state:** SQLite queue for pending/failed jobs and last hashes.  
10. **Database schema:** read-only against ERP SQL Server unless customer explicitly approves changes.

---

## 16. Deliverable status

| Deliverable | Status |
|-------------|--------|
| `docs/RWLINK_ANALYSIS.md` | **Complete (this document)** |
| Implementation of Valliani Data Sync | **Not started — blocked until Phase 1 accepted as complete** |
| `docs/API_SPECIFICATION.md` | Phase 2+ |
| `docs/DATABASE_MAPPING.md` | Phase 3+ |
| `docs/DEPLOYMENT.md` | Phase 10 |

---

## 17. Source of truth artifacts used

- `RWLink_extracted/RWLink/RWLink.exe` (+ config/manifests)  
- `SettingsInfo.txt`, `VersionInfo.txt`, `UpdaterConfig.txt`  
- `logs/rwlink_ROHIT.log19.08.2026.log`, `debug.txt`  
- Reflection type/method dump (`rwlink_types_dump.txt`)  
- Unicode string extraction (`rwlink_strings_interesting.txt`)  

---

*End of Phase 1 analysis.*
