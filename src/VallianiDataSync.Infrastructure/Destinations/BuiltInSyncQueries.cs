using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// Fixed sync sources: sales / inventory / products / caratone-sale / payments (+ test caps).
/// </summary>
public static class BuiltInSyncQueries
{
    public const string Sales = "sales";
    public const string Inventory = "inventory";
    /// <summary>Same inventory template, TOP 10,000 rows — fast marketplace test.</summary>
    public const string InventoryTest = "marketplace-inv-test";
    /// <summary>Marketplace product catalog (35 template columns) — one CSV push.</summary>
    public const string Products = "marketplace-products";
    /// <summary>Same products template, TOP 3,000 rows — fast marketplace test.</summary>
    public const string ProductsTest = "marketplace-products-test";
    /// <summary>JewelMate sales report (25 cols) — requires SaleReportDateFrom/To.</summary>
    public const string CaratoneSale = "caratone-sale";
    /// <summary>JewelMate payment report (15 cols) — same date UI as caratone-sale.</summary>
    public const string CaratonePayment = "caratone-payment";
    public const string Payments = "payments";

    /// <summary>Sources shown in Destinations dropdown (sales/payments removed — use caratone-*).</summary>
    public static IReadOnlyList<string> AllKeys { get; } =
        new[] { Inventory, InventoryTest, Products, ProductsTest, CaratoneSale, CaratonePayment };

    /// <summary>Default source for new projects.</summary>
    public static IReadOnlyList<string> DefaultKeys { get; } =
        new[] { Inventory };

    public static string DisplayName(string key) => key.ToLowerInvariant() switch
    {
        Sales => "Sales (invoice / commission lines)",
        Inventory => "Inventory FULL (all rows)",
        InventoryTest => "marketplace-inv-test (10,000 rows — quick test)",
        Products => "marketplace-products (35-col catalog FULL)",
        ProductsTest => "marketplace-products-test (3,000 rows — quick test)",
        CaratoneSale => "caratone-sale (JewelMate sales report — set dates)",
        CaratonePayment => "caratone-payment (JewelMate payments — set dates)",
        Payments => "Payments",
        _ => key
    };

    public static bool IsInventoryFamily(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals(Inventory, StringComparison.OrdinalIgnoreCase)
         || name.Equals(InventoryTest, StringComparison.OrdinalIgnoreCase));

    public static bool IsProductsFamily(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Equals(Products, StringComparison.OrdinalIgnoreCase)
         || name.Equals(ProductsTest, StringComparison.OrdinalIgnoreCase));

