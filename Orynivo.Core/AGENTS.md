# Orynivo.Core Instructions

This file applies to `Orynivo.Core/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build Orynivo.Core/Orynivo.Core.csproj` and also build each
  affected consumer when a public contract changes.
- Public/internal C# APIs require complete English XML documentation.

## Core Invariants

- Manual cover searches fetch only bounded CAA `front-250` previews, with three
  concurrent workers, a 35-second search budget and one retry for transient
  failures. Keep successes when another candidate fails. Fetch `front` originals
  only after explicit selection; never silently save a preview as the original.
  Preserve punctuation-aware query fallbacks. The transport regression harness
  is `scripts/CoverSearchSmoke` (offline by default; `--live` is opt-in).

- The music visualizer analyses audio through `Orynivo.Core/Audio`:
  `PcmVisualizationTap` is the lock-free hand-off from the audio pump and must
  never block or wait (drop the oldest samples instead), `AudioSpectrumAnalyzer`
  owns windowing, FFT, band grouping, and smoothing, and `Fft` stays a pure,
  allocation-free transform. Never evaluate preset expressions or render frames
  on the audio thread. `PresetVariableLayout.RegisterStandardVariables` is the single place
  that declares the Milkdrop variable set, so every expression block of a preset shares one
  slot layout and `q1`-`q32` keep their value between the per-frame and per-pixel stages. The
  renderer runs the stages in Milkdrop order: the init blocks once, the per-frame block and
  the four waveform per-frame blocks, the motion warp (`zoom`, `zoomexp`, `rot`, `cx`/`cy`,
  `dx`/`dy`, `sx`/`sy`) with the per-pixel block on top, the blur passes, the decay fade, the
  centre darkening, the gamma adjustment, and the overlay. The per-pixel block sees the warped
  position in `x`/`y`, which is a deliberate deviation from Milkdrop offset semantics so the
  built-in presets keep working; revisit it with the shader runtime in phase 38d. Numeric
  preset keys are parsed into `VisualizerPreset.Defaults` and applied as the per-frame
  starting values after the computed seeds, which is how Milkdrop presets carry most of their
  settings; never drop that step or key-only presets lose their wave, border, and echo
  parameters. The default wave mode is the single line (3), not the circular mode (0), because
  every built-in preset and the legacy `per_point` contract assume a line.
  `VisualizerTextureBank` generates the Milkdrop noise and random textures from fixed seeds
  instead of bundling third party images: keep generation deterministic and lazy, and keep the
  sizes (32, 256, 512) so shader sampling stays comparable. The `sampler_main`,
  `sampler_pc_main`, `sampler_fc_main`, `GetBlur1`-`GetBlur3`, and `GetPixel` constructs are
  HLSL shader features and belong to the shader runtime in phase 38d, not to the texture bank.
  The Milkdrop format has no per-preset texture block, so unknown `tex_*` keys stay ignored.
  The shader runtime is built in three steps: `ShaderLexer` is the tokenizer and stays a pure,
  allocation-bounded function over the source, `ShaderParser` builds the tagged-union
  `ShaderNode` tree from it, and `ShaderInterpreter` evaluates that tree,
  and the `warp_N_*`/`comp_N_*` bindings with the per-frame cost budget come last.
  `ShaderInterpreter` keeps its variables in a plain dictionary the caller seeds and reads back,
  samples only through `IShaderSampler`, and guards itself with a loop budget and a call depth
  limit; keep both, because a runaway shader must never stall a frame.
  `VisualizerPreset` parses the numbered `warp_N`/`comp_N` keys with their `_enabled`,
  `_per_frame`, and `_per_pixel` companions, and the preset reader must keep the newlines inside
  a multi-line value because that is how Milkdrop stores shader source. A shader that fails to
  parse is skipped, never fatal. `VisualizerPreset.ParseSections` splits a `.milk` file at its
  `[presetNN]` headers, so a file that holds several presets yields several presets; the declared
  format version is reported but never gates loading, because Milkdrop versions its presets and
  every version must stay usable.   `PresetRenderer` implements `IShaderSampler` and enforces
  `ShaderTimeBudgetMilliseconds`: the shaders never stop running, they lose resolution. Both the
  warp shader and the comp shader run on the grid `ShaderGrid` returns and are scaled back over the
  frame; `AdaptShaderGrid` moves the grid's pixel target between `ShaderPixelFloor` and
  `ShaderPixelBudget` from the measured shader cost, so a preset that overruns settles at a coarse
  but visible picture. Never reintroduce a mechanism that switches the shaders off for a frame, and
  never let the budget compare the whole frame against the shader threshold: that combination is
  what made a real preset collection render nothing but the shared overlay. `ShaderGridReduced`
  reports the reduced grid for diagnostics. The warp shader grid is scaled back with
  `PixelBuffer.SampleBilinear`, because it is the base picture; only the comp pass may scale with
  nearest-neighbour. `WarpStageBudgetMilliseconds` is the warp stage's own ceiling: the per-pixel
  program runs once per screen pixel, so a preset that loops inside it can cost seconds for a single
  frame. When the stage passes that ceiling the program is left out for the rest of the frame and
  retried a moment later, so the frame still draws with the per-frame motion values. Never let that
  path become a per-frame hang, and never make a frame wait for a per-pixel program that cannot
  finish. The interpreter
  walks the tree per pixel, which is the known cost limit; a JIT compiler for shaders is the
  documented follow-up if the CPU cost proves too high.
  `PresetRenderer.Timings` and `AverageTimings` carry the `RenderTimings` breakdown per frame and
  per averaging window, and `ResetTimings` restarts that window; keep the measurement cheap (one
  stopwatch and a handful of marks per frame) and never time a per-pixel shader call, because the
  measurement would cost more than the work. A warp shader is therefore part of `Warp`, while
  `Shader` is the comp stage.
  `ShaderRuntime` owns every shader operation and both execution paths call it, so the interpreter
  stays the reference implementation and `ShaderCompiler` can only be correct if it produces the
  same values; `ShaderCompilerTests` asserts exactly that and must keep passing. Only straight-line
  bodies (declarations and one return) are compiled — branches, loops, and swizzle assignments stay
  on the interpreter, because a wrong picture is worse than a slow one — and the compiled path must
  seed the frame-constant variables once per frame and only `uv`, `uv_orig`, `rad`, and `ang` per
  pixel. A comp shader is a post-processing pass, so it runs on a grid bounded to
  `ShaderPixelBudget` pixels and is scaled back over the frame; keep that, because it is what makes
  the pass affordable on the CPU, and keep the direct-to-frame write when the grid matches so the
  picture is not resampled through an identical-size copy.
  The per-pixel path is the measured bottleneck, so it must stay free of avoidable work:
  `PresetProgram.ReferencedVariables` (filled by the compiler as it resolves each variable) is
  the conservative usage set, and the warp stage resolves its slots once in the constructor and
  only computes `rad`, `ang`, the motion grid, and the seeded sampling position when the preset's
  per-pixel code references them. Keep that set conservative — reporting a variable a program
  does not really use only costs a little work, while missing one changes the picture — and keep a
  rendered frame allocation-free, which `RenderTimingTests` asserts.
  Full-frame passes may run in parallel only when they are row-independent: a pixel reads the
  source buffer and writes its own pixel, nothing else. `ParallelRows.For` owns the split and
  keeps small frames on the calling thread. The warp is parallel only when the per-pixel program
  writes nothing but the values the engine re-seeds per pixel (`x`, `y`, `rad`, `ang`), because a
  value written by one pixel and read by another would make the picture depend on the split; a
  warp shader or an active motion grid keeps it sequential for the same reason. Never give two
  workers the same slot array, and never claim a speed-up without the identical-frame test in
  `ParallelWarpTests`.
  The preset compiler must accept the Milkdrop function set, because a block it cannot compile is
  dropped and takes that preset's motion with it: `above`, `below`, and `equal` yield one or zero
  (never a boolean), and `sqr`, `sigmoid`, `band`, `bor`, and `bnot` belong to it too. Names are
  case-insensitive, and a block that still fails is skipped and recorded on
  `VisualizerPreset.FailedBlocks` instead of rejecting the preset, so one unsupported construct
  costs a block rather than a preset. `PresetFolderDiagnosticTests` reports what still fails
  against a real collection; run it after touching the compiler. The numbered parts of an
  expression block are joined by concatenation, because presets split one expression across parts
  and a part may end with an operator; a semicolon is inserted only when the previous part is
  complete and the next does not bring its own. `megabuf` and `gmegabuf` are Milkdrop's shared
  memory buffers and their accesses are serialised, because a preset writes lookup tables that
  other pixels read. `loop(count, statements)` is a statement, not an expression, and its
  iteration count is clamped; both buffer write spellings presets use (`gmegabuf(i, value)` and
  `gmegabuf(i) = value`) must keep working, because presets build their lookup tables with them. Keep the
  interpreter off the audio thread and bound its per-frame cost so a heavy shader degrades the
  render resolution instead of stalling playback.
  The expression language also has compound assignments: `PresetLexer` must emit `+=`, `-=`, `*=`,
  `/=`, and `%=` as one token, because splitting them leaves the statement parser with a stray `=`
  and fails the whole block, and the compiler applies them to the variable and to the
  `gmegabuf(i) += x` buffer form. On the shader side the parser accepts `while` loops, the comma
  operator at statement level and inside parentheses (never in a call argument list, where the
  commas separate arguments), a declaration initialized with a braced list or a `sampler_state`
  block, element access on a vector, and the integer and double vector types. A macro definition
  ends at its line comment, so `#define a b //comment` must not expand the comment into the middle
  of a call. Array declarations such as `const float4 samples[5] = { ... }` stay a deliberate hard
  failure rather than an ignored declaration: an unknown array name would render a wrong picture,
  and modelling arrays needs its own value kind in the interpreter.
