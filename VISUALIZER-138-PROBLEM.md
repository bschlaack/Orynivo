# Offenes Problem: Milkdrop-Visualizer zeigt `$$$ Royal - Mashup (138)` zu hell

Diese Datei ist eine **in sich geschlossene** Problembeschreibung für eine externe
Analyse. Sie setzt keinen Zugriff auf die Sitzungshistorie voraus. Alles Genannte
ist entweder gemessen oder mit Datei/Zeile belegt.

## 1. Kontext

**Orynivo** ist ein Avalonia-Musikplayer (C#, .NET 10). Der Visualizer ist eine
Eigenimplementierung im Milkdrop-Stil — **nicht** projectM. Er verarbeitet
`.milk`-Presets (Preset-Code: `per_frame`, `per_pixel`, `warp_*`, `comp_*`,
Shapes, Wellenformen).

Es gibt zwei Renderer für denselben Preset-Code:

- **CPU-Pfad** — interpretiert den Preset-Code (Zeilen-/Punktprogramme), rechnet
  Frame-Pässe (Blur, Overlay, Comp) auf `PixelBuffer`-Objekten, präsentiert über
  Skia.
- **GL-Pfad** — der **Standard**. Präsentiert und rechnet die Kernkette auf der
  GPU (Shader-Mesh-Warp, Blur-Kette, Comp-Shader, Shape-Fills), mit dem CPU-Pfad
  als Rückfallebene.

Jeder Pfad schreibt seine Frames als BMP (`cpu-NNN.bmp`, `gl-NNN.bmp`), damit
beide direkt vergleichbar sind.

**Referenzen, die zur Verfügung stehen:**

- **projectM**-Quelltext (lokaler Checkout) und ein daraus gebauter
  Referenz-Renderer („Oracle", `the reference oracle`), der ein
  Preset headless rendert und `pm-NNN.bmp` schreibt.
- **Original MilkDrop 2.25c**-Quelltext (BSD-artige Nullsoft-Lizenz) unter
  `%TEMP%\opencode\milkdrop-src\milkdrop_225c_src\vis_milk2` — **nur als
  Lesereferenz**, es darf kein Code daraus kopiert werden. (Kein DirectX-SDK
  dabei, daher nicht baubar; `include.fx` fehlt.)

**Eingeschränkter als es klingt:** Weder das Oracle noch die Quellen sind Beweis
für das genaue Winamp-Verhalten. Der Nutzer hat einen Winamp-Screenshot
beigesteuert, aber keinen Frame-genauen Mitschnitt.

## 2. Symptom

Der Nutzer berichtet, das Preset **`$$$ Royal - Mashup (138)`** rendere in
Orynivo viel zu hell, mit großen scharfen Quads und strahlenden Streifen, während
Winamp/MilkDrop das Bild abdunkeln lässt.

Preset-Datei:
`X:\projects\presets-cream-of-the-crop\Dancer\Glowsticks\$$$ Royal - Mashup (138).milk`

Relevante Preset-Eigenschaften (gemessen):

- `fDecay=0.960`
- `per_frame_init_6=decay=0;`
- `per_frame_1=decay=1;` — in *jedem* Frame wird `decay` auf 1 gesetzt, also
  „keine Dämpfung"
- `b1n=0.000`, `b2n=0.000`, `b3n=0.000`, `b1x=1.000`, `b2x=1.000`, `b3x=1.000`
- kein `blur_level`-Schlüssel
- `per_frame_init_1=mv_x=64;mv_y=48;`
- besitzt Warp-Shader, Comp-Shader, Per-Pixel-Code und Shapes
- sein Comp-Shader **addiert** zwei Terme: `GetPixel(uv) + GetBlur2(uv)` und
  `0.2*(1-uv.y)*(hue_shader-0.8)*4`
- sein Warp-Shader ist „Geiss's Motion Blur" und **dunkelt selbst um 5 % pro
  Frame ab** (`ret *= 0.95`), siehe Abschnitt 5.1

## 3. Messmethodik

Ein **440-Hz-Ton** wird als identische Audiodaten an beide Engines gegeben und über
300 Frames mit **320×180** gerendert. Anschließend wird die **mittlere Luma**
(`0.299R + 0.587G + 0.114B`, alle Pixel) jedes gespeicherten Frames berechnet —
also dieselbe Metrik für alle drei Renderer.

Reproduziert wurde mit den lokalen, **nicht versionierten** Entwicklungs-Harnessen: einem
Referenz-Renderer, der `pm-NNN.bmp`-Frames schreibt, und einem Orynivo-Harness, das dieselben
Audiodaten liest und `cpu-NNN.bmp`/`gl-NNN.bmp` schreibt.

## 4. Messergebnis (Original-Preset, gleiche Metrik, gleicher Ton)

| Frame | projectM | Orynivo CPU | Orynivo GL |
| ----- | -------- | ----------- | ---------- |
| 10 | 0.1366 | 0.1445 | 0.0747 |
| 50 | 0.1427 | 0.3056 | 0.3632 |
| 150 | 0.2219 | 0.2300 | 0.3303 |
| 299 | 0.0889 | 0.2206 | 0.3987 |

Und die ersten Frames (zur zeitlichen Einordnung):

| Frame | projectM | CPU | GL | GL−CPU |
| ----- | -------- | --- | -- | ------ |
| 0 | 0.0000 | 0.0972 | 0.0515 | −0.0457 |
| 1 | 0.0188 | 0.0999 | 0.0534 | −0.0465 |
| 5 | 0.0486 | 0.1149 | 0.0626 | −0.0523 |
| 20 | 0.2232 | 0.1765 | 0.0759 | −0.1006 |
| 40 | 0.1933 | 0.2480 | 0.1606 | −0.0874 |

## 5. Zwei unabhängige Befunde

### Befund A — CPU-Pfad: der Ausklang fehlt, nicht die Grundhelligkeit

Bei Frame 150 liegt der CPU-Pfad nur **4 %** über projectM (0.2300 vs 0.2219).
Danach **blendet die Referenz ab** (auf 0.0889), Orynivo bleibt bei 0.2206.
Das Preset ist also nicht pauschal „zu hell", sondern **blendet am Ende nicht ab**,
obwohl `per_frame_1=decay=1` in beiden Engines gesetzt ist.

### Befund B — GL-Pfad (Standard): eigener, größerer Fehler

Der GL-Pfad ist bereits **im ersten Bild zu dunkel** (dort ist das Feedback
schwarz, es gibt also gar keine Rückkopplung) und endet ~4,5× so hell wie
projectM.

**Bisect GL ↔ CPU** (gleiches Preset, gleicher Ton; Δ bei Frame 0):

| Fixture | Δ (GL − CPU) bei Frame 0 | Interpretation |
| ------- | ------------------------ | -------------- |
| shaderfreies Preset (`Jc - Lungs.milk`) | 0.0000 | Der GL-Pfad ohne Shader ist **korrekt**. |
| 138 + Pass-Through-Comp (`ret = tex2D(sampler_main, uv).xyz;`) | 0.0000 | Die Comp-**Maschinerie** ist **korrekt**. |
| 138 + echter Comp | −0.0239 (42 % zu dunkel) | Der Fehler liegt in der **Auswertung des Comp-Inhalts**. |

Damit ist Befund B eingegrenzt auf: **die GPU wertet den Comp-Shader-Inhalt von
138 anders aus als der CPU-Pfad** (und projectM), nicht die Rückkopplung, nicht das
Overlay, nicht die Compositing-Maschinerie, nicht das Feedback (Frame 0!).

### 5.1 Der Preset-Shader-Code (vermutlich der Schlüssel)

Der **Warp-Shader** von 138 (`warp_1`, Zeilen 574–597 der Datei):

```glsl
shader_body
{
    //Geiss's Motion Blur
    // sample previous frame
    ret = tex2D( sampler_main, uv ).xyz;

    // this vector points exactly one pixel, in the direction of motion
    float2 v = normalize(uv-uv_orig)*texsize.zw;

    float3 s;
    ret = max(ret, tex2D(sampler_main, uv+v)*0.97);
    ret = max(ret, tex2D(sampler_main, uv-v)*0.97);
    ret = max(ret, tex2D(sampler_main, uv+v*2)*0.90);
    ret = max(ret, tex2D(sampler_main, uv-v*2)*0.90);

    // darken over time
    ret *= 0.95;

    // add noise
    //float2 uv_noise = uv*texsize_noise_lq.zw*texsize.xy + rand_frame.xy;
    //ret += (tex2D(sampler_noise_lq, uv_noise)*2-1)*0.02;
}
```

Der **Comp-Shader** von 138 (`comp_1`, Zeilen 598–606):

```glsl
shader_body {

ret = GetPixel(uv) + (GetBlur2(uv)) ;

//ret +=.1- .1*saturate(1-4*lum(ret)) * lum(GetBlur1(uv-.2));
ret += .2*(1-uv.y)*(hue_shader-.8)*4;
}
```

Beide Blöcke enthalten Mechanismen, die die Helligkeit **direkt** verändern, und
sind die wichtigsten Prüfpunkte:

1. **`ret *= 0.95` im Warp** — der Warp-Shader dunkelt selbst um 5 % pro Frame ab.
   Die Referenz tut das, Orynivo (scheinbar) nicht. Das allein erklärt Befund A:
   projectM 0.222 → 0.089, Orynivo bleibt.
2. **`float2 v = normalize(uv-uv_orig)*texsize.zw`** — hier liegt die
   entscheidende Semantikdifferenz:
   - In **projectM** ist `uv_orig` als `_uv.xy` definiert, also `uv_orig == uv`
     (im projectM-Quelltext ausdrücklich mit dem Kommentar `//[sic]` markiert).
     Damit ist `uv - uv_orig` der Nullvektor, `normalize(0)` ist **undefiniert**
     (NaN oder (0,0)). In der Praxis degeneriert der Motion Blur, und es bleibt
     nur die 5-%-Abdunklung: projectM **blendet ab**.
   - In **MilkDrop** ist `uv_orig` die *ursprüngliche, unverzerrte*
     Vertex-Koordinate; `uv - uv_orig` ist dann der **echte Bewegungsvektor**.
     Die `max(...)`-Samples über benachbarte Pixel **verteilen helle Pixel und
     heben den Mittelwert** — genau das Muster „strahlende Streifen" und
     „wird heller", das der Nutzer sieht.
   Wenn Orynivo einen **echten** Bewegungsvektor liefert, projectM aber einen
   Nullvektor, wächst Orynivo dort, wo projectM abblendet. Das erklärt Befund A
   vollständig und möglicherweise auch den Zeitverlauf von Befund B.
3. **`texsize.zw`** — muss der Kehrwert der *Frame*-Größe sein (ein Pixel
   Schrittweite). Wenn Orynivo hier etwas anderes liefert (z. B. die Kehrwerte
   der Shader-Gitter-Größe), zeigt `v` in eine andere Richtung/Weite, und die
   `max`-Samples verteilen die Helligkeit anders.
4. **`ret += .2*(1-uv.y)*(hue_shader-.8)*4`** — ein rein **additiver** Term, der
   von `hue_shader` abhängt. Er ist nur dann von Null verschieden, wenn
   `hue_shader > 0.8`. Über den Frame gemittelt ist der Faktor `(1-uv.y)`
   ungefähr 0.5, der Term also etwa `0.4*(hue_shader-0.8)`. Bei `hue_shader`
   nahe 1 ist das eine erhebliche Aufhellung. Da `hue_shader` animiert ist,
   erklärt das ein zeitlich schwankendes Zu-hell. Orynivos `hue_shader`-Binding
   ist **neu** und der am wenigsten verifizierte Teil.
5. **`GetBlur2`** — der zweite Comp-Term; die Blur-Ranges sind Identität
   (min 0 / max 1), also ist `GetBlur2(uv)` der Blur-2-Sample. Verdacht: die
   Blur-Kette der Comp-Stufe ist auf der GPU ein Frame alt bzw. bei Frame 0
   schwarz.

**Achtung:** `uv_orig == uv` (`//[sic]`) in projectM bedeutet, dass projectM das
Preset *anders* auswertet als MilkDrop. Ein reiner „match projectM"-Fix kann
also in die falsche Richtung zeigen, wenn das Ziel Winamp ist. Diese Frage muss
zuerst geklärt werden (siehe Abschnitt 9, Frage 1).

## 6. Was bereits ausgeschlossen ist (mit Beleg)

Alles per Messung ausgeschlossen, nicht per Vermutung:

- **Shapes** — `shapecode_*_enabled=0` ändert den Mittelwert nicht (85.2 → 84.8).
- **Welle/Overlay** — `wave_a=0` ändert den Mittelwert nicht (72.3 → 72.4).
- **Comp-Zusatzterm-Maschinerie** — ein Pass-Through-Comp verhält sich wie der
  echte (CPU 53.6/69.0 vs 54.8/68.6 in einer früheren Messreihe).
- **Frame-Blur** — innerhalb jedes Frames sind Blur-Eingang und Overlay-Eingang
  identisch; der Blur verändert den Mittelwert nicht.
- **Frame-Blur-Anzahl** — `blur=0` im GL-Log ist **korrekt**: 138 setzt kein
  `blur_level`, und der Alias `b1x` bildet auf `blur1_max` ab, nicht auf `blur1`;
  beide Pfade fahren 0 Frame-Blur-Pässe.
- **Sampling-Geometrie** — `WarpSampling.SamplePosition` ist bei `zoom=1`,
  `rot=0`, `stretch=1`, `warp=0` **exakt** die Identität.
- **Decay-Wirkung als solches** — `decay` ist eine echte registrierte
  Per-Frame-Variable (Referenz `state.cpp:287`, vorbesetzt aus `fDecay`
  (`milkdropfs.cpp:498`), angewandt als Vertex-Farbe `cDecay`
  (`milkdropfs.cpp:1995-2007`)). Orynivos Vorgabewert ist 0.96, projectMs 0.98 —
  Orynivo dämpft also *stärker*, nicht schwächer.
- **CPU-Comp-Blur-Quelle** — Verdacht war, die GPU baue die Blur-Kette der
  Comp-Stufe aus dem *vorherigen* Frame; der CPU-Pfad tut das aber ebenfalls
  (`PresetRenderer.cs:2604`), also ist das **nicht** die Ursache der Differenz.

**Ein früherer Fehlschluss, zur Warnung:** Die Sonde `blur` heißt irreführend —
sie läuft **vor** `_warped.Scale(decay)` und den Blur-Pässen, zeigt also den
**Warp-Ausgang**. Daraus entstand die falsche Annahme, der Blur sei die Ursache.
Innerhalb eines Frames gilt: `blur` == `overlay` == Blur-Ausgang.

## 7. Relevante Architektur (Datei/Zeile)

### CPU-Pfad — `Orynivo.Core/Visualization/PresetRenderer.cs`

Reihenfolge eines Frames (etwa Zeilen 806–892):

1. Warp: sampelt `_previous` (`_previous.SampleBilinear(...)`, Zeile 1298).
   Per-Pixel-Koordinaten: `normalizedX = x/(width-1)*2-1` (Zeilen 1292/1295).
2. `decay` lesen (Zeile 806), Blur-Pässe (`BlurPasses()`, Zeile 1070).
3. Overlay zeichnen + `Composite()`; `DarkenCenter()`; `DrawBorders()`.
4. Ohne Comp-Shader: `ApplyVideoEcho()` + `ApplyHueShadeAndGamma()` (Zeilen 862–866).
5. `Publish()` (Zeile 869), dann Comp-Shader (`ApplyCompShaders()`, Zeile 874 ff).
6. Feedback: `_previous.CopyFrom(_compStageRan ? _frameCopy : _fresh)` (Zeile 890)
   — der **Pre-Comp-Frame** ist die Rückkopplung (Invariante).

`ApplyCompShaders()` (Zeile 2068):

```csharp
var width = _fresh.Width; var height = _fresh.Height;
_frameCopy.CopyFrom(_fresh);          // Zeile 2075 — Comp-Eingang
_compStageRan = true;
_samplerMainIsWarped = true;          // Zeile 2077
Array.Clear(_blurLevelReady);         // Zeile 2079 — Blur-Stufen neu bauen
...
var (shaderWidth, shaderHeight) = ShaderGrid(width, height);   // Zeile 2090
var scaled = shaderWidth != width || shaderHeight != height;
var output = scaled ? _shaderOutput : _fresh;
...
ScaleGridIntoFresh(output, width, height);   // Zeile 2130 — bilineares Zurückskalieren
```

Der Comp sampelt also `sampler_main` = `_frameCopy` (aktueller Frame, inkl.
Overlay/Rand) — Zeile 2575:

```csharp
var source = _samplerMainIsWarped ? _frameCopy : _previous;
```

Die Blur-Stufen der Comp-Stufe werden aus `_previous` aufgebaut — Zeile 2604:

```csharp
_blurLevels[build - 1].ResampleFrom(build == 1 ? _previous : _blurLevels[build - 2]);
```

`PixelBuffer`-Operationen: `Blur()` (Neun-Tap, geklemmt), die
Referenz-8-Tap-Variante, `SampleBilinear` (Zeile 152), `SampleShader` (Zeile 194),
`ResampleFrom` (Zeile 364).

### GL-Pfad — `Orynivo/Controls/VisualizerGlPipeline.cs`

Reihenfolge in `Render(...)`:

- Zuerst `BuildShaderBlurLevels`-Kontext: Zeilen 899–947, Warp rendert in
  `_pingFramebuffer[0]`; der Custom-Warp-Shader bindet `_feedbackTexture` und
  `_previousTexture` als Sampler (Zeile 910).
- **Zeile 953:** `BuildShaderBlurLevels(gl, _feedbackTexture, frameWidth, frameHeight, uniforms)`
  — `_feedbackTexture` ist hier noch der **vorherige** Frame (überschrieben erst
  im Post-Pass, Zeile 988).
- Zeile 957: `Blit(_feedbackTexture → _previousFramebuffer)` (Pre-Comp-Frame merken).
- Zeilen 959–974: Frame-Blur-Ping-Pong über `parameters.BlurPasses`.
- Zeilen 979–985: GPU-Shape-Fills.
- Zeilen 988–1025: Post-Pass (Overlay, Zentrumsabdunklung, Ränder) → `_feedbackFramebuffer`.
- Zeile 1029: `var output = _feedbackTexture;` — **jetzt** ist `_feedbackTexture`
  der **aktuelle** Frame.
- Zeile 1030: Comp-Shader läuft; **Zeile 1380** bindet
  `BindShaderSamplers(gl, _compShaderProgram, ..., _feedbackTexture, _previousTexture)`
  — `sampler_main` = aktueller Frame.
- `BuildShaderBlurLevels`-Definition Zeile 1396; Scale/Bias Zeilen 1407–1414;
  Blur-Größen `ShaderBlurSize` (Blur1 = ¼, Blur2 = ⅛, Blur3 = 1/16).
- Debug-Zeile 1078 gibt `blur={parameters.BlurPasses}` aus.

### Preset-Parsing — `Orynivo.Core/Visualization/VisualizerPreset.cs`

- `BlurLevel` wird aus `blur_level` gelesen (max. 4).
- Aliase (Zeilen 669–671): `b1n`→`blur1_min`, `b1x`→`blur1_max`,
  `b1ed`→`blur1_edge_darken` (analog `b2*`, `b3*`).
- `fDecay`→`decay` (Zeile 649).

### Referenz-Fakten (projectM / MilkDrop)

- projectM: `#define GetPixel(uv) (tex2D(sampler_main,uv).xyz)`;
  `GetBlur1/2` sind gewichtete Filter mit `_c5`-Range-Kompression.
- Blur-Scale/Bias: `scale = 1/(blurMax-blurMin)`, `bias = -blurMin*scale`
  (`BlurTexture.cpp:135-144`). Bei 138 (min 0, max 1) → **Identität**.
- Blur-Texturen werden je Stufe halbiert (¼, ⅛, 1/16).
- Warp-Vertex-Shader: teilt durch Zoom/Stretch, subtrahiert Offsets, radialer Zoom
  `pow(zoom, pow(zoomExp, radius*2-1))`; Aspect `(1,h/w)` bzw. `(w/h,1)`.
- MilkDrop: `hue_shader = _vDiffuse.xyz`, immer berechnet.
- Motion-Vektoren sind *kein* Engine-Grid: `mv_x`/`mv_y` dimensionieren das
  gezeichnete Pfeilgitter, `mv_dx`/`mv_dy`/`mv_l`/`mv_a` sind gewöhnliche
  blendbare Variablen.
- Shape-Vertexraum ist Direct3D y-up.

### Bewusste, dokumentierte Abweichungen von der Referenz

- Hue-Offsets je Preset werden deterministisch aus dem Preset-Namen abgeleitet
  (FNV-1a) statt projectMs Zufall pro Ladevorgang.
- Der Skia-Warp komponiert den Per-Pixel-Block pro Pixel (keine Vertex-Stufe).
- CPU/Skia-Blur bleibt eine Näherung der Referenz.
- Orynivos `mv_enabled`-Motion-Recording ist eine eigene Erweiterung.

## 8. Hypothesen und unterscheidende Experimente

### Priorität 1: der Warp-Shader (erklärt vermutlich Befund A, evtl. auch B)

**H0a — `ret *= 0.95` wird nicht (oder nicht identisch) angewandt.** Wenn der
Warp-Shader um 5 %/Frame abdunkelt, die Referenz das tut und Orynivo nicht, dann
*fällt* die Referenz und Orynivo bleibt hell.
*Experiment:* den Mittelwert **direkt nach** dem Warp-Shader-Pass in beiden
Engines messen (im CPU-Pfad vor dem späteren `Scale(decay)`). Diskriminierend:
wenn Orynivos Wert nach dem Warp nicht um ~5 % pro Frame fällt, während er vor
dem Warp konstant ist, ist H0a bestätigt.

**H0b — `uv_orig` ist in Orynivo nicht gleich `uv` — oder umgekehrt.** Wenn
Orynivo einen echten Bewegungsvektor liefert, samplet `max(...)` benachbarte
Pixel und hebt den Mittelwert; wenn `uv_orig == uv` gilt, kollabiert der Term zu
`ret` und es bleibt nur die 5-%-Abdunklung.
*Experiment:* das eingesetzte `uv_orig` und den daraus berechneten Vektor `v`
loggen (bzw. im CPU-Pfad das `v` der Per-Pixel-Auswertung), dazu im
Referenzquelltext klären, was `uv_orig` in MilkDrop **und** in projectM genau
ist. Diskriminierend: `|uv - uv_orig|` ist entweder in allen Pixeln 0 (dann ist
der Term ein reines `max(ret, ret*0.97)` = no-op) oder nicht.

**H0c — `texsize.zw` ist die falsche Größe.** Muss `1/framewidth`, `1/frameheight`
sein. Wenn Orynivo `1/shaderGridWidth` liefert, ist die Schrittweite zu groß und
der `max`-Blur weiter.
*Experiment:* `texsize` im shaderseitigen Code gegenprüfen.

### Priorität 2: der Comp-Shader

**H0d — das `hue_shader`-Term im Comp.** `ret += .2*(1-uv.y)*(hue_shader-.8)*4`
ist rein additiv. Wenn Orynivos GPU-`hue_shader` (nach dem neuen Bindung:
`hue_shader_<channel><corner>`-Skalare bzw. `_vDiffuse`) höher ist als das des
CPU-Pfads, hellt die GPU stärker auf.
*Experiment:* `hue_shader` (bzw. `_vDiffuse`) bei Frame 0 für CPU und GPU
ausgeben. Diskriminierend: sind die Werte verschieden, ist H0d bestätigt;
Orynivo hat die Bindung erst kürzlich ergänzt, das ist der unverifizierteste Teil.

### Priorität 3: die Blur-Kette der Comp-Stufe

Die übrigen Hypothesen zu Befund B (vorher aufgeführt):

**H1 — `GetBlur2` liefert auf der GPU einen falschen Wert.** 138's Comp ist
`GetPixel + GetBlur2`; der additive Term fehlt/ist zu klein → 42 % zu dunkel.
*Experiment:* die tatsächlichen Werte von `GetPixel`, `GetBlur2` (und der
Blur-Stufen-Texturen) im Comp bei Frame 0 für CPU und GPU ausgeben und
vergleichen. Diskriminierend: wenn die GPU-`GetBlur2`-Werte ≈ 0 sind oder
systematisch kleiner, ist H1 bestätigt.

**H2 — der Post-Pass/Overlay-Eingang des Comp weicht ab.** Der Comp liest auf der
GPU `_feedbackTexture` (nach dem Post-Pass), auf der CPU `_frameCopy` (nach
Overlay/Rand). Wenn Overlay/Rand auf der GPU anders ins `_feedbackTexture`
gelangen (z. B. Alpha/Reihenfolge), wäre der Comp-Eingang anders.
*Experiment:* Mittelwert des Comp-**Eingangs** (nicht des Ausgangs) in beiden
Pfaden bei Frame 0 vergleichen. Bei einem Pass-Through-Comp ist das identisch
(gemessen!) — H2 ist damit **schon stark geschwächt**.

**H3 — Skia-Comp-Pfad.** Auf der CPU kann ein Comp mit leerem Per-Pixel-Block als
**Skia-Runtime-Effect** laufen (`TryApplySkiaCompShader`, Zeile 2083). Wenn die
Referenzmessung der CPU diesen Pfad genommen hat, könnte die Differenz
CPU↔GL in Wahrheit der Unterschied Skia↔Interpreter sein, nicht CPU↔GPU.
*Experiment:* im lokalen Harness den Skia-Comp-Pfad an- und abschalten und die CPU-Mittelwerte
vergleichen; zusätzlich prüfen, ob der Interpreter- und der Skia-Pfad dasselbe
liefern.

Wichtig: H1/H2 sind nur dann sinnvoll, wenn vorher geklärt ist, ob die CPU die
Comp-Stufe überhaupt mit dem **Interpreter** gerechnet hat (H3).

### Priorität 4: weitere Hypothesen zu Befund A

**H4 — `decay` wird in Orynivo an einer anderen Stelle angewandt als in der
Referenz.** Orynivo skaliert `_warped` nach dem Warp
(`_warped.Scale(decay)`); die Referenz multipliziert über die Vertex-Farbe
`cDecay` im Warp. Bei `decay=1` ist beides neutral — die Referenz blendet aber ab
(0.222 → 0.089), Orynivo nicht. Also muss die Referenz **noch etwas anderes** tun,
was Orynivo nicht tut.

*Experiment:* die Per-Frame-Variablen (`zoom`, `zoomexp`, `rot`, `cx`, `cy`,
`dx`, `dy`, `sx`, `sy`, `warp`, `decay`) und die Per-Pixel-Ausgaben in beiden
Engines bei identischen Frames ausgeben und vergleichen. Diskriminierend: wenn
`decay` in beiden 1 ist und die Frame-Variablen identisch sind, muss die
Abweichung später in der Kette (Comp/Hue/Gamma/Feedback-Invariante) liegen.

## 9. Konkrete Fragen an die externe Analyse

1. **`uv_orig` in MilkDrop 2.25c vs projectM:** Was genau ist `uv_orig` im
   Warp-Shader? Ist es die originale Vertex-UV (echter Bewegungsvektor) oder
   identisch mit `uv`? projectM definiert `#define uv_orig _uv.xy` mit dem
   Kommentar `//[sic]` — ist das ein bewusster Kompatibilitäts-Kompromiss, und
   welches Verhalten zeigt Winamp? Und was macht `normalize(0,0)` in der
   jeweiligen Engine (NaN? (0,0)?), und welche Folge hat das für die
   `max(...)`-Samples?
2. **Geiss's Motion Blur:** Ist es die *Absicht* des Presets, dass `ret *= 0.95`
   pro Frame abdunkelt und die `max`-Samples helle Pixel entlang der
   Bewegungsrichtung verschmieren — oder soll der Term bei diesem Preset
   degenerieren? Wie sieht das Ergebnis in Winamp aus (abklingend oder
   aufschäumend)?
3. **`texsize`-Semantik:** Welche Größe liefert `texsize` in MilkDrop für einen
   Warp-Shader — immer die Frame-Größe, oder die der aktuellen Zieltextur (die
   bei einem kleineren Shader-Gitter anders wäre)?
4. **`hue_shader`-Werte:** Welchen Wertebereich hat `hue_shader`
   (`_vDiffuse.xyz`) typischerweise, und wie wird es genau berechnet? Der
   Comp-Term `0.2*(1-uv.y)*(hue_shader-0.8)*4` hängt empfindlich davon ab.
5. **Semantik von `decay` bei explizitem `decay=1`:** Gibt es in MilkDrop 2.25c
   außer der Vertex-Farbe `cDecay` eine *zweite*, unabhängige Dämpfung (z. B. in
   `fDecay`, im Composite, im Video-Echo), die auch bei `decay=1` wirkt? Wo
   genau im Quelltext?
6. **Reihenfolge der Blur-Kette relativ zu Warp/Comp:** Wird die
   Blur-Textur-Kette in MilkDrop aus dem **Warp-Ausgang** (nach Warp, vor
   Overlay) oder aus dem **Compositing-Ausgang** (nach Overlay) gebaut? Und
   welcher Frame sieht sie — der Warp des aktuellen Frames („retained chain")
   oder der des nächsten?
7. **`GetBlurN` bei einem Frame alten bzw. schwarzen Bild:** Was folgt aus der
   Referenz für `GetBlur2(uv)` im Comp, wenn die Basis ein *ein Frame altes*
   bzw. ein *schwarzes* (Frame 0) Bild ist? Ist ein 42-%-Defizit im ersten Bild
   damit vereinbar, oder muss ein Pass-Through-Comp bei Frame 0 identisch sein?
8. **Comp-Grid/-Auflösung:** Muss der Comp-Shader auf voller Auflösung laufen,
   oder ist ein kleineres Gitter mit bilinearem Zurückskalieren im Sinne der
   Referenz? (Orynivo-CPU nutzt `ShaderGrid` + `ScaleGridIntoFresh`.)
9. **Hue-Shade/Gamma bei vorhandenem Comp-Shader:** Bestätigt sich, dass die
   Legacy-Finaleffekte (Video-Echo, Hue-Shade, Gamma) entfallen, sobald ein
   Comp-Shader existiert? Falls nicht, wäre das eine zusätzliche Hellquelle.

## 10. Randbedingungen für einen Fix

- **Kein Trial-and-Error.** Der Nutzer hat ausdrücklich verlangt: erst die
  Referenz lesen, die Ursache finden, dann korrigieren.
- **Nach jeder Änderung:** `scripts/verify-all.ps1` (Build mit `--warnaserror`,
  Non-Windows-Desktop-Kompilierung, drei Testprojekte, drei Parity-Skripte) muss
  grün sein, und die vier lokalen GPU-Regressionen (Feedback, Fidelity, Pixel-Warp,
  Warp-Target) müssen bestehen. Die Fidelity-Regression pinnt u. a.
  `GetBlur1`/`GetBlur2`-Werte sowie eine `hue_shader`-Probe und eine Shape-Probe.
- **Reproduzierbarkeit:** Diagnosen müssen auf gespeicherten Frames und derselben
  Metrik beruhen; neue Diagnostik lieber als Schalter der lokalen Harnesse statt als
  ad-hoc-Code.
- **Keine Regressionen an anderen Presets.** Frühere Messungen nennen
  `$$$ Royal - Mashup (138)` mit 85–109 mittlerer Helligkeit gegen projectMs ~32.
- **Secrets:** In Logs/Diagnostik dürfen keine Credentials oder authentifizierten
  URLs auftauchen (betrifft hier nur generelle Projektregeln, nicht dieses Preset).

## 11. Lokale Diagnose-Werkzeuge

Die Harnesse liegen bewusst außerhalb der Versionskontrolle (siehe `.gitignore`). Sie lesen
dieselben Audiodaten wie der Referenz-Renderer, schreiben BMP-Frames je Renderstufe und erlauben,
einzelne Stufen (Warp, Blur, Overlay, Comp) gezielt an- und abzuschalten.

Umgebungsvariablen der App: `ORYNIVO_VISUALIZER_OPENGL=0` erzwingt Bitmap-Präsentation
(GL ist Standard), `ORYNIVO_VISUALIZER_PIXELWARP=0` erzwingt den CPU-Warp.

## 12. Zusammenfassung in einem Satz

Auf dem Original-Preset `$$$ Royal - Mashup (138)` mit identischem Ton und
identischer Metrik liegt der CPU-Pfad bei Frame 150 nur 4 % neben projectM,
**blendet danach aber nicht ab** (Befund A), während der GL-Standardpfad schon im
ersten Bild 42 % zu dunkel ist und am Ende ~4,5× zu hell (Befund B).

Die **aussichtsreichste gemeinsame Erklärung** ist der Preset-Code selbst
(Abschnitt 5.1): Der Warp-Shader ist „Geiss's Motion Blur" mit `ret *= 0.95`
(dunkelt pro Frame 5 % ab, auch bei `decay=1`) und `max(...)`-Samples entlang
`normalize(uv-uv_orig)`. In projectM ist `uv_orig == uv` (`//[sic]`), der
Bewegungsvektor ist null und es bleibt die reine Abdunklung; in MilkDrop ist
`uv_orig` die originale Vertex-UV, der `max`-Blur verteilt helle Pixel und hebt
den Mittelwert. Wenn Orynivo hier einen echten Bewegungsvektor einsetzt, wächst
das Bild, wo die Referenz abblendet. Zweiter Kandidat: der rein additive
Comp-Term `0.2*(1-uv.y)*(hue_shader-0.8)*4` (neues, unverifiziertes
`hue_shader`-Binding).

Alles bisher per Messung Ausgeschlossene steht in Abschnitt 6; die
Architektur- und Datei/Zeilen-Referenzen, Hypothesen mit unterscheidenden
Experimenten und die konkreten Fragen stehen in den Abschnitten 7 bis 9.
