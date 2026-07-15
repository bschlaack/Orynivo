# Orynivo.Core Instructions

This file applies to `Orynivo.Core/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build Orynivo.Core/Orynivo.Core.csproj` and also build each
  affected consumer when a public contract changes.
- Public/internal C# APIs require complete English XML documentation.

## Core Invariants

- Keep the project cross-platform `net8.0`; do not introduce Avalonia, Windows,
  DPAPI, WASAPI, ASIO, or other platform-specific dependencies.
- Put shared library scanning, SQLite persistence, search, streaming models and
  clients, FFmpeg primitives, and web-fetching behavior here.
- Preserve SQLite migrations, stable IDs, WAL behavior, CUE virtual-path
  identity, user favorites, artwork caches, ReplayGain data, and `added_at`.
- Scanner, watcher, and reconciliation writes must keep SQLite and Lucene in
  sync and use the shared scanner gate.
- Keep compact query models compact; do not add artwork BLOBs, lyrics, or full
  records to list/facet/folder queries.
- `AudioDatabase.GetListeningTrend` supports up to 366 equal chronological
  buckets so the client can request daily Dashboard points without materializing
  playback-history rows.
- `ArtistNameNormalizer.CreateComparisonKey` is the shared identity key for
  comparing artist names across local and Orynivo Server catalogs.
- Web page fetching must retain SSRF protection, connect-time address checks,
  redirect and size limits, text-only responses, timeouts, and audit logging.
- Streaming URL builders may carry credentials for immediate playback, but such
  URLs must never be persisted, logged, documented, or returned to a model.
- `Orynivo.Server` has no `InternalsVisibleTo` grant; server-facing Core APIs must
  be deliberately public.

Consult the detailed database, scanner, search, streaming, audio, and web rules
in the root `AGENTS.md` before modifying those areas.
