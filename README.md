# Klub100 Generator

A tool for creating audio compilations from YouTube clips. Feed it a CSV of YouTube URLs and timestamps, and it downloads the audio, trims each clip to a fixed length, optionally inserts transition audio between clips, and merges everything into one file.

## Features

- **CSV-driven**: Two-column CSV (YouTube URL, timestamp like `01:22`)
- **Auto title fetching**: Fetches real YouTube video titles for display in the clip list
- **Configurable clip length**: Default 60 seconds, adjustable in the GUI
- **Transition audio**: Insert a single audio file or random files from a folder between clips. Optionally add transitions at the start and/or end
- **Clip reordering**: Shuffle clips randomly, then manually move individual clips up or down
- **Cookies support**: Optional `cookies.txt` for age-restricted videos
- **Progress bar**: Visual progress for download, trim, and title-fetching steps
- **Cross-platform**: Windows and macOS (MacCatalyst)

## CSV Format

```csv
URL,timestamp
https://www.youtube.com/watch?v=abc123,01:22
https://youtu.be/def456,02:15
https://www.youtube.com/watch?v=ghi789,00:45
```

- First column: full YouTube URL
- Second column: timestamp in `SS`, `MM:SS`, or `HH:MM:SS` format
- Header row is optional and auto-detected
- Quoted fields are supported

## Building & Running

### Prerequisites

- .NET 9 SDK with MAUI workload: `dotnet workload install maui`

### Windows

```bash
dotnet build
dotnet run
```

The bundled `yt-dlp.exe` and `ffmpeg.exe` in `tools/` are used automatically.

### macOS

```bash
# Install required tools (not bundled on macOS)
brew install yt-dlp ffmpeg

dotnet build
dotnet run
```

On macOS, the app looks for `yt-dlp` and `ffmpeg` on the system PATH.

## Publishing for Distribution

### Windows (self-contained single-file .exe)

```powershell
powershell -ExecutionPolicy Bypass -File publish-windows.ps1
```

This produces a self-contained `Klub100Generator.exe` (no .NET install needed on the target machine) plus the bundled `tools/` folder, zipped into `bin/Release/Klub100Generator-win-x64.zip` (~265 MB compressed). Recipients just unzip and run the exe.

### macOS (.app bundle)

```bash
chmod +x publish-macos.sh
./publish-macos.sh
```

This produces a `Klub100Generator.app` bundle for each architecture (x64 and arm64). The recipient needs `yt-dlp` and `ffmpeg` installed via Homebrew. Without notarization, right-click the app and select Open the first time.

## How It Works

1. **Choose CSV** - Load your CSV file. Video titles are fetched automatically.
2. **Reorder** (optional) - Shuffle or manually move clips to set the merge order.
3. **Download** - Fetches audio from YouTube using yt-dlp.
4. **Trim** - Cuts each clip to the configured length starting from the timestamp.
5. **Merge** - Concatenates all clips (with optional transitions) into a single MP3.

You can run steps individually or use **Run All** to execute the full pipeline.

## Project Structure

| File | Description |
|---|---|
| `AudioGeneratorService.cs` | Core logic: CSV parsing, title fetching, download, trim, merge |
| `MainPage.xaml` / `MainPage.xaml.cs` | GUI: controls, clip list, logs |
| `ClipInfo.cs` | Clip data model (title, URL, timestamp, status) |
| `TransitionSettings.cs` | Transition audio configuration |
| `MauiProgram.cs` | MAUI app builder |
| `tools/` | Bundled yt-dlp and ffmpeg (Windows) |
| `Klub100Generator.Tests/` | Unit tests for core logic |
