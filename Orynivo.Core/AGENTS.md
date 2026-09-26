# Orynivo.Core Instructions

This file applies to `Orynivo.Core/` and supplements `../AGENTS.md`.

## Completion

- Follow the root mandatory completion checklist.
- Build with `dotnet build Orynivo.Core/Orynivo.Core.csproj` and also build each
  affected consumer when a public contract changes.
- Public/internal C# APIs require complete English XML documentation.

## Core Invariants

- Milkdrop custom elements share a **layout**, never mutable frame/point slot arrays.
  Seed saved parameters before init, preserve private user variables, restore init T
  before each element frame/instance, copy preset Q in and frame Q/T to wave points,
  and never write element state back into the preset. Compact numbered equation keys
  (`wave_0_per_frame1`) and underscore variants must both parse.
- Shapes read `shapecode_N_*` numeric keys and `shape_N_*` code, honor enabled flags
  and sparse indices, and use zero-to-one coordinates only for the Milkdrop namespace.
  Draw shapes before custom/default waves. The overlay stores premultiplied colour
  plus accumulated non-additive coverage; composite as `feedback*(1-coverage)+RGB`.
  Preserve alpha when exporting the GL overlay, but keep ordinary bitmap output opaque.
  A shape whose `shapecode_N_textured` is set must sample the previous feedback frame (`VS[0]`) instead of the
  gradient: interpolate the reference's fan texture coordinates (centre at the
  texture centre, rim on a circle of radius `0.5 / tex_zoom` rotated by `tex_ang`)
  and read the frame with repeat. Use the reference's own formula verbatim
  (`milkdropfs.cpp`, marked "DON'T TOUCH!"): `u = 0.5 + 0.5 * cos(angle) / tex_zoom * aspectY`,
  `v = 0.5 + 0.5 * sin(angle) / tex_zoom`, with `angle = t * 2pi + tex_ang + pi/4`. The v sine is
  **positive**; the engine's frames are top-down like the reference's capture, so the OpenGL fragment
  stage's bottom-up flip is the only one. Negating it sampled the capture mirrored.
  `PresetShapeTests.RenderFrame_TexturedShapeUsesTheReferenceTextureCoordinate` pins the sign.
  A Milkdrop shape's space is Direct3D's y-up space (`v[0].y = shape_y*-2+1`), so a
  `MilkdropCoordinates` shape's y is negated out of the preset's expression space in
  `BuildVertices` and for the fan centre, because the overlay rasterizer is y-down. The
  reference measures `shape_y` from the top and `wave_y` from the bottom (`wavePosY =
  wave_y*2-1` where top is one), so the waveform paths flip explicitly and the shape
  paths must not double-flip. `shape_N_*` (non-Milkdrop) shapes keep raster space.
  Milkdrop's shape blend is `SRCALPHA`/`INVSRCALPHA` (or `ONE` additive), which the GPU
  fill reproduces as premultiplied `ONE, ONE_MINUS_SRC_ALPHA` and `ONE, ONE`. A Milkdrop
  fan closes by repeating its first rim vertex.
- The stereo custom waveform has 512 contiguous normalized PCM samples, aligned to
  the previous frame. Convert by
  128 at the custom-wave rendering boundary before applying the reference 0.004 scale.
  Custom-wave line segments interpolate vertex colour and alpha; using only the end
  vertex colour changes the density and colour of overlapping MilkDrop line strips.
  Thick custom-wave dots cover 2×2 pixels at normal texture sizes.
  `bDarkenCenter` is a small diamond-shaped fan with 3/32 peak alpha and radius
  0.025 times the smaller frame dimension in pixels, not a full-frame radial fade.
  The legacy hue shade is mixed with white by `shader` (`fShader`); zero leaves
  the image untinted while shader uniforms still receive the raw animated shade.
  This does not assert exact Winamp FFT compatibility. `fWarpAnimSpeed` and
  `fWarpScale` must reach every warp path; GLSL audio uniforms use relative bands.
  FPS reflects the supplied interval, and aspect factors remain at most one.
- `MilkdropFidelityRegressionTests` and the current `VISUALIZER-FIDELITY-RECHECK.md`
  supersede older completion claims below. Textured shapes and exact default-wave
  geometry remain incomplete; do not describe parsing as full visual compatibility.


