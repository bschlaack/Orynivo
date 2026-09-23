# Milkdrop fidelity recheck — 2026-09-23

Reviewed baseline: `7be0c5f`. The user's visual reference is **MilkDrop in Winamp**.
The supplied projectM checkout is a source/reference oracle, not proof that a
render matches that particular Winamp version. No Winamp render was captured here.

**Initial finding at the reviewed baseline: the previous repair list was only partially implemented.** Several
changes existed in source but were not connected to the actual GL program state.
CPU tests and successful shader compilation did not catch those integration bugs.

## Reproduced and corrected

### P1: Missing fixed-warp decay uniform destroys all feedback

`VisualizerGlPipeline.Init` resolved only four fixed-warp uniforms. The later
`Set` calls for `uDecay`, `uWarpTime` and `uWarpScale` silently did nothing because
`Set` only writes previously resolved locations. GL initializes uniforms to zero:
the fixed warp therefore multiplied every sampled pixel by zero. A preset with
no custom warp lost all trails and retained only the new overlay each frame.

The regression emits an asymmetric waveform on frame zero only, with identity
motion, warp zero and decay one. Before correction the red-channel sum was
34,500 on frame zero and zero on frames one and two. Uniforms are now resolved.

### P1: Fixed identity warp reflects feedback vertically each frame

After fixing decay, the same fixture alternated its lit region between the upper
and lower halves. The mesh already uses GL positions and the feedback is bottom-up;
the fragment shader's extra `1 - uv.y` reflected the image. Removing that extra
flip makes all three saved identity frames byte-identical. This is a feedback
invariant, independent of audio, randomness or the external reference.

### P1: Custom mesh uniforms were also never resolved

After compiling a custom warp, `_warpShaderUniforms` was empty. Dynamic fragment
uniform binding resolved preset names, but not the vertex stage's `uFrameWidth`,
`uFrameHeight`, `uNeedsRadius`, `uWarpTime` or `uWarpScale`. Subsequent `Set` calls
were no-ops. The radius stayed zero, radial zoom was disabled and the aspect
fell back to a square. Resolve the vertex uniforms when linking the custom warp.
The GPU regression checks a radius-output shader in landscape and portrait.

### P1: Extra legacy fade modifies custom warp output

With `fDecay=0.5`, a custom warp returning RGB `(0.5,0.5,0.5)` and an identity
comp shader produced centre red **127 in projectM and 63 in Orynivo**. The GL
post pass reapplied legacy decay to a custom shader that owns its own output.
The post-pass multiplier is now one: the fixed warp already fades its own output,
and a custom warp must implement whatever fade it needs. The regression checks
every pixel of the constant custom output over two frames.

### Maintenance and comparison fixes

- Removed unused motion interpolation local functions left behind by the UV-mesh
  migration; they produced CS8321 and failed the required warnings-as-errors build.
- The oracle's BMP comparison now skips row padding. Previously widths not
  divisible by four caused padding to count as pixels and final pixels to be omitted.

## Follow-up implementation

The user requested implementation of the outstanding corrections. The following
contracts are now implemented and covered by targeted tests:

- **Shapes:** numeric `shapecode_N_*` and equation `shape_N_*` namespaces,
  enabled flags, sparse indices, zero-to-one Milkdrop positions, quarter-turn
  polygon offset, aspect correction, centre/edge colour interpolation, separate
  instance evaluation and live border colours/thickness. Legacy Orynivo `shape_*`
  numeric presets retain their coordinate convention.
- **Equation spelling:** `wave_0_per_frame1`, `wave_0_per_point1`, `shape_0_init1`
  and the underscored forms are accepted. Previously the compact forms were
  silently ignored even though they occur in real presets.
- **Element state:** private frame/point contexts, init seeded with saved element
  parameters, preset Q inputs copied into elements, init T restored per frame,
  frame Q/T copied into point contexts, no writes back into preset state, and
  one execution of each enabled wave's frame block. User frame variables persist
  privately. Reset clears those contexts.
- **PCM waves:** 512 contiguous stereo samples, the missing 128-times conversion
  before the reference 0.004 scale, centered sample windows and channel separation.
  The normalized general-purpose audio API remains separate.
