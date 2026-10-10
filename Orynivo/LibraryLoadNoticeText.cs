using System.Globalization;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Formats an incomplete catalog notice without exposing source identities or errors.</summary>
internal static class LibraryLoadNoticeText
{
    /// <summary>Returns localized failure text, or no notice for complete and cancelled loads.</summary>
    /// <param name="status">Aggregate catalog outcome.</param>
    /// <param name="unavailableCount">Number of failed or timed-out sources.</param>
    /// <param name="strings">Current interface language.</param>
    /// <returns>A localized notice, or an empty string when no notice is needed.</returns>
    internal static string Format(LibraryLoadStatus status, int unavailableCount, LocalizedStrings strings) =>
        status switch
        {
            LibraryLoadStatus.Partial => string.Format(CultureInfo.CurrentCulture,
                strings.LibraryLoadPartial, unavailableCount),
            LibraryLoadStatus.Failed => string.Format(CultureInfo.CurrentCulture,
                strings.LibraryLoadFailed, unavailableCount),
            _ => string.Empty
        };
}