- `PresetRenderer.BuildMesh` must reseed zoom, zoomexp, rot, warp, cx, cy, dx,
  dy, sx and sy from the per-frame state before every vertex equation execution.
  Restore that state afterward; compound assignments must never accumulate motion
  across vertices. `PerVertexMeshTests.RenderFrame_MeshReseedsMotionForEveryVertex`
  covers this. See `VISUALIZER-FIDELITY-AUDIT.md` for remaining reference mismatches;
  agreement with the CPU renderer alone does not establish Milkdrop compatibility.
  A per-pixel block that writes both x/y and motion still has to publish the
  per-vertex motion mesh for the GL renderer; its x/y values feed the motion
  equations at each vertex. `PerPixelWritesMotion` lets the desktop select that
  path instead of a fullscreen fragment warp that drops dx/dy.
  Composite shader `rad` divides the aspect-scaled position's length by the
  aspect-scaled corner radius, and `ang` is in 0..2π as in MilkDrop's
  `UvToMathSpace`. The CPU interpreter (`PresetRenderer.BindShaderVariables` /
  `SeedPolar` with `mathSpacePolar`) and the GPU emitter
  (`ShaderTranspiler.EmitCompMain`) must both use that convention; a comp shader
  such as Mashup (13) that derives its whole picture from `rad`/`ang` otherwise
  renders differently on the two paths. The warp shader keeps its own per-vertex
  polar pair, which is not aspect-normalised. `CompShaderPolarTests` pins the
  interpreter convention. The four hue-shader indices are bottom-right,
  bottom-left, top-right, top-left in a top-down image.

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
  allocation-free transform. The analyzer serves two contracts: normalized
  `Bands`/`Bass`/`Mid`/`Treble`/`Volume` for other consumers, and preset-facing
  `BassRelative`/`MidRelative`/`TrebleRelative` with their attenuated counterparts.
  The latter must follow Winamp MilkDrop's `CPlugin::DoCustomSoundAnalysis`, not the
  separate `CPluginShell` display analysis: quantize the aligned left PCM to signed
  eight-bit samples, apply a raised-sine window to 576 samples, run an unnormalized
  1024-point FFT with logarithmic equalization, sum the first three sixths, and use
  the reference's 30-FPS-adjusted attack and long-average rates. The histories start
  at zero; their near-zero guard is 0.001 in those unnormalized units. Do not seed a
  first non-silent band to a relative value of one, since that erases the large
  initial response and leaves constant-tone middle and treble values wrong. A matched
  Winamp 440 Hz capture and `AudioSpectrumAnalyzerTests` pin this behavior. The
  stereo waveform stays aligned by `WaveformAligner`, and the normalized display
  bands remain a separate contract. Never evaluate preset expressions or render
  frames on the audio thread. `PresetVariableLayout.RegisterStandardVariables` is the single place
  that declares the Milkdrop variable set, so every expression block of a preset shares one slot
  layout. The layout is the only shared thing: the per-frame and per-pixel blocks run on separate
  slot arrays (`PresetRenderer._slots` and `_pixelSlots`, with the per-pixel array seeded from the
  per-frame state each frame), because Milkdrop keeps per-frame (`m_pf_eel`) and per-vertex
  (`m_pv_eel`) as separate variable universes. A shared array lets a per-pixel write such as `x1`
  clobber the per-frame spring state, which is what made Mashup (13) render wrong; never reintroduce
  it. `q1`-`q32` reach the per-pixel stage because the per-frame state is copied in, not because the
  arrays are shared. Slot values are `double`, matching ns-eel2's `EEL_F`. The
  renderer runs the stages in Milkdrop order: the init blocks once, the preset per-frame block, the motion warp (`zoom`, `zoomexp`, `rot`, `cx`/`cy`,
  `dx`/`dy`, `sx`/`sy`) with the per-pixel block on top, the decay fade (the reference's warp
  fragment shader applies it to the sampled colour), the blur passes and the blur chain's edge
  darkening, the shapes and waves (which are added to the warped frame before the
  centre darkening and the border, so those later passes cover them), the centre darkening, the
  border, and finally the reference's final composite — either the custom comp shader or the legacy
  video echo and gamma adjustment, never both. The custom-wave rasterizer clips line segments
  against the frame before converting endpoints to pixels; clamping endpoints creates spurious
  bright border lines. Custom-wave points first use MilkDrop's inverse aspect factors about the
  frame centre; thick lines use four full-opacity offsets in a 2x2 pixel footprint. The CPU
  textured-shape fallback multiplies the sampled frame RGB by the interpolated shape RGB and
  blends using interpolated shape alpha, matching MilkDrop's fixed-function texture stage. The legacy
  path also multiplies the finished frame by
  the reference's animated hue shade, a four-corner colour whose three channels are animated sines
  normalised so their maximum is one; the per-preset offsets are seeded from the preset name so the
  look is reproducible, and the OpenGL display pass applies the same shade. The warp shader reads
  the **retained** blur chain; after the warp the chain is rebuilt from `VS[0]`, the feedback the warp
  just sampled, and kept for the next warp, so the next warp's `sampler_main` is one generation newer
  than its `GetBlur1`-`GetBlur3` chain. `milkdropfs.cpp` documents this as intentional ("when
  sampling the blurred textures in the warp shader, they are one frame old"); never rebuild the chain
  from the same feedback the warp samples to make a `GetBlur - GetPixel` shader settle. A fresh chain
  is black and marked ready so the first warp reads retained black. The Skia warp pass binds the
  renderer's retained levels (`WarpPass.Render`'s `blurLevels`) instead of rebuilding them, and a
  blur-sampling program stays off the Skia frame path when the levels cannot be supplied. Its comp
  shader also binds VS[0] to every main
  sampler; VS[1], the current warp plus overlays, only becomes feedback after presentation.
  `Composite` adds the overlay frame into the warped
  frame and `Publish` copies the finished frame into the display buffer, so a post effect never runs
  after the publish. The per-pixel block sees the warped
  position in `x`/`y` on the per-pixel fallback path, which is a deliberate deviation from Milkdrop
  offset semantics so the built-in presets keep working; the mesh path gives it the reference's
  aspect-scaled zero-to-one vertex position. Numeric
  preset keys are parsed into `VisualizerPreset.Defaults` and applied as the per-frame
  starting values after the computed seeds, which is how Milkdrop presets carry most of their
  settings; never drop that step or key-only presets lose their wave, border, and echo
  parameters. `VisualizerPreset.KeyAliases` maps the Milkdrop 2 short keys onto the Milkdrop 1 names
  the variables use, so the `b1n`/`b1x`/`b1ed` and `b2`/`b3` blur and edge keys resolve to
  `blurN_min`/`blurN_max`/`blurN_edge_darken` instead of being dropped, and a preset that carries
  only them takes its blur amount from their `blurN_max` sum when Orynivo's own `blur_level` key is
  absent. Keep new aliases in that one table. The default wave mode is the single line, which is six
  in the reference's `nWaveMode` numbering (its idle preset uses six), and the default `wave_scale`
  is one. Preset wave scales may exceed one (for example Mashup (129) uses 28.599); never
  clamp MilkDrop's `fWaveScale` to the zero-to-one range. `MilkdropWaveform` owns the default wave's per-mode geometry — ring, spiral, centred
  spirograph, derivative line, explosive hash, line, double line and spectrum line — with the
  reference's edge clipping, closed-loop modes and four-tap polyline smoothing; `DrawDefaultWave`
  only prepares the PCM, applies the legacy global `per_point` block, and draws the result. Never
  reintroduce a line/circle approximation, and keep the geometry's sample count following the
  source so a short buffer cannot read an empty tail. `wave_smoothing` is a registered variable
  (`fWaveSmoothing` aliases it) and `DrawDefaultWave` reads it from `Preset.Defaults`; it was
  missing from the layout, so a preset such as `suksma - frust` had its `fWaveSmoothing=0.9`
  ignored and its explosive hash stayed unsmoothed. The explosive-hash mode is the only mode that
  tones its alpha down, by the reference's resolution factor (`milkdropfs.cpp`: `alpha *= 0.07` at
  256 through `0.13` at 2048); `ExplosiveHashAlphaScale` maps the rounded-up power-of-two frame
  width onto the same table, because skipping it made that mode roughly an order of magnitude too
  bright.
  The default waveform and the four custom waveforms are separate: the default one uses the global
  `wave_*` settings and the global `per_point` block, while each `VisualizerWave` carries its own
  `wavecode_N_*` state and `wave_N_*` blocks. `PresetRenderer.DrawCustomWave` builds the sample data
  from the waveform or, only when `wavecode_N_bSpectrum` is set, from `IVisualizerAudioSource.Spectrum`,
  smooths and scales it, and gives the per-point block the reference's `sample`/`value1`/`value2`
  contract; the polyline is smoothed with the reference's four taps. Never draw spectrum bars
  unconditionally, and keep the waveform scratch buffers reused so an overlay stays allocation-free.
  A GPU overlay draws the custom waveforms instead of the CPU: `PresetRenderer.CollectWaveGeometry`
  makes `DrawWavePoints` expand the smoothed polyline into a triangle list (`WaveGeometry`) and skip
  the per-pixel line rasterizer, whose 4-offset thick lines otherwise cost hundreds of milliseconds
  at a high render resolution. The geometry is in the engine's minus-one-to-one top-down overlay
  space, the same space `ShapeFillVertex` uses, and the pipeline draws it with the shape program
  untextured and the same "over"/additive blend `PaintPixel` applies. Keep the CPU rasterizer as the
  fallback for a caller that does not collect the geometry; `CollectWaveGeometry` must only be set
  while the pipeline reports it can draw the geometry, because the renderer then skips its own draw.
  `VisualizerTextureBank` generates the Milkdrop noise and random textures from fixed seeds
  instead of bundling third party images: keep generation deterministic and lazy, and keep the
  sizes (32, 256, 512) so shader sampling stays comparable. It also generates the two 32³ volume
  noises (`sampler_noisevol_lq`/`hq`) that `tex3D` samples, quantised to eight bits and laid out
  as a slice atlas (`VolumeAtlasColumns`/`VolumeAtlasRows`); the CPU interpreter and the GPU
  helper must read that one volume with the identical trilinear math, because a procedural hash
  cannot be reproduced bit-exactly on the GPU. The `sampler_main`,
  `sampler_pc_main`, `sampler_fc_main`, `GetBlur1`-`GetBlur3`, and `GetPixel` constructs are
  HLSL shader features and belong to the shader runtime in phase 38d, not to the texture bank.
  A sampler's `fc_`/`fw_`/`pc_`/`pw_` (or swapped) qualifier selects its wrap and filter mode, not a
  different moment in time, so every qualified `main` sampler reads the same frame; `ShaderSamplerName`
  is the shared parser, `PixelBuffer.SampleShader` performs the wrap and filter, and the qualifier is
  stripped before the generated noise and random textures are resolved. Keep the interpreter, the Skia
  passes, and the OpenGL pipeline on that one parser and one sampling rule.
  The Milkdrop format has no per-preset texture block, so unknown `tex_*` keys stay ignored.
  A preset may still declare its own texture in the shader source, such as
  `sampler sampler_cells;` in Royal Mashup (142). `ShaderTranspiler` collects every sampler a shader
  names beyond its built-in set and declares it with the type the dialect needs: `sampler2D` in GLSL,
  `shader` in SkSL. Emitting the SkSL spelling on both back ends failed the complete GLSL warp shader
  because `uniform shader` is not GLSL; a texture the engine cannot provide falls back to the previous
  feedback, which is what the binding already did.
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
  finish. A disabled shader silently costs a preset its picture, so keep `ShaderError` carrying the
  reason, and keep the three bindings real presets depend on: `ret` is the output variable when a
  body returns nothing (`ShaderInterpreter.ReturnedValue` says which case applies), `GetPixel`
  accepts both `GetPixel(x, y)` and `GetPixel(float2(x, y))`, and `aspect` is bound as the float4
  `(aspectX, aspectY, 1/aspectX, 1/aspectY)`, because presets read `aspect.zw` and Milkdrop/projectM
  define it that way. The preset *scalars* `aspectx`/`aspecty` are not the same pair: Milkdrop binds
  them to the **inverse** factors (`var_pf_aspectx = m_fInvAspectX`, `milkdropfs.cpp`), so a
  landscape frame is `(1, width/height)`. Keep the two apart, because per-pixel code that
  multiplies its position delta by `aspecty` otherwise applies the aspect correction twice. `hue_shader` is bound too: the reference
  defines it as the final quad's vertex diffuse (`#define hue_shader _vDiffuse.xyz`) and always
  computes it, four corners of `0.5 + 0.5*normalised sine` ("since we don't know if shader uses it or
  not"), so a warp or comp shader may read it and a shader that reads zero paints the wrong picture -
  `$$$ Royal - Mashup (138)` clamped itself to black that way. `PresetRenderer.ComputeHueShades` runs
  before the overlay half returns, the interpreter binds the value per pixel through
  `HueShadeAt(originalU, originalV)`, and the GPU path carries the four corners as the twelve
  `hue_shader_<channel><corner>` scalars that `ShaderTranspiler.EmitHueShader` mixes by the fragment's
  own position. Motion vectors are **not** an engine grid: the reference's `mv_x`/`mv_y` are the arrow
  grid's size, `mv_dx`/`mv_dy`/`mv_l`/`mv_a` are ordinary blendable variables (`mv_a` defaults to one,
  the legacy key is `bMotionVectorsOn`), and `DrawMotionVectors()` only draws that arrow grid.
  Orynivo's own `mv_enabled`-gated recording is an extension, not reference behaviour, and must be
  documented as such. The interpreter
  walks the tree per pixel, which is the known cost limit; a JIT compiler for shaders is the
  documented follow-up if the CPU cost proves too high.
  `PresetRenderer.Timings` and `AverageTimings` carry the `RenderTimings` breakdown per frame and
  per averaging window, and `ResetTimings` restarts that window; keep the measurement cheap (one
  stopwatch and a handful of marks per frame) and never time a per-pixel shader call, because the
  measurement would cost more than the work. A warp shader is therefore part of `Warp`, while
  `Shader` is the comp stage.
  `ShaderRuntime` owns every shader operation and both execution paths call it, so the interpreter
  stays the reference implementation and `ShaderCompiler` can only be correct if it produces the
  same values; `ShaderCompilerTests` asserts exactly that and must keep passing. Keep its function
  set aligned with `ShaderTranspiler`'s vocabulary: a function the emitter can translate but the
  runtime does not know makes the CPU silently disable a shader the GPU renders, and `lum` alone
  appears in a third of a real collection. `lum` must use MilkDrop's `include.fx` weights
  (`dot(x, float3(0.32, 0.49, 0.29))`), not the conventional Rec. 601 luma, because the interpreter
  and both emitters have to agree and a comp shader derives its blur gradient through it. The matrix types are part of that set now: `float2x2`,
  `float3x3`, and `float4x4` were unknown and disabled 788 presets' shaders. A matrix lives in a
  per-pixel pool in `ShaderRuntime` (`StoreMatrix`/`ResetMatrixPool`), and `ShaderValue` carries only
  its index and dimension; do not move the nine or sixteen components into the value, because an
  inline matrix made the measured 640 x 360 comp-shader frame go from 26 ms to 47 ms. `ConstructMatrix`
  builds any of the three dimensions from scalars or a vector, `HlslMultiply` handles
  matrix-by-vector, vector-by-matrix, and matrix-by-matrix, and `ShaderTranspiler` maps `floatNxN`
  onto SkSL's `matN` (spreading a vector argument into scalars, because `matN` has no four-component
  constructor) and must not narrow a matrix argument. The shader entry points must clear the pool
  before evaluating a pixel, so a handle can never point at a matrix another pixel built.
  `PresetRenderer.SeedFrameVariables` must keep `fps`
  finite on the first frame, because a preset that divides by it otherwise accumulates an infinity
  that then reaches the sampler. The per-frame `rand_frame` vector uses `Random.Shared` by default so
  a preset still looks different on every run like the reference; `PresetRenderer.RandomSeed` draws it
  from a private generator instead, so diagnostics and A/B comparison can reproduce a render, and the
  GL harness exposes that as `GLH_RANDOM_SEED`. `PresetSkiaComparisonDiagnosticTests` is the harness that compares
  both execution paths over a real collection and reports their drift. Keep the interpreter and the
  emitter agreeing on the shader semantics the harness covers: a declaration and an assignment coerce
  their value to the declared type (`ShaderRuntime.Coerce`, which HLSL and the emitter both apply),
  the shader's `/` treats a zero divisor as zero (`orynivoSafeDiv` on the GPU), a blur and `GetPixel`
  read the same frame `sampler_main` refers to, and the compiler remembers each variable's declared
  component count so its compiled path matches the interpreter.
  The comp shader is a **display** pass, not a feedback stage: `PresetRenderer.Output` is the
  post-comp frame the presenter shows, while the next frame warps from the pre-comp composite
  (`MeshSource`, and the frame-end copy of `_frameCopy`). Feeding the comp output back lets a
  `ret *= 10` comp shader compound every frame until the whole frame is white, which is what
  `LuxXx - BadBallz Beta` did; `CompFeedbackTests` is the check.
  `ShaderTranspiler`
  is the SkSL back end for the GPU path and must stay honest against the same reference: the GPU and
  the interpreter have to agree on one variable universe, so the emitter knows the engine-bound
        per-pixel variables (`uv`, `uv_orig`, `rad`, `ang`), infers an intrinsic's return type from its
  arguments (and reports the narrowed component count for the intrinsics whose arguments it narrows,
  because a later operation otherwise skips a conversion SkSL needs), declares a sampler from the
  shader's own `tex2D`/`tex3D` call instead of a fixed list,
  reads an undeclared variable as a zero constant the way the interpreter does, gives a written
  uniform a writable copy in main, and renames a name SkSL reserves. A comparison of vectors is
  emitted component-wise with `step`/`sign`, because SkSL rejects a bool vector as a condition, and
  a comparison used as a condition compares the first components to match `ShaderValue.IsTrue`. A
  file-scope variable a helper reads is passed into the helper as a parameter, transitively through
  the helpers it calls, because SkSL runtime effects have no mutable globals and the helper is
  emitted before `main` declares it.
 Milkdrop keeps one variable
  universe for the expression blocks and the shader, so `q1`-`q32` and `t1`-`t8` are seeded from the
  preset slots on both CPU paths (`BindShaderVariables` per pixel and `SeedCompiledShaderFrame` once
  per frame) and on the GPU; keep the two sides seeding the same set, because a shader that reads a
  variable the interpreter leaves at zero renders a different picture on the two paths. That universe
  also carries MilkDrop's `include.fx` extras that real presets use: the `float4` q banks `_qa`-`_qh`
  (q1-q4 through q29-q32) and `vol_att`. `_qa`-`_qh` are macros in the GLSL prelude and `float4`
  uniforms in the SkSL prelude, the type table marks them `float4` so `float2x2(_qb)` spreads its
  components, and `WriteShaderUniforms` seeds them from the preset slots; a bank read as an unknown
  scalar (a zero) broke Royal Mashup (151) with a divide by zero. Do not drop these: a scan of a
  10,353-preset collection uses the banks in about 600 presets and `vol_att` in 206. The sampler-size
  uniforms (`texsize`, `texsize_main`/`fc_main`/`pc_main`, and `texsize_noise_lq`/`mq`/`hq`/
  `noisevol_lq`/`hq`) are part of the same universe: declare each in the GLSL prelude as scalars with
  a reconstructing macro (the GL setter is scalar-only), declare them as `float4` in the SkSL prelude,
  and seed them in `WriteShaderUniforms`, `BindShaderVariables`, and `SeedCompiledShaderFrame`. A
  shader that reads `texsize_noise_lq.zw` for a dither coordinate (Royal Mashup (188)) rendered a
  different picture when they were left at zero. `SamplerSizeUniformTests` pins the values.
  MilkDrop's twenty-four `rot_*` matrices are `float4x3`, a non-square type that neither the
  interpreter's square-matrix pool nor SkSL models, so they are never materialised as a type.
  `ShaderRotationMatrices` builds the reference's row-vector composition (`Rx * T * Rz * Ry`, the
  rotation speeds following `0.9 * (k / 8)^3.2`, the last four re-randomised every frame) into three
  `float4` columns per matrix, seeds them from the preset name so a preset looks the same across runs,
  and rewrites the shader source before the parser sees it: `rot_d1[1].y` becomes the column component
  `rot_d1_c1[1]` and `mul(uv, rot_d1)` becomes the `orynivo_mul4x3` call the prelude defines. Keep the
  rewrite in `TranslateShaderDialect`, keep the columns in `ShaderTranspiler.UniformComponents` (so
  both back ends declare and seed them), and keep `Build` once per frame in `RenderFrame` before any
  stage reads them, because the interpreter binds them per pixel and the GPU reads them through
  `WriteShaderUniforms`. Only bind them for a preset whose shaders reference a `rot_` name.
 `SkiaShaderRunner` must bind a
  child shader for every sampler `Transpile` reports, and the CPU/GPU comparison tests must keep
  passing. Its `CompPass` runs a comp shader, with its own per-pixel block emitted into the same
  effect, as a runtime effect over the
  renderer's frames; it binds the previous feedback to every main sampler, builds blur from it,
  and scales each sampler by its own `texsize_*`. `PresetRenderer.UseSkiaPasses` gates
  it because the eight-bit Skia surface differs from the float interpreter by up to one level. The
  visualizer enables it by default and the interpreter stays the fallback, so a preset or pass the
  Skia path cannot handle still renders. `UseSkiaPasses` covers the per-pixel shader passes (the
  comp shader and a warp shader), whose compiled SkSL is faster than the interpreter; the full-frame
  passes (the geometric warp, the video echo, the borders, and the composite) are gated separately
  by `PresetRenderer.UseSkiaFramePasses` and off by default, because on the raster Skia surface each
  converts the whole frame to an eight-bit bitmap and back and measured slower than the interpreter's
  in-place float passes. The frame passes' runtime effects are cached for the process, because their
  SkSL is constant and Skia compiles an effect when it is created; keep that cache, because
  rebuilding them every frame cost more than the passes.
  `SkiaShaderRunner.BlurFrame` is the GPU blur pass and must keep reproducing `PixelBuffer.Blur`'s
  nine-tap clamped filter; the comp pass builds its blur levels from it, and the frame helpers
  (`GetBlur1`-`GetBlur3`) must keep scaling their normalised coordinate by `texsize`.
  `SkiaShaderRunner.VideoEcho` is the GPU video-echo pass and must keep the CPU pass's zoom, flip,
  alpha blend, and leave-untouched rule.   `SkiaShaderRunner.Composite` is the GPU additive composite
  and must keep the CPU pass's clamp.   `SkiaShaderRunner.Borders` is the GPU border pass and must
  keep the CPU ring geometry and blend: MilkDrop draws two clip-space Chebyshev rings sized by the
  preset's `ob_size`/`ib_size` (default 0.01), `[1 - ob_size, 1]` and
  `[1 - ob_size - ib_size, 1 - ob_size]`, blended with `SRCALPHA`/`INVSRCALPHA`. Never reintroduce
  fixed band values; a wrong border changes the feedback of a preset whose only content is its
  border, such as Royal Mashup (13). `SkiaShaderRunner.Warp` is the GPU geometric warp and must
  keep the CPU motion transform and the black-outside-the-frame rule. `PresetExpressionTranspiler`
  emits the preset's per-pixel expression language as SkSL from the same `PresetSyntaxNode` tree the
  interpreter compiles, so the GPU and the CPU cannot disagree about a block; it reports the uniforms
  the caller seeds, maps `x`/`y`/`rad`/`ang` onto engine locals, and refuses `megabuf`/`gmegabuf` and
  `rand`, which stay on the interpreter. `ShaderTranspiler.TranspileWarp` composes that block with a
  warp shader (or a direct frame sample) into a warped-`uv` entry point: `uv` comes from the motion
  transform plus the per-pixel block, `uv_orig` is the pixel position, and the shader's polar pair is
  derived from `uv`, exactly as the CPU stage computes them. `SkiaShaderRunner.WarpPass` runs that
  effect over the previous frame; the renderer uses it for a preset with at most one warp shader, no
  per-shader per-frame block, no motion recording, and a per-pixel program whose written values are
  assigned before they are read (`PresetExpressionTranspiler.CanRunInParallel`), because the GPU
  evaluates every pixel independently and cannot reproduce a value carried from the previous pixel.
  Anything else keeps the interpreter, which is the reference for those expressions. The per-pixel
  emitter and the shader emitter share the naming and type helpers in `SkSL`.
  A frame sampler must keep the renderer's coordinate convention: `PixelBuffer.SampleBilinear` maps a
  normalised coordinate to `zero..size-1`, while a generated texture maps it to `zero..size`, so
  `tex2D`, `GetBlur1`-`GetBlur3`, and `GetPixel` on a frame scale by `texsize - 1` with a half-texel
  shift (or an integer truncation for `GetPixel`).
  `PresetRenderer.UseSkiaPasses` gates the comp shader, the warp pass, and
  these frame passes together. Only
  straight-line
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
  keeps small frames on the calling thread. The blur, decay, gamma, centre darkening, edge
  darkening, video echo, and composite passes are row-independent and split the same way, gated by
  `PresetRenderer.ParallelismEnabled`; `PixelBuffer.Blur` reuses one scratch array instead of
  allocating a copy per pass, and `PresetRenderer` gives each worker its own sample scratch.
  `PresetRenderer.DarkenEdges` applies the blur chain's `blurN_edge_darken` after the blur passes; its
  falloff is our own documented approximation, because a preset does not store the shape, so the
  centre stays untouched and the border is multiplied towards `1 - amount`. The warp
  is parallel when the per-pixel program cannot leak a value out of its pixel: everything it
  writes is either re-seeded per pixel (`x`, `y`, `rad`, `ang`), or a pixel-local temporary the
  block assigns before it reads and no other stage reads. `PresetVariableLayout.Standard` is what
  makes that decision precise, so a write to a standard name such as the shape alpha `a2` stays
  sequential even when nothing else seems to read it; a preset's own temporary such as `spin`
  does not. `PresetExpressionTranspiler.CanRunInParallel` covers the assign-before-read half. A
  warp shader or an active motion grid keeps it sequential for the same reason. Never give two
  workers the same slot array, and never claim a speed-up without the identical-frame test in
  `ParallelWarpTests`.
  `PresetRenderer.MeshPerPixelEnabled` (off by default) makes the warp evaluate the per-pixel
  program once per mesh vertex and interpolate the coordinate each vertex transforms to across the
  quad, which is what the reference warp vertex shader does; `BuildMesh` fills the mesh from the
  per-frame values first, so a program that exceeds the warp budget leaves a plain warp rather than an
  unfinished mesh, and it restores the per-frame motion afterwards because a later stage means the
  frame's values, not the last vertex's. The coordinate mesh is interpolated with a lerp, so an
  affine transform stays within floating-point precision of the per-pixel path — `PerVertexMeshTests`
  asserts that and that a varying motion is interpolated. A program that writes `x` or `y`, records
  motion vectors, or feeds a warp shader keeps the per-pixel path, because an interpolated sample
  position has no meaning. `BuildMesh` writes the per-vertex `x`/`y`/`rad`/`ang` with the reference
  aspect (`WarpSampling.GetAspect`), matching the reference's aspect-scaled zero-to-one vertex
  position, and a measurement against the reference is how that is checked.
  `MeshGridX`, `MeshGridY`, and `MeshValues` are public because a GPU warp reads the mesh as vertex
  attributes, and the value order is part of that contract: zoom, zoomexp, rot, cx, cy, dx, dy, sx,
  sy, warp. `MeshRequested` builds the mesh while the CPU keeps evaluating per pixel, which is the
  transitional double work a GPU warp needs before it replaces the CPU warp; `TryCopyMeshMotion`
  copies the values and `MeshSource` exposes the frame the mesh samples. Requesting the mesh must
  leave the CPU frame byte-identical, which `PerVertexMeshTests` asserts. A per-pixel block that
  writes the sample position exposes no mesh, except when `MeshForPixelWarp` is set: the emitted
  warp fragment shader computes that coordinate itself and reads only the mesh's motion values, so
  the mesh is still built for it and `ExpressionsOnly` stops the CPU at the overlay instead of
  running the complete frame while the presenter also draws the GPU pipeline.
  `ReadFrameParameters` publishes the clamped per-frame pass values and `RenderOverlayFrame` the
  overlay-only frame, so a GPU pipeline reads them instead of duplicating the key lookups and clamps;
  `RenderOverlayFrame` seeds the live variables first, because the overlay reads them.
  `WarpSampling.SamplePosition` is the single definition
 of the warp's sampling arithmetic: the CPU
  warp calls it per pixel and a GPU warp's fragment shader must be a translation of it, so never
  duplicate that formula. It follows the reference warp vertex shader's coordinate contract — scale
  by the aspect, divide by the radial zoom, stretch, apply the time-dependent warp displacement,
  rotate, translate, and scale back by the inverse aspect — and its result is in the engine's
  minus-one-to-one space, which the frame sampler expects. `WarpSampling.WarpDisplacement` is the
  reference's four travelling waves (amplitude `warp * 0.0035`, `warp` defaults to one) and the three
  `_orynivo_warp`/`_orynivo_warpTime`/`_orynivo_warpScale` uniforms carry it to the GPU path; every
  declared warp uniform must be set in `SkiaShaderRunner.WarpPass.Render`, because Skia leaves an
  unset declared uniform undefined. The aspect is passed in explicitly via `WarpSampling.GetAspect`
  (the reference keeps both factors at or below one — a landscape frame is `(1, height/width)`, a
  portrait frame `(width/height, 1)`) and `rad`/`ang` are the aspect-scaled distance and angle the
  reference derives from the position. `WarpSamplingTests` is the reference the translation is
  checked against.
  A shader's blur levels each keep their own buffer (`_blurLevels`) and build on one another, and a
  level asked for first builds the ones below it. Each level is **one** pair of passes from its
  downscaled source — the reference's blur loop advances the level only every second pass
  (`fscale_now = fscale[i/2]` in `milkdropfs.cpp`), and the OpenGL chain's `BuildShaderBlurLevels`
  builds exactly that. Never run N pairs for level N: that over-blurred `GetBlur2`/`GetBlur3` on the
  CPU alone, so a comp shader sampling them rendered differently on the two paths.
  Do not collapse them back into one cached level: a
  comp shader that samples `GetBlur1` and `GetBlur3` in the same pixel otherwise invalidates the
  cache on every sample and rebuilds a full-frame blur each time, which cost seconds per frame.
  The Skia comp pass runs on the same adaptive grid as the interpreter and hands the preset to the
  interpreter when a pass exceeds `ShaderPassBudgetMilliseconds`, because Skia rasterises a runtime
  effect on the CPU and a comp shader that samples the blur levels otherwise stalls the frame.
  The preset compiler must accept the Milkdrop function set, because a block it cannot compile is
  dropped and takes that preset's motion with it: `above`, `below`, and `equal` yield one or zero
  (never a boolean), and `sqr`, `sigmoid`, `band`, `bor`, and `bnot` belong to it too. Names are
  case-insensitive, and a block that still fails is skipped and recorded on
  `VisualizerPreset.FailedBlocks` instead of rejecting the preset, so one unsupported construct
  costs a block rather than a preset. `PresetFolderDiagnosticTests` reports what still fails
  against a real collection; run it after touching the compiler. The numbered parts of an
  expression block are joined by concatenation, because presets split one expression across parts
  and a part may end with an operator; a semicolon is inserted only when the previous part is
  complete, the next does not bring its own, and the next does not continue a call whose function
  name ended the previous part. A part's line comment is stripped before the join, because the
  parts are concatenated without a newline and a trailing `// ...` would otherwise swallow every
  part after it, and the numbered parts are read up to the shader-line bound, because a real
  per-frame block reaches the hundredth part and a lower bound cut a block off mid-loop.
  `megabuf` and `gmegabuf` are Milkdrop's shared memory buffers and their accesses are serialised,
  because a preset writes lookup tables that other pixels read. `loop(count, statements)` repeats a
  statement list, and `while` accepts both `while(condition, statements)` and the one-argument
  `while(condition)` a real collection writes, where the condition is re-evaluated until it turns
  false and the repeated work sits inside it as `exec2(statements, condition)`; their iteration count
  is clamped, and
  presets nest them inside `if()` and other constructs, so the call parser accepts both wherever a
  primary expression starts; both buffer write spellings presets use (`gmegabuf(i, value)` and
  `gmegabuf(i) = value`) must keep working, because presets build their lookup tables with them.
  `exec2`/`exec3`/`exec4` evaluate their arguments and yield the last one, an assignment is a valid
  `if(...)` argument, and a semicolon continues that argument as a statement sequence. Keep the
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
  of a call. `VisualizerPreset.TranslateShaderDialect` resolves Milkdrop's preprocessor conditionals
  (`#define`, `#if`, `#ifdef`, `#ifndef`, `#else`, `#endif`) and its built-in math constants
  (`M_PI`, `M_PI_2`, `M_2PI`, `M_INV_PI`, `M_INV_PI_2`, `M_E`) before `ShaderParser` sees the
  source, so the parser stays plain HLSL; keep the constants in that one table. A fixed-size array
  declaration such as `const float4 samples[5] = { ... }` is parsed
  into its own node and stored in the interpreter's array storage, because indexing an unknown
  array name would render a wrong picture; keep the element type, the size, and the flattened
  initializer together.
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