- Keep the project cross-platform `net10.0`; do not introduce Avalonia, Windows,
  DPAPI, WASAPI, ASIO, or other platform-specific dependencies.
- Put shared library scanning, SQLite persistence, search, streaming models and
  clients, FFmpeg primitives, and web-fetching behavior here.
- Preserve SQLite migrations, stable IDs, WAL behavior, CUE virtual-path
  identity, user favorites, artwork caches, ReplayGain data, and `added_at`.
- `LibraryBackupService` supports both the process data root and an explicit
  server data root. Backups remain versioned, exclude audio and credentials,
  use a consistent SQLite snapshot, validate staged imports, and roll back
  partial replacements before rebuilding the search index.
- Chaptered MKA containers use FFprobe-derived stable `mka://chapter/` virtual
  paths and the existing segment columns; unchaptered MKA files remain ordinary
  tracks. Each probe uses bounded analysis and a 30-second timeout.
- Library-only genre corrections live in `track_genre_overrides` (keyed by stable
  track path, so `cue://` and `mka://chapter/` tracks are covered) and are
  reapplied by every `AudioDatabase.Upsert`. `SetTrackGenres` updates several
  tracks in one transaction and writes the override at the same time; an empty
  value removes it so the next scan restores the embedded genre. Never write these
  corrections into media files.