- **Shader inputs:** relative bass/mid/treble reach GLSL as well as equations;
  aspect factors are at most one, and FPS comes from the supplied frame interval.
- **GL samplers:** independent native sampler objects for point/linear and
  clamp/repeat; custom names are resolved from shader declarations. Fixed warp
  uses `bTexWrap` instead of black-outside sampling. Sampler bindings are released
  before fixed passes and before returning to Avalonia.
- **Warp constants:** `fWarpAnimSpeed` and `fWarpScale` reach CPU, Skia and GL warp
  inputs; time is multiplied by speed and displacement uses reciprocal scale.
- **GL blur:** progressive min/max scale and bias, first-level edge darkening,
  and GetBlur decoding. Warp reads the retained chain; the chain is then updated
  from the previous feedback before overlay compositing and read by comp, matching
  the supplied projectM render order. Equal/reversed ranges are widened to 0.1;
  the local projectM source contains a zero-width typo in this safeguard, which
  is deliberately not copied. Newly allocated retained blur targets start black.
- **Overlay compositing:** RGB carries accumulated premultiplied colour and alpha
  carries coverage. Non-additive shapes/waves now attenuate the underlying feedback
  instead of merely adding colour. Shapes precede custom waves and the default wave.
- **Final display:** legacy echo and gamma use a separate GL display target.
  Echo sees the completed overlay/borders and gamma is a brightness multiplier,
  not a power. Neither enters next-frame feedback. The custom comp sampler flips
  the bottom-up frame texture into its top-down UV contract; an asymmetric real
  shape fixture exposed the previous reflection.
- **Oracle:** native frame time is explicit; FPS is forwarded using invariant
  formatting; the GL harness analyzes the same constant/silent PCM block as the
  native oracle. BMP metrics ignore row padding. Noise and analysis algorithms
  remain different, so arbitrary-preset image equality is still not a valid gate.

## Validation

- `scripts/gl-harness/verify-feedback.ps1`: identity feedback, decay, landscape
  and portrait custom radius, shader-owned fade.
- `scripts/gl-harness/verify-fidelity.ps1`: simultaneous point/linear and
  clamp/repeat reads, compressed and decoded blur ranges, multi-frame legacy
  gamma without feedback accumulation, asymmetric shape orientation and alpha.
- `scripts/gl-harness/verify-warp-target.ps1`: full-size custom warp target.
- `MilkdropFidelityRegressionTests`: real key spellings, enabled/sparse shapes,
  isolated wave state, once-per-frame execution, init T restoration, PCM scaling,
  contiguous sample windows, warp parameters and stable FPS.
- Complete Debug and Release verification through `scripts/verify-all.ps1`: passed.
- All three GPU regression scripts passed on the NVIDIA RTX 4090 WGL context.
- Local native projectM oracle rebuilt with explicit frame times.

The asymmetric shape fixture (`x=0.25`, `y=0.75`, alpha 0.5 over grey 0.2)
produced centre RGB approximately `(153,26,26)` in projectM. The GL regression
checks that same value and position, allowing two byte levels for rasterization.
The fixture must declare Milkdrop 201 / PSVERSION 2 for the native oracle to
actually run the custom shaders.

## Remaining compatibility limits

The original MilkDrop source is available as a reference: the
[MilkDrop-MusicVisualizer](https://github.com/xlimit91/MilkDrop-MusicVisualizer) archive carries
`milkdrop_225c_src` (Nullsoft, BSD-style licence). It is read-only reference material for behavior
and conventions; no code is copied from it. It settled the shape conventions below.

- Milkdrop shapes were drawn vertically mirrored: the reference's shape space is Direct3D's y-up
  space (`v[0].y = shape_y*-2+1`, `milkdropfs.cpp`), so `shape_y` counts from the top, while the
  overlay rasterizer is y-down. A comparison against projectM put a `shapecode_0_y=0.75` shape 72 %
  from the top while Orynivo drew it 25 % down. `BuildVertices` and the fan centre now convert, and
  both paths land 75 % down. The waveform was already correct because the reference measures `wave_y`
  the other way round (top is one). The shape fill's additive/normal blending (`SRCALPHA` with
  `INVSRCALPHA`, or `ONE` additive) and the fan's repeated first rim vertex (`v[sides+1] = v[1];`)
  were confirmed against the same source.

