# Database Mapping Guide

Valliani Data Sync reads your existing SQL Server database **without changing its schema**.

## How mapping works

1. Open **Data Mapping** in the app.
2. Set **Products table / view** (example: `ItemMaster`, or a view you create yourself).
3. Optionally set a **Modified-at column** for timestamp-based incremental reads.
4. Map each logical field to a physical column name.

Logical fields:

| Logical field | Purpose |
|---------------|---------|
| Sku | Primary product identity (required) |
| ItemNumber | Alternate identity |
| Name | Product name |
| Description | Long description |
| Category / Brand / Vendor | Classification |
| Cost / RetailPrice / SalePrice | Pricing |
| Quantity | On-hand qty |
| Store | Store code column |
| Barcode | UPC/EAN |
| Metal / Stone / Shape / Weight | Jewelry attributes |
| ImagePath | Relative or absolute path / file name |

## SQL generation

Queries are parameterized. Table/column identifiers are restricted to `[A-Za-z0-9_$#]` to prevent SQL injection.

Example pattern:

```sql
SELECT * FROM [ItemMaster]
WHERE (@StoreCode IS NULL OR [StoreCode] = @StoreCode)
  AND ([ModifiedAt] > @ModifiedSince) -- only if mapped
ORDER BY (SELECT NULL)
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
```

## Incremental sync

1. If a modified-at column is mapped, rows newer than the last successful sync are preferred.
2. Regardless, each mapped row gets a **content hash**. Unchanged hashes are skipped unless **Full Sync** is used.

## Recommended approach for iTransactDB / custom ERPs

**Preferred:** create a **read-only SQL view** that projects your ERP columns into stable names, then map those view columns in the UI.

Example (illustrative only — adjust to your real schema; do not run blindly):

```sql
CREATE VIEW dbo.v_VallianiProducts AS
SELECT
  SKU          AS SKU,
  ItemNo       AS ItemNumber,
  Description  AS ProductName,
  Category     AS Category,
  Cost         AS Cost,
  Price        AS RetailPrice,
  QtyOnHand    AS Quantity,
  Location     AS StoreCode,
  Barcode      AS Barcode,
  PictureFile  AS ImagePath,
  LastUpdated  AS ModifiedAt
FROM dbo.YourActualTable;
```

Ask before applying any DDL to production.

## What we are NOT using from RWLink

RWLink expected Irys stored procedures such as `SP_GetIrysItemDetailsForMergebyRow`. Valliani Data Sync does **not** require those procedures.