- Library-only title corrections live in `track_title_overrides` and must be
  applied by every `AudioDatabase.Upsert`; never write these corrections back to
  the source media.
- Scanner, watcher, and reconciliation writes must keep SQLite and Lucene in
  sync and use the shared scanner gate.
- `LibraryScanner.ConfigureReplayGainThrottling` controls FFmpeg threads and a
  cancellable inter-track delay for server maintenance. Defaults remain inert
  for desktop callers; the server configures conservative values at startup.
- Explicit ReplayGain maintenance must retain only missing album IDs across the
  track pass and update Lucene in bounded batches. Never accumulate complete
  `TrackRecord` instances or one library-wide update list for this workflow.
- Multi-root owner scans must hold a watcher-operation suspension for their
  complete duration. This prevents incremental updates or periodic
  reconciliations from taking the shared scanner gate between roots and making
  the reported foreground scan appear stalled on its next root.
- `LibraryScanner.RefreshMetadataAsync` is the explicit maintenance path that
  bypasses timestamp skipping and re-reads every supported file. It shares the
  normal scanner gate and reconciliation/indexing pipeline, preserves
  library-only overrides, and never changes source media.
- A failed TagLib read must never upsert the file-system-only fallback record.
  Full, forced, watcher, and reconciliation scans retry boundedly, count a final
  failure, and preserve the existing database metadata unchanged.
- When an upsert changes a track's album identity inside the same physical album
  directory, carry the previous album's artwork and favorite flag to the target.
  Existing target artwork wins; favorites are combined rather than cleared.