- Shaders read `hue_shader` as zero, which blacked out every preset whose warp or comp shader uses it.
  The reference defines `hue_shader` as the final quad's vertex diffuse (`#define hue_shader
  _vDiffuse.xyz`) and always computes it, four corners of `0.5 + 0.5*normalised sine`
  (`milkdropfs.cpp`, `fShaderAmount = 1; // since we don't know if shader uses it or not!`). It is now
  bound per pixel on the CPU and mixed from the four corners on the GPU through the twelve
  `hue_shader_<channel><corner>` uniforms; `verify-fidelity.ps1` pins it. `$$$ Royal - Mashup (138)`
  went from a black frame to a rendered one on both paths.
- Milkdrop's motion vectors are not an engine grid: `mv_x`/`mv_y` size the drawn arrow grid,
  `mv_dx`/`mv_dy`/`mv_l`/`mv_a` are ordinary blendable variables (`mv_a` defaults to one, the legacy
  key is `bMotionVectorsOn`), and `DrawMotionVectors()` only draws that grid. Orynivo's
  `mv_enabled`-gated motion recording is its own extension and was previously described as if it were
  reference behaviour.

These corrections do **not** establish complete Winamp MilkDrop fidelity:

- Textured custom-shape fills now sample the frame through the reference's fan
  texture coordinates on the CPU overlay rasterizer; a GPU geometry path and the
  reference's texture antialiasing are still absent, and CPU rasterized
  lines/polygons differ from native antialiasing.
- The default waveform's geometries are now the reference's per-mode math
  (`MilkdropWaveform`). The analyzer now uses the reference's one-sample
  pre-emphasis on every FFT input and its raised-sine window period (the complete
  transform length, not length minus one). The stereo waveform is aligned to the
  previous frame with the reference's multi-octave cross-correlation
  (`WaveformAligner`), so a custom waveform holds its shape instead of sliding. The
  aligner keeps the reference's margin of 96 samples after the window, so the exposed
  window is the older part of a 608-sample buffer and lags the newest audio by up to
  96 samples, exactly as the reference does. One reference analysis step is **not**
  adopted:
  - The reference multiplies each magnitude by `-0.02 * ln((half - i) / half)`, a
    logarithmic frequency equalization. Measuring the reference itself
    (`scripts/projectm-oracle/measure-bands.cpp`) shows this does **not** suppress
    the bass: a 60 Hz onset drives projectM's bass band to 125 (peak 249), a 6 kHz
    onset drives its mid band to 125, and a 12 kHz onset drives its treble band to
    83.5. It does change how much broadband content each band sums. Adopting it
    needs the reference's magnitude scale as well: projectM scales every sample by
    128 and leaves its FFT unnormalized, so its magnitudes are roughly `2.6e5`
    times Orynivo's normalized ones, and the reference's own guard
    (`|long-term average| < 0.001`, `Loudness.cpp:49`) therefore fires on Orynivo's
    magnitudes where it would not on the reference's.
  Very large/custom waveform coordinates need fuller clipping.
- The legacy final effects now include the reference's animated hue shade and its
  brightness gain on the display target; the CPU and OpenGL paths both apply them,
  and the per-preset hue offsets are seeded from the preset name for
  reproducibility. CPU/Skia blur remains an approximation.
- Existing per-pixel variable lifecycle and fallback-path differences need more
  named-preset comparisons. These tests do not prove every expression/shader dialect.
- GPU checks use a hidden WGL context. Avalonia/ANGLE composition and a direct
  Winamp capture with identical music, preset and settings were not exercised.
  `scripts/visualizer-compare/render-compare.ps1` now renders both engines under
  identical resolution, frame time and audio and writes a matching test tone, so a
  reference-player capture can be produced and compared.

The old audit's blanket “Corrected” labels must not be read as completion of these
remaining features. Use isolated reference fixtures and then a named Winamp preset
with matched audio, time, resolution and settings to assess the remaining difference.
