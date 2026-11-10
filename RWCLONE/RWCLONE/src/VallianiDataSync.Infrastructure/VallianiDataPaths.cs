namespace VallianiDataSync.Infrastructure;

/// <summary>
/// Resolves where settings / secrets / SQLite live.
/// Desktop: %LocalAppData%\VallianiDataSync
/// Web service: %ProgramData%\VallianiDataSync (C:\ProgramData\...)
/// </summary>
public static class VallianiDataPaths
{
    public static string DefaultDesktopRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VallianiDataSync");

    public static string DefaultServiceRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VallianiDataSync");

    public static string EnsureRoot(string? dataRoot)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot) ? DefaultDesktopRoot : dataRoot.Trim();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "secrets"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        return root;
    }
}