- Keep compact query models compact; do not add artwork BLOBs, lyrics, or full
  records to list/facet/folder queries.
- Free-text Lucene queries are term-centric across their supplied fields: every
  analysed term is required, but different terms may match different fields.
  Preserve this behavior so combined artist/title searches work identically in
  the desktop and Orynivo Server indexes.
- `LibrarySearchFilter` owns structured release-year and library-added timestamp
  filtering plus deterministic relevance/title/year/addition ordering shared by
  desktop and server searches. Date ranges are half-open Unix ranges after the
  client converts inclusive calendar dates. `SmartPlaylistTrackInfo.AlbumId`
  supports bounded album grouping and must remain compact.
- `QueuePathPolicy.CanPersist` is the single decision for whether a queue or
  playback path may be persisted without credentials. It keeps local, `cue://`,
  and `orynivo://` paths persistable and rejects HTTP/HTTPS URLs that embed user
  information or known credential query parameters (`X-Plex-Token`, `token=`,
  `key=`). Desktop and server consumers must not duplicate this URL policy; it is
  covered by `Orynivo.Core.Tests/QueuePathPolicyTests.cs`.
- Track scans preserve personal ratings, cached MusicBrainz rating/vote data,
  and a client-resolved recording MBID when the media tag has no recording ID.
  `MusicBrainzRatingService` prefers a valid recording MBID and permits fallback
  matching only for one exact artist/title candidate compatible with duration.
  Community ratings use MusicBrainz's zero-to-five scale and remain separate
  from the personal zero-to-five integer rating. Compact track list/streaming
  DTOs carry the rating fetch timestamp so clients can enforce cache freshness.
  Metadata fallback returns a resolved MBID and follows it with a direct
  recording lookup. Do not cache community ratings from batch/search responses:
  MusicBrainz does not reliably populate their rating field even when
  `inc=ratings` is supplied.
  Direct lookups request `ratings+genres+tags`; keep genres with positive counts,
  tags with at least two positive votes, and persist both as separate bounded
  JSON arrays. `MusicBrainzGenreMetadata.Combine` is the shared effective-genre
  composition used by facets and indexing without altering embedded metadata.
  A completed conservative lookup that cannot resolve one recording is
  persisted through `SetTrackMusicBrainzLookupAttempt`; scanner upserts must
  preserve that timestamp so clients can apply a longer retry cooldown.
- Album catalog queries, recent-album queries, detail lookup, and Dashboard
  album totals expose only albums referenced by at least one indexed track.
  Artist catalog rows and artist totals likewise require a track-backed album;
  never surface orphaned normalization rows as usable library entities.
- `LibraryMetadataRepairService` groups tracks by their immediate physical
  directory, detects inconsistent album metadata, and uses the MusicBrainz fuzzy
  CD-TOC endpoint only when every candidate track has a duration. Confirmed
  corrections are persisted in `track_metadata_overrides` and reapplied by every
  `AudioDatabase.Upsert`; never write them into media files implicitly.
  MusicBrainz matching accepts optional user-edited release and artist queries;
  text-search results must still be fetched with their recording lists and
  scored against available track durations plus title similarity before they
  are offered. Text search must remain usable when local or MusicBrainz
  durations are missing, and fuzzy TOC lookup includes all medium formats.
  Its Library Doctor analysis also reports missing track ReplayGain and
  MusicBrainz recording IDs even when basic album metadata is consistent.
  Duplicate candidates require the same non-empty AcoustID fingerprint on
  distinct physical source paths. Equal file sizes are hashed sequentially and
  become exact duplicates only when complete SHA-256 content matches; an
  unavailable hash remains “likely,” while differing size or content means an
  alternate-file/edition candidate. No class may trigger automatic deletion or
  metadata merging.
  `LibraryMetadataRepairService.FindDuplicateGroups` exposes the same evidence as
  `LibraryDuplicateGroup` records (`Exact`/`Likely`) for a user-confirmed review;
  it is read-only and must never remove, move, or merge anything. Any removal
  stays explicit and user-confirmed and goes through
  `LibraryScanner.RemoveTracksByPaths`, which keeps SQLite, Lucene, and the
  waveform cache in sync, removes virtual CUE/MKA tracks that share a removed
  physical source, and only deletes files from disk when the caller asks for it.
  Artist spelling variants use the shared conservative comparison key and are
  guided-review findings only; name similarity must never merge artist records
  automatically.
  Remote Library Doctor requests use a separate bounded maintenance timeout;
  do not increase the normal catalog/streaming client timeout for long-running
  server-side file checks.
  `Analyze(inspectFiles: false)` performs index-only analysis without physical
  source/image existence checks or duplicate hashes. Default callers retain full
  checks. Optional progress reports phase counters, never private paths.
  `OrderTracks` is shared by search, display and persistence (disc, track,
  source, stable ID). Corrections must reject mismatched track counts.
