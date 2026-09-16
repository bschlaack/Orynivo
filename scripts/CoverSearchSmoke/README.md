# Cover search verification

`dotnet run --project scripts/CoverSearchSmoke` runs offline transport regression
checks against the production source: preview-only requests, concurrency,
incremental delivery, partial failures, retry limits, cancellation, response size
bounds, explicit original downloads and punctuation fallbacks.

Opt-in live checks contact MusicBrainz and Cover Art Archive for Sade / Lovers Rock:

```powershell
dotnet run --project scripts/CoverSearchSmoke -- --live
dotnet run --project scripts/CoverSearchUiSmoke -- --live
```

The second command runs the actual Avalonia dialog with headless Skia rendering,
uses an isolated temporary data directory, and checks incremental binding,
duplicate-start suppression, supersession and cancellation on close. It does not
save artwork to a real library. External failures can fail these live checks;
elapsed times are observations, not fixed performance assertions. The UI phase
log contains no artist/album names or URLs.
