namespace Orynivo;

/// <summary>Groups one source's ordered category rows without registering remote playback state.</summary>
/// <param name="Tracks">Score-ordered tracks.</param>
/// <param name="Albums">Score-ordered albums.</param>
/// <param name="Artists">Score-ordered artists.</param>
internal sealed record LibrarySearchRows(
    IReadOnlyList<ContentRow> Tracks,
    IReadOnlyList<ContentRow> Albums,
    IReadOnlyList<ContentRow> Artists);
