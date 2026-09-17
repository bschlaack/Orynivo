using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Local and Orynivo Server artist rename and merge flows.
/// </summary>
public partial class MainWindow : Window
{
    private async void EditArtistNameButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_artistInfoDisplayedRemoteRow is { } remoteRow)
        {
            await EditOrynivoArtistNameAsync(remoteRow);
            return;
        }

        if (_artistInfoDisplayedId is not long artistId)
            return;

        ArtistInfo? artist;
        using (var db = AudioDatabase.OpenDefault())
            artist = db.GetArtistById(artistId);
        if (artist is null)
            return;

        var editDialog = new EditArtistNameDialog(artist.Id, artist.Artist);
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        bool? preferCurrentArtistOnMerge = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artist.Id,
                artist.Artist,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            preferCurrentArtistOnMerge = mergeDialog.PreferredArtistId == artist.Id;

            try
            {
                result = await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return db.MergeArtists(
                        artist.Id,
                        matchingArtist.Id,
                        mergeDialog.PreferredArtistId,
                        editDialog.ArtistName);
                });
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        var remoteRenameSucceeded = await RenameMatchingOrynivoArtistsAsync(
            artist.Artist,
            result.ArtistName,
            preferCurrentArtistOnMerge);

        if (_currentArtistId == artistId || _currentArtistId == matchingArtistId)
        {
            _currentArtistId = result.ArtistId;
            _currentArtistName = result.ArtistName;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }
        if (_activeArtistFilterId == artistId || _activeArtistFilterId == matchingArtistId)
        {
            _activeArtistFilterId = result.ArtistId;
            _activeArtistFilterName = result.ArtistName;
        }

        _artistInfoDisplayedId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;
        await ReloadVisibleArtistListAsync(result.ArtistId);
        await ShowArtistInfoAsync(result.ArtistId, forceRefresh: false);
        _ = UpdateSearchIndexAfterArtistRenameAsync(result.ArtistId);
        if (!remoteRenameSucceeded)
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }
    }

    /// <summary>
    /// Renames every reachable Orynivo Server artist whose normalized identity matches the
    /// artist's previous local name. When the local rename required a merge, the same choice
    /// of surviving identity is applied to an equivalent collision on every server.
    /// </summary>
    /// <param name="previousArtistName">Display name used to find matching remote identities.</param>
    /// <param name="artistName">New display name to apply.</param>
    /// <param name="preferCurrentArtistOnMerge">
    /// <see langword="true"/> to retain the identity being renamed,
    /// <see langword="false"/> to retain the existing target-name identity, or
    /// <see langword="null"/> when no merge decision was made.
    /// </param>
    /// <returns><see langword="true"/> when every matching reachable identity was renamed.</returns>
    private async Task<bool> RenameMatchingOrynivoArtistsAsync(
        string? previousArtistName,
        string artistName,
        bool? preferCurrentArtistOnMerge = null)
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(previousArtistName);
        if (comparisonKey.Length == 0)
            return true;

        var succeeded = true;
        foreach (var server in _settings.OrynivoServers)
        {
            try
            {
                var artists = await _orynivoClient.GetArtistsAsync(server);
                foreach (var artist in artists.Where(candidate =>
                             ArtistNameNormalizer.CreateComparisonKey(candidate.Name) == comparisonKey))
                {
                    var response = await _orynivoClient.RenameArtistAsync(
                        server,
                        artist.Id,
                        artistName,
                        preferredArtistId: null);
                    if (response?.Result is null &&
                        response?.MatchingArtist is { } matchingArtist &&
                        preferCurrentArtistOnMerge is bool preferCurrent)
                    {
                        response = await _orynivoClient.RenameArtistAsync(
                            server,
                            artist.Id,
                            artistName,
                            preferCurrent ? artist.Id : matchingArtist.Id);
                    }
                    if (response?.Result is null)
                        succeeded = false;
                }
                DeleteOrynivoArtistListCache(server);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Unified remote artist rename");
                succeeded = false;
            }
        }

        return succeeded;
    }

    private async Task EditOrynivoArtistNameAsync(ContentRow row)
    {
        if (ResolveRowOrynivoServer(row) is not { } server ||
            row.Id is not long artistId ||
            row.EntityType != "OrynivoArtist")
        {
            return;
        }

        var editDialog = new EditArtistNameDialog(
            artistId,
            row.Title ?? string.Empty,
            (_, name) => CommitOrynivoArtistRenameAsync(server, artistId, name, preferredArtistId: null));
        if (await editDialog.ShowDialog<bool>(this) == false)
            return;

        var result = editDialog.Result;
        long? matchingArtistId = null;
        if (result is null && editDialog.MatchingArtist is { } matchingArtist)
        {
            matchingArtistId = matchingArtist.Id;
            var mergeDialog = new ArtistMergeDialog(
                artistId,
                row.Title ?? string.Empty,
                matchingArtist.Id,
                matchingArtist.Artist);
            if (await mergeDialog.ShowDialog<bool>(this) == false)
                return;

            try
            {
                (result, _) = await CommitOrynivoArtistRenameAsync(
                    server,
                    artistId,
                    editDialog.ArtistName,
                    mergeDialog.PreferredArtistId);
            }
            catch (Exception ex)
            {
                CrashLogger.Log(ex, "Remote artist merge");
                ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.ArtistRenameFailed;
                ArtistInfoStatusTextBlock.IsVisible = true;
                return;
            }
        }

        if (result is null)
            return;

        var previousArtistName = row.Title;
        var localRenameSucceeded = await RenameMatchingLocalArtistAsync(
            previousArtistName,
            result.ArtistName);
        var otherRemoteRenamesSucceeded = await RenameMatchingOrynivoArtistsAsync(
            previousArtistName,
            result.ArtistName);

        row.ArtistId = result.ArtistId;
        ArtistInfoTitleButton.Content = result.ArtistName;
        ArtistInfoStatusTextBlock.IsVisible = false;

        if (_currentOrynivoTrackRow is { } currentRemoteTrack &&
            (currentRemoteTrack.ArtistId == artistId || currentRemoteTrack.ArtistId == matchingArtistId))
        {
            currentRemoteTrack.ArtistId = result.ArtistId;
            NowPlayingArtistBlock.Text = result.ArtistName;
        }

        ContentRow detailRow;
        var refreshed = await _orynivoClient.GetArtistAsync(server, result.ArtistId);
        if (refreshed is not null)
        {
            detailRow = ToOrynivoArtistContentRow(server, refreshed);
        }
        else
        {
            detailRow = new ContentRow
            {
                Id = result.ArtistId,
                ArtistId = result.ArtistId,
                Title = result.ArtistName,
                EntityType = "OrynivoArtist",
                ExternalId = result.ArtistId.ToString(CultureInfo.InvariantCulture),
                OrynivoServer = server,
                FilePath = string.Empty
            };
        }

        if (_activeOrynivoView == "Artists")
            await LoadOrynivoViewAsync();
        await ShowOrynivoArtistInfoAsync(detailRow, forceRefresh: false);
        if (!localRenameSucceeded || !otherRemoteRenamesSucceeded)
        {
            ArtistInfoStatusTextBlock.Text = LocalizationManager.Current.OrynivoConnectionFailed;
            ArtistInfoStatusTextBlock.IsVisible = true;
        }
    }

    /// <summary>
    /// Renames the local artist whose normalized identity matches a remotely renamed artist.
    /// A target-name collision is left unresolved because a merge requires an explicit choice.
    /// </summary>
    /// <param name="previousArtistName">Display name used to find the local identity.</param>
    /// <param name="artistName">New display name to apply.</param>
    /// <returns><see langword="true"/> when no local match exists or the match was renamed.</returns>
    private static Task<bool> RenameMatchingLocalArtistAsync(
        string? previousArtistName,
        string artistName) => Task.Run(() =>
    {
        var comparisonKey = ArtistNameNormalizer.CreateComparisonKey(previousArtistName);
        if (comparisonKey.Length == 0)
            return true;

        using var db = AudioDatabase.OpenDefault();
        var localArtist = db.GetArtistsLite().FirstOrDefault(candidate =>
            ArtistNameNormalizer.CreateComparisonKey(candidate.Artist) == comparisonKey);
        if (localArtist is null)
            return true;
        if (db.FindArtistByName(artistName, localArtist.Id) is not null)
            return false;

        db.RenameArtist(localArtist.Id, artistName);
        TrackSearchIndex.UpdateMany(db.GetTracksForArtistSearchIndex(localArtist.Id));
        return true;
    });

    private async Task<(ArtistRenameResult? Result, ArtistInfo? MatchingArtist)> CommitOrynivoArtistRenameAsync(
        OrynivoServerSettings server,
        long artistId,
        string artistName,
        long? preferredArtistId)
    {
        var response = await _orynivoClient.RenameArtistAsync(
            server,
            artistId,
            artistName,
            preferredArtistId);
        if (response is null)
            throw new InvalidOperationException("The remote artist could not be renamed.");

        return (response.Result, response.MatchingArtist is null ? null : ToArtistInfo(response.MatchingArtist));
    }
}
