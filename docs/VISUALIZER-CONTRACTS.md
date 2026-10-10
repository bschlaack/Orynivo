# Visualizer contracts

These contracts supplement the root and applicable nested `AGENTS.md`. Read the
complete relevant chapter before changing rendering or audio analysis. All
source paths are repository-relative. See
[Visualizer status](VISUALIZER-STATUS.md) for the current execution paths; older
measurements in this document describe their original benchmark setup. Skia
raster runtime effects execute on the CPU. OpenGL/GLSL is the GPU path.

## Core rendering and audio analysis

- The music visualizer analyses audio through `Orynivo.Core/Audio`:
  `PcmVisualizationTap` is the lock-free hand-off from the audio pump and must
  never block or wait (drop the oldest samples instead), `AudioSpectrumAnalyzer`
  owns windowing, FFT, band grouping, and smoothing, and `Fft` stays a pure,
  allocation-free transform. The analyzer serves two contracts: normalized
  `Bands`/`Bass`/`Mid`/`Treble`/`Volume` for other consumers, and preset-facing
  `BassRelative`/`MidRelative`/`TrebleRelative` with their attenuated
  counterparts. The latter must follow Winamp MilkDrop's
  `CPlugin::DoCustomSoundAnalysis`, not the separate `CPluginShell` display
  analysis: quantize the aligned left PCM to signed eight-bit samples, apply a
  raised-sine window to 576 samples, run an unnormalized 1024-point FFT with
  logarithmic equalization, sum the first three sixths, and use the reference's
  30-FPS-adjusted attack and long-average rates. The histories start at zero;
  their near-zero guard is 0.001 in those unnormalized units. Do not seed a
  first non-silent band to a relative value of one, since that erases the large
  initial response and leaves constant-tone middle and treble values wrong. A
  matched Winamp 440 Hz capture and `AudioSpectrumAnalyzerTests` pin this
  behavior. The stereo waveform stays aligned by `WaveformAligner`, and the
  normalized display bands remain a separate contract. Never evaluate preset
  expressions or render frames on the audio thread.
  `PresetVariableLayout.RegisterStandardVariables` is the single place that
  declares the Milkdrop variable set, so every expression block of a preset
  shares one slot layout. The layout is the only shared thing: the per-frame and
  per-pixel blocks run on separate slot arrays (`PresetRenderer._slots` and
  `_pixelSlots`, with the per-pixel array seeded from the per-frame state each
  frame), because Milkdrop keeps per-frame (`m_pf_eel`) and per-vertex
  (`m_pv_eel`) as separate variable universes. A shared array lets a per-pixel
  write such as `x1` clobber the per-frame spring state, which is what made
  Mashup (13) render wrong; never reintroduce it. `q1`-`q32` reach the per-pixel
  stage because the per-frame state is copied in, not because the arrays are
  shared. Slot values are `double`, matching ns-eel2's `EEL_F`. The renderer
  runs the stages in Milkdrop order: the init blocks once, the preset per-frame
  block, the motion warp (`zoom`, `zoomexp`, `rot`, `cx`/`cy`, `dx`/`dy`,
  `sx`/`sy`) with the per-pixel block on top, the decay fade (the reference's
  warp fragment shader applies it to the sampled colour), the blur passes and
  the blur chain's edge darkening, the shapes and waves (which are added to the
  warped frame before the centre darkening and the border, so those later passes
  cover them), the centre darkening, the border, and finally the reference's
  final composite — either the custom comp shader or the legacy video echo and
  gamma adjustment, never both. The custom-wave rasterizer clips line segments
  against the frame before converting endpoints to pixels; clamping endpoints
  creates spurious bright border lines. Custom-wave points first use MilkDrop's
  inverse aspect factors about the frame centre; thick lines use four
  full-opacity offsets in a 2x2 pixel footprint. The CPU textured-shape fallback
  multiplies the sampled frame RGB by the interpolated shape RGB and blends
  using interpolated shape alpha, matching MilkDrop's fixed-function texture
  stage. The legacy path also multiplies the finished frame by the reference's
  animated hue shade, a four-corner colour whose three channels are animated
  sines normalised so their maximum is one; the per-preset offsets are seeded
  from the preset name so the look is reproducible, and the OpenGL display pass
  applies the same shade. The warp shader reads the **retained** blur chain;
  after the warp the chain is rebuilt from `VS[0]`, the feedback the warp just
  sampled, and kept for the next warp, so the next warp's `sampler_main` is one
  generation newer than its `GetBlur1`-`GetBlur3` chain. `milkdropfs.cpp`
  documents this as intentional ("when sampling the blurred textures in the warp
  shader, they are one frame old"); never rebuild the chain from the same
  feedback the warp samples to make a `GetBlur - GetPixel` shader settle. A
  fresh chain is black and marked ready so the first warp reads retained black.
  The Skia warp pass binds the renderer's retained levels (`WarpPass.Render`'s
  `blurLevels`) instead of rebuilding them, and a blur-sampling program stays
  off the Skia frame path when the levels cannot be supplied. Its comp shader
  also binds VS[0] to every main sampler; VS[1], the current warp plus overlays,
  only becomes feedback after presentation. `Composite` adds the overlay frame
  into the warped frame and `Publish` copies the finished frame into the display
  buffer, so a post effect never runs after the publish. The per-pixel block
  sees the warped position in `x`/`y` on the per-pixel fallback path, which is a
  deliberate deviation from Milkdrop offset semantics so the built-in presets
  keep working; the mesh path gives it the reference's aspect-scaled zero-to-one
  vertex position. Numeric preset keys are parsed into
  `VisualizerPreset.Defaults` and applied as the per-frame starting values after
  the computed seeds, which is how Milkdrop presets carry most of their
  settings; never drop that step or key-only presets lose their wave, border,
  and echo parameters. `VisualizerPreset.KeyAliases` maps the Milkdrop 2 short
  keys onto the Milkdrop 1 names the variables use, so the `b1n`/`b1x`/`b1ed`
  and `b2`/`b3` blur and edge keys resolve to
  `blurN_min`/`blurN_max`/`blurN_edge_darken` instead of being dropped, and a
  preset that carries only them takes its blur amount from their `blurN_max` sum
  when Orynivo's own `blur_level` key is absent. Keep new aliases in that one
  table. The reference's per-frame variable names are the authoritative ones
  (`gamma`, `wrap`, `wave_usedots`), so register those and alias the MilkDrop 2
  keys onto them (`fGammaAdj`, `bTexWrap`, `bWaveDots`); per-frame code writes
  the variable, not the key. The default wave mode is the single line, which is
  six in the reference's `nWaveMode` numbering (its idle preset uses six), and
  the default `wave_scale` is one. Preset wave scales may exceed one (for
  example Mashup (129) uses 28.599); never clamp MilkDrop's `fWaveScale` to the
  zero-to-one range. `MilkdropWaveform` owns the default wave's per-mode
  geometry — ring, spiral, centred spirograph, derivative line, explosive hash,
  line, double line and spectrum line — with the reference's edge clipping,
  closed-loop modes and four-tap polyline smoothing; `DrawDefaultWave` only
  prepares the PCM, applies the legacy global `per_point` block, and draws the
  result. Never reintroduce a line/circle approximation, and keep the geometry's
  sample count following the source so a short buffer cannot read an empty tail.
  The default wave draws `MilkdropWaveform.VertexCount` (480, the reference's
  `NUM_WAVEFORM_SAMPLES`) vertices while its prepared buffer keeps 512 samples,
  because the explosive-hash and derivative modes peek `i + 32` ahead.
  `wave_smoothing` is a registered variable (`fWaveSmoothing` aliases it) and
  `DrawDefaultWave` reads it from `Preset.Defaults`; it was missing from the
  layout, so a preset such as `suksma - frust` had its `fWaveSmoothing=0.9`
  ignored and its explosive hash stayed unsmoothed. The explosive-hash mode is
  the only mode that tones its alpha down, by the reference's resolution factor
  (`milkdropfs.cpp`: `alpha *= 0.07` at 256 through `0.13` at 2048);
  `WaveSizeAlphaFactor` maps the rounded-up power-of-two frame width onto that
  table. Every mode's alpha rule is in `ApplyWaveAlphaScale` (mode 1 `*1.25`,
  mode 3 `factor*1.3*treble²`, mode 4 `*0.2`), followed by the
  `bModWaveAlphaByVolume` modulation through
  `fModWaveAlphaStart`/`fModWaveAlphaEnd`. `bMaximizeWaveColor`
  (`wave_brighten`) normalises the wave colour so its brightest channel is one.
  The legacy display filters `bBrighten` (`sqrt`), `bDarken` (square),
  `bSolarize`, and `bInvert` are applied after the hue tint and gamma in
  `ApplyHueShadeAndGamma` and in the GL display pass, in the reference's
  `GenCompPShaderText` order. The default waveform and the four custom waveforms
  are separate: the default one uses the global `wave_*` settings and the global
  `per_point` block, while each `VisualizerWave` carries its own `wavecode_N_*`
  state and `wave_N_*` blocks. `PresetRenderer.DrawCustomWave` builds the sample
  data from the waveform or, only when `wavecode_N_bSpectrum` is set, from
  `IVisualizerAudioSource.Spectrum`, smooths and scales it, and gives the
  per-point block the reference's `sample`/`value1`/`value2` contract; the
  polyline is smoothed with the reference's four taps. Never draw spectrum bars
  unconditionally, and keep the waveform scratch buffers reused so an overlay
  stays allocation-free. A GPU overlay draws the custom waveforms instead of the
  CPU: `PresetRenderer.CollectWaveGeometry` makes `DrawWavePoints` expand the
  smoothed polyline into a triangle list (`WaveGeometry`) and skip the per-pixel
  line rasterizer, whose 4-offset thick lines otherwise cost hundreds of
  milliseconds at a high render resolution. The geometry is in the engine's
  minus-one-to-one top-down overlay space, the same space `ShapeFillVertex`
  uses, and the pipeline draws it with the shape program untextured and the same
  "over"/additive blend `PaintPixel` applies. Keep the CPU rasterizer as the
  fallback for a caller that does not collect the geometry;
  `CollectWaveGeometry` must only be set while the pipeline reports it can draw
  the geometry, because the renderer then skips its own draw.
  `VisualizerTextureBank` generates the Milkdrop noise and random textures from
  fixed seeds instead of bundling third party images: keep generation
  deterministic and lazy. Use MilkDrop's own sizes, not arbitrary ones:
  `noise_lq`, `noise_mq`, and `noise_hq` are 256×256, `noise_lq_lite` and the
  random textures are 32×32, and the volume noises are 32³, because
  `texsize_noise_*` and every shader that scales sampling by it must match the
  reference. It also generates the two 32³ volume noises
  (`sampler_noisevol_lq`/`hq`) that `tex3D` samples, quantised to eight bits and
  laid out as a slice atlas (`VolumeAtlasColumns`/`VolumeAtlasRows`); the CPU
  interpreter and the GPU helper must read that one volume with the identical
  trilinear math, because a procedural hash cannot be reproduced bit-exactly on
  the GPU. `VisualizerUserTextures` loads a preset's own image textures by name
  (`sampler sampler_seaweed;` resolves to
  `textures/seaweed.jpg|.png|.bmp|.gif|.webp|.tga` beside the presets, like
  MilkDrop) with SkiaSharp, caches them per name, and is configured by the
  window and the harness; an unresolved name keeps the frame fallback. It ships
  no third-party asset. The `sampler_main`, `sampler_pc_main`,
  `sampler_fc_main`, `GetBlur1`-`GetBlur3`, and `GetPixel` constructs are HLSL
  shader features and belong to the shader runtime in phase 38d, not to the
  texture bank. A sampler's `fc_`/`fw_`/`pc_`/`pw_` (or swapped) qualifier
  selects its wrap and filter mode, not a different moment in time, so every
  qualified `main` sampler reads the same frame; `ShaderSamplerName` is the
  shared parser, `PixelBuffer.SampleShader` performs the wrap and filter, and
  the qualifier is stripped before the generated noise and random textures are
  resolved. Keep the interpreter, the Skia passes, and the OpenGL pipeline on
  that one parser and one sampling rule. The Milkdrop format has no per-preset
  texture block, so unknown `tex_*` keys stay ignored. A preset may still
  declare its own texture in the shader source, such as `sampler sampler_cells;`
  in Royal Mashup (142). `ShaderTranspiler` collects every sampler a shader
  names beyond its built-in set and declares it with the type the dialect needs:
  `sampler2D` in GLSL, `shader` in SkSL. Emitting the SkSL spelling on both back
  ends failed the complete GLSL warp shader because `uniform shader` is not
  GLSL; a texture the engine cannot provide falls back to the previous feedback,
  which is what the binding already did. The shader runtime is built in three
  steps: `ShaderLexer` is the tokenizer and stays a pure, allocation-bounded
  function over the source, `ShaderParser` builds the tagged-union `ShaderNode`
  tree from it, and `ShaderInterpreter` evaluates that tree, and the
  `warp_N_*`/`comp_N_*` bindings with the per-frame cost budget come last.
  `ShaderInterpreter` keeps its variables in a plain dictionary the caller seeds
  and reads back, samples only through `IShaderSampler`, and guards itself with
  a loop budget and a call depth limit; keep both, because a runaway shader must
  never stall a frame. `VisualizerPreset` parses the numbered `warp_N`/`comp_N`
  keys with their `_enabled`, `_per_frame`, and `_per_pixel` companions, and the
  preset reader must keep the newlines inside a multi-line value because that is
  how Milkdrop stores shader source. A shader that fails to parse is skipped,
  never fatal. `VisualizerPreset.ParseSections` splits a `.milk` file at its
  `[presetNN]` headers, so a file that holds several presets yields several
  presets; the declared format version is reported but never gates loading,
  because Milkdrop versions its presets and every version must stay usable.
  `PresetRenderer` implements `IShaderSampler` and enforces
  `ShaderTimeBudgetMilliseconds`: the shaders never stop running, they lose
  resolution. Both the warp shader and the comp shader run on the grid
  `ShaderGrid` returns and are scaled back over the frame; `AdaptShaderGrid`
  moves the grid's pixel target between `ShaderPixelFloor` and
  `ShaderPixelBudget` from the measured shader cost, so a preset that overruns
  settles at a coarse but visible picture. Never reintroduce a mechanism that
  switches the shaders off for a frame, and never let the budget compare the
  whole frame against the shader threshold: that combination is what made a real
  preset collection render nothing but the shared overlay. `ShaderGridReduced`
  reports the reduced grid for diagnostics. The warp shader grid is scaled back
  with `PixelBuffer.SampleBilinear`, because it is the base picture; only the
  comp pass may scale with nearest-neighbour. `WarpStageBudgetMilliseconds` is
  the warp stage's own ceiling: the per-pixel program runs once per screen
  pixel, so a preset that loops inside it can cost seconds for a single frame.
  When the stage passes that ceiling the program is left out for the rest of the
  frame and retried a moment later, so the frame still draws with the per-frame
  motion values. Never let that path become a per-frame hang, and never make a
  frame wait for a per-pixel program that cannot finish. A disabled shader
  silently costs a preset its picture, so keep `ShaderError` carrying the
  reason, and keep the three bindings real presets depend on: `ret` is the
  output variable when a body returns nothing (`ShaderInterpreter.ReturnedValue`
  says which case applies), `GetPixel` accepts both `GetPixel(x, y)` and
  `GetPixel(float2(x, y))`, and `aspect` is bound as the float4
  `(aspectX, aspectY, 1/aspectX, 1/aspectY)`, because presets read `aspect.zw`
  and Milkdrop/projectM define it that way. The preset _scalars_
  `aspectx`/`aspecty` are not the same pair: Milkdrop binds them to the
  **inverse** factors (`var_pf_aspectx = m_fInvAspectX`, `milkdropfs.cpp`), so a
  landscape frame is `(1, width/height)`. Keep the two apart, because per-pixel
  code that multiplies its position delta by `aspecty` otherwise applies the
  aspect correction twice. `hue_shader` is bound too: the reference defines it
  as the final quad's vertex diffuse (`#define hue_shader _vDiffuse.xyz`) and
  always computes it, four corners of `0.5 + 0.5*normalised sine` ("since we
  don't know if shader uses it or not"), so a warp or comp shader may read it
  and a shader that reads zero paints the wrong picture -
  `$$$ Royal - Mashup (138)` clamped itself to black that way.
  `PresetRenderer.ComputeHueShades` runs before the overlay half returns, the
  interpreter binds the value per pixel through
  `HueShadeAt(originalU, originalV)`, and the GPU path carries the four corners
  as the twelve `hue_shader_<channel><corner>` scalars that
  `ShaderTranspiler.EmitHueShader` mixes by the fragment's own position. Motion
  vectors are **not** an engine grid: the reference's `mv_x`/`mv_y` are the
  arrow grid's size, `mv_dx`/`mv_dy`/`mv_l`/`mv_a` are ordinary blendable
  variables (`mv_a` defaults to one when the legacy `bMotionVectorsOn` key is
  set, otherwise zero), and `DrawMotionVectors()` only draws that arrow grid.
  Orynivo gates the arrows on `mv_a` (via the
  `bMotionVectors`/`bMotionVectorsOn` aliases) and colours them from
  `mv_r`/`mv_g`/`mv_b`, but the field itself is Orynivo's own `mv_enabled`-gated
  recording on a fixed grid rather than the reference's reverse-propagated
  `mv_x`/`mv_y` output; that recording remains a documented extension, not
  reference behaviour. The interpreter walks the tree per pixel, which is the
  known cost limit; a JIT compiler for shaders is the documented follow-up if
  the CPU cost proves too high. `PresetRenderer.Timings` and `AverageTimings`
  carry the `RenderTimings` breakdown per frame and per averaging window, and
  `ResetTimings` restarts that window; keep the measurement cheap (one stopwatch
  and a handful of marks per frame) and never time a per-pixel shader call,
  because the measurement would cost more than the work. A warp shader is
  therefore part of `Warp`, while `Shader` is the comp stage. `ShaderRuntime`
  owns every shader operation and both execution paths call it, so the
  interpreter stays the reference implementation and `ShaderCompiler` can only
  be correct if it produces the same values; `ShaderCompilerTests` asserts
  exactly that and must keep passing. Keep its function set aligned with
  `ShaderTranspiler`'s vocabulary: a function the emitter can translate but the
  runtime does not know makes the CPU silently disable a shader the GPU renders,
  and `lum` alone appears in a third of a real collection. `lum` must use
  MilkDrop's `include.fx` weights (`dot(x, float3(0.32, 0.49, 0.29))`), not the
  conventional Rec. 601 luma, because the interpreter and both emitters have to
  agree and a comp shader derives its blur gradient through it. The matrix types
  are part of that set now: `float2x2`, `float3x3`, and `float4x4` were unknown
  and disabled 788 presets' shaders. A matrix lives in a per-pixel pool in
  `ShaderRuntime` (`StoreMatrix`/`ResetMatrixPool`), and `ShaderValue` carries
  only its index and dimension; do not move the nine or sixteen components into
  the value, because an inline matrix made the measured 640 x 360 comp-shader
  frame go from 26 ms to 47 ms. `ConstructMatrix` builds any of the three
  dimensions from scalars or a vector, `HlslMultiply` handles matrix-by-vector,
  vector-by-matrix, and matrix-by-matrix, and `ShaderTranspiler` maps `floatNxN`
  onto SkSL's `matN` (spreading a vector argument into scalars, because `matN`
  has no four-component constructor) and must not narrow a matrix argument. The
  shader entry points must clear the pool before evaluating a pixel, so a handle
  can never point at a matrix another pixel built. Preset switching blends like
  the reference. `PresetBlend` owns the pure cosine easing and the variable
  contract: the interpolated set (`decay`, the waveform colours and position,
  both border bands, the motion-vector variables, the video echo, `gamma`, and
  the blur range keys), the snapped set (`wrap`, `echo_orient`, the wave flags,
  `darken_center`, and the legacy display filters), and the motion set, which is
  never blended. `PresetRenderer.CaptureFrameState` returns a copy of the live
  slots and `SetBlend(outgoing, outgoingState, progress)` continues the outgoing
  preset from those slots; the outgoing preset's per-frame block runs each blend
  frame on its own `_blendSlots` so its user variables keep advancing, and
  `ApplyBlend` eases the interpolated values into `_slots` after the incoming
  per-frame block. Never blend the motion variables or the per-pixel state, and
  never re-run the outgoing preset's init block on every frame. The GPU warp
  additionally morphs its sampling coordinate from the outgoing preset's
  captured mesh to the incoming one (the outgoing mesh is passed as a second
  per-vertex attribute block and `uBlend` eases the mix); the outgoing preset's
  own warp shader is not drawn, so a preset with a custom warp shader shows the
  incoming shader on the morphed coordinate. The feedback still carries the
  previous picture, so a switch morphs rather than restarting from black.
  `PresetRenderer.SeedFrameVariables` must keep `fps` finite on the first frame,
  because a preset that divides by it otherwise accumulates an infinity that
  then reaches the sampler. The per-frame `rand_frame` vector uses
  `Random.Shared` by default so a preset still looks different on every run like
  the reference; `PresetRenderer.RandomSeed` draws it from a private generator
  instead, so diagnostics and A/B comparison can reproduce a render, and the GL
  harness exposes that as `GLH_RANDOM_SEED`.
  `PresetSkiaComparisonDiagnosticTests` is the harness that compares both
  execution paths over a real collection and reports their drift. Keep the
  interpreter and the emitter agreeing on the shader semantics the harness
  covers: a declaration and an assignment coerce their value to the declared
  type (`ShaderRuntime.Coerce`, which HLSL and the emitter both apply), the
  shader's `/` treats a zero divisor as zero (`orynivoSafeDiv` on the GPU), a
  blur and `GetPixel` read the same frame `sampler_main` refers to, and the
  compiler remembers each variable's declared component count so its compiled
  path matches the interpreter. The comp shader is a **display** pass, not a
  feedback stage: `PresetRenderer.Output` is the post-comp frame the presenter
  shows, while the next frame warps from the pre-comp composite (`MeshSource`,
  and the frame-end copy of `_frameCopy`). Feeding the comp output back lets a
  `ret *= 10` comp shader compound every frame until the whole frame is white,
  which is what `LuxXx - BadBallz Beta` did; `CompFeedbackTests` is the check.
  `ShaderTranspiler` emits SkSL for the CPU runtime-effect path and GLSL for the
  OpenGL GPU path and must stay honest against the same reference: the GPU and
  the interpreter have to agree on one variable universe, so the emitter knows
  the engine-bound per-pixel variables (`uv`, `uv_orig`, `rad`, `ang`), infers
  an intrinsic's return type from its arguments (and reports the narrowed
  component count for the intrinsics whose arguments it narrows, because a later
  operation otherwise skips a conversion SkSL needs), declares a sampler from
  the shader's own `tex2D`/`tex3D` call instead of a fixed list, reads an
  undeclared variable as a zero constant the way the interpreter does, gives a
  written uniform a writable copy in main, and renames a name SkSL reserves. A
  comparison of vectors is emitted component-wise with `step`/`sign`, because
  SkSL rejects a bool vector as a condition, and a comparison used as a
  condition compares the first components to match `ShaderValue.IsTrue`. A
  file-scope variable a helper reads is passed into the helper as a parameter,
  transitively through the helpers it calls, because SkSL runtime effects have
  no mutable globals and the helper is emitted before `main` declares it.
  Milkdrop keeps one variable universe for the expression blocks and the shader,
  so `q1`-`q32` and `t1`-`t8` are seeded from the preset slots on both CPU paths
  (`BindShaderVariables` per pixel and `SeedCompiledShaderFrame` once per frame)
  and on the GPU; keep the two sides seeding the same set, because a shader that
  reads a variable the interpreter leaves at zero renders a different picture on
  the two paths. That universe also carries MilkDrop's `include.fx` extras that
  real presets use: the `float4` q banks `_qa`-`_qh` (q1-q4 through q29-q32) and
  `vol_att`. `_qa`-`_qh` are macros in the GLSL prelude and `float4` uniforms in
  the SkSL prelude, the type table marks them `float4` so `float2x2(_qb)`
  spreads its components, and `WriteShaderUniforms` seeds them from the preset
  slots; a bank read as an unknown scalar (a zero) broke Royal Mashup (151) with
  a divide by zero. Do not drop these: a scan of a 10,353-preset collection uses
  the banks in about 600 presets and `vol_att` in 206. The sampler-size uniforms
  (`texsize`, `texsize_main`/`fc_main`/`pc_main`, and
  `texsize_noise_lq`/`mq`/`hq`/ `noisevol_lq`/`hq`) are part of the same
  universe: declare each in the GLSL prelude as scalars with a reconstructing
  macro (the GL setter is scalar-only), declare them as `float4` in the SkSL
  prelude, and seed them in `WriteShaderUniforms`, `BindShaderVariables`, and
  `SeedCompiledShaderFrame`. A shader that reads `texsize_noise_lq.zw` for a
  dither coordinate (Royal Mashup (188)) rendered a different picture when they
  were left at zero. `SamplerSizeUniformTests` pins the values. MilkDrop's
  twenty-four `rot_*` matrices are `float4x3`, a non-square type that neither
  the interpreter's square-matrix pool nor SkSL models, so they are never
  materialised as a type. `ShaderRotationMatrices` builds the reference's
  row-vector composition (`Rx * T * Rz * Ry`, the rotation speeds following
  `0.9 * (k / 8)^3.2`, the last four re-randomised every frame) into three
  `float4` columns per matrix, seeds them from the preset name so a preset looks
  the same across runs, and rewrites the shader source before the parser sees
  it: `rot_d1[1].y` becomes the column component `rot_d1_c1[1]` and
  `mul(uv, rot_d1)` becomes the `orynivo_mul4x3` call the prelude defines. Keep
  the rewrite in `TranslateShaderDialect`, keep the columns in
  `ShaderTranspiler.UniformComponents` (so both back ends declare and seed
  them), and keep `Build` once per frame in `RenderFrame` before any stage reads
  them, because the interpreter binds them per pixel and the GPU reads them
  through `WriteShaderUniforms`. Only bind them for a preset whose shaders
  reference a `rot_` name. `SkiaShaderRunner` must bind a child shader for every
  sampler `Transpile` reports, and the CPU/GPU comparison tests must keep
  passing. Its `CompPass` runs a comp shader, with its own per-pixel block
  emitted into the same effect, as a runtime effect over the renderer's frames;
  it binds the previous feedback to every main sampler, builds blur from it, and
  scales each sampler by its own `texsize_*`. `PresetRenderer.UseSkiaPasses`
  gates it because the eight-bit Skia surface differs from the float interpreter
  by up to one level. The visualizer enables it by default and the interpreter
  stays the fallback, so a preset or pass the Skia path cannot handle still
  renders. `UseSkiaPasses` covers the per-pixel shader passes (the comp shader
  and a warp shader), whose compiled SkSL is faster than the interpreter; the
  full-frame passes (the geometric warp, the video echo, the borders, and the
  composite) are gated separately by `PresetRenderer.UseSkiaFramePasses` and off
  by default, because on the raster Skia surface each converts the whole frame
  to an eight-bit bitmap and back and measured slower than the interpreter's
  in-place float passes. The frame passes' runtime effects are cached for the
  process, because their SkSL is constant and Skia compiles an effect when it is
  created; keep that cache, because rebuilding them every frame cost more than
  the passes. `SkiaShaderRunner.BlurFrame` is the CPU runtime-effect blur pass
  and must keep reproducing `PixelBuffer.Blur`'s nine-tap clamped filter; the
  comp pass builds its blur levels from it, and the frame helpers
  (`GetBlur1`-`GetBlur3`) must keep scaling their normalised coordinate by
  `texsize`. `SkiaShaderRunner.VideoEcho` is the CPU runtime-effect video-echo
  pass and must keep the CPU pass's zoom, flip, alpha blend, and leave-untouched
  rule. `SkiaShaderRunner.Composite` is the CPU runtime-effect additive
  composite and must keep the CPU pass's clamp. `SkiaShaderRunner.Borders` is
  the CPU runtime-effect border pass and must keep the CPU ring geometry and
  blend: MilkDrop draws two clip-space Chebyshev rings sized by the preset's
  `ob_size`/`ib_size` (default 0.01), `[1 - ob_size, 1]` and
  `[1 - ob_size - ib_size, 1 - ob_size]`, blended with `SRCALPHA`/`INVSRCALPHA`.
  Never reintroduce fixed band values; a wrong border changes the feedback of a
  preset whose only content is its border, such as Royal Mashup (13). A border
  channel is converted the way MilkDrop's `D3DCOLOR_RGBA_01` macro does: an
  in-range value is kept, an out-of-range value is truncated to a byte and
  therefore wraps (`ib_a = 1 - mytime + bass` reaching 1.5 draws at 126/255, not
  at full opacity). `PresetRenderer.BorderChannel` is the single place for that
  conversion, used by both the CPU ring and `ReadFrameParameters`.
  `SkiaShaderRunner.Warp` is the CPU runtime-effect geometric warp and must keep
  the CPU motion transform and the black-outside-the-frame rule.
  `PresetExpressionTranspiler` emits the preset's per-pixel expression language
  as SkSL from the same `PresetSyntaxNode` tree the interpreter compiles, so the
  GPU and the CPU cannot disagree about a block; it reports the uniforms the
  caller seeds, maps `x`/`y`/`rad`/`ang` onto engine locals, and refuses
  `megabuf`/`gmegabuf` and `rand`, which stay on the interpreter.
  `ShaderTranspiler.TranspileWarp` composes that block with a warp shader (or a
  direct frame sample) into a warped-`uv` entry point: `uv` comes from the
  motion transform plus the per-pixel block, `uv_orig` is the pixel position,
  and the shader's polar pair is derived from `uv`, exactly as the CPU stage
  computes them. `SkiaShaderRunner.WarpPass` runs that effect over the previous
  frame; the renderer uses it for a preset with at most one warp shader, no
  per-shader per-frame block, no motion recording, and a per-pixel program whose
  written values are assigned before they are read
  (`PresetExpressionTranspiler.CanRunInParallel`), because the runtime effect
  evaluates every pixel independently and cannot reproduce a value carried from
  the previous pixel. Anything else keeps the interpreter, which is the
  reference for those expressions. The per-pixel emitter and the shader emitter
  share the naming and type helpers in `SkSL`. A frame sampler must keep the
  renderer's coordinate convention: `PixelBuffer.SampleBilinear` maps a
  normalised coordinate to `zero..size-1`, while a generated texture maps it to
  `zero..size`, so `tex2D`, `GetBlur1`-`GetBlur3`, and `GetPixel` on a frame
  scale by `texsize - 1` with a half-texel shift (or an integer truncation for
  `GetPixel`). `PresetRenderer.UseSkiaPasses` gates the comp shader and warp
  shader pass. `UseSkiaFramePasses` separately gates full-frame helpers. Only
  straight-line bodies (declarations and one return) are compiled — branches,
  loops, and swizzle assignments stay on the interpreter, because a wrong
  picture is worse than a slow one — and the compiled path must seed the
  frame-constant variables once per frame and only `uv`, `uv_orig`, `rad`, and
  `ang` per pixel. A comp shader is a post-processing pass, so it runs on a grid
  bounded to `ShaderPixelBudget` pixels and is scaled back over the frame; keep
  that, because it is what makes the pass affordable on the CPU, and keep the
  direct-to-frame write when the grid matches so the picture is not resampled
  through an identical-size copy. The per-pixel path is the measured bottleneck,
  so it must stay free of avoidable work: `PresetProgram.ReferencedVariables`
  (filled by the compiler as it resolves each variable) is the conservative
  usage set, and the warp stage resolves its slots once in the constructor and
  only computes `rad`, `ang`, the motion grid, and the seeded sampling position
  when the preset's per-pixel code references them. Keep that set conservative —
  reporting a variable a program does not really use only costs a little work,
  while missing one changes the picture — and keep a rendered frame
  allocation-free, which `RenderTimingTests` asserts. Full-frame passes may run
  in parallel only when they are row-independent: a pixel reads the source
  buffer and writes its own pixel, nothing else. `ParallelRows.For` owns the
  split and keeps small frames on the calling thread. The blur, decay, gamma,
  centre darkening, edge darkening, video echo, and composite passes are
  row-independent and split the same way, gated by
  `PresetRenderer.ParallelismEnabled`; `PixelBuffer.Blur` reuses one scratch
  array instead of allocating a copy per pass, and `PresetRenderer` gives each
  worker its own sample scratch. `PresetRenderer.DarkenEdges` applies the blur
  chain's `blurN_edge_darken` after the blur passes; its falloff is our own
  documented approximation, because a preset does not store the shape, so the
  centre stays untouched and the border is multiplied towards `1 - amount`. The
  warp is parallel when the per-pixel program cannot leak a value out of its
  pixel: everything it writes is either re-seeded per pixel (`x`, `y`, `rad`,
  `ang`), or a pixel-local temporary the block assigns before it reads and no
  other stage reads. `PresetVariableLayout.Standard` is what makes that decision
  precise, so a write to a standard name such as the shape alpha `a2` stays
  sequential even when nothing else seems to read it; a preset's own temporary
  such as `spin` does not. `PresetExpressionTranspiler.CanRunInParallel` covers
  the assign-before-read half. A warp shader or an active motion grid keeps it
  sequential for the same reason. Never give two workers the same slot array,
  and never claim a speed-up without the identical-frame test in
  `ParallelWarpTests`. `PresetRenderer.MeshPerPixelEnabled` (off by default)
  makes the warp evaluate the per-pixel program once per mesh vertex and
  interpolate the coordinate each vertex transforms to across the quad, which is
  what the reference warp vertex shader does; `BuildMesh` fills the mesh from
  the per-frame values first, so a program that exceeds the warp budget leaves a
  plain warp rather than an unfinished mesh, and it restores the per-frame
  motion afterwards because a later stage means the frame's values, not the last
  vertex's. The coordinate mesh is interpolated with a lerp, so an affine
  transform stays within floating-point precision of the per-pixel path —
  `PerVertexMeshTests` asserts that and that a varying motion is interpolated. A
  program that writes `x` or `y`, records motion vectors, or feeds a warp shader
  keeps the per-pixel path, because an interpolated sample position has no
  meaning. `BuildMesh` writes the per-vertex `x`/`y`/`rad`/`ang` with the
  reference aspect (`WarpSampling.GetAspect`), matching the reference's
  aspect-scaled zero-to-one vertex position, and a measurement against the
  reference is how that is checked. `MeshGridX`, `MeshGridY`, and `MeshValues`
  are public because a GPU warp reads the mesh as vertex attributes, and the
  value order is part of that contract: zoom, zoomexp, rot, cx, cy, dx, dy, sx,
  sy, warp. `MeshRequested` builds the mesh while the CPU keeps evaluating per
  pixel, which is the transitional double work a GPU warp needs before it
  replaces the CPU warp; `TryCopyMeshMotion` copies the values and `MeshSource`
  exposes the frame the mesh samples. Requesting the mesh must leave the CPU
  frame byte-identical, which `PerVertexMeshTests` asserts. A per-pixel block
  that writes the sample position exposes no mesh, except when
  `MeshForPixelWarp` is set: the emitted warp fragment shader computes that
  coordinate itself and reads only the mesh's motion values, so the mesh is
  still built for it and `ExpressionsOnly` stops the CPU at the overlay instead
  of running the complete frame while the presenter also draws the GPU pipeline.
  `ReadFrameParameters` publishes the clamped per-frame pass values and
  `RenderOverlayFrame` the overlay-only frame, so a GPU pipeline reads them
  instead of duplicating the key lookups and clamps; `RenderOverlayFrame` seeds
  the live variables first, because the overlay reads them.
  `WarpSampling.SamplePosition` is the single definition of the warp's sampling
  arithmetic: the CPU warp calls it per pixel and a GPU warp's fragment shader
  must be a translation of it, so never duplicate that formula. It follows the
  reference warp vertex shader's coordinate contract — scale by the aspect,
  divide by the radial zoom, stretch, apply the time-dependent warp
  displacement, rotate, translate, and scale back by the inverse aspect — and
  its result is in the engine's minus-one-to-one space, which the frame sampler
  expects. `WarpSampling.WarpDisplacement` is the reference's four travelling
  waves (amplitude `warp * 0.0035`, `warp` defaults to one) and the three
  `_orynivo_warp`/`_orynivo_warpTime`/`_orynivo_warpScale` uniforms carry it to
  both translated paths; every declared warp uniform must be set in
  `SkiaShaderRunner.WarpPass.Render`, because Skia leaves an unset declared
  uniform undefined. The aspect is passed in explicitly via
  `WarpSampling.GetAspect` (the reference keeps both factors at or below one — a
  landscape frame is `(1, height/width)`, a portrait frame `(width/height, 1)`)
  and `rad`/`ang` are the aspect-scaled distance and angle the reference derives
  from the position. `WarpSamplingTests` is the reference the translation is
  checked against. A shader's blur levels each keep their own buffer
  (`_blurLevels`) and build on one another, and a level asked for first builds
  the ones below it. Each level is **one** pair of passes from its downscaled
  source — the reference's blur loop advances the level only every second pass
  (`fscale_now = fscale[i/2]` in `milkdropfs.cpp`), and the OpenGL chain's
  `BuildShaderBlurLevels` builds exactly that. Never run N pairs for level N:
  that over-blurred `GetBlur2`/`GetBlur3` on the CPU alone, so a comp shader
  sampling them rendered differently on the two paths. Do not collapse them back
  into one cached level: a comp shader that samples `GetBlur1` and `GetBlur3` in
  the same pixel otherwise invalidates the cache on every sample and rebuilds a
  full-frame blur each time, which cost seconds per frame. The Skia comp pass
  runs on the same adaptive grid as the interpreter and hands the preset to the
  interpreter when a pass exceeds `ShaderPassBudgetMilliseconds`, because Skia
  rasterises a runtime effect on the CPU and a comp shader that samples the blur
  levels otherwise stalls the frame. The preset compiler must accept the
  Milkdrop function set, because a block it cannot compile is dropped and takes
  that preset's motion with it: `above`, `below`, and `equal` yield one or zero
  (never a boolean), and `sqr`, `sigmoid`, `band`, `bor`, and `bnot` belong to
  it too. Names are case-insensitive, and a block that still fails is skipped
  and recorded on `VisualizerPreset.FailedBlocks` instead of rejecting the
  preset, so one unsupported construct costs a block rather than a preset.
  `PresetFolderDiagnosticTests` reports what still fails against a real
  collection; run it after touching the compiler. The numbered parts of an
  expression block are joined by concatenation, because presets split one
  expression across parts and a part may end with an operator; a semicolon is
  inserted only when the previous part is complete, the next does not bring its
  own, and the next does not continue a call whose function name ended the
  previous part. A part's line comment is stripped before the join, because the
  parts are concatenated without a newline and a trailing `// ...` would
  otherwise swallow every part after it, and the numbered parts are read up to
  the shader-line bound, because a real per-frame block reaches the hundredth
  part and a lower bound cut a block off mid-loop. `megabuf` and `gmegabuf` are
  Milkdrop's shared memory buffers and their accesses are serialised, because a
  preset writes lookup tables that other pixels read. `loop(count, statements)`
  repeats a statement list, and `while` accepts both
  `while(condition, statements)` and the one-argument `while(condition)` a real
  collection writes, where the condition is re-evaluated until it turns false
  and the repeated work sits inside it as `exec2(statements, condition)`; their
  iteration count is clamped, and presets nest them inside `if()` and other
  constructs, so the call parser accepts both wherever a primary expression
  starts; both buffer write spellings presets use (`gmegabuf(i, value)` and
  `gmegabuf(i) = value`) must keep working, because presets build their lookup
  tables with them. `exec2`/`exec3`/`exec4` evaluate their arguments and yield
  the last one, an assignment is a valid `if(...)` argument, and a semicolon
  continues that argument as a statement sequence. Keep the interpreter off the
  audio thread and bound its per-frame cost so a heavy shader degrades the
  render resolution instead of stalling playback. The expression language also
  has compound assignments: `PresetLexer` must emit `+=`, `-=`, `*=`, `/=`, and
  `%=` as one token, because splitting them leaves the statement parser with a
  stray `=` and fails the whole block, and the compiler applies them to the
  variable and to the `gmegabuf(i) += x` buffer form. On the shader side the
  parser accepts `while` loops, the comma operator at statement level and inside
  parentheses (never in a call argument list, where the commas separate
  arguments), a declaration initialized with a braced list or a `sampler_state`
  block, element access on a vector, and the integer and double vector types. A
  macro definition ends at its line comment, so `#define a b //comment` must not
  expand the comment into the middle of a call.
  `VisualizerPreset.TranslateShaderDialect` resolves Milkdrop's preprocessor
  conditionals (`#define`, `#if`, `#ifdef`, `#ifndef`, `#else`, `#endif`) and
  its built-in math constants (`M_PI`, `M_PI_2`, `M_2PI`, `M_INV_PI`,
  `M_INV_PI_2`, `M_E`) before `ShaderParser` sees the source, so the parser
  stays plain HLSL; keep the constants in that one table. A fixed-size array
  declaration such as `const float4 samples[5] = { ... }` is parsed into its own
  node and stored in the interpreter's array storage, because indexing an
  unknown array name would render a wrong picture; keep the element type, the
  size, and the flattened initializer together.

