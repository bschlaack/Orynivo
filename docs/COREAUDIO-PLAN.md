# macOS CoreAudio output plan

## Target behavior

On macOS, **Create output** and **Configure output** will offer distinct
**OpenAL** and **CoreAudio** output types. Existing OpenAL profiles retain their
device, behavior, and persisted values. CoreAudio is a native local-device PCM
path; AirPlay remains a separate existing output type.

CoreAudio must select a stable device UID, match the device's nominal sample
rate to the selected PCM rate, confirm the change before rendering, and restore
the previous rate when releasing the device. Pause retains device ownership;
Stop, output switching, explicit release, shutdown, and failed startup must stop
native rendering before restoration. An external device/rate/ownership change
must not be overwritten blindly.

Rate matching alone is not evidence of bit-perfect playback. Native stream
format, actual PCM bytes, device processing, volume, and DSP must also be
validated. Bluetooth and AirPlay devices cannot inherit a USB-DAC bit-perfect
claim from their presence in CoreAudio enumeration.

## Delivery steps

### 1. Device control and lifecycle foundation

- [x] Add macOS HAL device enumeration and stable-UID resolution.
- [x] Read advertised nominal rates and current device state.
- [x] Implement exact-rate acquisition with bounded readback confirmation.
- [x] Implement optional mandatory hog ownership without silent downgrade.
- [x] Reject concurrent process-local leases for the same UID.
- [x] Restore owned rate changes and release owned hog mode after failures or
      cancellation; share asynchronous disposal completion.
- [x] Exercise lifecycle decisions with synthetic device adapters on Windows.
- [ ] Validate native property layouts, permissions, asynchronous transitions,
      and removal behavior on Apple Silicon and Intel Macs.

This foundation lives in `Orynivo/Audio/CoreAudio/`. It is deliberately not
wired into playback or profile selection yet. Current macOS playback still uses
OpenAL. It introduces no new visible strings or settings fields.

### 2. Native PCM output and profile selection

- [ ] Implement a CoreAudio HAL/AudioUnit renderer with a bounded PCM ring and a
      callback that never waits, decodes, allocates, or invokes UI code.
- [ ] Negotiate a supported stereo stream format and report the delivered rate
      and precision. Reject unsupported exact-rate mode; an explicitly chosen
      converted mode must report conversion. A positive maximum PCM rate is a
      strict ceiling; DSD-to-PCM rate choices retain exact DSD-rate divisions.
- [ ] Feed the existing segment-aware FFmpeg decoder for local, server, Plex,
      radio, podcast, CUE, and MKA sources. Do not duplicate queue ownership.
- [ ] Support Pause/Resume, Seek, completion, device release, startup
      cancellation, and obsolete-player retirement through the existing
      factory/session/readiness/drain contracts.
- [ ] Preserve gapless PCM within one rate/format. Drain and reconfigure at a
      rate change rather than silently resampling the next track to the old
      rate. Account for the resulting hardware switching interruption.
- [ ] Add macOS-only CoreAudio/OpenAL choices and native device information to
      the shared profile dialog and output labels. Persist UIDs, never runtime
      HAL IDs. Preserve existing enum values and profile migration.
- [ ] Add per-CoreAudio-profile automatic rate matching, exclusive access, and
      restore-on-release options. Default automatic matching and restoration to
      enabled, and hog mode to opt-in. Strict bypass requires matching and
      exclusive ownership. Preserve the shared settings for maximum PCM rate,
      DSD-to-PCM, and DSP where supported; unavailable options must be disabled
      with an explanation.
- [ ] Add a strict PCM bypass mode: unity software volume, no ReplayGain, boost,
      EQ, crossfeed, normalization, fades, or float sample rewriting. Ordinary
      DSP-enabled mode must not claim bit-perfect output.
- [ ] Keep native DSD and DoP outside the initial CoreAudio scope. Convert DSD
      to PCM through the existing decoder, and make the actual output clear.
- [ ] Localize all labels, errors, and capability explanations in all seven
      supported languages, including complete built-in resources.
- [ ] Update README, changelog, architecture reference, and client invariants
      with the actual integrated behavior and remaining hardware limits.

CoreAudio control exceptions currently remain internal and carry no custom UI
text. Integration must map them to localized, sanitized failure messages; never
expose native exception details, device UIDs, source URLs, or decoder arguments
as diagnostics to external clients.

### 3. Integration and hardware acceptance

- [ ] Add fake-renderer tests for buffering, underflow, seek generations, drain,
      device changes, rate changes, callback retirement, and cancellation.
- [ ] Cover real profile creation/editing, reload/migration, output switching,
      retained position/pause, and visible backend/capability labels.
- [ ] Run full Debug and Release verification plus macOS builds for both release
      architectures. Keep Windows, Linux, and OpenAL regression checks.
- [ ] Test on a Mac with built-in output and a USB DAC: 44.1/48/88.2/96/176.4/
      192 kHz as supported, 16/24-bit sources, and mixed-rate playlists.
- [ ] Observe Audio MIDI Setup or independent HAL measurements and the DAC's
      rate indication, including restoration after Stop/release/quit/failure.
- [ ] Exercise busy devices, denied/read-only properties, competing audio,
      unplug/reconnect, default-device changes, sleep/wake, and short tracks.
- [ ] Independently capture or verify bit-test output before advertising a
      verified bit-perfect device path. Listening alone does not prove it.

## Initial implementation limits

Software verification on 2026-11-06: all 27 focused device-control tests pass;
full Debug and Release `scripts/verify-all.ps1` runs each pass all eleven
checks. An additional Release compatibility build with `ORYNIVO_MACOS` defined
passes with zero warnings/errors on Windows. Documentation formatting/lint
passes. These are synthetic ownership tests and compile checks, not native Mac
or USB-DAC playback evidence.

The control-plane tests establish software ownership and cleanup decisions, not
native macOS behavior. The adapter uses bounded polling for nominal-rate
confirmation. Renderer integration must additionally observe device/rate changes
during playback and invalidate a lost lease. Cleanup readback guards cannot
detect an external actor writing the same value, and an unresponsive driver or
process crash can prevent restoration. Do not promise crash-proof restoration or
treat a successful nominal-rate write as proof of the physical stream format.

API references: Apple's [nominal sample rate property][rate], [device UID
property][uid], and [hog mode property][hog].

[rate]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertynominalsamplerate
[uid]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertydeviceuid
[hog]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertyhogmode
