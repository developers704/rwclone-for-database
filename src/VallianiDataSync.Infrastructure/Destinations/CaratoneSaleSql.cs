namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// JewelMate sales report (25 cols). Placeholders {{ReportDate}} / {{NextDate}}
/// (NextDate = exclusive end = last inclusive day + 1).
/// </summary>
public static class CaratoneSaleSql
{
    public const string ReportDateToken = "{{ReportDate}}";
    public const string NextDateToken = "{{NextDate}}";

    public static readonly string[] Headers =
    {
        "Transaction #", "Transaction Date", "Transaction Month", "Transaction Year",
        "Item #", "Style #", "Description", "VendorModel1", "Vendor Name",
        "Qty", "Inventory Cost", "Unit Price", "Sales Amount", "Disc Amt", "Total",
        "Store", "Department", "Design", "Class", "Sub-Class",
        "Image Dir.", "AvgWeight", "Pay Codes", "Salespersons", "Salesperson Name"
    };

    /// <summary>from/to inclusive → SQL exclusive NextDate.</summary>
    public static string ApplyDates(string sql, DateTime fromInclusive, DateTime toInclusive)
    {
        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        if (to < from)
            (from, to) = (to, from);
        var report = from.ToString("yyyy-MM-dd");
        var next = to.AddDays(1).ToString("yyyy-MM-dd");
        return sql
            .Replace(ReportDateToken, report, StringComparison.Ordinal)
            .Replace(NextDateToken, next, StringComparison.Ordinal);
    }