- Remote dashboard totals use `OrynivoServerClient.GetLibrarySummaryAsync` and
  the server's aggregate `/api/library/summary` response; do not replace this
  fast path with complete track or album payloads.
- Dashboard recommendations use compact album-level genre/BPM candidates from
  `AudioDatabase.GetRecommendationAlbums` and the matching server endpoint.
  Keep this payload free of track rows, artwork bytes, and playback credentials.
  Local recent-album and recommendation queries must aggregate the `tracks`
  table before joining album, artist, and artwork metadata; joining those
  tables per track caused Dashboard load time to grow with the complete library.
  Preserve the covering `(album_id, added_at DESC)` index used by the recent
  album aggregation.
- `SimilarityFeatureService` owns the versioned provider-neutral track feature
  contract. Version two uses effective genre, BPM, explicit mood, favourite,
  personal/community rating, listening history, and optional cached acoustic
  descriptors. Preserve deterministic normalization and credential-free source
  keys; future descriptor changes must extend the schema explicitly.
  `RankSimilar` is deterministic, accepts mixed credential-free provider keys,
  rejects vectors from another schema version, and retains artist/album
  diversity limits; UI consumers must resolve returned provider-local IDs
  through their owning catalog.
  `RankMood` provides deterministic calm, balanced, and energetic ordering from
  explicit mood tags, normalized BPM, preferences, community confidence, and
  familiarity, with the same provider-aware diversity constraints.
  `RankPreset` provides deterministic **Focus**, **Workout**, and **Wind down**
  ordering from cached acoustic descriptors (energy, brightness, dynamics),
  normalized tempo, explicit mood tags, and preference signals; missing
  descriptors use a neutral prior rather than excluding the track. All three
  rankings share one private diversity-limited selection helper, and results
  always carry credential-free provider keys.
  Server vectors are paged by stable track ID order, and the desktop client
  must replace the server's placeholder source with `orynivo:{server.Id}`;
  never place a server URL or credential in `SourceKey`.
  `SmartPlaylistCriteria.Resolve(candidates, similarityFeatures)` uses
  `RankSimilar` whenever `SimilaritySourceKey` and `SimilarityTrackId` are set:
  the reference is matched by provider key plus provider-local track id, results
  are ordered by descending score, `SimilarityMinimumScore` removes weak
  neighbours, an unresolvable reference returns an empty list rather than an
  unrelated set, and every other criterion still applies. The desktop re-keys
  remote vectors onto the smart-playlist candidate provider key and pseudo-IDs
  before resolving, and the stored reference must never contain a server URL or
  credential.
- Optional acoustic descriptors live in the provider-local
  `track_audio_features` table and survive metadata scans. Version changes must
  trigger bounded reanalysis. `AudioFeatureAnalysisService` decodes at most 90
  seconds as 8-kHz mono, restricts FFmpeg to one thread and best-effort reduced
  priority, and produces only normalized energy, brightness, and dynamics plus an
  optional estimated key. Maintenance is sequential and failed sources have a
  seven-day retry cooldown.
- `CamelotKey` is the single Camelot-wheel mapping: it parses conventional key
  names and wheel labels, exposes wheel distance (identical `0`, relative or
  neighbouring `1`), and never guesses an unsupported spelling. Key estimation
  uses a bounded Goertzel chromagram correlated against Krumhansl-Schmuckler
  profiles and must stay conservative — a flat or ambiguous chroma returns no
  key rather than a guess — and the canonical wheel label is what gets cached in
  `track_audio_features.camelot_key` and carried on facet, similarity, and genre
  cloud payloads. `HarmonicOrdering.Order` is the single greedy wheel walk used
  by playback batches: it is deterministic, keeps keyless items in their original
  relative order after the chain, and returns its input unchanged when fewer than
  two items carry a key.
