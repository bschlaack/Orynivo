using System.IO;

namespace Player;

public sealed record PlaylistItem(string FilePath)
{
    public string FileName => Path.GetFileName(FilePath);
    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;
}
