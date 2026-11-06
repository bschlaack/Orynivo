# macOS CoreAudio output plan

## Target behavior

On macOS, **Create output** and **Configure output** offer distinct **OpenAL**
and **CoreAudio** output types. Existing OpenAL profiles retain their device,
behavior, and persisted values. CoreAudio is a native local-device PCM path;
AirPlay remains a separate existing output type.

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

The device-control foundation lives in `Orynivo/Audio/CoreAudio/` and now feeds
the experimental native renderer and profile selection below. Mac/DAC validation
remains pending.

### 2. Native PCM output and profile selection

- [x] Implement a CoreAudio HAL/AudioUnit renderer with a bounded PCM ring and a
      callback that never waits, decodes, allocates, or invokes UI code.
- [x] Validate the native stereo S32 client format and delivered nominal rate.
      Reject unsupported exact-rate mode. Device information distinguishes the
      client format from physical device precision. With matching disabled,
      explicitly convert to the current nominal device rate. A positive maximum
      PCM rate is a strict ceiling; DSD-to-PCM rate choices retain exact
      DSD-rate divisions.
- [x] Feed desktop-resolved local, server, Plex, radio, podcast, CUE, and MKA
      sources through the existing segment-aware FFmpeg decoder. No second queue
      is introduced. Physical playback/source acceptance is still open.
- [x] Support Pause/Resume, Seek, completion, device release, startup
      cancellation, and obsolete-player retirement through the existing
      factory/session/readiness/drain contracts.
- [ ] Preserve gapless PCM within one rate/format. Drain and reconfigure at a
      rate change rather than silently resampling the next track to the old
      rate. Account for the resulting hardware switching interruption.
- [x] Add macOS-only CoreAudio/OpenAL choices and native device information to
      the shared profile dialog and output labels. Persist UIDs, never runtime
      HAL IDs. Preserve existing enum values and profile migration.
- [x] Add per-CoreAudio-profile automatic rate matching, exclusive access, and
      restore-on-release options. Default automatic matching and restoration to
      enabled, and hog mode to opt-in. Strict bypass requires matching and
      exclusive ownership. Preserve the shared settings for maximum PCM rate,
      DSD-to-PCM, and supported DSP. Profile bypass enforces its required
      options; unsupported crossfeed/normalization and gapless support are
      documented separately rather than advertised as implemented.
- [x] Add a strict PCM bypass mode: unity software volume, no ReplayGain, boost,
      EQ, crossfeed, normalization, fades, or float sample rewriting. Ordinary
      DSP-enabled mode must not claim bit-perfect output.
- [x] Keep native DSD and DoP outside the initial CoreAudio scope. Convert DSD
      to PCM through the existing decoder, and make the actual output clear.
- [x] Localize all labels, errors, and capability explanations in all seven
      supported languages, including complete built-in resources.
- [x] Update README, changelog, architecture reference, and client invariants
      with the actual integrated behavior and remaining hardware limits.

Playback maps control/render/decode failures to localized sanitized messages.
Keep native exception details, source URLs and decoder arguments out of UI and
external diagnostics. Device UIDs remain local profile identities.

### 3. Integration and hardware acceptance

- [x] Add synthetic ring/player tests for buffering, underflow, fragmented PCM,
      seeks and overlapping seek epochs, drain/restoration, rate changes, native
      retirement ordering and cancelled late decoder preparation. Actual native
      callback/device-removal behavior remains part of Mac acceptance.
- [x] Cover the compiled profile dialog's native/OpenAL choices, UID/options
      Save, seven-language accessible layout, profile normalization/cloning and
      JSON roundtrip using synthetic device boundaries on Windows.
- [ ] Validate native profile switching and retained position/pause on a Mac,
      including physical device/capability labels and native window behavior.
- [x] Run full Debug and Release verification, the shared Unix compatibility
      compile and an explicit macOS source-branch compile on Windows.
- [ ] Run native macOS builds/runtime acceptance on both release architectures.
- [ ] Test on a Mac with built-in output and a USB DAC: 44.1/48/88.2/96/176.4/
      192 kHz as supported, 16/24-bit sources, and mixed-rate playlists.
- [ ] Observe Audio MIDI Setup or independent HAL measurements and the DAC's
      rate indication, including restoration after Stop/release/quit/failure.
- [ ] Exercise busy devices, denied/read-only properties, competing audio,
      unplug/reconnect, default-device changes, sleep/wake, and short tracks.
- [ ] Independently capture or verify bit-test output before advertising a
      verified bit-perfect device path. Listening alone does not prove it.

## Initial implementation limits

The native renderer/profile implementation is experimental. The initial player
uses existing single-track queue advancement; a same-rate gapless session,
native DSD, DoP, crossfeed, and loudness normalization are not implemented.
Unprocessed PCM bypasses Orynivo processing, not unspecified processing by
CoreAudio, the device driver or the DAC. No physical bit-perfect claim is made.

Software tests exercise the production ring, rate policy, profile persistence,
player orchestration and shared dialog against synthetic device/decoder/output
boundaries. They do not execute Apple's HAL on Windows. Final Debug/Release,
compatibility compilation and layout evidence are recorded at completion below.

Nominal-rate confirmation uses bounded polling; active health checks run every
200 ms. A device change can therefore be observed after output has already been
interrupted or converted by the driver. Cleanup readback cannot detect another
actor writing the same value. Device removal, an unresponsive driver or a crash
can prevent restoration. After unrecoverable native disposal failure, retaining
an outstanding callback's managed root/device lease is intentional; do not claim
that such a failure released the device. Position follows consumed media frames
and excludes underflow/paused silence; hardware latency can delay audible
output.

API references: Apple's [nominal sample rate property][rate], [device UID
property][uid], and [hog mode property][hog].

## Software verification on 2026-11-06

- All 50 focused CoreAudio tests pass. Full Debug and Release
  `scripts/verify-all.ps1` runs each pass all eleven checks, including three
  unit-test projects, the 68-group Windows UI smoke process and seven-language
  localization parity (1,049 desktop keys).
- A separate Release `OS=Unix` build with `ORYNIVO_MACOS` defined passes with
  zero warnings/errors. This is compilation on Windows, not native Mac runtime
  evidence or an Apple Silicon/Intel hardware result.
- The new dialog group checks seven languages, two themes and two widths using a
  simulated Mac policy and injected device lists. German dark and Hindi light
  captures at 440 DIP were visually inspected. After increasing hint contrast,
  the final Debug UI smoke was rerun successfully; Release includes that change.
- No personal catalog, output device or audio application was opened/stopped.
  The native HAL callback and real FFmpeg/native-output composition still need
  physical acceptance. Same-rate gapless output remains a separate follow-up.

[rate]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertynominalsamplerate
[uid]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertydeviceuid
[hog]:
  https://developer.apple.com/documentation/coreaudio/kaudiodevicepropertyhogmode