- `GenreCloudService` owns the stable hierarchical genre taxonomy, tag
  normalization, count aggregation, breadcrumbs, and bounded provider-local
  candidate selection. Candidate offsets rotate and wrap the stable order for
  repeated background batches. Its curated data lives in the embedded
  `Library/GenreTaxonomy.json`; definitions may be top-level and have multiple
  parents, so traversal must be cycle-safe and node counts must deduplicate a
  track even when several graph paths reach the same ancestor. Every node also
  carries a distinct provider-local album count derived from the compact facet
  row's optional `AlbumId`; cross-provider merging sums those counts. Controlled
  compound-name matching resolves recognized descriptive tags without guessing
  from arbitrary substrings. Unmapped tags retain dynamic `unmapped:` keys and
  appear by their actual names beneath `more-genres`; never collapse them into
  an Other bucket. The desktop merges snapshots across providers.
  `ResolveLeafGenreKeys` performs cycle-safe recursive expansion of one or more
  visible taxonomy branches for Genre Cloud-driven Infinite Mix filters and
  preserves dynamic unmapped keys as leaves.
  `ResolveDescendantGenreKeys` returns each supplied branch itself plus every
  recursive descendant, preserving direct parent-tag matches in branch-based
  recommendation queries.
- `AudioDatabase.GetListeningTrend` supports up to 366 equal chronological
  buckets so the client can request daily Dashboard points without materializing
  playback-history rows.
- `ArtistNameNormalizer.CreateComparisonKey` is the shared identity key for
  comparing artist names across local and Orynivo Server catalogs.
- `ArtistProfileService` accepts `de`, `en`, `fr`, `es`, `ru`, `zh`, and `hi` profile language
  codes; supported UI languages must not silently fall back to another language.
- Local artist browsing is album-artist-centered. The scanner records whether
  `ALBUMARTIST` was missing, imports supported compilation flags, and
  `AudioDatabase.ReconcileAlbumArtists` resolves the complete album before the
  Artists view is exposed. Explicit consistent album artists win; otherwise
  compilation or differing inferred track artists resolve to `Various Artists`.
  Primary track artists stay attached to their tracks. Featured suffixes remain
  governed by `ArtistNameNormalizer`. Embedded MusicBrainz artist IDs take
  precedence over name comparison when resolving a local artist identity.
- `FanartTvArtistImageService` uses a known MusicBrainz artist ID or an
  unambiguous exact MusicBrainz name match, accepts only HTTPS `artistthumb`
  URLs, bounds image downloads, and never includes the Fanart.tv API key in
  diagnostics. `ArtistProfileService` prefers that image only when automatic
  image refresh is allowed; manual artist images must remain untouched.
  `ArtistImageSearchService` owns writes and explicit deletion of the
  provider-local `artist-images/<id>.*` cache variants.
- Web page fetching must retain SSRF protection, connect-time address checks,
  redirect and size limits, text-only responses, timeouts, and audit logging.
  The loopback/private/link-local/CGNAT/multicast/reserved address classification
  lives in the tested `Orynivo.Web.PrivateNetworkPolicy`; keep it there rather
  than inlining the range checks.
- Streaming URL builders may carry credentials for immediate playback, but such
  URLs must never be persisted, logged, documented, or returned to a model.
- Shared release-update models verify the ECDSA P-256 signed manifest before an
  asset is selected and verify its SHA-256 digest after download. Consumers must
  never bypass either verification or accept an unsigned fallback.
- `FfmpegLocator` searches platform-specific installation directories in
  addition to the inherited `PATH`. Windows and macOS may download matching
  FFmpeg/FFprobe binaries into the per-user cache; Linux remains
  system-package-managed. Downloaded macOS tools must be marked executable.
- `Orynivo.Server` has no `InternalsVisibleTo` grant; server-facing Core APIs must
  be deliberately public.

Consult the detailed database, scanner, search, streaming, audio, and web rules
in the root `AGENTS.md` before modifying those areas.
- `AudioDatabase` applies connection pragmas on every connection but coalesces
  schema creation/migration once per physical path and process. A newly created
  file at a reused path must discard the old initialization marker.
- Use `GetTrackListPage` when only one ordered page is required; do not load the
  complete track list and page it in managed memory.
