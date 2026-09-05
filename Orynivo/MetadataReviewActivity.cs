using System.Diagnostics;
using System.Globalization;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Coalesces worker progress and formats honest phase-local time estimates for review surfaces.</summary>
internal sealed class MetadataReviewActivity : IProgress<MetadataReviewProgress>
{
    private readonly object _gate = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Stopwatch _phaseElapsed = Stopwatch.StartNew();
    private MetadataReviewProgress _current = new("database", 0, 0);

    /// <summary>Records the latest phase without dispatching one UI event per track or folder.</summary>
    /// <param name="value">Measured progress without private paths.</param>
    public void Report(MetadataReviewProgress value)
    {
        lock (_gate)
        {
            if (_current.Phase != value.Phase)
                _phaseElapsed.Restart();
            _current = value;
        }
    }

    /// <summary>Gets a localized status and optional percentage for the current phase.</summary>
    /// <returns>Status text and percentage; null means the total is unknown.</returns>
    internal (string Text, double? Percent) Snapshot()
    {
        lock (_gate)
        {
            var s = LocalizationManager.Current;
            var phase = _current.Phase switch
            {
                "folders" => s.MetadataPhaseFolders,
                "hashes" => s.MetadataPhaseHashes,
                "servers" => s.MetadataPhaseServers,
                "search" => s.MetadataSearching,
                "releases" => s.MetadataPhaseReleases,
                "saving" => s.MetadataPhaseSaving,
                _ => s.MetadataPhaseDatabase
            };
            var remaining = _current.Total > 0 && _current.Completed > 0 && _phaseElapsed.Elapsed.TotalSeconds >= 1
                ? string.Format(CultureInfo.CurrentCulture, s.MetadataRemaining,
                    FormatTime(TimeSpan.FromSeconds(_phaseElapsed.Elapsed.TotalSeconds *
                        Math.Max(0, _current.Total - _current.Completed) / _current.Completed)))
                : s.MetadataRemainingUnknown;
            var count = _current.Total > 0 ? $" · {_current.Completed:N0} / {_current.Total:N0}" : "";
            return ($"{phase}{count}\n{string.Format(CultureInfo.CurrentCulture, s.MetadataElapsed, FormatTime(_elapsed.Elapsed))} · {remaining}",
                _current.Total > 0 ? 100d * _current.Completed / _current.Total : null);
        }
    }

    private static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
