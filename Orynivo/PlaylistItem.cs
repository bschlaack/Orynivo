using System.IO;

namespace Orynivo;

/// <summary>
/// Represents a single track in the playback queue.
/// </summary>
/// <param name="FilePath">Absolute path to the audio file.</param>
/// <param name="Title">Optional display title for remote or virtual queue entries.</param>
/// <param name="Artist">Optional display artist for remote or virtual queue entries.</param>
/// <param name="Album">Optional display album for remote or virtual queue entries.</param>
/// <param name="Duration">Optional formatted duration for remote or virtual queue entries.</param>
/// <param name="Format">Optional format label for remote or virtual queue entries.</param>
/// <param name="KnownDuration">Optional authoritative duration for streamed queue entries.</param>
public sealed record PlaylistItem(
    string FilePath,
    string? Title = null,
    string? Artist = null,
    string? Album = null,
    string? Duration = null,
    string? Format = null,
    TimeSpan? KnownDuration = null)
{
    /// <summary>File name including extension.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Display title used when no local database metadata is available.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? Path.GetFileNameWithoutExtension(FilePath)
        : Title;

    /// <summary>Directory path of the file without a trailing separator.</summary>
    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;
}
