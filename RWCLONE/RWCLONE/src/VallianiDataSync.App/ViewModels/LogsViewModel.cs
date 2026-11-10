using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.App.ViewModels;

public partial class LogsViewModel : ObservableObject
{
    private readonly ISyncStateStore _state;

    public ObservableCollection<SyncLogEntry> Entries { get; } = new();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _storeFilter = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public LogsViewModel(ISyncStateStore state) => _state = state;

    public async Task LoadAsync()
    {
        var logs = await _state.QueryLogsAsync(
            string.IsNullOrWhiteSpace(Search) ? null : Search,
            string.IsNullOrWhiteSpace(StoreFilter) ? null : StoreFilter,
            null,
            500);
        Entries.Clear();
        foreach (var log in logs)
            Entries.Add(log);
    }

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV files|*.csv",
            FileName = $"valliani-logs-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog() != true)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,Level,Store,Sku,Operation,ApiEndpoint,Response,Error,Message");
        foreach (var e in Entries)
        {
            sb.AppendLine(string.Join(',',
                Csv(e.TimestampUtc.ToString("O")),
                Csv(e.Level.ToString()),
                Csv(e.StoreCode),
                Csv(e.Sku),
                Csv(e.Operation),
                Csv(e.ApiEndpoint),
                Csv(e.Response),
                Csv(e.Error),
                Csv(e.Message)));
        }
        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
        StatusMessage = $"Exported {Entries.Count} rows.";
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
