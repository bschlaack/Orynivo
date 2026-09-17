# Orynivo Feature Roadmap

A resumable work plan for the agreed feature set. Each item is a **Hauptpunkt**
that ends with its own commit. Work top to bottom unless a priority changes.

## How to resume

1. Read this file and the item's `Status`.
2. Follow the repository completion checklist in `AGENTS.md`
   (build, tests, `CHANGELOG.md`, `README.md`, the applicable nested `AGENTS.md`,
   XML docs, seven-language localization).
3. Run all three test projects before finishing an item:
   `dotnet test Orynivo.Core.Tests/Orynivo.Core.Tests.csproj`,
   `dotnet test Orynivo.Tests/Orynivo.Tests.csproj`,
   `dotnet test Orynivo.Server.Tests/Orynivo.Server.Tests.csproj`.
4. Mark the item `Done` and add its commit hash.

Status legend: `Todo` · `In progress` · `Done` · `Blocked`.

## Explicitly out of scope

- Third-party music-service imports (Spotify/Apple Music/…): unclear licensing
  and ToS; the Qobuz scaffold stays inactive.
- Automatic duplicate deletion or automatic metadata merging: violates the
  documented library invariants. Any such action stays user-confirmed.

---

## 1. Last.fm scrobbling — `Done`

Highest-value missing standard feature. Additive: it never touches the audio
path, so it cannot regress playback.

Steps:

- 1a Core scrobbling layer — `Done` (`LastFmSignature`, `ScrobbleRules`,
  `LastFmClient`, `LastFmTrack`, `LastFmSession`; 15 tests).
- 1b Desktop service, settings/credential plumbing, offline queue, and playback
  hooks — `Done` (`LastFmScrobblingService`, `PendingScrobbleStore`; 6 tests).
- 1c Settings UI (enable toggle, API key/secret fields, two-step Connect,
  Disconnect, status) and seven-language localization — `Done`.

Remaining follow-up (optional): flush queued scrobbles periodically while the
app is running, not only at startup.

**Design**

- `Orynivo.Core/Scrobbling/LastFmSignature.cs`: pure md5 signature builder
  (sorted `key`+`value` concatenation plus the API secret).
- `Orynivo.Core/Scrobbling/ScrobbleRules.cs`: pure eligibility rules
  (>= 30 s duration, played >= 50 % or >= 4 minutes).
- `Orynivo.Core/Scrobbling/LastFmClient.cs`: HTTP methods for `auth.getToken`,
  `auth.getSession`, `track.updateNowPlaying`, `track.scrobble`.
- `Orynivo.Core/Scrobbling/ScrobbleQueue.cs`: persisted pending scrobbles,
  flushed when the network is available.
- Desktop: `AppSettings.LastFmScrobbling` (enabled, username), secrets
  (`api_key`, `api_secret`, `session_key`) via `ApplicationCredentialStore`,
  an auth dialog that opens the Last.fm authorization page, and
  now-playing/scrobble hooks on playback start and completion.
- Localization: connection/enable/labels in all seven languages.

**Tests**: signature stability and ordering, eligibility boundaries, queue
persistence/ordering.

**Commit**: `feat(scrobbling): add Last.fm scrobbling with offline queue`

## 2. Headphone crossfeed — `Done`

Blend left/right channels to reduce the unnatural separation of headphone
listening. Runs after ReplayGain and the equalizer, before the output stage;
**off by default**.

Implemented:

- `Orynivo.Core/Audio/CrossfeedProcessor.cs` and `CrossfeedStrength`
  (Light/Medium/Strong): one-pole low-pass blend with a level-preserving direct
  path, `Update`/`Reset`/`Process` mirroring `ParametricEqualizer`.
- `ICrossfeedAudioPlayer` wired into `FfmpegAudioPlayer` and
  `WasapiAudioPlayer` (pending-update pattern, filter reset on seek).
- `AppSettings.CrossfeedEnabled`/`CrossfeedStrength`, applied when a player is
  created and when settings are saved.
- Settings toggle + strength selector with seven-language localization.

Tests: bypass when disabled, centered mono content, strength monotonicity,
silence, finite output, filter reset.

## 3. Linux MPRIS and media keys — `Blocked`

Blocked: needs a Linux session bus to build and verify. The Linux target
compiles locally (`dotnet build Orynivo/Orynivo.csproj -p:OS=Linux`), but the
`Tmds.DBus.Protocol` 0.92.0 API is low-level and cannot be runtime-tested on
Windows. Implement and verify on a Linux system before shipping. Note that the
`Tmds.DBus.Protocol` package is referenced only on Linux, so a local
compile-check requires temporarily referencing it.

Platform parity with the Windows SMTC integration.

**Design**

- `Orynivo/Compatibility/Linux/MprisMediaTransport.cs` using the existing
  `Tmds.DBus.Protocol` dependency: `org.mpris.MediaPlayer2.Player` with
  Play/Pause/Next/Previous/Stop/Seek, metadata, and position.
- Reuse the existing shared transport methods so state, history, and UI stay
  synchronized.
- Hide on non-Linux targets, like the SMTC service is Windows-only.

**Tests**: pure metadata/DBus-signature mapping where feasible.

**Commit**: `feat(linux): expose MPRIS media transport and media keys`

## 4. Streaming loudness normalization — `Done`

Fix the loudness jump between the library (ReplayGain) and radio/podcasts.

Implemented:

