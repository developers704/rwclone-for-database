using System.Text.RegularExpressions;

namespace VallianiDataSync.Core.Security;

public static class PathSanitizer
{
    private static readonly Regex Dangerous = new(@"(\.\.[\\/])", RegexOptions.Compiled);

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path.Trim());
        if (Dangerous.IsMatch(path))
            throw new InvalidOperationException("Path traversal is not allowed.");

        return full;
    }

    public static bool IsUnderRoot(string filePath, string rootFolder)
    {
        var file = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        return file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               || string.Equals(file, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
