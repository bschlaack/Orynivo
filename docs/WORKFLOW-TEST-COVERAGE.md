# Workflow verification scopes

CoreAudio (2026-11-06): 50 device-free cases cover exact-rate/hog leases,
restoration/cancellation, bounded concurrent PCM transport, byte preservation,
rate ceilings, ordinary gain, pause/seek and overlapping seek epochs, late
decoder cleanup, monitored rate loss and profile normalization/cloning/JSON. The
production player composes injected device, output and decoder boundaries; these
tests do not execute native HAL entry points or FFmpeg decoding. One additional
Windows UI group simulates Mac choice policy with injected PCM device lists,
then exercises the actual CoreAudio/OpenAL profile dialog and Save across seven
languages, two themes and two widths. Synthetic captures verify the shared
layout, not macOS windowing or audio devices. Native Mac/DAC acceptance and
same-rate gapless output remain open in [COREAUDIO-PLAN.md](COREAUDIO-PLAN.md).

Mobile catalog row artwork (2026-11-04): unit checks cover opaque source keys,
deduplication and bounded eviction. API smoke checks private image bytes/ETags
and missing/invalid key rejection. Muted Edge verifies artist and album images
before row text, including artist-to-album navigation and missing-image layout;
synthetic phone screenshots are visually inspected. No personal library is used.

Mobile music delivery (2026-11-04): synthetic tests cover literal all-word
search and cross-source ranking, format whitelisting, no realtime input
throttling and input-side seek bounds. Real FFmpeg/FFprobe verifies all offered
FLAC precisions/rates, MP3 bitrates and DSD-to-FLAC output. API/browser checks
exercise ordered library/search suffixes, duplicates, bounds, dedicated
album/artist navigation, the More menu, format handoff and all seven languages.
The existing held-source streaming checks establish packet delivery before
source EOF, not a physical phone startup-latency guarantee.

Browser output (2026-11-04): `BrowserAudioPlayerTests` covers
source/page/command ownership, seek cancellation, actual-browser-end completion,
full encoder drain, DSD/segment encoding arguments and inactivity pause with a
controlled clock. `scripts/RemoteSmoke` holds output admission, checks
session-only HttpOnly cookie scope, bearer-only/media-only rejection, stream
bytes, takeover/revocation and old-page release isolation through the real host
with fake playback adapters. The browser harness plays synthetic audio in muted
headless Edge, checks that media waits for output acknowledgement, and exercises
pause/resume, seek stream replacement, native return, takeover and autoplay
refusal. The opt-in `scripts/BrowserAudioSmoke` invokes actual FFmpeg/FFprobe on
generated PCM/DSF, virtual segments and multipart input and validates decodable
stereo MP3 plus write-failure/cancellation cleanup. It opens no audio hardware.
Held loopback sources also prove live byte delivery and real FFmpeg packet
delivery before upstream EOF, unchanged direct bytes and forwarded 206 ranges.
Playlist tests cover duplicate occurrences, stale identity and suffix ordering.
The headless UI suite checks the actual transient output picker and detached
resume decision, without opening outputs or starting playback. These checks do
not establish native WASAPI/ASIO driver release, physical phone sound, mobile
background behavior or audible gapless playback; real-device acceptance remains
separate. All sources and HTTP fixtures are synthetic.

Mobile playback presentation (2026-11-04): `MobileRemoteArtworkCacheTests`
checks shared loads, source replacement and delayed-image revision without UI,
network or audio. `scripts/RemoteSmoke` checks active-source metadata, omission
of an old queue album, seeking flags, authenticated thumbnail bytes/private
validators and rejected delayed images. The browser harness checks successful
Play navigation for radio, podcasts, music, playlists and queue entries; failed
Play and append remain in browsing. It verifies radio/podcast details, missing
then delayed logos and responsive layout with synthetic images. This is software
evidence; physical playback and live directory artwork availability are not
established by these checks.

Mobile radio/podcast coverage (2026-11-04): `MobileRemoteMediaCatalogTests`
checks source-kind isolation, retained podcast identity and bounded eviction
without identity reuse. `scripts/RemoteSmoke` checks authenticated production
media routes, search bounds, sanitized provider failures, expired podcasts and
asynchronous playback acknowledgements through fake dispatcher-bound adapters.
`scripts/verify-mobile-remote.cjs` checks radio search/play, podcast episode
browsing/play, navigation and seven languages at phone/tablet sizes using
synthetic HTTP. These checks never contact directories, read personal catalogs
or start an audio device. They do not prove real receiver playback or provider
availability.

