# Visualizer status

Reviewed against the repository on 2026-10-10. This document describes the
implementation and available verification, not a new measurement of Winamp
fidelity or real-device performance.

## Current execution paths

- The desktop defaults to `VisualizerGlPresenter` and `VisualizerGlPipeline`.
  Supported presets use OpenGL/GLSL for the pixel pipeline; Core still evaluates
  preset expressions, motion, and CPU overlays on a background render thread.
  Shape fills and custom-wave geometry can be handed to the GPU when the
  presenter supports them.
- `VisualizerWindow.ConfigureRenderer` selects GPU execution only when the
  preset's required shader stages can be emitted. Unsupported presets and failed
  or unavailable GL contexts retain the complete CPU frame path.
- The bitmap fallback presents a CPU-rendered `PixelBuffer` through a
  `WriteableBitmap`. `SkiaShaderRunner` uses raster `SKSurface` objects: its
  SkSL runtime effects run on the CPU, even though final presentation may be
  accelerated. Never call these passes a GPU renderer.
- `PresetRenderer.UseSkiaPasses` enables CPU runtime effects for comp and warp
  shader passes. `UseSkiaFramePasses` separately controls full-frame helper
  passes and is disabled by default. The interpreter remains the fallback.
- `ORYNIVO_VISUALIZER_OPENGL=0` selects bitmap presentation;
  `ORYNIVO_VISUALIZER_PIXELWARP=0` disables the emitted pixel-warp path.
- Analysis and rendering must never execute on the audio pump. Closed
  visualizers disable the audio tap; UI presentation consumes coherent frame
  snapshots rather than the render thread's live mutable buffers.

## Implemented compatibility

The implementation includes textured custom shapes, per-mode default-wave
geometry, preset aliases, shader translation, retained blur levels, independent
sampler modes, overlay alpha, display-only composite stages, and reference-based
audio-analysis rules. Their detailed invariants live in
[Visualizer contracts](VISUALIZER-CONTRACTS.md) and the applicable nested
`AGENTS.md` files.

These features do not establish complete MilkDrop compatibility. The CPU, Skia,
and GL paths must preserve their documented coordinate, feedback, and sampling
contracts; a passing parser or shader compiler proves acceptance, not the final
picture's fidelity.

## Verification and evidence limits

- `Orynivo.Core.Tests` contains focused waveform, spectrum, mesh, sampler,
  shader, and feedback regression tests.
- `Orynivo.Tests` contains preset collection, Skia comparison, frame-dump,
  brightness, and render-timing diagnostic tests. Collection-dependent checks
  require the matching external preset directory and are not evidence for
  collections they did not exercise.
- GPU and reference-player harnesses live under `scripts/local/` when present in
  a developer checkout. That directory is ignored by Git, so these tools and
  their reports are not guaranteed to exist in a fresh clone.
- `ROADMAP.md` and `CHANGELOG.md` preserve historical implementation and
  verification notes. Previously referenced `VISUALIZER-FIDELITY-AUDIT.md` and
  `VISUALIZER-FIDELITY-RECHECK.md` are absent from this checkout. Do not cite
  their missing contents as current verification.
- Complete Winamp fidelity requires matched preset, texture assets, resolution,
  aspect ratio, audio input, frame timing, initial state, and reproducible
  captures. Dynamic unsynchronized single-frame comparisons are diagnostic only.
  Hardware-specific ANGLE/OpenGL behavior requires actual platform runs.

Future reports should record the revision, available harness, inputs, execution
path, commands, results, and remaining deviations. Keep media metadata,
credentials, authenticated URLs, and private device addresses out of reports.
