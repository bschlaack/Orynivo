using Orynivo.Library;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// One provider-local record that contributes to a logically coalesced album. It lives
/// outside the main window so XAML can name it in an <c>x:DataType</c> directive, which
/// compiled bindings require (a nested type cannot be referenced from XAML).
/// </summary>
/// <param name="AlbumId">Provider-local album identifier.</param>
/// <param name="ArtistId">Provider-local primary artist identifier, when known.</param>
/// <param name="Server">Owning remote server, or <see langword="null"/> for the local library.</param>
internal sealed record LogicalAlbumPart(
    long AlbumId,
    long? ArtistId,
    OrynivoServerSettings? Server);