    public static bool IsCaratoneSale(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Equals(CaratoneSale, StringComparison.OrdinalIgnoreCase);

    public static bool IsCaratonePayment(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Equals(CaratonePayment, StringComparison.OrdinalIgnoreCase);

    /// <summary>caratone-sale or caratone-payment — both need the shared date pickers.</summary>
    public static bool IsCaratoneDated(string? name) =>
        IsCaratoneSale(name) || IsCaratonePayment(name);

    /// <summary>Inventory or products — always one CSV file to API.</summary>
    public static bool WantsSingleFileSource(string? name) =>
        IsInventoryFamily(name) || IsProductsFamily(name) || IsCaratoneDated(name);

    /// <summary>Exact CSV headers for inventory → marketplace (nothing else).</summary>
    public static readonly string[] InventoryHeaders =
    {
        "Sku", "Barcode", "Warehouse", "Quantity",
        "lastRestocked", "P-Cost", "Tag Price", "99 Price"
    };

    /// <summary>Exact 35 CSV headers for marketplace-products (template order).</summary>
    public static readonly string[] ProductHeaders =
    {
        "Sku", "Vendor-Model", "Description-Name", "Tag Price", "99-Price",
        "Category", "Subcategory-Department", "Style", "Brand-Design", "Metal-Color",
        "Metal-Type", "Size", "Gender", "Extent-Width", "AvgWeight",
        "Stone Type", "Center-Stone", "Center-Carat", "Center-Shape", "Center-Color",
        "Center-Clarity", "Side-Stone", "Side-Carat", "Side-Shape", "Side-Color",
        "Side-Clarity", "Dial", "Year", "p-cost", "Model-No",
        "Featureimages_Link", "Galleryimage_Link", "Video_Link", "JewelMate-Created-At", "Vendor"
    };

    /// <summary>Sales lines with salesperson split — user-provided SQL.</summary>
    public const string SalesSql = """
SELECT
    c.TransID AS [Transaction ID],
    c.TransDtlID AS [Transaction Detail ID],
    d.Invoice__0s AS [Transaction #],
    d.Invoice_Date AS [Transaction Date],
    DATENAME(MONTH, d.Invoice_Date) AS [Transaction Month],
    YEAR(d.Invoice_Date) AS [Transaction Year],
    COALESCE(d.Item__0s, i.ItemCode) AS [Item #],
    d.StyleName AS [Style #],
    COALESCE(d.StyleDesc, i.ItemDesc) AS [Description],
    v.VendorName AS [Vendor Name],
    id.ItemCost * COALESCE(c.SplitRatio, 100) / 100.0 AS [Inventory Cost],
    c.ExttPrice * COALESCE(c.SplitRatio, 100) / 100.0 AS [Sales Amount],
    id.DiscountAmt * COALESCE(c.SplitRatio, 100) / 100.0 AS [Disc Amt],
    (id.ItemPrice - ISNULL(id.DiscountAmt, 0)) * COALESCE(c.SplitRatio, 100) / 100.0 AS [Total],
    CAST(NULL AS nvarchar(100)) AS [Department],
    CAST(NULL AS nvarchar(100)) AS [Design],
    CAST(NULL AS nvarchar(100)) AS [Class],
    CAST(NULL AS nvarchar(100)) AS [Sub-Class],
    COALESCE(iset.ImagePath1, id.TransImagePath) AS [Image Dir.],
    sp.SPersonCode AS [Salespersons],
    LTRIM(RTRIM(
        CONCAT(
            sp.FirstName,
            CASE WHEN NULLIF(sp.MiddleName, '') IS NOT NULL THEN ' ' + sp.MiddleName ELSE '' END,
            CASE WHEN NULLIF(sp.LastName, '') IS NOT NULL THEN ' ' + sp.LastName ELSE '' END
        )
    )) AS [Salesperson Name],
    i.IVendorModel AS [VendorModel1],
    d.Quantity * COALESCE(c.SplitRatio, 100) / 100.0 AS [Qty],
    d.Quantity AS [Original Qty],
    c.SplitRatio AS [Split Percentage],
    id.UnitPrice AS [Unit Price],
    id.ItemPrice AS [Original Item Price],
    id.DiscountRate AS [Discount Rate],
    c.CommAmount AS [Commission Amount],
    c.BaseRate AS [Commission Base Rate],
    cs.SiteDesc AS [Store],
    cs.SiteCode AS [Store Code],
    c.TranswarehouseID AS [Warehouse ID],
    c.TransStationID AS [Station ID],
    c.TransactionDate AS [Sales Transaction Date],
    i.ItemAvgWeight AS [AvgWeight],
    ist.InvCat1ValueCode AS [InvCat1 Value],
    ist.InvCat2ValueCode AS [InvCat2 Value],
    ist.InvCat3ValueCode AS [InvCat3 Value],
    ist.InvCat4ValueCode AS [InvCat4 Value],
    ist.InvCat5ValueCode AS [InvCat5 Value],
    ist.InvCat6ValueCode AS [InvCat6 Value],
    ist.InvCat7ValueCode AS [InvCat7 Value],
    ist.InvCat8ValueCode AS [InvCat8 Value]
FROM dbo.JM_InvoiceDtlsCommVW AS c
LEFT JOIN dbo.JM_InvoicesDetailsListVW AS d
    ON d.InvoiceDtlID = c.TransDtlID AND d.InvoiceID = c.TransID
LEFT JOIN dbo.JM_InvoiceDetails AS id
    ON id.InvoiceDtlID = c.TransDtlID AND id.InvoiceID = c.TransID
LEFT JOIN dbo.JM_Invoices AS inv ON inv.InvoiceID = c.TransID
LEFT JOIN dbo.JM_Items AS i ON i.ItemID = id.ItemID
LEFT JOIN dbo.JM_Vendors AS v ON v.VendorID = i.IVendorID
LEFT JOIN dbo.JM_ItemStyles AS ist ON ist.ItemStyleID = i.ItemStyleID
LEFT JOIN dbo.JM_ItemSettings AS iset ON iset.ItemID = i.ItemID
LEFT JOIN dbo.JM_SalesPersons AS sp ON sp.SPersonID = c.SPersonID
LEFT JOIN dbo.JM_CompanySites AS cs ON cs.SiteID = d.SiteID
ORDER BY
    c.TransactionDate,
    d.Invoice__0s,
    [Salesperson Name],
    c.SplitRatio DESC
""";

    /// <summary>
    /// Marketplace inventory-update template: Sku, Barcode, Warehouse, Quantity,
    /// lastRestocked, P-Cost, Tag Price, 99 Price (clean SKU, OnHandQty &gt; 0).
    /// </summary>
    /// <summary>Exact user template — Warehouse = WLocationCode (VJ-PB, VJ-HEND), not description.</summary>
    public const string InventorySql = """
SELECT
    sku.CleanSku AS [Sku],
    i.BarcodeID AS [Barcode],
    wl.WLocationCode AS [Warehouse],
    CAST(CEILING(iwl.OnHandQty) AS INT) AS [Quantity],
    CASE
        WHEN i.ICreateDate IS NOT NULL THEN
            CONCAT(
                MONTH(i.ICreateDate), '/',
                DAY(i.ICreateDate), '/',
                YEAR(i.ICreateDate)
            )
        ELSE NULL
    END AS [lastRestocked],
    i.Cost AS [P-Cost],
    i.SellingPrice AS [Tag Price],
    vm.VMLevelCat2 AS [99 Price]
FROM dbo.JM_Items AS i
LEFT JOIN dbo.JM_VariationLevelsMatrix AS vm
    ON vm.VMLevelID = i.VMLevelID
INNER JOIN dbo.JM_ItemsInWarehouseLocations AS iwl
    ON iwl.ItemID = i.ItemID
LEFT JOIN dbo.JM_WarehouseLocations AS wl
    ON wl.WLocationID = iwl.WLocationID
CROSS APPLY (
    SELECT LTRIM(RTRIM(i.ItemCode)) AS RawSku
) AS raw
CROSS APPLY (
    SELECT
        CASE
            WHEN PATINDEX('%[^_]%', REVERSE(raw.RawSku)) = 0
                THEN ''
            ELSE LEFT(
                raw.RawSku,
                LEN(raw.RawSku)
                - PATINDEX('%[^_]%', REVERSE(raw.RawSku))
                + 1
            )
        END AS TrimmedSku
) AS trimmed
CROSS APPLY (
    SELECT
        CASE
            WHEN trimmed.TrimmedSku = '' THEN ''
            WHEN PATINDEX('%[^0]%', trimmed.TrimmedSku) = 0
                THEN '0'
            ELSE SUBSTRING(
                trimmed.TrimmedSku,
                PATINDEX('%[^0]%', trimmed.TrimmedSku),
                LEN(trimmed.TrimmedSku)
            )
        END AS CleanSku
) AS sku
WHERE
    iwl.OnHandQty > 0
    AND i.ItemCode IS NOT NULL
    AND sku.CleanSku <> ''
ORDER BY
    sku.CleanSku,
    wl.WLocationCode
""";

    /// <summary>Same as InventorySql but TOP 10,000 for quick marketplace push tests.</summary>
    public const string InventoryTestSql = """
SELECT TOP (10000)
    sku.CleanSku AS [Sku],
    i.BarcodeID AS [Barcode],
    wl.WLocationCode AS [Warehouse],
    CAST(CEILING(iwl.OnHandQty) AS INT) AS [Quantity],
    CASE
        WHEN i.ICreateDate IS NOT NULL THEN
            CONCAT(
                MONTH(i.ICreateDate), '/',
                DAY(i.ICreateDate), '/',
                YEAR(i.ICreateDate)
            )
        ELSE NULL
    END AS [lastRestocked],
    i.Cost AS [P-Cost],
    i.SellingPrice AS [Tag Price],
    vm.VMLevelCat2 AS [99 Price]
FROM dbo.JM_Items AS i
LEFT JOIN dbo.JM_VariationLevelsMatrix AS vm
    ON vm.VMLevelID = i.VMLevelID
INNER JOIN dbo.JM_ItemsInWarehouseLocations AS iwl
    ON iwl.ItemID = i.ItemID
LEFT JOIN dbo.JM_WarehouseLocations AS wl
    ON wl.WLocationID = iwl.WLocationID
CROSS APPLY (
    SELECT LTRIM(RTRIM(i.ItemCode)) AS RawSku
) AS raw
CROSS APPLY (
    SELECT
        CASE
            WHEN PATINDEX('%[^_]%', REVERSE(raw.RawSku)) = 0
                THEN ''
            ELSE LEFT(
                raw.RawSku,
                LEN(raw.RawSku)
                - PATINDEX('%[^_]%', REVERSE(raw.RawSku))
                + 1
            )
        END AS TrimmedSku
) AS trimmed
CROSS APPLY (
    SELECT
        CASE
            WHEN trimmed.TrimmedSku = '' THEN ''
            WHEN PATINDEX('%[^0]%', trimmed.TrimmedSku) = 0
                THEN '0'
            ELSE SUBSTRING(
                trimmed.TrimmedSku,
                PATINDEX('%[^0]%', trimmed.TrimmedSku),
                LEN(trimmed.TrimmedSku)
            )
        END AS CleanSku
) AS sku
WHERE
    iwl.OnHandQty > 0
    AND i.ItemCode IS NOT NULL
    AND sku.CleanSku <> ''
ORDER BY
    sku.CleanSku,
    wl.WLocationCode
""";

    /// <summary>
    /// Marketplace products catalog (35 columns). CTE — run as-is (do not wrap in derived table).
    /// </summary>
    public const string ProductsSql = """
WITH RawSku AS
(
    SELECT i.ItemID, i.ICreateDate,
           LTRIM(RTRIM(i.ItemCode)) AS RawCode
    FROM dbo.JM_Items AS i
    WHERE i.ItemCode IS NOT NULL
),
TrimmedSku AS
(
    SELECT a.ItemID, a.ICreateDate,
           CASE
             WHEN a.RawCode = N'' THEN N''
             WHEN PATINDEX(N'%[^_]%', REVERSE(a.RawCode)) = 0 THEN N''
             ELSE LEFT(a.RawCode,
                 LEN(a.RawCode) - PATINDEX(N'%[^_]%', REVERSE(a.RawCode)) + 1)
           END AS NoEndUnderscores
    FROM RawSku AS a
),
CleanItems AS
(
    SELECT t.ItemID, t.ICreateDate,
           CASE
             WHEN t.NoEndUnderscores = N'' THEN N''
             WHEN PATINDEX(N'%[^0]%', t.NoEndUnderscores) = 0 THEN N'0'
             ELSE SUBSTRING(t.NoEndUnderscores,
                    PATINDEX(N'%[^0]%', t.NoEndUnderscores),
                    LEN(t.NoEndUnderscores))
           END AS CleanSku
    FROM TrimmedSku AS t
),
SelectedItems AS
(
    SELECT c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
)
/*PRODUCTS_SELECT*/
SELECT
    COALESCE(r.CleanSku, N'') AS [Sku],
    COALESCE(
        NULLIF(LTRIM(RTRIM(i.IVendorModel)), N''),
        NULLIF(LTRIM(RTRIM(ist.VendorModel)), N''),
        N''
    ) AS [Vendor-Model],
    COALESCE(
        NULLIF(LTRIM(RTRIM(i.ItemName)), N''),
        NULLIF(LTRIM(RTRIM(i.ItemDesc)), N''),
        NULLIF(LTRIM(RTRIM(ist.ItemStyleName)), N''),
        N''
    ) AS [Description-Name],
    COALESCE(CONVERT(NVARCHAR(50), i.SellingPrice), N'') AS [Tag Price],
    COALESCE(CONVERT(NVARCHAR(150), vm.VMLevelCat2), N'') AS [99-Price],
    COALESCE(
        NULLIF(LTRIM(RTRIM(ist.InvCat4ValueCode)), N''),
        N'Unknown Category'
    ) AS [Category],
    COALESCE(
        NULLIF(LTRIM(RTRIM(ist.InvCat1ValueCode)), N''),
        N'Unknown SubCategory'
    ) AS [Subcategory-Department],
    COALESCE(
        CASE
            WHEN UPPER(LTRIM(RTRIM(ist.InvCat4ValueCode))) = N'LINKNLOCK'
                 AND UPPER(i.ItemName) LIKE N'%DOG TAG%'
            THEN N'DOG TAG'
            ELSE NULLIF(LTRIM(RTRIM(ist.ItemStyleCode)), N'')
        END,
        N''
    ) AS [Style],
    COALESCE(ist.InvCat4ValueCode, N'') AS [Brand-Design],
    CAST(N'' AS NVARCHAR(100)) AS [Metal-Color],
    COALESCE(NULLIF(LTRIM(RTRIM(ist.InvCat5ValueCode)), N''), N'') AS [Metal-Type],
    COALESCE(
        CASE WHEN UPPER(LTRIM(RTRIM(ist.InvCat1ValueCode))) = N'ROLEX'
             THEN NULLIF(LTRIM(RTRIM(ist.InvCat3ValueCode)), N'')
             ELSE NULL END,
        N''
    ) AS [Size],
    CASE
        WHEN UPPER(LTRIM(RTRIM(ist.InvCat2ValueCode))) IN (N'MEN', N'MENS') THEN N'Mens'
        WHEN UPPER(LTRIM(RTRIM(ist.InvCat2ValueCode))) IN (N'WOMEN', N'WOMENS', N'LADIES', N'LADYS') THEN N'Womens'
        WHEN UPPER(LTRIM(RTRIM(ist.InvCat2ValueCode))) = N'UNISEX' THEN N'Unisex'
        ELSE N''
    END AS [Gender],
    CAST(N'' AS NVARCHAR(100)) AS [Extent-Width],
    COALESCE(CONVERT(NVARCHAR(50), i.ItemAvgWeight), N'') AS [AvgWeight],
    COALESCE(
        CASE
            WHEN UPPER(centerElement.ElementType) LIKE N'%DIAMOND%'
              OR UPPER(sideElement.ElementType) LIKE N'%DIAMOND%' THEN N'Diamond'
            ELSE NULL
        END,
        N''
    ) AS [Stone Type],
    COALESCE(centerElement.ElementType, N'') AS [Center-Stone],
    COALESCE(CONVERT(NVARCHAR(50), centerElement.ElementQuantity), N'') AS [Center-Carat],
    COALESCE(NULLIF(sh.ShapeCode, ''), centerShape.ShapeCode, N'') AS [Center-Shape],
    COALESCE(NULLIF(fc.ColorCode, ''), centerElement.DiamondColor, N'') AS [Center-Color],
    COALESCE(NULLIF(fcl.ClarityCode, ''), centerElement.DiamondClarity, N'') AS [Center-Clarity],
    COALESCE(sideElement.ElementType, N'') AS [Side-Stone],
    COALESCE(CONVERT(NVARCHAR(50), sideElement.ElementQuantity), N'') AS [Side-Carat],
    COALESCE(sideShape.ShapeCode, N'') AS [Side-Shape],
    COALESCE(sideElement.DiamondColor, N'') AS [Side-Color],
    COALESCE(sideElement.DiamondClarity, N'') AS [Side-Clarity],
    CAST(N'' AS NVARCHAR(150)) AS [Dial],
    COALESCE(vm.VMLevelCat4, N'') AS [Year],
    COALESCE(CONVERT(NVARCHAR(50), i.Cost), N'') AS [p-cost],
    COALESCE(vm.VMLevelCat5, N'') AS [Model-No],
    COALESCE(NULLIF(LTRIM(RTRIM(iset.ImagePath1)), N''), N'') AS [Featureimages_Link],
    COALESCE(images.GalleryImagePaths, N'') AS [Galleryimage_Link],
    CAST(N'' AS NVARCHAR(MAX)) AS [Video_Link],
    CASE WHEN i.ICreateDate IS NOT NULL
         THEN CONCAT(MONTH(i.ICreateDate), '/', DAY(i.ICreateDate), '/', YEAR(i.ICreateDate))
         ELSE N'' END AS [JewelMate-Created-At],
    COALESCE(ist.VendorCode, N'') AS [Vendor]
FROM SelectedItems AS r
JOIN dbo.JM_Items AS i ON i.ItemID = r.ItemID
LEFT JOIN dbo.JM_ItemStyles AS ist ON ist.ItemStyleID = i.ItemStyleID
LEFT JOIN dbo.JM_VariationLevelsMatrix AS vm ON vm.VMLevelID = i.VMLevelID
OUTER APPLY
(
    SELECT TOP (1) s.*
    FROM dbo.JM_ItemSettings AS s
    WHERE s.ItemID = i.ItemID
    ORDER BY s.LastUpdate DESC, s.ISettingID
) AS iset
LEFT JOIN dbo.JM_Shapes AS sh ON sh.ShapeID = iset.ShapeID
LEFT JOIN dbo.JM_Colors AS fc ON fc.ColorID = iset.FromColorID
LEFT JOIN dbo.JM_Clarities AS fcl ON fcl.ClarityID = iset.FromClarityID
OUTER APPLY
(
    SELECT TOP (1)
        CASE WHEN UPPER(et.ElementType) = N'CENTER-DIAMOND' THEN N'Center-Diamond'
             ELSE et.ElementType END AS ElementType,
        ie.Quantity AS ElementQuantity,
        CASE WHEN UPPER(et.ElementType) = N'CENTER-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement3)), N'') ELSE NULL END AS ShapeCandidate,
        CASE WHEN UPPER(et.ElementType) = N'CENTER-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement4)), N'') ELSE NULL END AS DiamondColor,
        CASE WHEN UPPER(et.ElementType) = N'CENTER-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement5)), N'') ELSE NULL END AS DiamondClarity
    FROM dbo.JM_ItemElements AS ie
    JOIN dbo.JM_ElementTypes AS et ON et.ElementTypeID = ie.ElementTypeID
    LEFT JOIN dbo.JM_ComponentElementsMatrix AS cm ON cm.ItemElementID = ie.ItemElementID
    WHERE ie.ItemID = i.ItemID
      AND (UPPER(et.ElementType) LIKE N'CENTER-%'
           OR UPPER(et.ElementType) LIKE N'CENTER %')
    ORDER BY ie.ItemElementOrder, ie.ItemElementID
) AS centerElement
OUTER APPLY
(
    SELECT TOP (1)
        CASE WHEN UPPER(et.ElementType) = N'SIDE-DIAMOND' THEN N'Side-Diamond'
             ELSE et.ElementType END AS ElementType,
        ie.Quantity AS ElementQuantity,
        CASE WHEN UPPER(et.ElementType) = N'SIDE-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement3)), N'') ELSE NULL END AS ShapeCandidate,
        CASE WHEN UPPER(et.ElementType) = N'SIDE-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement4)), N'') ELSE NULL END AS DiamondColor,
        CASE WHEN UPPER(et.ElementType) = N'SIDE-DIAMOND'
             THEN NULLIF(LTRIM(RTRIM(cm.CMElement5)), N'') ELSE NULL END AS DiamondClarity
    FROM dbo.JM_ItemElements AS ie
    JOIN dbo.JM_ElementTypes AS et ON et.ElementTypeID = ie.ElementTypeID
    LEFT JOIN dbo.JM_ComponentElementsMatrix AS cm ON cm.ItemElementID = ie.ItemElementID
    WHERE ie.ItemID = i.ItemID
      AND (UPPER(et.ElementType) LIKE N'SIDE-%'
           OR UPPER(et.ElementType) LIKE N'SIDE %')
    ORDER BY ie.ItemElementOrder, ie.ItemElementID
) AS sideElement
OUTER APPLY
(
    SELECT TOP (1) s.ShapeCode
    FROM dbo.JM_Shapes AS s
    WHERE UPPER(LTRIM(RTRIM(s.ShapeCode))) = UPPER(centerElement.ShapeCandidate)
       OR UPPER(LTRIM(RTRIM(s.ShapeDesc))) = UPPER(centerElement.ShapeCandidate)
    ORDER BY s.ShapeID
) AS centerShape
OUTER APPLY
(
    SELECT TOP (1) s.ShapeCode
    FROM dbo.JM_Shapes AS s
    WHERE UPPER(LTRIM(RTRIM(s.ShapeCode))) = UPPER(sideElement.ShapeCandidate)
       OR UPPER(LTRIM(RTRIM(s.ShapeDesc))) = UPPER(sideElement.ShapeCandidate)
    ORDER BY s.ShapeID
) AS sideShape
OUTER APPLY
(
    SELECT STUFF((
       SELECT N' | ' + p.ImagePath
       FROM (
           SELECT NULLIF(LTRIM(RTRIM(iset.ImagePath2)), N'') AS ImagePath
           UNION ALL SELECT NULLIF(LTRIM(RTRIM(iset.ImagePath3)), N'')
           UNION ALL SELECT NULLIF(LTRIM(RTRIM(iset.ImagePath4)), N'')
           UNION ALL SELECT NULLIF(LTRIM(RTRIM(iset.ImagePath5)), N'')
           UNION ALL SELECT NULLIF(LTRIM(RTRIM(sii.ImagePath)), N'')
           FROM dbo.JM_ItemStyleImages AS sii
           WHERE sii.ItemStyleID = i.ItemStyleID
       ) AS p
       WHERE p.ImagePath IS NOT NULL
       GROUP BY p.ImagePath
       FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 3, N'') AS GalleryImagePaths
) AS images
ORDER BY i.ICreateDate DESC, r.CleanSku, i.ItemID
""";

    /// <summary>Fast fingerprint for marketplace-products (SelectedItems count ≈ output rows).</summary>
    public const string ProductsFingerprintSql = """
WITH RawSku AS
(
    SELECT i.ItemID, i.ICreateDate,
           LTRIM(RTRIM(i.ItemCode)) AS RawCode
    FROM dbo.JM_Items AS i
    WHERE i.ItemCode IS NOT NULL
),
TrimmedSku AS
(
    SELECT a.ItemID, a.ICreateDate,
           CASE
             WHEN a.RawCode = N'' THEN N''
             WHEN PATINDEX(N'%[^_]%', REVERSE(a.RawCode)) = 0 THEN N''
             ELSE LEFT(a.RawCode,
                 LEN(a.RawCode) - PATINDEX(N'%[^_]%', REVERSE(a.RawCode)) + 1)
           END AS NoEndUnderscores
    FROM RawSku AS a
),
CleanItems AS
(
    SELECT t.ItemID, t.ICreateDate,
           CASE
             WHEN t.NoEndUnderscores = N'' THEN N''
             WHEN PATINDEX(N'%[^0]%', t.NoEndUnderscores) = 0 THEN N'0'
             ELSE SUBSTRING(t.NoEndUnderscores,
                    PATINDEX(N'%[^0]%', t.NoEndUnderscores),
                    LEN(t.NoEndUnderscores))
           END AS CleanSku
    FROM TrimmedSku AS t
),
SelectedItems AS
(
    SELECT c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
)
SELECT COUNT_BIG(*) FROM SelectedItems
""";

    /// <summary>Same as ProductsSql but TOP 3,000 newest clean SKUs.</summary>
    public static string ProductsTestSql { get; } = ProductsSql.Replace(
        """
SelectedItems AS
(
    SELECT c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
)
/*PRODUCTS_SELECT*/
""",
        """
SelectedItems AS
(
    SELECT TOP (3000) c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
    ORDER BY c.ICreateDate DESC, c.CleanSku, c.ItemID
)
""",
        StringComparison.Ordinal);

    public static string ProductsTestFingerprintSql { get; } = ProductsFingerprintSql.Replace(
        """
SelectedItems AS
(
    SELECT c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
)
SELECT COUNT_BIG(*) FROM SelectedItems
""",
        """
SelectedItems AS
(
    SELECT TOP (3000) c.ItemID, c.CleanSku, c.ICreateDate
    FROM CleanItems AS c
    WHERE c.CleanSku <> N''
      AND UPPER(c.CleanSku) <> N'TEMPLATE'
    ORDER BY c.ICreateDate DESC, c.CleanSku, c.ItemID
)
SELECT COUNT_BIG(*) FROM SelectedItems
""",
        StringComparison.Ordinal);

    /// <summary>Invoice payments — user-provided SQL.</summary>
    public const string PaymentsSql = """
SELECT
    cs.SiteDesc AS [Store],
    COALESCE(inv.Customer__0s, cu.CustomerCode) AS [Customer #],
    COALESCE(inv.Customer_Name, cu.CustomerName) AS [Customer Name],
    COALESCE(inv.Trans__0s, inv.Invoice__0s, i.InvoiceCode) AS [Transaction #],
    COALESCE(inv.Invoice_Date, i.InvoiceDate) AS [Transaction Date],
    p.PaymentType AS [Type],
    ixp.ARCreateDate AS [Pymt Create Date],
    COALESCE(pd.AppliedDate, ixp.ARCreateDate) AS [Pymt Posted Date],
    COALESCE(
        NULLIF(LTRIM(RTRIM(pm.PayMethod)), ''),
        NULLIF(LTRIM(RTRIM(p.PAYMENT_MEDIA)), ''),
        NULLIF(LTRIM(RTRIM(p.PAYMENT_TYPE)), ''),
        NULLIF(LTRIM(RTRIM(p.PaymentType)), '')
    ) AS [Pay Method],
    COALESCE(
        NULLIF(LTRIM(RTRIM(pc.PayCode)), ''),
        NULLIF(LTRIM(RTRIM(p.PAYMENT_TYPE)), ''),
        NULLIF(LTRIM(RTRIM(p.PaymentType)), '')
    ) AS [Pay Code],
    COALESCE(
        NULLIF(LTRIM(RTRIM(p.ChkCardNum)), ''),
        NULLIF(LTRIM(RTRIM(p.ExternalGiftCardNumber)), '')
    ) AS [Check Card # GC #],
    p.CardHolder AS [Card Holder],
    p.PayAmount AS [Payment Amt],
    COALESCE(pd.AppliedAmt, p.PayAmount) AS [Applied Amt],
    CASE WHEN p.PayAmount IS NOT NULL THEN 100.00 ELSE NULL END AS [SplitPercentage]
FROM dbo.JM_InvoiceXPayments AS ixp
LEFT JOIN dbo.JM_InvoicesListVW AS inv ON inv.InvoiceID = ixp.InvoiceID
LEFT JOIN dbo.JM_Invoices AS i ON i.InvoiceID = ixp.InvoiceID
LEFT JOIN dbo.JM_Customers AS cu ON cu.CustomerID = COALESCE(inv.CustomerID, i.CustomerID)
LEFT JOIN dbo.JM_CompanySites AS cs ON cs.SiteID = COALESCE(inv.SiteID, cu.SiteID)
LEFT JOIN dbo.JM_ARPayments AS p ON p.PaymentID = ixp.PaymentID
LEFT JOIN dbo.JM_ARPaymentDetails AS pd
    ON pd.PaymentID = ixp.PaymentID AND pd.PayInvID = ixp.InvoiceID
LEFT JOIN dbo.JM_PayMethods AS pm ON pm.PayMethodID = p.PayMethodID
LEFT JOIN dbo.JM_PayCodes AS pc ON pc.PayCodeID = i.PayCodeID
ORDER BY
    COALESCE(inv.Invoice_Date, i.InvoiceDate),
    [Transaction #],
    ixp.ARCreateDate
""";

    /// <summary>Always install / refresh the three fixed queries (overwrites stale SQL).</summary>
    public static void EnsureDefaults(FieldMappingConfig mapping, IReadOnlyList<string>? discoveredTables = null)
    {
        mapping.TableQueries ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        mapping.TableAliases ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        mapping.TableColumns ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        mapping.DiscoveredTables ??= new List<string>();
        mapping.SelectedTables ??= new List<string>();
        mapping.DiscoveredColumns ??= new List<string>();

        // Remove null/blank entries that cause ArgumentNullException (Parameter 'key') on startup
        mapping.DiscoveredTables.RemoveAll(string.IsNullOrWhiteSpace);
        mapping.SelectedTables.RemoveAll(string.IsNullOrWhiteSpace);
        mapping.DiscoveredColumns.RemoveAll(string.IsNullOrWhiteSpace);

        mapping.TableQueries[Inventory] = InventorySql;
        mapping.TableQueries[InventoryTest] = InventoryTestSql;
        mapping.TableQueries[Products] = ProductsSql;
        mapping.TableQueries[ProductsTest] = ProductsTestSql;
        // Do NOT wipe caratone after ApplyDates — only (re)install template when missing or still has {{tokens}}
        PinQueryTemplate(mapping.TableQueries, CaratoneSale, CaratoneSaleSql.Template);
        PinQueryTemplate(mapping.TableQueries, CaratonePayment, CaratonePaymentSql.Template);

        // Always pin inventory / products / caratone UI fields to template headers
        mapping.TableColumns[Inventory] = InventoryHeaders.ToList();
        mapping.TableColumns[InventoryTest] = InventoryHeaders.ToList();
        mapping.TableColumns[Products] = ProductHeaders.ToList();
        mapping.TableColumns[ProductsTest] = ProductHeaders.ToList();
        mapping.TableColumns[CaratoneSale] = CaratoneSaleSql.Headers.ToList();
        mapping.TableColumns[CaratonePayment] = CaratonePaymentSql.Headers.ToList();

        foreach (var key in AllKeys)
        {
            if (!mapping.TableAliases.ContainsKey(key) || string.IsNullOrWhiteSpace(mapping.TableAliases[key]))
                mapping.TableAliases[key] = key;
        }

        // Keep fixed sources at top of discovered list for Data Mapping
        foreach (var key in AllKeys.Reverse())
        {
            mapping.DiscoveredTables.RemoveAll(t => t.Equals(key, StringComparison.OrdinalIgnoreCase));
            mapping.DiscoveredTables.Insert(0, key);
        }
    }

    public static void EnableNamedSources(FieldMappingConfig mapping, IReadOnlyList<string>? discoveredTables = null)
    {
        EnsureDefaults(mapping, discoveredTables);
        mapping.SyncAllTables = false;
        mapping.SelectedTables = AllKeys.ToList();
    }

    public static bool IsFixedSource(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        AllKeys.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Install template only when absent or still contains {{placeholders}}.
    /// Preserves in-memory ApplyDates() result used by caratone preview/sync.
    /// </summary>
    private static void PinQueryTemplate(Dictionary<string, string> queries, string key, string template)
    {
        if (!queries.TryGetValue(key, out var cur)
            || string.IsNullOrWhiteSpace(cur)
            || cur.Contains("{{", StringComparison.Ordinal))
            queries[key] = template;
    }

    public static bool TryGetQuery(FieldMappingConfig mapping, string name, out string sql)
    {
        sql = "";
        EnsureDefaults(mapping);
        if (mapping.TableQueries is null)
            return false;
        if (!mapping.TableQueries.TryGetValue(name, out var q) || string.IsNullOrWhiteSpace(q))
            return false;
        // Caratone DECLARE batches: keep full text (ORDER BY OK). Don't subquery-prep.
        if (IsCaratoneDated(name) || IsBatchSql(q))
        {
            sql = q.Trim().TrimStart(';').TrimEnd(';').Trim();
            return sql.Length > 0;
        }
        sql = PrepareSubquery(q);
        return sql.Length > 0;
    }

    /// <summary>Strip trailing semicolon / ORDER BY so the text can be wrapped for paging.</summary>
    public static string PrepareSubquery(string sql)
    {
        var s = sql.Trim().TrimStart(';').TrimEnd(';').Trim();
        var idx = s.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
            s = s[..idx].Trim();
        return s;
    }

    public static bool IsCteSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var s = sql.Trim().TrimStart(';').Trim();
        return s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>DECLARE… / WITH… batches must not be wrapped in a derived table.</summary>
    public static bool IsBatchSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var s = sql.Trim().TrimStart(';').Trim();
        return s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("DECLARE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CTE / DECLARE batches must run as-is; others wrap in a derived table.</summary>
    public static string ToExecutableSelect(string preparedSql)
    {
        var q = preparedSql.Trim().TrimStart(';').Trim();
        if (IsBatchSql(q))
            return q;
        return $"SELECT * FROM ({q}) AS _syncq";
    }

    public static string ToExecutableCount(string preparedSql, bool mysql)
    {
        var countExpr = mysql ? "COUNT(*)" : "COUNT_BIG(*)";
        var q = preparedSql.Trim().TrimStart(';').Trim();
        if (IsBatchSql(q))
            return ProductsFingerprintSql; // products CTE only; caratone uses date fingerprint in sync
        return $"SELECT {countExpr} FROM ({q}) AS _fp";
    }
}
