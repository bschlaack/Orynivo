# Orynivo.Server Instructions

This file applies to `Orynivo.Server/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build Orynivo.Server/Orynivo.Server.csproj`.
- Endpoint, configuration, packaging, compatibility, or API changes must be
  reflected in `README.md`, `CHANGELOG.md`, and the applicable `AGENTS.md`.

## Server Invariants

- Keep the server cross-platform `net8.0` and free of Windows-only dependencies.
- Every endpoint except `/api/health` requires the configured API key through
  `X-Api-Key` or `?key=`. Do not log or expose the key.
- Use only the public `Orynivo.Core` surface.
- Materialize SQLite-backed endpoint results before disposing their connection.
- Preserve byte-range streaming and cancellation of FFmpeg/transcode processes
  when clients disconnect.
- `cue://` tracks and `mka://chapter/` tracks are virtual source segments and
  are transcoded from their stored physical source and time boundaries.
- Keep external metadata/artwork searches on the client; the server stores and
  serves client-provided results.
- Keep `SkiaSharp.NativeAssets.Linux.NoDependencies`; do not add an ImageMagick
  runtime dependency.
- Linux service data belongs under `ORYNIVO_DATA_DIR=/var/lib/orynivo-server`.
  Do not fall back to the service user's non-writable home directory.
- Editable Linux configuration belongs under `/etc/orynivo-server`; packaged
  defaults under `/usr/lib/orynivo-server` are read-only and replaceable.
  Because this editable file is layered after `WebApplication.CreateBuilder`,
  `Program.cs` explicitly reapplies its configured Kestrel maximum request-body
  size to the web host.
- API additions must consider older clients/servers and the existing capability
  probing behavior.
- `GET /api/library/summary` returns `DashboardLibrarySummary` directly from an
  aggregate database query so dashboard counters never require complete track
  or album payloads.
- Remote package updates remain disabled by default. The server process may only
  stage a signed, matching DEB/RPM bundle beneath its data directory; installation
  belongs to the fixed-command root systemd helper, which independently verifies
  the manifest and hash and never accepts client-provided commands or paths.
  The package endpoint disables Kestrel's smaller default request limit only for
  that route and retains its own bounded one-GiB streaming limit.

Consult the detailed endpoint, configuration, scan, cache, and package rules in
the root `AGENTS.md` before changing those areas.