## Desktop presentation and lifecycle

- The bitmap fallback renders through `PresetRenderer` into a low-resolution
  `PixelBuffer` and presents it through a scaled `WriteableBitmap`. The default
  presentation uses the OpenGL path described below. Both renderer creations
  enable `PresetRenderer.UseSkiaPasses`, so the comp shader and a warp shader
  run as Skia runtime effects and the interpreter stays the per-pass fallback;
  the full-frame passes stay on the interpreter, and their effects are cached
  for the process because their SkSL is constant. A per-pixel warp program
  prefers the **parallel interpreter** over the Skia warp pass, because Skia
  rasterises a runtime effect on one thread while the interpreter splits the
  rows across every core: measured at 960 x 540, a parallelizable program costs
  about 18 ms through the interpreter against 65 ms through Skia. The Skia warp
  pass therefore only runs for a program the interpreter cannot split, which is
  why the built-in presets keep their per-pixel angle in their own temporary
  (`spin`, `wedge`, `s`) instead of a standard name like `a2`. Be clear about
  what that is: `SkiaShaderRunner` builds its surfaces with
  `SKSurface.Create(Info, pixels, rowBytes)`, which is Skia's **raster**
  constructor, so those "Skia" passes are Skia's CPU runtime-effect JIT and **no
  part of the bitmap fallback preset pipeline runs on the GPU**. The finished
  frame is copied into a `WriteableBitmap`, and the GPU only blits that bitmap.
  A comp shader that samples more than one blur level therefore costs tens to
  hundreds of milliseconds and needs the budget adaptation described below; do
  not describe the Skia passes as a GPU path, and do not remove the interpreter
  fallback on the assumption that Skia is hardware-accelerated. The implemented
  OpenGL path runs the supported pipeline on the GPU: `Avalonia` 12 already
  ships `Avalonia.OpenGL` with `OpenGlControlBase`, and the context is confirmed
  as OpenGL ES 3.0 through ANGLE on Windows.
  `Orynivo.Controls.VisualizerGlPresenter` presents the frame and owns the GPU
  pipeline; it is the default, and `ORYNIVO_VISUALIZER_OPENGL=0` forces the
  bitmap presentation. The GPU owns the frame only for a preset that builds a
  mesh, which excludes a per-pixel block that writes the sample position `x` or
  `y` unless it can be emitted as a warp fragment shader; a block that cannot be
  emitted keeps the CPU warp and the presenter then uploads the finished CPU
  frame. A Milkdrop shape fill runs on the GPU too:
  `PresetRenderer.CollectShapeFills` publishes `ShapeFills`, and
  `VisualizerGlPipeline.DrawShapeFills` draws the fans into the post's source
  with premultiplied `ONE, ONE_MINUS_SRC_ALPHA` (and `ONE, ONE` with the alpha
  channel masked for an additive fill), which is the same "over" `PaintPixel`
  applies. The fan repeats its first rim vertex because `GL_TRIANGLE_FAN` does
  not wrap, its position is y-flipped into OpenGL's space, and a textured fill
  samples the blurred frame with a flipped v. The fixed-function MilkDrop
  texture stage modulates sampled RGB by the interpolated shape RGB and selects
  the shape's diffuse alpha; do not use the sampled frame's alpha as fill
  opacity. A Milkdrop shape's own space is Direct3D's y-up space, so
  `BuildVertices` negates a `MilkdropCoordinates` shape's y as it leaves the
  preset's expression space, and the fan centre is converted the same way; the
  overlay rasterizer itself is y-down, which is why the waveform paths flip
  explicitly instead. Keep those two conventions apart: the reference measures
  `shape_y` from the top but `wave_y` from the bottom. `CollectShapeFills` must
  only be set while `VisualizerGlPresenter.ShapeFillsSupported` is true, because
  the renderer then skips its own fill. The custom waveforms are drawn on the
  GPU the same way: `PresetRenderer.CollectWaveGeometry` publishes
  `WaveGeometry` triangle lists and `VisualizerGlPipeline.DrawWaveGeometry`
  draws them with the shape program untextured and the same blend, so the CPU
  overlay no longer rasterizes the thick lines per pixel (that cost about 250 ms
  per frame for a 512-sample wave at 1920x1080). `CollectWaveGeometry` must only
  be set while `VisualizerGlPresenter.WaveGeometrySupported` is true, because
  the renderer then skips its own draw. The borders and the default waveform
  stay on the CPU. Such a block is emitted by default as a warp fragment shader
  that computes the coordinate per pixel (`ShaderTranspiler.TranspileGlslWarp`
  with a null body, drawn over a full-screen quad with the `_orynivo_*` motion
  uniforms seeded from the mesh's first vertex, and the decay moved to the post
  pass); `ORYNIVO_VISUALIZER_PIXELWARP=0` forces the CPU warp.
  `ShaderTranspiler` must not fail the mesh path over such a block: the mesh
  runs it and the shader never emits it, so only its uniforms are lost, while
  the comp and Skia paths keep failing. A whole-frame CPU comparison cannot see
  the warp, so its check draws the overlay only for the first frames and follows
  the brightness centroid of the remaining warped feedback, with a control
  preset without the block as the negative control. When the preset has no
  shaders, `Orynivo.Controls.VisualizerGlPipeline` owns the whole frame: the
  warp as a mesh draw whose fragment shader is a translation of
  `WarpSampling.SamplePosition`, the blur as the same nine-tap clamped box
  filter with ping-pong targets, and decay, video echo, centre darkening, both
  border bands, gamma, and the additive overlay composite in one post pass. The
  post pass draws the border bands as the same clip-space Chebyshev rings the
  CPU uses, reading `VisualizerFrameParameters.OuterBorder`/`InnerBorder` (Inset
  is the inner clip radius and Thickness the clip width); it must not fall back
  to a min-dimension inset. `PresetRenderer.ExpressionsOnly` is the CPU half:
  the per-frame block, the mesh, and the overlay, with no pixel pass. Keep the
  bitmap path as the fallback for a platform whose GL context never arrives: the
  window confirms the presenter once it has drawn a frame and otherwise switches
  back within `GlPresenterGraceSeconds`, re-enabling the complete CPU frame.
  Keep a shader, context, or pipeline failure logged and non-fatal (a failed
  pipeline hands the frame back to the CPU), and remember two `GlInterface`
  limits: it exposes only scalar uniforms, so a vector uniform is set component
  by component, and it exposes no `TexSubImage2D`, so a texture update
  re-specifies it through `TexImage2D` or goes through `GetProcAddress`. Every
  GL texture holds the frame bottom-up, which is OpenGL's natural orientation,
  so the overlay upload and the warp shader convert between that and the
  engine's top-down coordinates; do not mix the two. The presenter must draw on
  **every** refresh and re-present the texture it drew last when the render
  thread published nothing new: Avalonia's GL surface is double buffered, so a
  refresh that draws nothing swaps to the buffer two presentations old and the
  picture appears to jump backwards. The render loop publishes at the configured
  frame rate while the control refreshes at the display rate, so an undrawn
  refresh is the normal case. **Every** pass clamps its colour output to
  zero-to-one. When publishing a GPU frame, snapshot the overlay, mesh, shape
  fills, and uniforms together: the render thread reuses its buffers
  immediately, while Avalonia's GL callback consumes the published frame later.
  `VisualizerPresetLibrary.At` logs the exact selected external section and
  SHA-256 digest on first parse; the window logs the first frame's wave mode and
  whether the GPU pipeline or CPU frame was actually handed to the presenter.
  The GL callback also logs the applied preset index, linked shader stages, and
  GLSL digests; inspect this event when the selected label and displayed picture
  disagree. Mouse navigation advances only on the first primary-button press and
  must exclude the transport button itself as well as its descendants. The clamp
  is what gives a preset that amplifies its own feedback a stable fixed point,
  so a float format without it diverges exponentially, becomes an infinity and
  then a NaN, and paints the frame white. The float format buys precision, not
  range. For `RGBA16F` render textures, `TexImage2D` must use `GL_HALF_FLOAT` as
  its data type even with null initial data; ANGLE rejects `GL_UNSIGNED_BYTE`
  with 0x502 and forces the pipeline's eight-bit fallback. A preset with shaders
  now uses the GL pipeline too when its comp and warp shaders emit GLSL:
  `ShaderTranspiler`'s GLSL dialect (`TranspileGlsl`, `TranspileGlslComp`,
  `TranspileGlslWarp`) produces a `void main()` writing `orynivoColor`,
  `VisualizerGlPipeline` runs the warp shader in place of the fixed mesh warp
  and the comp shader after the post pass into its own display target, and the
  feedback stays the pre-comp frame. The GLSL samples with normalised
  coordinates while Skia's `eval` takes pixels, so the emitter branches on the
  dialect; the vector uniforms are declared as scalars with a reconstructing
  macro because `GlInterface` only exposes scalar uniform setters; and
  `PresetRenderer.WriteShaderUniforms` seeds the same values the interpreter
  binds. A shader the dialect cannot express leaves that stage on the fixed
  pipeline, and a preset whose shaders do not emit keeps the CPU frame path.
  `VisualizerWindow` owns `VisualizerAudioHub.IsActive`: while it is false the
  players skip the tap entirely, so a closed visualizer costs nothing. Never
  render, analyse, or evaluate preset expressions on the audio thread. A preset
  switch must retain the audio analyzer's long-term loudness history; clearing
  it on every selection can produce an extreme first-frame warp in MilkDrop
  presets. Keep the window's `ReduceMotion` path on
  `PresetRenderer.RenderOverlayOnly` so the reduce-motion preference is
  honoured. The window renders with a silent audio source when nothing is
  playing, so it never stays black, and `PixelBuffer.SampleBilinear` returns
  transparent black outside the frame: clamping to the edge smeared the border
  colour into long gradients when a preset warped outwards. Writing the frame
  into the bitmap is not enough to make it visible: `Present()` must also call
  `InvalidateVisual()` on the image, otherwise the window stays black even
  though every frame renders correctly. The transport button **Visualisierung**
  opens the window (there is no sidebar entry); it overlays the current title
  and artist at the top and previous, play/pause, and next buttons at the bottom
  left, wired through `VisualizerTransport` to the normal transport methods so
  playback can be driven from the fullscreen window. Its overlay buttons copy
  the transport bar's own geometry and sizes (36 px skip buttons with the
  transport's 16 px glyphs, a 50 px play button with its 20 px glyph) instead of
  scaling a generic path, because a stretched glyph does not sit optically
  centred in its circle. The overlay buttons are deliberately not focusable: the
  arrow keys switch presets, and a focusable button would keep a focus ring
  after the key press. `AppSettings` stores the render size
  (`VisualizerRenderWidth`/`VisualizerRenderHeight`), the target
  `VisualizerFrameRate`, and the user preset folder; the **Visualisierung**
  settings section edits all three, and the window clamps them to a sane range
  (160-7680 wide, 5-240 fps). The resolution choices run from 320 x 180 up to
  3840 x 2160; keep the list ordered from the largest down, and resolve a
  missing selection through a named default rather than an index, because an
  index silently changes meaning when an entry is added.
  `VisualizerAutoAdvanceEnabled` and `VisualizerAutoAdvanceSeconds` (default 15)
  let the render loop advance to the next preset after the dwell time; the
  window only sets `_presetIndex` and lets the next frame apply the switch, so
  the change stays a render-thread request like the key and mouse navigation.
  `AppSettings.VisualizerPresetBlendSeconds` (default 0 = hard switch) makes the
  window capture the outgoing preset's live slots with
  `PresetRenderer.CaptureFrameState` before the old renderer is disposed and
  call `SetBlend` each frame with the eased progress, so a switch eases its
  non-motion parameters instead of snapping; the blend is cleared on reset, on
  the next switch, and when the progress reaches one. The window additionally
  snapshots the outgoing preset's GPU mesh and passes it with the eased mix to
  `VisualizerGlPresenter.SetPipeline`, whose warp morphs the sampling coordinate
  from that mesh to the incoming one through the second per-vertex motion block
  and `uBlend`. The defaults are 640 x 360 at 60 frames per second, which the
  parallel frame passes made affordable. The built-in presets are structured
  warp-shader effects (see `VisualizerPresets`), and
  `RenderTimingDiagnosticTests` documents their cost profile at two resolutions.
  `VisualizerAlwaysShowOverlay` decides whether the overlay is permanent or
  appears on pointer activity for three seconds; the reveal is driven by the
  window's own `PointerMoved`, which only fires while the pointer is over it, so
  a mouse move on another monitor must never reveal the overlay. Never replace
  that with a global pointer hook. The window's once-per-second diagnostic line
  also carries the averaged `RenderTimings` per stage (render, warp, blur, post,
  overlay, composite, comp shader) plus the render size, the frame's mean
  brightness and its **saturated share**, the frame's brightness **per stage**,
  the presenter's source and destination brightness (`presentBrightness`), the
  shader grid state, whether the per-pixel program is suspended, and any shader,
  render, preset, or presentation error, so render cost is measured rather than
  guessed. Keep the saturated share: a white window is either a genuinely
  saturated frame or a frame that never reaches the screen, and only that number
  tells the two apart, because a presentation fault leaves the rendered frame's
  brightness and saturation untouched. Keep the line bounded and free of media
  names and paths. The per-stage brightness must stay truthful: `PresetRenderer`
  invokes its `StageBrightnessLogger` once per stage on **every** frame with the
  mean brightness of the buffer that stage reads, and `PresetRenderer.Output` is
  the post-comp display frame while `MeshSource` is the pre-comp feedback.
  Sampling `Output` at a stage boundary instead reported the previous frame,
  which made every stage look identical and pointed a white frame at the wrong
  stage; a stale diagnostic is worse than none. The `presentBrightness` pair
  samples the presentation buffer under `_presentLock` before and after the copy
  plus the destination bytes, so a copy or bitmap fault is told apart from a
  genuinely white source frame in one run. The render loop runs on a background
  thread (`RenderLoop`, `RenderOneFrame`) because a frame can cost tens of
  milliseconds; never move it back onto the Avalonia dispatcher. The UI thread
  only ever reads the presentation buffer, never the renderer's live buffers,
  and the short copy under `_presentLock` is the only shared state between the
  two threads: keep it that way, and keep `PostPresent` coalescing to one queued
  present so a busy UI thread cannot build a backlog. Preset switching
  (`_presetIndex`), the reset key (`_resetRequested`), and shutdown
  (`_renderRunning`, `_closed`) travel as flags that the render thread applies,
  so UI event handlers must never touch the renderer directly. A completed
  presentation snapshot carries the preset index, name, shader sources, mesh,
  overlay, and uniforms together. The label follows
  `GlPresenter.DrawnPresetIndex` on the GPU path, or the copied frame index on
  the bitmap path; never advance it from `_presetIndex` or
  `_renderedPresetIndex` before the frame reaches the presenter. A preset switch
  must **not** clear the feedback: like Milkdrop, the new preset continues from
  the last frame of the previous one, so `VisualizerGlPresenter` clears the
  feedback only when its size changes (a freshly allocated texture holds
  undefined content), and the CPU path seeds the new renderer with the previous
  `MeshSource` through `PresetRenderer.SeedFeedback`. Frame pacing lives in the
  pure, tested `Orynivo.Visualization.FramePacing`. `VisualizerPresetLibrary`
  loads the built-in presets plus `.oryvis` and `.milk` files from
  `AppSettings.VisualizerPresetDirectory` (default: a `visualizer-presets`
  folder below the data root, including its subfolders, because preset
  collections are sorted into directories; every `[presetNN]` section of a
  `.milk` file becomes its own preset, a file that fails to parse is skipped and
  reported with its reason through `RejectedReasons`, never fatal, and preset
  files stay user data like equalizer profiles. User presets are discovered
  eagerly but parsed lazily in `At`, one at a time, because compiling a preset
  builds and JIT-compiles its expression trees; a collection of several hundred
  presets must never be compiled when the window opens. Keep the discovered
  count (`Count`) separate from the parsed presets, report a failure on first
  use through `RejectedReasons`, and fall back to the first built-in so one
  broken file can never stop the visualizer.
  `AppSettings.DisabledVisualizerPresets` stores the stable keys the user
  deactivated (`builtin:<name>` for a built-in,
  `file:<path relative to the preset folder>` for a user file, so every section
  of one file shares the file's key). `VisualizerPresetLibrary.Describe` lists
  the built-ins and every discovered file without reading them for the **Select
  presets…** dialog, and `SetDisabledKeys`/`ResolveEnabledIndex` make the
  window's open, step, auto-advance, and mouse navigation skip a deactivated
  preset in the requested direction; when every preset is deactivated the
  requested index is kept so the visualizer never stops rendering. The window
  must also re-check `IsDisabled` before every preset switch and once discovery
  finishes, because the initial index is resolved against the built-ins only and
  the deactivated set (or a user file) can arrive later; a deactivated preset
  must never reach the renderer. `LoadBuiltIns` is the only thing the window
  constructor may call: it touches no disk, so the window opens and renders
  while `Discover` enumerates the folder on a worker thread. Discovery records
  file paths only, never file contents, because a real collection holds
  thousands of files and reading them up front froze the whole application; a
  file is read the first time one of its presets is shown, and a multi-section
  file exposes its remaining sections then. Never move discovery back onto the
  UI thread, and keep `Count` and `At` usable while it runs.
  `VisualizerPresets.BuiltIn` holds nine hand-written warp-shader presets
  (`Spiral`, `Kaleidoscope`, `Fractal`, `Ripple`, `Vortex`, `Bloom`,
  `Spectrum Bars`, `Starfield`, `Orbit`): each carries a small `warp_N` shader
  that reads the previous feedback, so a first run shows structure instead of a
  flat full-screen smear. Keep them on the documented expression and shader
  subset, because they double as authoring examples; a procedural background
  that accumulates through the feedback must be bounded (for example with `max`)
  so it cannot blow out to white.
  `VisualizerPresetsTests.BuiltIn_ContainsAWarpingPreset` accepts either a
  per-pixel program or a warp shader. Preset stages share one slot layout, so a
  stage-local built-in such as `x` or `rad` is one slot that each stage seeds
  and reads back for itself: the per-pixel stage seeds it per pixel, a shape
  seeds it per shape and per vertex. Never let a stage assume another stage's
  value is still in place.
