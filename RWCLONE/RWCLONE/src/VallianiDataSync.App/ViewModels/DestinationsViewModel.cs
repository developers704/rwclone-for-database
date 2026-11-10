using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Infrastructure.Destinations;

namespace VallianiDataSync.App.ViewModels;

public partial class FormatFieldRow : ObservableObject
{
    [ObservableProperty] private string _sourceField = "Sku";
    [ObservableProperty] private string _destinationKey = "sku";
}

public partial class DestinationsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IDestinationSyncService _sync;
    private readonly IOtpAuthService _otp;

    public ObservableCollection<DestinationProject> Projects { get; } = new();
    public ObservableCollection<FormatFieldRow> FormatRows { get; } = new();
    public ObservableCollection<string> SourceFields { get; } = new();
    public IReadOnlyList<string> BodyFormats { get; } = new[] { "JsonArray", "Csv" };

    [ObservableProperty] private DestinationProject? _selectedProject;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _sensitiveOtp1 = string.Empty;
    [ObservableProperty] private string _sensitiveOtp2 = string.Empty;
    [ObservableProperty] private string _formatFileHint = "Format is empty — upload a format file first (CSV/JSON). Sync will only send those fields.";

    public DestinationsViewModel(
        ISettingsService settings,
        ICredentialStore credentials,
        IDestinationSyncService sync,
        IOtpAuthService otp)
    {
        _settings = settings;
        _credentials = credentials;
        _sync = sync;
        _otp = otp;
        Load();
    }

    public void Load()
    {
        RefreshSourceFields();
        Projects.Clear();
        foreach (var p in _settings.Current.Destinations)
            Projects.Add(p);
        if (SelectedProject is null && Projects.Count > 0)
            SelectedProject = Projects[0];
        else
            LoadSelected();
    }

    private void RefreshSourceFields()
    {
        SourceFields.Clear();
        foreach (var f in ProductFormatExporter.GetAvailableSourceFields(_settings.Current.Mapping.DiscoveredColumns))
            SourceFields.Add(f);
    }

    partial void OnSelectedProjectChanged(DestinationProject? value) => LoadSelected();

    private void LoadSelected()
    {
        FormatRows.Clear();
        ApiKey = string.Empty;
        if (SelectedProject is null)
        {
            FormatFileHint = "Select a project.";
            return;
        }
        foreach (var f in SelectedProject.FormatFields)
            FormatRows.Add(new FormatFieldRow { SourceField = f.SourceField, DestinationKey = f.DestinationKey });
        FormatFileHint = string.IsNullOrWhiteSpace(SelectedProject.FormatTemplateFileName)
            ? (FormatRows.Count == 0
                ? "Format is empty — upload a format (CSV header / JSON sample). Only those keys will sync."
                : $"{FormatRows.Count} fields. Only these will be sent to the destination — nothing else.")
            : $"Template: {SelectedProject.FormatTemplateFileName} ({FormatRows.Count} fields). Sync uses exactly these keys.";
        StatusMessage = _credentials.HasSecret(CredentialKeys.DestinationApiKey(SelectedProject.Id))
            ? "API key on file (DPAPI)."
            : "No API key saved yet.";
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (!await EnsureOtpIfNeededAsync("Admin: create destination").ConfigureAwait(true))
            return;

        var project = new DestinationProject
        {
            Name = $"Project {Projects.Count + 1}",
            IntervalMinutes = 120,
            FormatFields = new List<DestinationFormatField>()
        };
        _settings.Update(s => s.Destinations.Add(project));
        await _settings.SaveAsync();
        Projects.Add(project);
        SelectedProject = project;
        StatusMessage = "Project created (empty format). Next: Upload format → map sources → set API → Enable → Save.";
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        if (SelectedProject is null)
            return;
        if (!await EnsureOtpIfNeededAsync("Admin: delete destination").ConfigureAwait(true))
            return;

        var id = SelectedProject.Id;
        _credentials.DeleteSecret(CredentialKeys.DestinationApiKey(id));
        _settings.Update(s => s.Destinations.RemoveAll(p => p.Id == id));
        await _settings.SaveAsync();
        Projects.Remove(SelectedProject);
        SelectedProject = Projects.FirstOrDefault();
        StatusMessage = "Project deleted.";
    }

    [RelayCommand]
    private async Task ImportFormatAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Select or create a project first.";
            return;
        }
        if (!await EnsureOtpIfNeededAsync("Admin: upload / change format").ConfigureAwait(true))
            return;

        var dlg = new OpenFileDialog
        {
            Title = "Upload destination format",
            Filter = "Format files|*.json;*.csv;*.tsv;*.txt|JSON|*.json|CSV|*.csv|All|*.*"
        };
        if (dlg.ShowDialog() != true)
            return;

        var (ok, message, fields, bodyHint) = FormatTemplateImporter.Import(dlg.FileName);
        if (!ok)
        {
            StatusMessage = message;
            return;
        }

        var raw = await File.ReadAllTextAsync(dlg.FileName);
        FormatRows.Clear();
        foreach (var f in fields)
            FormatRows.Add(new FormatFieldRow { SourceField = f.SourceField, DestinationKey = f.DestinationKey });

        SelectedProject.BodyFormat = bodyHint;
        SelectedProject.FormatTemplateFileName = Path.GetFileName(dlg.FileName);
        SelectedProject.FormatTemplateRaw = raw.Length > 20000 ? raw[..20000] : raw;
        FormatFileHint = $"{message} File: {SelectedProject.FormatTemplateFileName}. Check the source field for each destination key, then Save.";
        StatusMessage = message + " Verify source mapping, then click Save.";
    }

    [RelayCommand]
    private void AddFormatField()
    {
        FormatRows.Add(new FormatFieldRow { SourceField = "Sku", DestinationKey = "field" });
    }

    [RelayCommand]
    private void DeleteFormatField(FormatFieldRow? row)
    {
        if (row is not null)
            FormatRows.Remove(row);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Select a project first.";
            return;
        }

        if (!await EnsureOtpIfNeededAsync("Admin: save destination / format / API").ConfigureAwait(true))
            return;

        if (FormatRows.Count == 0 && !MultiTableSyncEnabled)
        {
            StatusMessage = "Format is empty. Upload a format, or enable Sync all tables on Data Mapping.";
            return;
        }

        var id = SelectedProject.Id;
        _settings.Update(s =>
        {
            var p = s.Destinations.FirstOrDefault(x => x.Id == id);
            if (p is null)
                return;
            p.Name = SelectedProject.Name.Trim();
            p.Enabled = SelectedProject.Enabled;
            p.AutoSyncEnabled = SelectedProject.AutoSyncEnabled;
            p.IntervalMinutes = Math.Max(15, SelectedProject.IntervalMinutes);
            p.ApiUrl = SelectedProject.ApiUrl.Trim();
            p.HttpMethod = "POST";
            p.BodyFormat = string.IsNullOrWhiteSpace(SelectedProject.BodyFormat) ? "JsonArray" : SelectedProject.BodyFormat;
            p.AuthHeaderName = "Authorization";
            p.AuthHeaderPrefix = "Bearer ";
            p.FormatTemplateFileName = SelectedProject.FormatTemplateFileName;
            p.FormatTemplateRaw = SelectedProject.FormatTemplateRaw;
            p.FormatFields = FormatRows
                .Where(r => !string.IsNullOrWhiteSpace(r.DestinationKey) && !string.IsNullOrWhiteSpace(r.SourceField))
                .Select(r => new DestinationFormatField
                {
                    SourceField = r.SourceField.Trim(),
                    DestinationKey = r.DestinationKey.Trim()
                })
                .ToList();
        });

        if (!string.IsNullOrWhiteSpace(ApiKey))
            _credentials.SaveSecret(CredentialKeys.DestinationApiKey(id), ApiKey);

        await _settings.SaveAsync();
        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == id);
        StatusMessage = $"Saved. Sync will send only these {FormatRows.Count} format fields — exact keys only.";
    }

    private bool MultiTableSyncEnabled =>
        _settings.Current.Mapping.SyncAllTables || _settings.Current.Mapping.SelectedTables.Count > 0;

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (SelectedProject is null)
            return;
        if (!MultiTableSyncEnabled && FormatRows.Count == 0)
        {
            StatusMessage = "Format missing. Upload a format, or enable Sync all tables on Data Mapping.";
            return;
        }
        StatusMessage = MultiTableSyncEnabled ? "Syncing all selected tables..." : "Syncing (exact format only)...";
        var (ok, message, sent, skipped) = await _sync.SyncProjectAsync(SelectedProject.Id, fullSync: false);
        StatusMessage = ok ? $"{message} (sent={sent}, skipped={skipped})" : $"Failed: {message}";
        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == SelectedProject.Id);
    }

    [RelayCommand]
    private async Task FullSyncAsync()
    {
        if (SelectedProject is null)
            return;
        if (!MultiTableSyncEnabled && FormatRows.Count == 0)
        {
            StatusMessage = "Format missing. Upload a format, or enable Sync all tables on Data Mapping.";
            return;
        }
        StatusMessage = MultiTableSyncEnabled ? "Full sync all selected tables..." : "Full sync (exact format only)...";
        var (ok, message, sent, skipped) = await _sync.SyncProjectAsync(SelectedProject.Id, fullSync: true);
        StatusMessage = ok ? $"{message} (sent={sent}, skipped={skipped})" : $"Failed: {message}";
        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == SelectedProject.Id);
    }

    [RelayCommand]
    private async Task SendSensitiveOtpAsync()
    {
        var (ok, message) = await _otp.SendSensitiveOtpsAsync("Admin destination change");
        StatusMessage = message;
    }

    private async Task<bool> EnsureOtpIfNeededAsync(string reason)
    {
        if (!_settings.Current.Security.OtpEnabled)
            return true;

        if (string.IsNullOrWhiteSpace(SensitiveOtp1) || string.IsNullOrWhiteSpace(SensitiveOtp2))
        {
            StatusMessage = $"{reason}: click Send OTP, enter both email codes, then try the action again.";
            MessageBox.Show(StatusMessage, "Admin OTP required", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var (ok, message) = await _otp.VerifySensitiveOtpsAsync(SensitiveOtp1, SensitiveOtp2);
        StatusMessage = message;
        SensitiveOtp1 = string.Empty;
        SensitiveOtp2 = string.Empty;
        if (!ok)
            MessageBox.Show(message, "Admin OTP failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        return ok;
    }
}