- `Orynivo.Core/Audio/StreamingLoudnessNormalizer.cs`: slow, bounded automatic
  gain from a running mean square of the mono sum (3 s level window, 2 s gain
  window, ±12 dB clamp, silence guard).
- Applied after ReplayGain, the equalizer, and crossfeed in the ASIO and WASAPI
  PCM paths via `ILoudnessNormalizerAudioPlayer`; enabled only for radio and
  podcast playback, never for library tracks or native DSD.
- `AppSettings.StreamingLoudnessNormalizationEnabled` with a Settings toggle and
  seven-language localization. The target is fixed at -18 dBFS RMS.

Tests: bypass, boost/attenuation convergence, maximum-gain clamp, silence
safety, reset.

Follow-up: an optional target selector (e.g. -16/-18/-20 dBFS).

## 5. Remote transcoding with bitrate selection — `Done`

Bandwidth-friendly streaming for the mobile remote and slow links.

Implemented:

- `Orynivo.Server/Endpoints/StreamTranscodeOptions.cs`: validates
  `?format=opus|aac` and `?bitrate=` (64-320 kbps, per-format default) and maps
  them to FFmpeg output arguments and a response content type.
- `StreamEndpoints` transcodes regular files and virtual segments alike through
  the existing FFmpeg pipe/cancellation path when a lossy format is requested;
  an unsupported request returns 400. The parameters are optional, so older
  clients keep receiving the original stream unchanged.
- `OrynivoServerSettings.StreamingFormat`/`StreamingBitrateKbps` and
  `OrynivoServerClient.GetStreamUrl` append the parameters; the server dialog
  exposes a streaming-quality selector (Original / Opus 128 / AAC 192) with
  seven-language localization.

Tests: option validation and mapping (`Orynivo.Server.Tests`), stream-URL
building (`Orynivo.Core.Tests`).

## 6. Library Doctor duplicate resolution — `In progress`

Turn the existing read-only duplicate findings into a user-confirmed workflow.

Steps:

- 6a Core duplicate grouping — `Done`: `LibraryMetadataRepairService.FindDuplicateGroups`
  reuses the Library Doctor fingerprint/SHA-256 evidence and returns
  `LibraryDuplicateGroup` records (`Exact` for byte-identical files, `Likely` for
  unhashed same-size matches; alternate recordings are never reported). 6 tests.
- 6b Core library removal API — `Done`: `LibraryScanner.RemoveTracksByPaths`
  removes confirmed paths from SQLite, Lucene, and the waveform cache together,
  includes virtual CUE/MKA tracks that share a removed physical source, and
  optionally deletes the files from disk. 3 tests cover the row selection.
- 6c Desktop review UI — `Todo`: a per-group "keep this / remove the others"
  list in `MetadataRepairDialog` with explicit confirmation and seven-language
  localization.

Never automatic; no action without confirmation.

## 7. Bulk editing in tables — `Todo`

Multi-select rows, then set genre, personal rating, or favorite in one step.

**Design**

- `DataGrid.SelectionMode="Extended"` for the track tables; a bulk action bar.
- Local: transactional `AudioDatabase` updates; remote: batched rating/favorite
  API calls.
- Preserve the existing single-row behavior and column masks.

**Tests**: batch-building logic, mixed local/remote selection handling.

**Commit**: `feat(library): add bulk genre, rating and favorite editing`

## 8. Smart playlist "similar to track" — `Todo`

**Design**

- Extend `SmartPlaylistCriteria` with a similarity reference (provider-local
  source key + track id) and a strength.
- Resolve through the existing `SimilarityFeatureService` in `Orynivo.Core` so
  both desktop and server resolve identically.

**Tests**: criteria serialization compatibility, resolver behavior.

**Commit**: `feat(playlists): add a similarity criterion to smart playlists`

## 9. Mood/activity presets — `Todo`

**Design**

- Presets (Focus, Workout, Wind down) over the existing acoustic descriptors
  (energy/brightness/dynamics) and BPM, reusing the Infinite Mix queue path.
- Shown beside the existing mood selector.

**Tests**: preset ranking determinism.

**Commit**: `feat(infinite-mix): add mood and activity presets`

## 10. Harmonic mixing (Camelot) — `Todo`

**Design**

- Add musical-key detection to `AudioFeatureAnalysisService` (bounded, cached
  like the other descriptors).
- Order Infinite Mix batches by Camelot-wheel adjacency.

**Tests**: Camelot mapping and adjacency.

**Commit**: `feat(infinite-mix): add harmonic mixing on the Camelot wheel`

## 11. Year-in-review export — `Todo`

**Design**

- Render the existing Dashboard statistics for a chosen year into a shareable
  image/PDF.
- No new data collection.

**Tests**: layout/aggregation helpers.

**Commit**: `feat(dashboard): add a year-in-review export`

## 12. Karaoke fullscreen lyrics — `Todo`

**Design**

- A fullscreen mode for the existing synced-lyrics view with large,
  centered, animated lines.

**Tests**: lyric-line selection timing (pure).

**Commit**: `feat(lyrics): add a fullscreen karaoke view`

## 13. Scheduled auto-backup with retention — `Todo`

**Design**

- Optional scheduled library backup using the existing `LibraryBackupService`,
  with a retention count and a last-run timestamp in settings.

**Tests**: retention selection (pure).

**Commit**: `feat(backup): add scheduled backups with retention`
