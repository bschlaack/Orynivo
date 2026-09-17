namespace Orynivo.Library;

/// <summary>
/// Distinguishes byte-identical duplicate files from fingerprint/size matches
/// that could not be confirmed by hashing.
/// </summary>
public enum LibraryDuplicateKind
{
    /// <summary>The files have identical SHA-256 content.</summary>
    Exact,

    /// <summary>
    /// The files share an AcoustID fingerprint and size but could not be hashed,
    /// so they may still be duplicates.
    /// </summary>
    Likely
}

/// <summary>One physical file inside a duplicate group.</summary>
/// <param name="Path">Absolute physical source path.</param>
/// <param name="FileSize">Physical size in bytes, when known.</param>
public sealed record LibraryDuplicateFile(string Path, long? FileSize);

/// <summary>
/// A set of physical files that appear to be duplicates of one another. The
/// library never removes or merges anything automatically; a group is only a
/// review proposal.
/// </summary>
/// <param name="Kind">Evidence strength for the group.</param>
/// <param name="Files">The duplicate files, at least two.</param>
public sealed record LibraryDuplicateGroup(
    LibraryDuplicateKind Kind,
    IReadOnlyList<LibraryDuplicateFile> Files);
