# Royal Mashup (138): Befund und Korrekturen

Die Untersuchung zu [VISUALIZER-138-PROBLEM.md](VISUALIZER-138-PROBLEM.md) hat weitere konkrete
Semantikfehler gefunden und korrigiert. Sie erklärt zugleich, warum die dortige projectM-Luma
kein belastbarer Sollwert für den Comp-Shader dieses Presets ist.

## Korrigierte Abweichungen

- `GetPixel(uv)` ist in MilkDrop ein Sample mit **normierten UV-Koordinaten**. Der CPU-Interpreter
  las `uv` als ganzzahlige Pixelkoordinate, während GLSL korrekt normiert las. Die Erweiterung
  `GetPixel(x,y)` für ganzzahlige Koordinaten bleibt separat erhalten. Eine asymmetrische
  Testtextur und ein Shader-Test sichern beide Aufrufarten ab.
- Ein Skalar aus einem Swizzle (`1-uv.y`) wurde im CPU-Interpreter beim Multiplizieren mit
  `hue_shader` nur auf den Rotkanal angewendet. Die anderen Kanäle erhielten Null. Der
  Comp-Hue-Term des Presets ist damit nun auf CPU und GPU nahezu identisch (isolierte Probe:
  CPU 0,05248, GL 0,05244 mittlere Luma).
- Die tatsächlichen Bindungen in `milkdropfs.cpp` widersprechen einem Kommentar in
  `BlurPasses()`: Nach dem Warp wird für den ersten Blur-Pass **VS[0]**, das vorherige
  Feedback, als Quelle gebunden, nicht der aktuelle Warp-Ausgang VS[1]. Auch
  `ApplyShaderParams()` bindet in `ShowToUser_Shaders()` für alle `main`-Sampler VS[0].
  CPU-, Skia- und GL-Comp lesen nun dieses vorherige Bild; VS[1] wird erst nach der
  Präsentation zum nächsten Feedback. Im ersten Frame des Original-Presets stimmen
  CPU und GL damit auf 0,0515 Luma überein. Eine Probe prüft ausdrücklich, dass
  `GetPixel` zunächst Schwarz, `GetBlur2` das ältere Feedback und erst im zweiten
  Frame die vorherige Warp-Ausgabe sehen.
- Der Originalrenderer schneidet benutzerdefinierte Wellenlinien am Bildrand ab. Orynivo klemmte
  zuvor beide Endpunkte auf den Rand und konnte so außerhalb verlaufende Linien als helle
  Randstreifen malen. Die Linien werden jetzt geometrisch geclippt; eine Offscreen-Probe sichert
  ab, dass der Rand schwarz bleibt. Der 300-Frame-Lauf zeigte dadurch allerdings keine relevante
  Änderung der mittleren Helligkeit dieses Presets.
- Winamp MilkDrop 2.25c und projectM rechnen die X/Y-Punkte jeder benutzerdefinierten Welle mit
  `invAspectX`/`invAspectY` in Clip-Koordinaten um. Orynivo ließ diese Skalierung aus. Auf einer
  320x180-Fläche wird die Welle in der kurzen Y-Achse jetzt um 320/180 um die Mitte erweitert;
  Linien außerhalb des Bildes bleiben abgeschnitten. Eine nichtquadratische Testfläche sichert
  die Position ab.
- Bei `bDrawThick=1` zeichnen beide Referenzen eine Custom-Wave in vier versetzten Durchläufen
  mit voller Deckkraft. Orynivo zeichnete nur eine zweite, halbtransparente Pixelreihe. Die
  Linien haben jetzt die vier vollen Offsets. Die drei Royal-Mashup-Custom-Waves nutzen diesen
  Modus; ein Regressionstest prüft die zusätzliche Pixelreihe.

Der Vergleichsharness schaltet bei `GLH_ORACLE_TONE=1` jetzt tatsächlich auf den
`AudioSpectrumAnalyzer` um. Zuvor erhielt projectM einen 440-Hz-Ton, Orynivo aber synthetische
`HarnessAudio`-Werte. Der Ton wird mit einer Phase in `double` berechnet, wie im Oracle;
eine `float`-Phase erzeugte bei langen Läufen zusätzliche Frequenzfehler. Der FFT-Eingang
nutzt die ersten 480 Werte des 576-Werte-Analysefensters; die letzten 96 sind der
zeitliche Rand wie in der Referenz.