    public const string Template = """
DECLARE @ReportDate DATE = '{{ReportDate}}';
DECLARE @NextDate   DATE = '{{NextDate}}';

;WITH
RepairHeaders AS (
    SELECT
        r.RepairID, r.RepairCode, r.WarehouseID,
        r.SPersonID, r.SPersonCode, r.PayCodeID,
        CASE WHEN r.ClosedDate >= @ReportDate
                  AND r.ClosedDate < @NextDate
             THEN r.ClosedDate ELSE r.RepairDate END AS SalesReportDate
    FROM dbo.JM_Repairs AS r
    WHERE ISNULL(r.CanceledStatus, 0) = 0
      AND (
            (r.ClosedDate >= @ReportDate AND r.ClosedDate < @NextDate)
            OR
            (r.ClosedDate IS NULL
             AND r.RepairDate >= @ReportDate
             AND r.RepairDate < @NextDate)
          )
),
AllLines AS (
    SELECT
        CAST('INVOICE' AS VARCHAR(20)) AS SourceKind,
        inv.InvoiceID AS TransactionID,
        inv.InvoiceCode AS TransactionCode,
        inv.InvoiceDate AS TransactionDate,
        inv.WarehouseID,
        inv.SPersonID AS OwnerSPersonID,
        inv.SPersonCode AS OwnerSPersonCode,
        inv.PayCodeID AS HeaderPayCodeID,
        d.RowOrder,
        d.InvoiceDtlID AS SourceDetailID,
        d.ItemID,
        CAST(NULL AS NVARCHAR(150)) AS CodeOverride,
        d.StyleName AS LineDescription,
        CAST(NULL AS NVARCHAR(1000)) AS LineImagePath,
        CAST(ISNULL(d.Quantity, 0) - ISNULL(d.ReturnQuantity, 0)
             AS DECIMAL(19,6)) AS BaseQuantity,
        CAST(ISNULL(d.ItemCost, 0) AS DECIMAL(19,4)) AS ItemCost,
        CAST(ISNULL(d.UnitPrice, 0) AS DECIMAL(19,4)) AS BaseUnitPrice,
        CAST(ISNULL(d.DiscountAmt, 0) AS DECIMAL(19,4)) AS BaseDiscount
    FROM dbo.JM_Invoices AS inv
    JOIN dbo.JM_InvoiceDetails AS d ON d.InvoiceID = inv.InvoiceID
    LEFT JOIN dbo.JM_Items AS thisItem ON thisItem.ItemID = d.ItemID
    WHERE inv.InvoiceDate >= @ReportDate
      AND inv.InvoiceDate < @NextDate
      AND ISNULL(inv.CanceledStatus, 0) = 0
      AND NOT (
          ISNULL(thisItem.ItemCode, '') = 'ITEM'
          AND ISNULL(d.ItemPrice, 0) = 0
          AND ISNULL(d.UnitPrice, 0) > 0
          AND ISNULL(d.Quantity, 0) > 0
          AND ISNULL(d.ReturnQuantity, 0) = 0
          AND EXISTS (
              SELECT 1
              FROM dbo.JM_InvoiceDetails AS otherLine
              JOIN dbo.JM_Items AS otherItem
                   ON otherItem.ItemID = otherLine.ItemID
              WHERE otherLine.InvoiceID = inv.InvoiceID
                AND otherLine.InvoiceDtlID <> d.InvoiceDtlID
                AND otherItem.ItemCode <> 'ITEM'
                AND (ISNULL(otherLine.Quantity, 0)
                     - ISNULL(otherLine.ReturnQuantity, 0)) > 0
                AND (
                     ABS(ISNULL(otherLine.UnitPrice, 0)
                         - ISNULL(d.UnitPrice, 0)) <= 0.01
                     OR ABS(
                         ISNULL(otherLine.UnitPrice, 0)
                         * (ISNULL(otherLine.Quantity, 0)
                            - ISNULL(otherLine.ReturnQuantity, 0))
                         - ISNULL(otherLine.DiscountAmt, 0)
                         - (ISNULL(d.UnitPrice, 0)
                            * ISNULL(d.Quantity, 0))
                       ) <= 0.01
                    )
          )
      )

    UNION ALL

    SELECT
        CAST('REPAIR ITEM' AS VARCHAR(20)),
        r.RepairID, r.RepairCode, r.SalesReportDate,
        r.WarehouseID, r.SPersonID, r.SPersonCode, r.PayCodeID,
        d.RowOrder, d.RepairDtlID, d.ItemID, d.CustomItemNum,
        d.StyleName, d.TransImagePath,
        CAST(ISNULL(d.Quantity, 0) AS DECIMAL(19,6)),
        CAST(ISNULL(d.ItemCost, 0) AS DECIMAL(19,4)),
        CAST(ISNULL(d.UnitPrice, 0) AS DECIMAL(19,4)),
        CAST(ISNULL(d.DiscountAmt, 0) AS DECIMAL(19,4))
    FROM RepairHeaders AS r
    JOIN dbo.JM_RepairDetails AS d ON d.RepairID = r.RepairID
    WHERE ISNULL(d.Status, 1) >= 0
      AND NOT (
          ISNULL(d.UnitPrice, 0) > 0
          AND EXISTS (
              SELECT 1 FROM dbo.JM_RepairJobs AS j
              WHERE j.RepairID = r.RepairID
                AND ISNULL(j.Status, 1) >= 0
                AND ISNULL(j.UnitPrice, 0) > 0
          )
      )

    UNION ALL

    SELECT
        CAST('REPAIR JOB' AS VARCHAR(20)),
        r.RepairID, r.RepairCode, r.SalesReportDate,
        r.WarehouseID, r.SPersonID, r.SPersonCode, r.PayCodeID,
        j.RowOrder, j.RepairJobID,
        CAST(NULL AS UNIQUEIDENTIFIER), j.JobCode,
        j.JobDesc, j.TransImagePath,
        CAST(ISNULL(j.Quantity, 0) AS DECIMAL(19,6)),
        CAST(ISNULL(j.ItemCost, 0) AS DECIMAL(19,4)),
        CAST(ISNULL(j.UnitPrice, 0) AS DECIMAL(19,4)),
        CAST(ISNULL(j.DiscountAmt, 0) AS DECIMAL(19,4))
    FROM RepairHeaders AS r
    JOIN dbo.JM_RepairJobs AS j ON j.RepairID = r.RepairID
    WHERE ISNULL(j.Status, 1) >= 0
),
SplitRanked AS (
    SELECT
        sx.TranID, sx.TranType, sx.SPersonID,
        sx.SplitRatio, sx.IsOwnerSP, sx.TxSPID,
        ROW_NUMBER() OVER (
            PARTITION BY sx.TranID, sx.TranType, sx.SPersonID
            ORDER BY ISNULL(sx.IsOwnerSP, 0) DESC, sx.TxSPID
        ) AS DuplicateRank
    FROM dbo.JM_TransXSPersonSplitRatios AS sx
),
SplitClean AS (
    SELECT TranID, TranType, SPersonID, SplitRatio,
           IsOwnerSP, TxSPID
    FROM SplitRanked
    WHERE DuplicateRank = 1
),
SplitGroups AS (
    SELECT
        TranID, TranType,
        SUM(CAST(ISNULL(SplitRatio, 0) AS DECIMAL(19,4))) AS SumPercent,
        COUNT(*) AS PeopleCount
    FROM SplitClean
    GROUP BY TranID, TranType
)
SELECT
    b.TransactionCode AS [Transaction #],
    CONVERT(VARCHAR(2), MONTH(b.TransactionDate)) + '/' +
    CONVERT(VARCHAR(2), DAY(b.TransactionDate)) + '/' +
    CONVERT(VARCHAR(4), YEAR(b.TransactionDate)) AS [Transaction Date],
    MONTH(b.TransactionDate) AS [Transaction Month],
    YEAR(b.TransactionDate) AS [Transaction Year],
    CASE WHEN b.SourceKind = 'REPAIR JOB'
         THEN COALESCE(NULLIF(LTRIM(RTRIM(b.CodeOverride)), ''), 'ITEM')
         ELSE COALESCE(NULLIF(LTRIM(RTRIM(it.ItemCode)), ''),
                       NULLIF(LTRIM(RTRIM(b.CodeOverride)), ''), 'ITEM')
    END AS [Item #],
    CASE WHEN b.SourceKind = 'REPAIR JOB' THEN ''
         ELSE COALESCE(NULLIF(st.ItemStyleCode, ''), 'ITEM')
    END AS [Style #],
    COALESCE(NULLIF(b.LineDescription, ''),
             NULLIF(it.ItemName, ''), it.ItemDesc, '') AS [Description],
    COALESCE(NULLIF(it.IVendorModel, ''),
             NULLIF(st.VendorModel, '')) AS [VendorModel1],
    st.VendorCode AS [Vendor Name],
    CAST(ROUND(b.BaseQuantity * rate.Factor, 2)
         AS DECIMAL(19,2)) AS [Qty],
    CAST(b.ItemCost AS DECIMAL(19,2)) AS [Inventory Cost],
    CAST(ROUND(ABS(b.BaseUnitPrice * rate.Factor), 2)
         AS DECIMAL(19,2)) AS [Unit Price],
    CAST(ROUND(b.BaseUnitPrice * b.BaseQuantity * rate.Factor, 2)
         AS DECIMAL(19,2)) AS [Sales Amount],
    CAST(ROUND(b.BaseDiscount * rate.Factor, 2)
         AS DECIMAL(19,2)) AS [Disc Amt],
    CAST(ROUND((b.BaseUnitPrice * b.BaseQuantity - b.BaseDiscount)
               * rate.Factor, 2) AS DECIMAL(19,2)) AS [Total],
    w.WarehouseCode AS [Store],
    st.InvCat1ValueCode AS [Department],
    st.InvCat4ValueCode AS [Design],
    st.InvCat2ValueCode AS [Class],
    st.InvCat3ValueCode AS [Sub-Class],
    COALESCE(NULLIF(b.LineImagePath, ''), image1.ImagePath1)
        AS [Image Dir.],
    it.ItemAvgWeight AS [AvgWeight],
    CASE
        WHEN COALESCE(NULLIF(pay.Codes, ''),
                      NULLIF(hpc.PayCode, '')) IS NULL THEN ''
        ELSE COALESCE(NULLIF(pay.Codes, ''),
                      NULLIF(hpc.PayCode, '')) + ','
    END AS [Pay Codes],
    COALESCE(NULLIF(splitNames.AllNames, ''),
             CASE WHEN b.OwnerSPersonCode IS NOT NULL
                  THEN RTRIM(b.OwnerSPersonCode) + '/100% -'
             END, '') AS [Salespersons],
    LTRIM(RTRIM(
        COALESCE(sp.FirstName, owner.FirstName, '') + ' ' +
        COALESCE(sp.LastName, owner.LastName, '')
    )) AS [Salesperson Name]
FROM AllLines AS b
LEFT JOIN dbo.JM_Items AS it ON it.ItemID = b.ItemID
LEFT JOIN dbo.JM_ItemStyles AS st ON st.ItemStyleID = it.ItemStyleID
LEFT JOIN dbo.JM_Warehouses AS w ON w.WarehouseID = b.WarehouseID
LEFT JOIN dbo.JM_PayCodes AS hpc ON hpc.PayCodeID = b.HeaderPayCodeID
OUTER APPLY (
    SELECT TOP (1) g.TranType
    FROM SplitGroups AS g
    WHERE g.TranID = b.TransactionID
      AND (b.SourceKind <> 'INVOICE' OR g.TranType = 1)
    ORDER BY
        CASE WHEN ABS(g.SumPercent - 100.0) <= 0.01 THEN 0 ELSE 1 END,
        ABS(g.SumPercent - 100.0),
        CASE WHEN g.TranType = 1 THEN 0 ELSE 1 END,
        g.TranType
) AS selectedGroup
LEFT JOIN SplitClean AS sx
    ON sx.TranID = b.TransactionID
   AND sx.TranType = selectedGroup.TranType
LEFT JOIN dbo.JM_SalesPersons AS sp ON sp.SPersonID = sx.SPersonID
LEFT JOIN dbo.JM_SalesPersons AS owner
    ON owner.SPersonID = b.OwnerSPersonID
CROSS APPLY (
    SELECT CAST(ISNULL(sx.SplitRatio, 100.0) / 100.0
                AS DECIMAL(16,8)) AS Factor
) AS rate
OUTER APPLY (
    SELECT TOP (1) s.ImagePath1
    FROM dbo.JM_ItemSettings AS s
    WHERE s.ItemID = b.ItemID
    ORDER BY s.LastUpdate DESC, s.ISettingID
) AS image1
OUTER APPLY (
    SELECT STUFF((
        SELECT DISTINCT ',' + RTRIM(pc.PayCode)
        FROM dbo.JM_ARPaymentDetails AS pd
        JOIN dbo.JM_ARPayments AS p ON p.PaymentID = pd.PaymentID
        JOIN dbo.JM_PayCodes AS pc ON pc.PayCodeID = p.PayMethodID
        WHERE pd.InvoiceID = b.TransactionID
          AND pc.PayCode IS NOT NULL
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS Codes
) AS pay
OUTER APPLY (
    SELECT STUFF((
        SELECT ' - ' + RTRIM(sp2.SPersonCode) + '/' +
               REPLACE(CONVERT(VARCHAR(30),
                   CAST(sx2.SplitRatio AS DECIMAL(12,2))), '.00', '') + '%'
        FROM SplitClean AS sx2
        JOIN dbo.JM_SalesPersons AS sp2
            ON sp2.SPersonID = sx2.SPersonID
        WHERE sx2.TranID = b.TransactionID
          AND sx2.TranType = selectedGroup.TranType
        ORDER BY ISNULL(sx2.IsOwnerSP, 0) DESC, sp2.SPersonCode
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 3, '') + ' -' AS AllNames
) AS splitNames
ORDER BY
    b.TransactionDate DESC,
    b.TransactionCode,
    b.RowOrder,
    sp.SPersonCode,
    b.SourceDetailID
""";
}
