# Player

This repo now contains a small Steinberg-ASIO-backed playback stack:

- `Native/AsioBridge` — native x64 DLL built directly against `C:\Dev\asiosdk_2.3`
- `Player/Audio/SteinbergAsioStream.cs` — the C# wrapper exposed to the WPF app
- `Player/Audio/FfmpegAudioPlayer.cs` — file playback via `ffprobe` + `ffmpeg`

Build with:

```powershell
.\build.ps1
```

The bridge currently targets stereo PCM playback and accepts interleaved `float` samples from C#. It converts to the ASIO driver’s output format for the common little-endian sample types used by Windows drivers.

The player UI supports `.dsf`, `.dff`, `.flac`, `.mp3`, `.wav`, `.aiff`, `.m4a`, `.aac`, `.ogg`, `.opus`, and `.wma`.
DSD files are currently decoded to PCM for playback; native DSD-over-ASIO is not implemented yet.
# Player
