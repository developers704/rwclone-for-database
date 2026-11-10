namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// JewelMate payment report. Placeholders {{StartDate}} / {{EndDate}} (both inclusive;
/// SQL uses PostedDate &lt; EndDate+1). NULLs exported as blank strings.
/// </summary>
public static class CaratonePaymentSql
{
    public const string StartDateToken = "{{StartDate}}";
    public const string EndDateToken = "{{EndDate}}";

    public static readonly string[] Headers =
    {
        "Store", "Customer #", "Customer Name", "Transaction #", "Transaction Date",
        "Type", "Pymt Create Date", "Pymt Posted Date", "Pay Method", "Pay Code",
        "Check Card # GC #", "Card Holder", "Payment Amt", "Applied Amt", "SplitPercentage"
    };

    public static string ApplyDates(string sql, DateTime fromInclusive, DateTime toInclusive)
    {
        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        if (to < from)
            (from, to) = (to, from);
        return sql
            .Replace(StartDateToken, from.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
            .Replace(EndDateToken, to.ToString("yyyy-MM-dd"), StringComparison.Ordinal);
    }

    public const string Template = """
DECLARE @StartDate DATE = CONVERT(date, '{{StartDate}}', 23);
DECLARE @EndDate DATE = CONVERT(date, '{{EndDate}}', 23);

WITH ActiveAllocations AS
(
    SELECT
        d.PaymentID,
        d.PayInvID,
        d.InvoiceID,
        d.AppliedAmt,
        d.ACPercentage,
        d.TAppType
    FROM dbo.JM_ARPaymentDetails d
    WHERE d.Status = 1
),
ReportRows AS
(
    SELECT
        p.PaymentID,
        p.ReceiptCode,

        s.SiteCode AS [Store],
        c.CustomerCode AS [Customer #],
        c.CustomerName AS [Customer Name],

        COALESCE(
            i.InvoiceCode,
            l.LayawayCode,
            r.RepairCode
        ) AS [Transaction #],

        COALESCE(
            i.InvoiceDate,
            l.LayawayDate,
            r.RepairDate
        ) AS [Transaction Date],

        CASE
            WHEN i.InvoiceID IS NOT NULL THEN 'Sales'
            WHEN l.LayawayID IS NOT NULL THEN 'OL'
            WHEN r.RepairID IS NOT NULL THEN 'OR'
            ELSE NULL
        END AS [Type],

        p.CurrDate AS [Pymt Create Date],
        p.PostedDate AS [Pymt Posted Date],

        CASE pc.PayCatID
            WHEN 1 THEN 'CA'
            WHEN 2 THEN 'CHK'
            WHEN 3 THEN 'CC'
            WHEN 5 THEN 'OTH'
            WHEN 10 THEN 'GC'
            ELSE cat.PayCat
        END AS [Pay Method],

        pc.PayCode AS [Pay Code],

        COALESCE(
            NULLIF(p.ChkCardNum, ''),
            NULLIF(p.ExternalGiftCardNumber, '')
        ) AS [Check Card # GC #],

        p.CardHolder AS [Card Holder],

        p.PayAmount,
        a.AppliedAmt,
        a.ACPercentage,
        a.PayInvID,

        ROW_NUMBER() OVER
        (
            PARTITION BY p.PaymentID
            ORDER BY a.PayInvID
        ) AS PaymentRowNumber

    FROM dbo.JM_ARPayments p

    LEFT JOIN dbo.JM_CompanySites s
        ON p.SiteID = s.SiteID

    LEFT JOIN dbo.JM_Customers c
        ON p.CustomerID = c.CustomerID

    LEFT JOIN ActiveAllocations a
        ON p.PaymentID = a.PaymentID

    LEFT JOIN dbo.JM_Invoices i
        ON a.InvoiceID = i.InvoiceID

    LEFT JOIN dbo.JM_Layaways l
        ON a.InvoiceID = l.LayawayID

    LEFT JOIN dbo.JM_Repairs r
        ON a.InvoiceID = r.RepairID

    LEFT JOIN dbo.JM_PayCodes pc
        ON p.PayMethodID = pc.PayCodeID

    LEFT JOIN dbo.JM_PayCats cat
        ON pc.PayCatID = cat.PayCatID

    WHERE p.PostedDate >= @StartDate
      AND p.PostedDate < DATEADD(DAY, 1, @EndDate)
      AND p.Status = 1
      AND p.PayMethodID IS NOT NULL
)

SELECT
    COALESCE(CONVERT(NVARCHAR(MAX), [Store]), N'')
        AS [Store],

    COALESCE(CONVERT(NVARCHAR(MAX), [Customer #]), N'')
        AS [Customer #],

    COALESCE(CONVERT(NVARCHAR(MAX), [Customer Name]), N'')
        AS [Customer Name],

    COALESCE(CONVERT(NVARCHAR(MAX), [Transaction #]), N'')
        AS [Transaction #],

    COALESCE(
        CONVERT(VARCHAR(10), [Transaction Date], 23),
        ''
    ) AS [Transaction Date],

    COALESCE([Type], '')
        AS [Type],

    COALESCE(
        CONVERT(VARCHAR(10), [Pymt Create Date], 23),
        ''
    ) AS [Pymt Create Date],

    COALESCE(
        CONVERT(VARCHAR(10), [Pymt Posted Date], 23),
        ''
    ) AS [Pymt Posted Date],

    COALESCE(CONVERT(NVARCHAR(MAX), [Pay Method]), N'')
        AS [Pay Method],

    COALESCE(CONVERT(NVARCHAR(MAX), [Pay Code]), N'')
        AS [Pay Code],

    COALESCE(CONVERT(NVARCHAR(MAX), [Check Card # GC #]), N'')
        AS [Check Card # GC #],

    COALESCE(CONVERT(NVARCHAR(MAX), [Card Holder]), N'')
        AS [Card Holder],

    COALESCE(
        CONVERT(
            VARCHAR(50),
            CASE
                WHEN PaymentRowNumber = 1 THEN PayAmount
                ELSE 0
            END
        ),
        ''
    ) AS [Payment Amt],

    COALESCE(
        CONVERT(VARCHAR(50), AppliedAmt),
        ''
    ) AS [Applied Amt],

    COALESCE(
        CONVERT(VARCHAR(50), ACPercentage),
        ''
    ) AS [SplitPercentage]

FROM ReportRows

ORDER BY
    [Store],
    [Pymt Posted Date],
    ReceiptCode,
    PaymentRowNumber
""";
}
