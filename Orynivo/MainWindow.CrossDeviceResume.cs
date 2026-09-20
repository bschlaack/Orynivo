using System.Globalization;
using Avalonia.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// Cross-device resume for remote Orynivo Server tracks. The last position is
/// stored per profile and track on the owning server through the authenticated
/// position endpoint, so no credential-bearing URL is ever persisted. The pure
/// <see cref="CrossDeviceResume"/> helper decides whether a stored position is
/// worth offering here.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan RemotePositionPublishInterval = TimeSpan.FromSeconds(20);

    private DateTimeOffset _lastRemotePositionPublishedAt = DateTimeOffset.MinValue;
    private double _lastRemotePositionPublished = -1;
    private double? _pendingRemoteResumeSeconds;

    /// <summary>
    /// Publishes the current position of a playing remote track to its owning server.
    /// Publishing is throttled and best effort: a failure never affects playback.
    /// </summary>
    /// <param name="position">Audible position of the current track.</param>
    private void PublishRemoteTrackPosition(TimeSpan position)
    {
        if (_currentOrynivoTrackRow is not { OrynivoServer: { } server, Id: long trackId })
        {
            _lastRemotePositionPublishedAt = DateTimeOffset.MinValue;
            _lastRemotePositionPublished = -1;
            ClearRemoteResumePrompt();
            return;
        }

        if (_player is null || position <= TimeSpan.Zero)
            return;
        if (DateTimeOffset.UtcNow - _lastRemotePositionPublishedAt < RemotePositionPublishInterval)
            return;

        var seconds = position.TotalSeconds;
        if (Math.Abs(seconds - _lastRemotePositionPublished) < 1)
            return;

        _lastRemotePositionPublishedAt = DateTimeOffset.UtcNow;
        _lastRemotePositionPublished = seconds;
        _ = _orynivoClient
            .SaveTrackPositionAsync(server, trackId, seconds)
            .ContinueWith(static _ => { }, TaskScheduler.Default);
    }

    /// <summary>
    /// Offers the stored position of a remote track when another device left off
    /// meaningfully later than the position this device would start at.
    /// </summary>
    /// <param name="row">Remote track about to play.</param>
    /// <param name="localSeconds">Position this device starts at.</param>
    private async Task OfferRemoteResumeAsync(ContentRow row, double localSeconds)
    {
        ClearRemoteResumePrompt();
        if (row is not { OrynivoServer: { } server, Id: long trackId })
            return;

        double? remoteSeconds;
        try
        {
            remoteSeconds = await _orynivoClient.GetTrackPositionAsync(server, trackId);
        }
        catch
        {
            return;
        }

        if (remoteSeconds is not double stored || !ReferenceEquals(_currentOrynivoTrackRow, row))
            return;

        var durationSeconds = _player?.Duration.TotalSeconds ?? 0;
        if (!CrossDeviceResume.ShouldOffer(stored, durationSeconds, localSeconds))
            return;

        _pendingRemoteResumeSeconds = CrossDeviceResume.Normalize(stored, durationSeconds);
        ResumePositionButton.IsVisible = true;
        ToolTip.SetTip(
            ResumePositionButton,
            string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.ResumeOnThisDeviceTooltip,
                FormatTime(TimeSpan.FromSeconds(_pendingRemoteResumeSeconds.Value))));
    }

    /// <summary>Hides the resume prompt and forgets the offered position.</summary>
    private void ClearRemoteResumePrompt()
    {
        _pendingRemoteResumeSeconds = null;
        ResumePositionButton.IsVisible = false;
    }

    private void ResumePositionButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_pendingRemoteResumeSeconds is not double position || _player is null)
        {
            ClearRemoteResumePrompt();
            return;
        }

        var target = Math.Min(position, Math.Max(0, _player.Duration.TotalSeconds));
        ClearRemoteResumePrompt();
        if (_player is { } player)
            _ = player.SeekAsync(TimeSpan.FromSeconds(target));
        StatusTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.ResumeOnThisDeviceHint,
            FormatTime(TimeSpan.FromSeconds(target)));
    }
}