## Grenze des Vergleichs mit projectM

Die nun zugängliche Quelle im lokalen MilkDrop-MusicVisualizer-Checkout ist bytegleich mit
der zuvor verwendeten 2.25c-Kopie. Der Winamp-Installer dort enthält zusätzlich das fehlende
`data/include.fx`: Es bestätigt `GetPixel(uv) = tex2D(sampler_main, uv).xyz` und die
Skalierung der `GetBlurN`-Makros. Die Shader-Vertexdatei bestätigt, dass Warp-UV und
ursprüngliche UV getrennt an den Pixelshader gehen.

Bei einem kontrollierten Austausch von `comp_1` im selben Preset lieferte projectM mit einem
konstant roten Comp-Shader und mit einem Pass-Through-Comp fast dasselbe Bild: Bei Frame 5
lagen die mittleren RGB-Werte bei etwa 0,22421 bzw. 0,22418. Der Original-Comp lieferte
etwa 0,20076. Daraus folgt mindestens, dass der projectM-Oracle-Pfad die Comp-Varianten
nicht so unterscheidet, wie ihr Shader-Code erwarten lässt. Seine absolute Helligkeit ist
deshalb kein Nachweis für das Aussehen in Winamp. Der Original-MilkDrop-Code definiert
`uv_orig` zudem als originale Vertex-UV; projectM setzt sie auf die aktuelle UV. Den
projectM-Sonderfall auf Orynivo zu übertragen würde die Winamp-Kompatibilität verschlechtern.

Mit echtem Referenzton bleibt nach den Korrekturen bei Frame 299 eine Differenz zwischen
Orynivo CPU (0,3784), Orynivo GL (0,6225) und projectM (ca. 0,124). Sie ist **nicht**
als gelöste Winamp-Bildtreue ausgewiesen. `wave_a=0` deaktiviert nur
die Standardwelle; die drei `wavecode_N_enabled=1` bleiben aktiv und tragen wesentlich zur
Feedback-Helligkeit bei. Ohne diese Wellen fällt die Orynivo-Feedback-Luma im isolierten
Experiment fast auf Null. Ein bloßes Dämpfen der gesamten Ausgabe wäre daher keine
referenzgestützte Korrektur.

Bei diesen Wellen fordert das Preset 512 Samples, obwohl `pluginshell.h` nur 480 gültige
Waveform-Samples ausweist. In `DrawCustomWaves()` wird zunächst auf 480 begrenzt, nach dem
Per-Frame-Code aber erneut `samples` gelesen und nur auf 512 begrenzt. Bei 512 Samples beginnt
der Quellenindex deshalb 16 Werte vor dem Waveform-Array. Das ist ein Fehler im Originalcode,
keine wohldefinierte Shader-Semantik; ein Probe-Preset mit 480 statt 512 Samples senkte die
GL-Feedback-Luma bei Frame 50 lediglich von 0,709 auf 0,696. Eine willkürliche Begrenzung
auf 480 löst das Helligkeitsproblem nicht und wurde nicht in Orynivo übernommen. Die
Per-Point-Blöcke aller drei Wellen lesen `value1` und `value2` überhaupt nicht: Der
fehlerhafte Sample-Zugriff im Original beeinflusst ihre gezeichneten Koordinaten und
Farben daher nicht. Die leichte Änderung durch 480 statt 512 stammt aus der anderen
Punktzahl und dem normalisierten `sample`-Parameter.

Ein GL-Lauf ohne Comp-Pass isoliert die Rückkopplung: Bei Frame 50 hat das sichtbare
GL-Feedback ca. 0,709 Luma, während der CPU-Stufenlog für das Feedback ca. 0,216 meldet.
Der fortbestehende Unterschied entsteht somit bereits **vor** dem Comp-Pass. Nach den
Aspect- und Thick-Korrekturen liegt die einfache mittlere RGB-Helligkeit des GL-Bildes bei
Frame 299 bei 0,561 (320x180, 440 Hz). Dieser Wert ist nicht direkt mit der zuvor protokollierten
gewichteten Luma von 0,623 vergleichbar; er zeigt weiterhin ein helles Langzeitbild. Die nächste
gezielte Untersuchung muss den GPU-Warp und die Custom-Wave-Einspeisung getrennt gegen einen
Winamp-Mitschnitt prüfen; die korrigierten Comp-Terme allein erklären die Langzeithelligkeit
nicht.

