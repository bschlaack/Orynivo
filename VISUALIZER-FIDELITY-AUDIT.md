# Milkdrop fidelity audit — 2026-09-23

The current engine is a Milkdrop-inspired renderer, not yet a faithful Milkdrop
implementation. Successful parsing, shader compilation, and CPU/GPU agreement
do not establish compatibility: the CPU reference itself has different semantics.
The findings below compare this checkout with the supplied local projectM source.
Reference paths are relative to that separate checkout; no reference source was
copied or linked into Orynivo.

## Corrected in this review

### P1: Custom warp output was drawn into a blur framebuffer

`Orynivo/Controls/VisualizerGlPipeline.cs`, `Render`: the warp target was bound
before `BuildShaderBlurLevels`, which changes both framebuffer and viewport.
The subsequent custom shader draw therefore targeted blur3 at reduced resolution,
leaving the actual warp target black. It could also sample the attached texture.
Restore the full-size ping framebuffer and viewport after building blur samplers.

Reproduction on the real WGL/NVIDIA GPU pipeline: a constant RGB
`(0.25, 0.5, 0.75)` warp with no overlay produced RGB `(0, 0, 0)` before the
fix and `(64, 127, 191)` in 8-bit readback afterward. Both runs reported GL error
zero, so the existing error diagnostic alone did not detect the fault.
`scripts/gl-harness/verify-warp-target.ps1` checks every pixel at 64×48 and 97×61
over two frames. This verifies the shared pipeline, not Avalonia/ANGLE integration.

### P1: Mesh motion accumulated across vertices

`Orynivo.Core/Visualization/PresetRenderer.cs`, `BuildMesh`: per-pixel code ran
repeatedly on the same slots without resetting motion inputs. For example,
`zoom *= 1.01` compounded across thousands of vertices instead of applying once
to each vertex's per-frame zoom. projectM's
`src/libprojectM/MilkdropPreset/PerPixelMesh.cpp`, `CalculateMesh`, explicitly
reseeds all ten motion variables for every vertex.

The implementation now reseeds zoom, zoomexp, rot, warp, cx/cy, dx/dy and sx/sy
and restores them afterward. The new regression failed before the fix and passes
afterward, checking all nine exported mesh components over two frames, with warp
also contributing to dx. All 13 `PerVertexMeshTests` pass.

## Remaining compatibility defects, in repair order

### 1. P1: Geometry and shader equation order

**Corrected for the OpenGL path.** `WarpSampling.SamplePosition` now divides by zoom and stretch,
subtracts the offsets, uses the reference's radial zoom `pow(zoom, pow(zoomexp, radius * 2 - 1))`,
takes the reference's aspect via `WarpSampling.GetAspect`, and applies the reference's
time-dependent `warp` displacement through `WarpSampling.WarpDisplacement` (carried as a tenth mesh
value and the `_orynivo_warp`/`_orynivo_warpTime`/`_orynivo_warpScale` uniforms). The warp now
transforms at the mesh vertices and interpolates the resulting coordinate: the OpenGL warp does it in
its vertex shader for both the fixed and the custom warp (the custom warp draws the mesh, and
`TranspileGlslWarpMesh` emits a fragment stage that no longer recomputes the position or re-emits the
per-pixel block), and the CPU mesh warp interpolates a per-vertex coordinate mesh. The per-vertex
block's `x`/`y`/`rad`/`ang` use the reference aspect. The Skia warp pass still composes the per-pixel
block into the fragment effect with the warped position in `x`/`y`, because a runtime effect has no
vertex stage.

- `WarpSampling.SamplePosition` multiplies by zoom and stretch and adds offsets.
  projectM's `MilkdropPreset/Shaders/PresetWarpVertexShaderGlsl330.vert` divides by
  zoom and stretch and subtracts offsets. Its radial zoom is
  `pow(zoom, pow(zoomexp, radius * 2 - 1))`, not Orynivo's
  `pow(zoom, 1 + zoomexp * radius * 2)`. Centres also use different spaces.
- `ShaderTranspiler.EmitWarpMain` computes the sample position **before** running
  per-pixel equations and then reads only x/y for the final uv. Motion writes such
  as `zoom`, `rot` and `dx` do not rebuild that position. The GL custom-warp path
  uses a full-screen quad, bypassing the prepared mesh entirely.
- `SeedFrameVariables` supplies aspect `(width/height, 1)`. projectM's
  `ProjectM.cpp`, `GetRenderContext`, supplies `(1, height/width)` in landscape
  and `(width/height, 1)` in portrait. This changes radius and aspect-dependent
  shader math, even with matching resolution.
- The nine-value mesh omits `warp`, and the fixed GL warp has no corresponding
  time-dependent warp displacement. projectM includes it in its vertex transform.
- projectM transforms texture coordinates at vertices and interpolates the
  resulting UVs. Orynivo interpolates motion values and transforms in the fragment
  shader; these are not equivalent for nonlinear transforms.

Repair as one explicit Milkdrop coordinate contract: frame equations → per-vertex
equations → vertex UV transform → interpolation → warp fragment shader. Preserve
the existing `.oryvis` coordinate behavior through an explicit legacy dialect;
silently changing the shared formula would also change built-in presets.