Updated: 2026-11-06. Counts identify the added workflow cases, not the total
number of unit tests in the repository.

| Scope                      | Tracked evidence                                                                                                                                                   | Execution                                                   | Limits                                                                                                                |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| Playback lifecycle         | 14 PlaybackSessionLifecycleTests cases, including four output-switch cases and one composed gapless queue-edit case; separate ownership/drain/refresh tests remain | Desktop unit tests                                          | Fake factories, seek, positions, pause, completion and disposal; no hardware or physical audio                        |
| Desktop setup and recovery | 68 Orynivo.Ui.Smoke groups using production adapters, original history and source collisions                                                                       | Windows Debug/Release CI configuration and local verify-all | MainWindow unshown; synthetic SQLite/HTTP and bounded headless layout; no pointer-dispatch or other-platform UI claim |
| Track HTTP routes          | Eight paging/detail/ID/auth/profile cases                                                                                                                          | Server TestServer unit-test project                         | Real route/middleware subset with temporary SQLite; no startup services or TCP listener                               |
| Playlist HTTP routes       | Seven order/duplicate/mutation/profile/smart-resolution cases                                                                                                      | Server TestServer unit-test project                         | Shared definitions and per-request track state; synthetic catalog                                                     |
| Original-file streaming    | Thirteen full/range/error/access/file-write cancellation cases                                                                                                     | Server TestServer unit-test project                         | Synthetic opaque files; no audio decoding, FFmpeg codec or physical network disconnect                                |
| Transcode lifetime         | Five tests with actual managed child processes                                                                                                                     | Server unit-test project                                    | Production runner, parent exit/copy/cleanup; not FFmpeg codecs or descendant-tree validation                          |
| Compact track profiles     | Four profile cases across all five reader methods                                                                                                                  | Core unit-test project                                      | Explicit false/zero and compatibility fallback; not every entity/DTO query                                            |
| Catalog measurements       | Two Release observations of eight readers at 1,000/100,000 tracks                                                                                                  | Opt-in scripts/CatalogBench                                 | Warm synchronous SQLite and per-thread managed allocations; no cold-start/UI/HTTP latency or RSS budget               |

The ordinary three test projects run through local verification and the existing
CI configuration. Windows adds the isolated UI smoke process. CI configuration
is separate from a completed GitHub Actions run; local commits alone do not
prove that a remote run succeeded. The benchmark is outside CI time gates;
methodology and results are in
[CATALOG-MEASUREMENTS.md](CATALOG-MEASUREMENTS.md).

The composed playback cases use the same production lifecycle helpers as the
application. Fake queue inputs are test data, not a second application queue or
navigation model. Gate-driven assertions check readiness separately from output
completion, reject superseded work and drain uniquely owned disposal. Diagnostic
timeouts only prevent stuck tests; they are not audible latency budgets.

Retirement checks hold synchronous fake disposal on independent workers and
assert that neither the caller context nor the shared thread pool owns it. Four
simultaneous blocked retirements retain a complete drain until the final
release. Existing failure, budget, readiness and output-switch checks remain
active; diagnostic timeouts and test parallelism are unchanged.

Physical devices/receivers, FFmpeg codecs, operating-system network disconnects,
other-platform UI runtime behavior and portable end-to-end latency need separate
opt-in evidence. Fresh-profile first-playback usability remains a separate
follow-up. Keep these boundaries when adding features or reporting verification.

Shuffle adds one Windows group using the actual routed control across seven view
tags, with a synthetic refresh adapter that cannot prepare audio. It checks
enable/disable refresh, distinct-path exhaustion, duplicate exclusion and
forward history after Previous through the real queue navigation methods. This
does not exercise native gapless output or audible playback.

Fresh-profile setup adds ten Windows UI groups: source visibility (empty, local
and server-only), compiled layout/focus/automation names across seven languages,
two themes and 180/230 DIP row widths, and routed Button.Click activation of the
original embedded Settings sections, and output setup without profiles (Create
output enabled; Configure output and Delete output disabled). A fifth group
checks the detached output selector/actions at 240/640 DIP widths across seven
languages and both themes, including caption bounds and automation names. The
sixth checks the shared root setter and original visibility adapter: the first
local source reveals Artists/Albums/Tracks, roots are copied, and later changes
retain a collapsed Library. It does not invoke Save, a watcher or catalog
cleanup. MainWindow stays unshown; only the detached compiled hint is hosted for
bounded layout. The layout groups check focus admission rather than native
pointer/key dispatch. Settings opens without device enumeration or playback. No
folder picker, server authentication, actual scan, physical display scaling or
audible first-playback evidence is inferred. Full interactive onboarding
acceptance remains open.

