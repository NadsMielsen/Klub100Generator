using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Klub100Generator
{
    public class AudioGeneratorService
    {
        public event Action<string>? Log;

    private string GetFfmpegPath(string basePath)
    {
        var toolsDir = Path.Combine(basePath, "tools");
#if WINDOWS
        return Path.Combine(toolsDir, "ffmpeg.exe");
#elif MACOS
        return Path.Combine(toolsDir, "ffmpeg");
#else
        throw new PlatformNotSupportedException("ffmpeg binary not found for this OS.");
#endif
    }
        public async Task<(List<string> urls, List<string> timeStamps)> ParseCsvAsync(string csvPath)
        {
            EnsureSupportedPlatform();
            var urls = new List<string>();
            var timeStamps = new List<string>();
            if (!File.Exists(csvPath))
                throw new FileNotFoundException($"CSV file not found: {csvPath}");

            var text = File.ReadAllText(csvPath).Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < text.Length; i++)
            {
                if (i % 2 == 0)
                    urls.Add(text[i]);
                else
                    timeStamps.Add(text[i]);
            }
            return (urls, timeStamps);
        }

        public async Task DownloadSongsAsync(List<string> urls, string basePath, int startIndex = 0, bool onlyOne = false)
        {
            EnsureSupportedPlatform();
            if (startIndex >= urls.Count) return;
            Directory.CreateDirectory(Path.Combine(basePath, "songs"));
            for (int i = startIndex; i < urls.Count; i++)
            {
                Log?.Invoke($"Downloading audio {urls[i]}");
                var outputPath = Path.Combine(basePath, "songs", $"{i}.mp3");
                var ytDlpPath = GetYtDlpPath(basePath);
                var args = $"-x --audio-format mp3 -o '{outputPath}' {urls[i]}";
                await RunProcessAsync(ytDlpPath, args, basePath);
                if (onlyOne) break;
            }
        }
    private string GetYtDlpPath(string basePath)
    {
        var toolsDir = Path.Combine(basePath, "tools");
#if WINDOWS
        return Path.Combine(toolsDir, "yt-dlp.exe");
#elif MACOS
        return Path.Combine(toolsDir, "yt-dlp_macos");
#else
        throw new PlatformNotSupportedException("yt-dlp binary not found for this OS.");
#endif
    }

        public async Task CutAudioAsync(string basePath, List<string> timeStamps)
        {
            EnsureSupportedPlatform();
            var songsDir = Path.Combine(basePath, "songs");
            var trimmedDir = Path.Combine(songsDir, "trimmed");
            Directory.CreateDirectory(trimmedDir);
            var songFiles = Directory.GetFiles(songsDir, "*.mp3").Select(f => new FileInfo(f)).ToList();
            for (int i = 0; i < songFiles.Count; i++)
            {
                var index = int.Parse(Path.GetFileNameWithoutExtension(songFiles[i].Name));
                var trimmedPath = Path.Combine(trimmedDir, $"{index}-cut.mp3");
                    var ffmpegPath = GetFfmpegPath(basePath);
                var args = $"-ss {timeStamps[index]} -t 59 -i '{songFiles[i].FullName}' '{trimmedPath}' -y";
                Log?.Invoke($"Cutting audio {songFiles[i].Name}");
                await RunProcessAsync(ffmpegPath, args, basePath);
            }
        }

        public async Task MergeAsync(string basePath, string outputFilePath)
        {
            EnsureSupportedPlatform();
            var trimmedDir = Path.Combine(basePath, "songs", "trimmed");
            var songFiles = Directory.GetFiles(trimmedDir, "*.mp3").Select(f => new FileInfo(f)).OrderBy(_ => Guid.NewGuid()).ToList();
            var cheersDir = Path.Combine(basePath, "cheers");
            var cheers = Directory.Exists(cheersDir) ? Directory.GetFiles(cheersDir, "*.mp3").Select(f => new FileInfo(f)).ToList() : new List<FileInfo>();
            var songListPath = Path.Combine(basePath, "songList.txt");
            var lines = new List<string>();
            foreach (var songFile in songFiles)
            {
                lines.Add($"file '{songFile.FullName.Replace("'", "''")}'");
                if (cheers.Count > 0)
                {
                    var cheer = cheers[new Random().Next(cheers.Count)];
                    lines.Add($"file '{cheer.FullName.Replace("'", "''")}'");
                }
            }
            File.WriteAllLines(songListPath, lines);
            var ffmpegPath = GetFfmpegPath(basePath);
            var args = $"-f concat -safe 0 -i '{songListPath}' -codec mp3 '{outputFilePath}' -y";
            Log?.Invoke($"Merging audio to {outputFilePath}");
            await RunProcessAsync(ffmpegPath, args, basePath);
        }
    private void EnsureSupportedPlatform()
    {
#if !(WINDOWS || MACOS)
        throw new PlatformNotSupportedException("This feature is only supported on Windows and macOS Desktop.");
#endif
    }

        private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log?.Invoke(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
        }
    }
}