### 2. P1: Sampler names have the wrong meaning

**Corrected on the interpreter and Skia paths.** `ShaderSamplerName` parses the qualifier into a wrap
mode and a filter mode, `PixelBuffer.SampleShader` performs both, and the qualifier is stripped before
the generated textures are resolved, so `sampler_pw_noise_lq` reads the noise texture and every
qualified `main` sampler reads the same frame. The OpenGL pipeline resolves the base texture and binds
every `main`-family sampler to the stage's main frame, but `GlInterface` exposes no per-sampler state,
so a frame sampler's exact filter and wrap still applies only on the interpreter and Skia paths; the
fixed warp still blacks out out-of-range UVs.

`VisualizerGlPipeline.BindShaderSamplers` and `PresetRenderer` interpret
`sampler_pc_main` as the previous frame. In projectM's
`Renderer/TextureManager.cpp`, `ExtractTextureSettings`, `pc_` means **point
filtering + clamp**, `fc_` means filtered + clamp, and `pw_`/`fw_` request wrap.
These are different sampling modes on the same main texture, not different
moments in time. Fix CPU and GPU together and test clamp/repeat and nearest/linear
with a small asymmetric texture. The fixed warp also blacks out out-of-range UVs
instead of implementing the preset's wrap/clamp behavior.

### 3. P1: Composite and feedback stages do not match

The GL post pass always applies decay, video echo, gamma and overlays before
running the optional comp shader, and stores that result as feedback.
projectM's `MilkdropPreset/FinalComposite.cpp` selects a custom composite shader
**or** the legacy video echo/gamma/filter path. Applying legacy effects before a
custom comp shader can change its input and repeatedly amplify feedback.
projectM's `MilkdropPreset.cpp` also places shapes/waves before centre darkening
and borders; Orynivo adds its overlay after them. Shader blur inputs and their
frame timing differ too: Orynivo rebuilds the comp blur chain from the just-created
feedback, whereas projectM updates its blur textures earlier in `RenderFrame`.

Define each pass's input, output, lifetime and sampling mode explicitly, then
test isolated decay, echo, gamma, border and custom-comp fixtures. Keep display
output separate from the next frame's feedback.

### 4. P1: Custom waves are not implemented with Milkdrop semantics

`VisualizerPreset.ParseWaves` loads equation blocks but does not construct a
separate custom-wave parameter state from `wavecode_N_*`. `DrawWave` instead uses
global wave settings and treats slot zero as the default waveform. projectM's
`MilkdropPreset/CustomWaveform.cpp` reads independent enabled/sample/spectrum/
scaling/smoothing/color settings and supplies `sample`, `value1` and `value2` to
per-point code. Orynivo currently supplies the audio amplitude as `sample`.
The basic wave modes are approximated by circles, lines and mirrored lines, and
`DrawOverlay` additionally draws spectrum bars whenever the wave alpha permits.
These choices add or remove prominent geometry regardless of shader correctness.

Separate the default waveform from the four custom waves, implement their own
state and sample contracts, and only draw spectrum geometry when requested.

### 5. P1: Audio variables drive different branches

`AudioSpectrumAnalyzer.Analyze` clamps band magnitudes to `[0,1]`, averages them
and exposes smoothed bass/mid/treble. projectM's `Audio/Loudness.cpp` divides
current and short-term levels by their long-term average; values can exceed one.
A preset condition such as `above(bass,1.2)` cannot trigger with the current
analyzer. Stereo is also averaged to mono before waveform processing.

Implement a separate Milkdrop audio-analysis adapter with relative band levels,
matching attenuation and stereo waveform/spectrum inputs. Keep it off the audio
thread and do not change other visualizations' normalized level contract blindly.

## Validation strategy

1. Keep the two new regressions as gates for the corrected defects.
2. Add analytic fixtures for identity, zoom, stretch, translation, aspect,
   coordinate-reporting shaders, sampler modes, echo and custom waves.
3. Feed both engines identical PCM, frame times, dimensions and mesh dimensions.
   Start with presets without random textures, external assets or random state.
4. Compare intermediate UVs, feedback and final output, then render representative
   real presets for hundreds of frames. A single final-image correlation cannot
   identify the faulty stage.

The existing oracle uses different audio representations on the two sides, and
its reference noise is not deterministic. It remains useful for visual diagnosis,
but its scores do not prove fidelity. No new collection-wide similarity claim is
made by this review. Full Milkdrop fidelity requires the remaining work above;
the two fixes remove confirmed faults but do not complete that work.

## Completed checks

- `scripts/verify-all.ps1` in Debug and Release: all managed builds, the non-Windows
  desktop compile, all three test projects, localization/MCP parity and action pins passed.
- All 13 mesh tests passed; the added regression was observed failing before correction.
- `scripts/gl-harness/verify-warp-target.ps1` passed on the real GPU at both sizes.
- `git diff --check` passed. No end-to-end Avalonia/ANGLE visual comparison or
  collection-wide projectM image comparison was performed in this review.