## Direkter Winamp-Mitschnitt (24. September 2026)

Mit `scripts/winamp-milkdrop-harness/compare-winamp.ps1` läuft nun die vom Nutzer bereitgestellte
`vis_milk2.dll` in einer isolierten Kopie des installierten Winamp. Der Harness kopiert genau das
Royal-Mashup-Preset in dessen Presetverzeichnis, spielt einen 440-Hz-Stereoton mit 44,1 kHz ab,
setzt dessen Wiedergabezeit vor dem Capture auf null und erfasst 300 Bilder der Direct3D-Clientfläche
bei 320x180. Winamp meldete für Bild 299 eine Wiedergabezeit von 4.990 ms. Die einfache mittlere
RGB-Farbe betrug dort **(0,367; 0,434; 0,643)**, bei Orynivos GPU-Bild 299 dagegen
**(0,689; 0,482; 0,511)**. Die mittlere absolute Kanalabweichung war 0,296. Winamp wirkt blau
und weich, Orynivo rotlastig und mosaikartig. Der Unterschied ist nun unmittelbar sichtbar und
nicht mehr nur aus projectM abgeleitet.

Der Winamp-Prozess rendert während seiner Initialisierung bereits vor dem Wiedergabe-Reset; seine
Feedback-Textur ist beim ersten aufgezeichneten Ton-Frame daher möglicherweise nicht leer.
Winamps variable Framerate und der Start der Plugin-Fenster erlauben in diesem Harness keine
framegenaue Cold-Start-Identität. Das Schlussbild liegt jedoch bei praktisch derselben
Wiedergabezeit wie Orynivos 299. 60-Hz-Frame. Der neue Mitschnitt bestätigt die deutlich
abweichende Langzeitdarstellung, aber noch nicht, welche Renderstufe die verbleibende Differenz
verursacht. Ein Vergleich der Stufen Warp-Feedback, Custom-Wave-Overlay und Comp-Shader ist der
nächste gezielte Schritt.

### Kontrollierter Stufenvergleich

Der erste Winamp-Harness startete MilkDrop in einem großen Visualisierungsfenster und verkleinerte
es erst vor dem Capture. Die interne Rendertextur und damit die für Custom-Waves und Shapes
verwendeten Aspect-Werte konnten die ursprünglichen Maße behalten. Der Harness setzt jetzt auch
`avs_ww`/`avs_wh` vor dem Pluginstart. Eine statische Wave-Probe bei 320x180 liegt dadurch in
Winamp und Orynivo an derselben Stelle und hat dieselbe Form; der Pixelfehler nach 180 Frames
beträgt 0,039. Die ältere absolute Royal-Mashup-Messung mit unpassender Startgröße ist daher
kein gültiger Maßstab für die Renderertreue.

Eine zweite Probe fixiert Wave-Phase und Zoom, belässt aber die drei texturierten Shapes aktiv.
Ihr Pixelfehler betrug vor der Korrektur 0,149. MilkDrop wählt für texturierte Shapes die
interpolierte Vertex-Alpha und multipliziert Textur-RGB mit der Vertexfarbe. Orynivos GPU-Shader
verwendete stattdessen die Alpha der Frame-Textur und ignorierte beide Shape-Werte. Nach der
Korrektur in GPU und CPU fällt der Fehler derselben Probe auf 0,093. Das vollständige dynamische
Preset bleibt sichtbar verschieden; einzelne Schlussbilder schwanken deutlich, weil Pluginzeit,
Audio-Anlauf und Feedback zwischen Winamp und Orynivo noch nicht framegenau synchronisiert sind.
Aus einem einzelnen Schlussbild lässt sich daher keine zuverlässige Rangfolge weiterer
Rendererkorrekturen ableiten.