The seventh setup group invokes original notice publication with explicit source
outcomes: complete empty views show guidance; failed, timed-out, cancelled,
partial, source-free and populated outcomes do not. Search, playlist and detail
guards suppress it; seven-language refresh and actual local/server Settings
routing are checked. Server routing reuses an open synthetic view to avoid
server probing. This is adapter/UI evidence, not a scan or playback run.

The eighth setup group runs real catalog loads before media seeding with
synthetic HTTP. It checks current empty publication, complete cache reuse,
invalidated and held reloads, a late empty completion after a newer partial
failure, fresh recovery and a worker-issued watcher signal. The original
coalesced dispatcher pass hides obsolete guidance; a fresh load restores it. No
real scan, HTTP connection, startup or playback is performed.

The ninth setup group dispatches headless Tab, Shift+Tab and Enter through the
actual compiled sidebar ListBox with only its setup item retained. It checks all
three child buttons, exit/reentry and actual local Settings activation. The
original sidebar items and placement are restored. This does not prove
full-sidebar traversal, native operating-system input or physical accessibility.

Existing setup groups also check compiled focus-visible template colors against
the active accent/soft-fill resources: all three sidebar actions after actual
headless Tab dispatch in both themes, and Create output in the localized 240/640
DIP layout loops. The output action bounds must remain unchanged by focus. Group
count remains 55. This proves template properties, not physical screen contrast,
native input, or assistive-technology behavior.

The tenth setup group checks fresh transport availability and no-player Stop
with empty/remembered source identities. No player is created or prepared.
Layout groups also simulate render scaling 1/2, reduced-motion setup transitions
and opaque theme contrast (text >= 4.5, focus stroke/fill >= 3). Optional local
frame export is disabled by default. Pre-seeded synthetic output settings avoid
first-run default endpoint discovery. See FIRST-PLAYBACK-ACCEPTANCE.md for
manual sound acceptance and isolated profile preparation.

A separate directory-add group substitutes the ordinary Settings scanner with
held/successful/failed synthetic tasks. It checks immediate scan admission after
source publication, ReplayGain preference, progress, duplicate/empty rejection,
active row identity across add/remove, cancellation ownership, backup-lock
release, failure retry controls and rejected picker results after deactivation.
No actual media scan or folder picker is invoked.

View refresh adds seven Windows groups using real Dashboard/Genre Cloud and
top-level navigation adapters. Deterministic held HTTP verifies early hero and
local-statistics publication, cached/unchanged section reuse, immediate cached
presentation changes during held HTTP, stale cloud display, changed-count
publication, failed refresh/recovery and uncached local fallback, new-window
restart previews before HTTP completion, late navigation rejection, runtime
source-path reconstruction, persisted secret exclusion and configuration
clearing. Four pure ViewPreviewStore tests cover restart context isolation,
rejected atomic writes, corrupt/incomplete/oversized/expired reads, recovery and
the 64-file bound. New-window restoration simulates restart in the same isolated
temporary data root; no native app startup, physical display/audio or live
provider is exercised. Timeouts remain failure diagnostics, not millisecond
performance budgets.

Cloud loading feedback adds one Windows group using routed Click on an actual
compiled genre button, a held synthetic server and the headless dispatcher timer
loop. It checks immediate old-content hiding, completed-step progress before
HTTP completion, cached branch reuse and navigation cleanup. Three pure
CompletedWorkProgress tests cover held/successful/faulted/cancelled steps,
concurrent completion and percentage bounds without a clock.

Cloud assembly checks also require selected server albums to appear without
complete album catalog requests, reuse of local facet input between branches,
and eviction of that input on catalog invalidation. Core tests cover selected
album/profile semantics, joined enriched facets, batch profile headers, 404/405
detail compatibility, bounds, cancellation and strict failure. An isolated
production TestServer album-batch check verifies authentication, profile
overrides, duplicate/missing IDs, empty requests and the 100-ID bound. These
checks do not measure latency in a personal or live remote library.

An exhaustive taxonomy test compares every known genre pair against independent
recursive descendant expansion and checks dynamic More genres behavior.
