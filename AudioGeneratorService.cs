
using System.Diagnostics;
// using YoutubeExplode.Converter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Klub100Generator
{
            public class AudioGeneratorService

    {
                    // Set the enforced audio extension/format (e.g., webm)
    private const string EnforcedAudioExtension = ".webm";
                // Limit concurrent yt-dlp windows
                private readonly SemaphoreSlim _ytDlpSemaphore = new SemaphoreSlim(10); // Default: 10 concurrent yt-dlp
                // Limit concurrent ffmpeg trimming
                private readonly SemaphoreSlim _trimSemaphore = new SemaphoreSlim(10); // Default: 10 concurrent trims
            public event Action<string>? Log;
            private string? _basePath;
            public string BasePath
            {
                get => _basePath ?? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                set => _basePath = value;
            }


        public AudioGeneratorService() { }


        private string GetFfmpegPath()
        {
            // Expect ffmpeg.exe to be in the same folder as the selected CSV file (BasePath)
            return Path.Combine(BasePath, "ffmpeg.exe");
        }

        public string? OverrideYtDlpPath { get; set; }
        private string GetYtDlpPath()
        {
            if (!string.IsNullOrEmpty(OverrideYtDlpPath))
                return OverrideYtDlpPath;
            // Expect yt-dlp.exe to be in the same folder as the selected CSV file (BasePath)
            return Path.Combine(BasePath, "yt-dlp.exe");
        }

        public async Task<(List<string> urls, List<string> timeStamps)> ParseCsvAsync(string csvPath)
        {
            // Windows only, no platform check needed
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
        /// <summary>
        /// Downloads audio from a YouTube URL using yt-dlp and ffmpeg as background processes.
        /// </summary>
        public async Task DownloadAudioAsync(string url, string? outputFile = null)
        {
            // Windows only, no platform check needed
            var ytDlpPath = GetYtDlpPath();
            var ffmpegPath = GetFfmpegPath();
            // Check if yt-dlp.exe exists
            if (!File.Exists(ytDlpPath))
            {
                Log?.Invoke($"[ERROR] yt-dlp.exe not found at: {ytDlpPath}");
                throw new FileNotFoundException($"yt-dlp.exe not found at: {ytDlpPath}");
            }
            // Check if ffmpeg.exe exists
            if (!File.Exists(ffmpegPath))
            {
                Log?.Invoke($"[ERROR] ffmpeg.exe not found at: {ffmpegPath}");
                throw new FileNotFoundException($"ffmpeg.exe not found at: {ffmpegPath}");
            }
            var songsDir = Path.Combine(BasePath, "songs");
            Directory.CreateDirectory(songsDir);
            var tempFileName = Path.Combine(songsDir, $"{Guid.NewGuid()}.%(ext)s");
            var outputPattern = outputFile ?? tempFileName;
            // Download best audio, keep original format
            var args = $"-f bestaudio --no-playlist -o \"{outputPattern}\" {url}";
            Log?.Invoke($"[INFO] yt-dlp: Downloading: {url}");
            await RunProcessAsync(ytDlpPath, args, BasePath);

            // Find the downloaded file (yt-dlp replaces %(ext)s with actual extension)
            var downloadedFile = Directory.GetFiles(songsDir)
                .OrderByDescending(f => File.GetCreationTime(f))
                .FirstOrDefault(f => f.Contains(Path.GetFileNameWithoutExtension(tempFileName)));

            if (downloadedFile != null && File.Exists(downloadedFile))
            {
                Log?.Invoke($"[INFO] yt-dlp: Downloaded to: {downloadedFile}");
                // Enforce file extension/format if needed using ffmpeg directly
                if (!downloadedFile.EndsWith(EnforcedAudioExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var enforcedFile = Path.ChangeExtension(downloadedFile, EnforcedAudioExtension);
                    Log?.Invoke($"[INFO] ffmpeg: Converting {downloadedFile} to enforced format: {enforcedFile}");
                    var ffmpegArgs = $"-i \"{downloadedFile}\" -c:a libopus -b:a 128k -vn -y \"{enforcedFile}\"";
                    await RunProcessAsync(ffmpegPath, ffmpegArgs, Path.GetDirectoryName(ffmpegPath) ?? BasePath);
                    File.Delete(downloadedFile);
                }
            }
            else
            {
                Log?.Invoke($"[ERROR] yt-dlp: Download failed for: {url}");
            }
        }
        private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                await process.StandardOutput.ReadToEndAsync(); // Discard output
                await process.StandardError.ReadToEndAsync();  // Discard error output
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Log?.Invoke($"[FAIL] Process exited with code {process.ExitCode}");
                    throw new Exception($"Process exited with code {process.ExitCode}");
                }
            }
        }
    public async Task DownloadSongsAsync(List<string> urls, int startIndex = 0, bool onlyOne = false)
        {
            // Windows only, no platform check needed
            if (startIndex >= urls.Count) return;
            Directory.CreateDirectory(Path.Combine(BasePath, "songs"));

            Log?.Invoke($"[INFO] Entering DownloadSongsAsync...");
            try
            {
                Log?.Invoke($"[INFO] Starting download of {urls.Count} songs...");
                const int batchSize = 10;
                for (int batchStart = startIndex; batchStart < urls.Count; batchStart += batchSize)
                {
                    Log?.Invoke($"[INFO] Downloading batch {batchStart / batchSize + 1} ({Math.Min(batchSize, urls.Count - batchStart)} songs)...");
                    var tasks = new List<Task>();
                    for (int i = batchStart; i < Math.Min(batchStart + batchSize, urls.Count); i++)
                    {
                        Log?.Invoke($"[INFO] Queueing download for song {i + 1} ({urls[i]})");
                        await _ytDlpSemaphore.WaitAsync();
                        var url = urls[i];
                        // Name files as 1.webm, 2.webm, ... (1-based index, but extension will be set by yt-dlp)
                        var outputPath = Path.Combine(BasePath, "songs", $"{i + 1}.%(ext)s");
                        var task = DownloadAudioWithSemaphoreAsync(url, outputPath);
                        tasks.Add(task);
                        if (onlyOne) break;
                    }
                    await Task.WhenAll(tasks);
                    Log?.Invoke($"[INFO] Finished batch {batchStart / batchSize + 1}");
                    if (onlyOne) break;
                }
                Log?.Invoke("[INFO] Download finished. [FINISHED]");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[EXCEPTION] DownloadSongsAsync: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private async Task DownloadAudioWithSemaphoreAsync(string url, string outputPath)
        {
            try
            {
                Log?.Invoke($"[INFO] Downloading audio: {url}");
                // Basic validation: check if URL contains 'youtube.com' or 'youtu.be'
                if (!url.Contains("youtube.com") && !url.Contains("youtu.be"))
                {
                    Log?.Invoke($"[ERROR] Invalid YouTube URL: {url}");
                    return;
                }
                await DownloadAudioAsync(url, outputPath);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[EXCEPTION] Error downloading: {url}: {ex.Message}");
            }
            finally
            {
                _ytDlpSemaphore.Release();
            }
    }

    public async Task CutAudioAsync(List<string> timeStamps)
        {
            // Windows only, no platform check needed
            Log?.Invoke($"[INFO] Entering CutAudioAsync...");
            try
            {
                Log?.Invoke($"[INFO] Starting trim of {timeStamps.Count} files...");
                var songsDir = Path.Combine(BasePath, "songs");
                var trimmedDir = Path.Combine(songsDir, "trimmed");
                Directory.CreateDirectory(trimmedDir);
                // Only use files with the enforced extension
                var songFiles = Directory.GetFiles(songsDir, "*" + EnforcedAudioExtension)
                    .Select(f => new FileInfo(f)).ToList();
                var trimTasks = new List<Task>();
                for (int i = 0; i < songFiles.Count; i++)
                {

                    await _trimSemaphore.WaitAsync();
                    var file = songFiles[i];
                    var index = int.Parse(Path.GetFileNameWithoutExtension(file.Name));
                    trimTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            if (index < 0 || index >= timeStamps.Count)
                            {
                                Log?.Invoke($"[WARN] No timestamp for file {file.Name} (index {index}). Skipping.");
                                return;
                            }
                            var trimmedPath = Path.Combine(trimmedDir, $"{index}-cut.mp3");


                            // Validate input file exists
                            if (!File.Exists(file.FullName))
                            {
                                Log?.Invoke($"[ERROR] Input file does not exist: {file.FullName}");
                                return;
                            }

                            // Only check for non-empty timestamp (let ffmpeg handle format)
                            var start = timeStamps[index];
                            if (string.IsNullOrWhiteSpace(start))
                            {
                                Log?.Invoke($"[ERROR] Empty timestamp for file: {file.Name}");
                                return;
                            }

                            // Convert timestamp to total seconds for ffmpeg -ss
                            int TimestampToSeconds(string ts)
                            {
                                var parts = ts.Split(':').Select(int.Parse).ToArray();
                                if (parts.Length == 1) return parts[0];
                                if (parts.Length == 2) return parts[0] * 60 + parts[1];
                                if (parts.Length == 3) return parts[0] * 3600 + parts[1] * 60 + parts[2];
                                return 0;
                            }
                            var totalSeconds = TimestampToSeconds(start);


                            // Use ffmpeg process directly for robust trimming and mp3 encoding
                            var ffmpegPath = GetFfmpegPath();
                            var args = $"-ss {totalSeconds} -t 60 -i \"{file.FullName}\" -vn -acodec libmp3lame -ar 44100 -ab 192k -y \"{trimmedPath}\"";
                            await RunProcessAsync(ffmpegPath, args, Path.GetDirectoryName(ffmpegPath) ?? BasePath);
                            Log?.Invoke($"[INFO] Finished trimming {file.Name}");
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke($"[EXCEPTION] CutAudioAsync (file {file.Name}): {ex.Message}\n{ex.StackTrace}");
                        }
                        finally
                        {
                            _trimSemaphore.Release();
                        }
                    }));
                }
                await Task.WhenAll(trimTasks);
                Log?.Invoke("[INFO] Trim finished. [FINISHED]");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[EXCEPTION] CutAudioAsync: {ex.Message}\n{ex.StackTrace}");
            }
        }

    public async Task MergeAsync(string outputFilePath)
        {
            try
            {
                Log?.Invoke("[INFO] Starting merge process...");
                var trimmedDir = Path.Combine(BasePath, "songs", "trimmed");
                // Merge all mp3 files in the trimmed folder
                var songFiles = Directory.GetFiles(trimmedDir, "*.mp3")
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.Name)
                    .ToList();

                // Validate and re-encode all files to a consistent format
                var validFiles = new List<FileInfo>();
                var reencodedDir = Path.Combine(trimmedDir, "reencoded");
                Directory.CreateDirectory(reencodedDir);
                var ffmpegPath = GetFfmpegPath();
                foreach (var file in songFiles)
                {
                    if (!file.Exists)
                    {
                        Log?.Invoke($"[ERROR] Merge validation: File does not exist: {file.FullName}");
                        continue;
                    }
                    if (file.Length == 0)
                    {
                        Log?.Invoke($"[ERROR] Merge validation: File is zero bytes: {file.FullName}");
                        continue;
                    }
                    // Probe file with ffmpeg to check if it can be read
                    var ffmpegProbePath = ffmpegPath;
                    var probeArgs = $"-v error -i \"{file.FullName}\" -f null -";
                    var psiProbe = new ProcessStartInfo
                    {
                        FileName = ffmpegProbePath,
                        Arguments = probeArgs,
                        WorkingDirectory = Path.GetDirectoryName(ffmpegProbePath) ?? BasePath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    try
                    {
                        using (var probeProc = new Process { StartInfo = psiProbe })
                        {
                            probeProc.Start();
                            probeProc.WaitForExit(10000); // 10s timeout for probe
                            if (probeProc.ExitCode == 0)
                            {
                                // Re-encode to a new file in reencodedDir
                                var reencodedPath = Path.Combine(reencodedDir, file.Name);
                                var reencodeArgs = $"-i \"{file.FullName}\" -vn -acodec libmp3lame -ar 44100 -ac 2 -ab 192k -y \"{reencodedPath}\"";
                                var psiReencode = new ProcessStartInfo
                                {
                                    FileName = ffmpegPath,
                                    Arguments = reencodeArgs,
                                    WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? BasePath,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                };
                                using (var reencodeProc = new Process { StartInfo = psiReencode })
                                {
                                    reencodeProc.Start();
                                    reencodeProc.WaitForExit();
                                    if (reencodeProc.ExitCode == 0 && File.Exists(reencodedPath))
                                    {
                                        validFiles.Add(new FileInfo(reencodedPath));
                                    }
                                    else
                                    {
                                        Log?.Invoke($"[ERROR] Re-encoding failed for file: {file.FullName}");
                                    }
                                }
                            }
                            else
                            {
                                Log?.Invoke($"[ERROR] Merge validation: ffmpeg cannot read file: {file.FullName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"[ERROR] Merge validation: Exception probing file {file.FullName}: {ex.Message}");
                    }
                }

                if (validFiles.Count == 0)
                {
                    Log?.Invoke("[ERROR] Merge validation: No valid cut files found to merge. Aborting merge.");
                    return;
                }

                // Create a concat list file for the re-encoded files
                var concatListPath = Path.Combine(reencodedDir, "concat_list.txt");
                using (var writer = new StreamWriter(concatListPath, false))
                {
                    foreach (var file in validFiles)
                    {
                        writer.WriteLine($"file '{file.FullName.Replace("'", "'\\''")}'");
                    }
                }

                // Run ffmpeg to merge
                var args = $"-f concat -safe 0 -i \"{concatListPath}\" -c copy \"{outputFilePath}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? BasePath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Log?.Invoke("[INFO] Running ffmpeg merge process...");
                using (var proc = new Process { StartInfo = psi })
                {
                    proc.Start();
                    var mergeTask = Task.Run(async () => {
                        await proc.StandardOutput.ReadToEndAsync();
                        await proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();
                    });
                    var timeout = TimeSpan.FromMinutes(5);
                    if (await Task.WhenAny(mergeTask, Task.Delay(timeout)) == mergeTask)
                    {
                        // Completed within timeout
                        if (proc.ExitCode != 0)
                        {
                            Log?.Invoke($"[ERROR] ffmpeg exited with code {proc.ExitCode}");
                        }
                        else
                        {
                            Log?.Invoke("[INFO] ffmpeg merge process completed successfully.");
                        }
                    }
                    else
                    {
                        try
                        {
                            proc.Kill();
                        }
                        catch { }
                        Log?.Invoke("[ERROR] ffmpeg merge process timed out and was killed.");
                    }
                }

                // Optionally delete the concat list file after merging
                try { File.Delete(concatListPath); } catch { }
                Log?.Invoke("[INFO] Merge finished. [FINISHED]");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[EXCEPTION] MergeAsync: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}