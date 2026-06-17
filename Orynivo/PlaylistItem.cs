using System.IO;

namespace Orynivo;

/// <summary>
/// Represents a single track in the playback queue.
/// </summary>
/// <param name="FilePath">Absolute path to the audio file.</param>
public sealed record PlaylistItem(string FilePath)
{
    /// <summary>File name including extension.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Directory path of the file without a trailing separator.</summary>
    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;
}
