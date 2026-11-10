using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VallianiDataSync.Core.Models;

public sealed class DestinationFormatField
{
    public string SourceField { get; set; } = string.Empty;
    public string DestinationKey { get; set; } = string.Empty;
    /// <summary>When false, field is shown in UI but not sent on sync.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class DestinationProject : INotifyPropertyChanged
{
    private string? _lastSyncMessage;
    private DateTime? _lastSyncUtc;
    private DateTime? _nextSyncLocal;
    private bool _saleDateIsRange;
    private DateTime? _saleReportDateFrom;
    private DateTime? _saleReportDateTo;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New project";
    public bool Enabled { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 120;
    public string ApiUrl { get; set; } = string.Empty;
    /// <summary>Optional GET URL that returns field schema/sample when import URL has no schema.</summary>
    public string? FieldsUrl { get; set; }
    public string HttpMethod { get; set; } = "POST";
    /// <summary>JsonArray or Csv</summary>
    public string BodyFormat { get; set; } = "JsonArray";
    public string AuthHeaderName { get; set; } = "Authorization";
    /// <summary>Optional prefix e.g. "Bearer "</summary>
    public string AuthHeaderPrefix { get; set; } = "Bearer ";
    public List<DestinationFormatField> FormatFields { get; set; } = new();
    /// <summary>
    /// Fixed sources this project pushes: sales, inventory, payments.
    /// Empty = all three fixed sources.
    /// </summary>
    public List<string> SyncSources { get; set; } = new();
    /// <summary>Original uploaded template file name (reference only).</summary>
    public string? FormatTemplateFileName { get; set; }
    /// <summary>Optional raw template text saved for audit.</summary>
    public string? FormatTemplateRaw { get; set; }

    /// <summary>False = single day (From only). True = inclusive From→To range.</summary>
    public bool SaleDateIsRange
    {
        get => _saleDateIsRange;
        set => SetField(ref _saleDateIsRange, value);
    }

    /// <summary>caratone-sale inclusive start (required when that source is selected).</summary>
    public DateTime? SaleReportDateFrom
    {
        get => _saleReportDateFrom;
        set => SetField(ref _saleReportDateFrom, value);
    }

    /// <summary>caratone-sale inclusive end (used when SaleDateIsRange; else = From).</summary>
    public DateTime? SaleReportDateTo
    {
        get => _saleReportDateTo;
        set => SetField(ref _saleReportDateTo, value);
    }

    public DateTime? LastSyncUtc
    {
        get => _lastSyncUtc;
        set => SetField(ref _lastSyncUtc, value);
    }

    public string? LastSyncMessage
    {
        get => _lastSyncMessage;
        set => SetField(ref _lastSyncMessage, value);
    }

    public DateTime? NextSyncLocal
    {
        get => _nextSyncLocal;
        set => SetField(ref _nextSyncLocal, value);
    }

    /// <summary>
    /// Inclusive from/to for caratone-sale / caratone-payment.
    /// Missing From defaults to PC today (daily sync).
    /// </summary>
    public bool TryGetSaleReportDates(out DateTime fromInclusive, out DateTime toInclusive)
    {
        fromInclusive = (SaleReportDateFrom ?? DateTime.Today).Date;
        toInclusive = SaleDateIsRange && SaleReportDateTo is not null
            ? SaleReportDateTo.Value.Date
            : fromInclusive;
        if (toInclusive < fromInclusive)
            (fromInclusive, toInclusive) = (toInclusive, fromInclusive);
        return true;
    }

    /// <summary>Pin caratone dates to PC today when unset (daily default).</summary>
    public void EnsureCaratoneDatesDefaultToday()
    {
        if (SaleReportDateFrom is null)
            SaleReportDateFrom = DateTime.Today;
        if (!SaleDateIsRange)
            SaleReportDateTo = SaleReportDateFrom;
        else if (SaleReportDateTo is null)
            SaleReportDateTo = SaleReportDateFrom;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
