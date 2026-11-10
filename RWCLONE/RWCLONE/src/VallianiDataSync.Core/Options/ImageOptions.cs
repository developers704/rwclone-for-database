namespace VallianiDataSync.Core.Options;

public sealed class ImageOptions
{
    public List<string> RootFolders { get; set; } = new();
    public bool UseChecksum { get; set; } = true;
    public bool UseModifiedTimestamp { get; set; } = true;
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];
}
