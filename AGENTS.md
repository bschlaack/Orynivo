# AGENTS.md

## Projektüberblick

Windows-Audioplayer mit:

- WPF-Frontend in `Player/`
- nativer ASIO-Bridge in `Native/AsioBridge/`
- PCM-Wiedergabe über `ffmpeg`
- nativer DSF/DFF-DSD-Wiedergabe über ASIO

## Build und Start

```powershell
.\build.ps1
.\Player\bin\Debug\net8.0-windows\Player.exe
```

`build.ps1` baut zuerst `AsioBridge.dll`, danach die .NET-App.

## Wichtige Architektur

- `Player/Audio/SteinbergAsioStream.cs`: C#-Wrapper für die native Bridge
- `Player/Audio/FfmpegAudioPlayer.cs`: PCM-Pfad
- `Player/Audio/DsfAudioPlayer.cs`: nativer DSF→DSD-Pfad
- `Player/Audio/DffAudioPlayer.cs`: nativer DFF/DSDIFF→DSD-Pfad
- `Player/Audio/WasapiAudioPlayer.cs`: WASAPI-PCM-Pfad
- `Player/Audio/WasapiDeviceProvider.cs`: WASAPI-Geräte und Capability-Abfragen
- `Native/AsioBridge/bridge.cpp`: ASIO-Initialisierung, PCM-/DSD-Ringpuffer, Callback
- `Player/SettingsWindow.*`: Geräteauswahl und Geräteinfo
- `Player/SettingsStore.cs`: persistiert `%LOCALAPPDATA%\Player\settings.json`

## Bekannte technische Details

- Zielplattform: `net8.0-windows`, x64
- Native DSD-Wiedergabe ist aktuell für `.dsf` und unkomprimierte Stereo-`.dff` implementiert
- `.dff` mit DST-Kompression wird derzeit nicht nativ abgespielt
- Ausgabearten sind im Settings-Modell vorbereitet: `ASIO`, `WASAPI`, `KernelStreaming`
- `ASIO` und `WASAPI` sind echte Wiedergabe-Backend-Pfade
- `KernelStreaming` ist vorbereitet, aber noch nicht implementiert
- WASAPI wird derzeit für PCM-Wiedergabe genutzt; natives DSD bleibt ASIO vorbehalten
- WASAPI läuft exklusiv und wählt für PCM das erste passende Stereoformat aus 32-Bit Float, 24-Bit PCM und 16-Bit PCM
- Transport-UI unterstützt Pause/Fortsetzen, Positionsanzeige und Seeking
- Seeking ist aktuell für ASIO-PCM, WASAPI-PCM sowie native DSF/DFF-Pfade implementiert
- Playlist-Grundfunktion ist vorhanden: Einzeldatei oder Ordner laden, Doppelklick startet einen Eintrag, nach Titelende folgt automatisch der nächste
- Playlist-Tabelle ist höhenbegrenzt und scrollbar, damit Transportelemente sichtbar bleiben
- Lautstärkeregler wirkt auf PCM-Pfade; natives DSD bleibt bitgenau und wird nicht digital abgesenkt
- Im ASIO-DSD-Modus zählt `preferredBufferSize` Samples, nicht Bytes; bei `ASIOSTDSDInt8*` werden daher `preferredBufferSize / 8` Bytes pro Kanal geschrieben
- ASIO-Capability-Abfragen können fehlschlagen, wenn andere Programme das Gerät belegen

## UI-Leitlinie

- Hauptfenster: Wiedergabe, Transport und aktive Geräteanzeige
- Settings-Fenster: Geräteauswahl und Geräteinfo
- Geräteinfo zeigt Kanäle, Buffer, PCM-Sampleraten, DSD-Stufen und lesbar übersetzte Rohformate

## Pflegehinweis

Diese Datei bei Architektur-, Build- oder Verhaltensänderungen mitpflegen.
