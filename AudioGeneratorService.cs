using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Klub100Generator;

public class AudioGeneratorService
{
    private readonly SemaphoreSlim _downloadSemaphore = new(2);
    private readonly SemaphoreSlim _trimSemaphore = new(5);
    private readonly SemaphoreSlim _oembedSemaphore = new(5);
    private static readonly HttpClient _httpClient = new();

    public event Action<string>? Log;
    public event Action<int, int>? ProgressChanged;

    private string? _basePath;
    public string BasePath
    {
        get => _basePath ?? AppContext.BaseDirectory;
        set => _basePath = value;
    }

    public AudioGeneratorService() { }

    #region Binary Resolution

    private string GetFfmpegPath()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
        {
            var path = Path.Combine(toolsDir, name);
            if (File.Exists(path)) return path;
        }
        foreach (var name in new[] { "ffmpeg.exe", "ffmpeg" })
        {
            var path = Path.Combine(BasePath, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(
            $"ffmpeg not found. Looked in '{toolsDir}' and '{BasePath}'. " +
            "Please ensure the tools folder contains ffmpeg.");
    }

    private string GetYtDlpPath()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        foreach (var name in new[] { "yt-dlp.exe", "yt-dlp" })
        {
            var path = Path.Combine(toolsDir, name);
            if (File.Exists(path)) return path;
        }
        foreach (var name in new[] { "yt-dlp.exe", "yt-dlp" })
        {
            var path = Path.Combine(BasePath, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(
            $"yt-dlp not found. Looked in '{toolsDir}' and '{BasePath}'. " +
            "Please ensure the tools folder contains yt-dlp.exe.");
    }

    private string GetFfmpegLocationArg()
    {
        var ffmpegPath = GetFfmpegPath();
        var dir = Path.GetDirectoryName(ffmpegPath);
        return !string.IsNullOrEmpty(dir) && ffmpegPath != "ffmpeg"
            ? $"--ffmpeg-location \"{dir}\""
            : string.Empty;
    }

    private string GetCookiesArg(string? cookiesPath)
    {
        if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            return $"--cookies \"{cookiesPath}\"";
        var defaultCookies = Path.Combine(BasePath, "cookies.txt");
        return File.Exists(defaultCookies) ? $"--cookies \"{defaultCookies}\"" : string.Empty;
    }

    #endregion

    #region CSV Parsing

    public Task<List<ClipInfo>> ParseCsvAsync(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"CSV file not found: {csvPath}");

        var clips = new List<ClipInfo>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(csvPath);
        int id = 1;
        bool headerSkipped = false;

        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = lines[lineNumber];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Count < 2)
            {
                Log?.Invoke($"[WARN] Skipping line {lineNumber + 1}: not enough columns (expected URL and timestamp)");
                continue;
            }

            var url = parts[0].Trim().Trim('"');
            var timestamp = parts[1].Trim().Trim('"');

            if (!headerSkipped)
            {
                if (url.Contains("url", StringComparison.OrdinalIgnoreCase) &&
                    timestamp.Contains("time", StringComparison.OrdinalIgnoreCase))
                {
                    headerSkipped = true;
                    continue;
                }
                headerSkipped = true;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                Log?.Invoke($"[WARN] Skipping line {lineNumber + 1}: URL is empty");
                continue;
            }

            if (!url.Contains("youtube.com") && !url.Contains("youtu.be") && !url.Contains("soundcloud.com"))
            {
                Log?.Invoke($"[WARN] Skipping line {lineNumber + 1}: URL '{url}' is not a supported link (YouTube or SoundCloud)");
                continue;
            }

            if (string.IsNullOrWhiteSpace(timestamp))
            {
                Log?.Invoke($"[WARN] Skipping line {lineNumber + 1}: timestamp is empty");
                continue;
            }

            if (!ValidateTimestamp(timestamp))
            {
                Log?.Invoke($"[WARN] Skipping line {lineNumber + 1}: timestamp '{timestamp}' is not a valid format (expected SS, MM:SS, or HH:MM:SS)");
                continue;
            }

            if (parts.Count > 2)
            {
                Log?.Invoke($"[WARN] Line {lineNumber + 1}: has {parts.Count} columns, expected 2. Extra columns will be ignored.");
            }

            if (!seenUrls.Add(url))
            {
                Log?.Invoke($"[WARN] Line {lineNumber + 1}: duplicate URL '{url}' (accepted anyway)");
            }

            clips.Add(new ClipInfo
            {
                Id = id++,
                Url = url,
                Timestamp = timestamp
            });
        }

        Log?.Invoke($"[INFO] Parsed {clips.Count} valid clip(s) from CSV.");
        return Task.FromResult(clips);
    }

    internal static bool ValidateTimestamp(string ts)
    {
        if (string.IsNullOrWhiteSpace(ts))
            return false;

        var parts = ts.Trim().Split(':');
        if (parts.Length is < 1 or > 3)
            return false;

        foreach (var p in parts)
            if (!int.TryParse(p, out _))
                return false;

        var nums = parts.Select(int.Parse).ToArray();

        return nums.Length switch
        {
            1 => nums[0] >= 0 && nums[0] < 86400,
            2 => nums[0] >= 0 && nums[0] < 600 && nums[1] >= 0 && nums[1] < 60,
            3 => nums[0] >= 0 && nums[0] < 100 && nums[1] >= 0 && nums[1] < 60 && nums[2] >= 0 && nums[2] < 60,
            _ => false
        };
    }

    internal static List<string> ParseCsvLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        parts.Add(current.ToString());
        return parts;
    }

    public static int TimestampToSeconds(string ts)
    {
        if (string.IsNullOrWhiteSpace(ts))
            return 0;

        var parts = ts.Trim().Split(':');
        try
        {
            return parts.Length switch
            {
                1 => int.Parse(parts[0]),
                2 => int.Parse(parts[0]) * 60 + int.Parse(parts[1]),
                3 => int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]),
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Title Fetching

    public async Task FetchTitlesAsync(List<ClipInfo> clips, string? cookiesPath)
    {
        Log?.Invoke($"[INFO] Fetching titles for {clips.Count} clips...");
        int completed = 0;
        ReportProgress(0, clips.Count);

        var tasks = clips.Select(async clip =>
        {
            try
            {
                var title = await FetchTitleViaOEmbedAsync(clip.Url);
                if (string.IsNullOrWhiteSpace(title))
                    title = await FetchTitleViaYtDlpAsync(clip.Url, cookiesPath);

                if (!string.IsNullOrWhiteSpace(title))
                {
                    clip.Title = title.Trim();
                    Log?.Invoke($"[INFO] Clip {clip.Id}: {clip.Title}");
                }
                else
                {
                    Log?.Invoke($"[WARN] Could not fetch title for clip {clip.Id}");
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[WARN] Error fetching title for clip {clip.Id}: {ex.Message}");
            }
            finally
            {
                Interlocked.Increment(ref completed);
                ReportProgress(completed, clips.Count);
            }
        }).ToList();

        await Task.WhenAll(tasks);
        Log?.Invoke("[INFO] Title fetching complete.");
    }

    private async Task<string?> FetchTitleViaOEmbedAsync(string url)
    {
        await _oembedSemaphore.WaitAsync();
        try
        {
            await Task.Delay(200);

            string oembedUrl;
            if (url.Contains("soundcloud.com"))
                oembedUrl = $"https://soundcloud.com/oembed?url={Uri.EscapeDataString(url)}&format=json";
            else
                oembedUrl = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(url)}&format=json";

            var response = await _httpClient.GetAsync(oembedUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var titleMatch = System.Text.RegularExpressions.Regex.Match(json, "\"title\"\\s*:\\s*\"(.*?)\"", System.Text.RegularExpressions.RegexOptions.Singleline);
            return titleMatch.Success ? titleMatch.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _oembedSemaphore.Release();
        }
    }

    private async Task<string?> FetchTitleViaYtDlpAsync(string url, string? cookiesPath)
    {
        try
        {
            var ytDlpPath = GetYtDlpPath();
            var cookiesArg = GetCookiesArg(cookiesPath);
            var args = $"--print \"%(title)s\" --skip-download --no-playlist --no-update --no-warnings {cookiesArg} {url}";
            var (stdout, _, exitCode) = await RunProcessCapturedAsync(ytDlpPath, args, BasePath);
            return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Download

    public async Task DownloadAsync(List<ClipInfo> clips, string? cookiesPath)
    {
        Log?.Invoke($"[INFO] Starting download of {clips.Count} clips...");
        var ytDlpPath = GetYtDlpPath();
        var ffmpegLocationArg = GetFfmpegLocationArg();
        var cookiesArg = GetCookiesArg(cookiesPath);
        var songsDir = Path.Combine(BasePath, "songs");
        Directory.CreateDirectory(songsDir);

        int completed = 0;
        ReportProgress(0, clips.Count);

        var tasks = clips.Select(async clip =>
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                clip.Status = ClipStatus.Downloading;
                Log?.Invoke($"[INFO] Downloading clip {clip.Id}: {clip.Url}");

                var outputPath = Path.Combine(songsDir, $"{clip.Id}.%(ext)s");
                var args = $"-f bestaudio --no-playlist --no-update --no-warnings --retries 5 --fragment-retries 5 --sleep-requests 1 {ffmpegLocationArg} {cookiesArg} -o \"{outputPath}\" {clip.Url}";

                var maxRetries = 5;
                var delays = new[] { 10, 20, 30, 60 };
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        await RunProcessAsync(ytDlpPath, args, BasePath);
                        break;
                    }
                    catch (Exception ex) when (attempt < maxRetries &&
                        (ex.Message.Contains("403") || ex.Message.Contains("429") ||
                         ex.Message.Contains("Forbidden") || ex.Message.Contains("rate")))
                    {
                        var delay = delays[attempt - 1];
                        Log?.Invoke($"[WARN] Clip {clip.Id} failed (attempt {attempt}/{maxRetries}). Retrying in {delay}s...");
                        await Task.Delay(delay * 1000);

                        var partialFiles = Directory.GetFiles(songsDir, $"{clip.Id}.*")
                            .Where(f => f.EndsWith(".part") || f.EndsWith(".ytdl") || f.EndsWith(".tmp"));
                        foreach (var partial in partialFiles)
                        {
                            try { File.Delete(partial); } catch { }
                        }
                    }
                }

                var files = Directory.GetFiles(songsDir, $"{clip.Id}.*")
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "trimmed"))
                    .ToArray();
                clip.DownloadedFilePath = files.FirstOrDefault();

                if (clip.DownloadedFilePath != null)
                {
                    clip.Status = ClipStatus.Downloaded;
                    Log?.Invoke($"[INFO] Downloaded clip {clip.Id}");
                }
                else
                {
                    clip.Status = ClipStatus.Failed;
                    clip.ErrorMessage = "Downloaded file not found";
                    Log?.Invoke($"[ERROR] Could not find downloaded file for clip {clip.Id}");
                }
            }
            catch (Exception ex)
            {
                clip.Status = ClipStatus.Failed;
                clip.ErrorMessage = ex.Message;
                Log?.Invoke($"[ERROR] Failed to download clip {clip.Id}: {ex.Message}");
            }
            finally
            {
                _downloadSemaphore.Release();
                Interlocked.Increment(ref completed);
                ReportProgress(completed, clips.Count);
            }
        }).ToList();

        await Task.WhenAll(tasks);

        var successCount = clips.Count(c => c.Status == ClipStatus.Downloaded);
        Log?.Invoke($"[INFO] Download complete. {successCount}/{clips.Count} clips downloaded.");
    }

    #endregion

    #region Trim

    public async Task TrimAsync(List<ClipInfo> clips, int clipLengthSeconds)
    {
        Log?.Invoke($"[INFO] Starting trim of {clips.Count} clips (length: {clipLengthSeconds}s)...");
        var ffmpegPath = GetFfmpegPath();
        var songsDir = Path.Combine(BasePath, "songs");
        var trimmedDir = Path.Combine(songsDir, "trimmed");
        Directory.CreateDirectory(trimmedDir);

        var clipsToTrim = clips.Where(c => c.Status is ClipStatus.Downloaded or ClipStatus.Trimmed).ToList();
        int completed = 0;
        ReportProgress(0, clipsToTrim.Count);

        var tasks = clipsToTrim.Select(async clip =>
        {
            await _trimSemaphore.WaitAsync();
            try
            {
                clip.Status = ClipStatus.Trimming;

                var inputFile = clip.DownloadedFilePath;
                if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
                {
                    var files = Directory.GetFiles(songsDir, $"{clip.Id}.*")
                        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "trimmed"));
                    inputFile = files.FirstOrDefault();
                }

                if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
                {
                    clip.Status = ClipStatus.Failed;
                    clip.ErrorMessage = "Source audio file not found";
                    Log?.Invoke($"[ERROR] Source file not found for clip {clip.Id}");
                    return;
                }

                var totalSeconds = TimestampToSeconds(clip.Timestamp);
                var trimmedPath = Path.Combine(trimmedDir, $"{clip.Id}-cut.mp3");
                var args = $"-ss {totalSeconds} -i \"{inputFile}\" -t {clipLengthSeconds} -vn -acodec libmp3lame -ar 44100 -ac 2 -ab 192k -y \"{trimmedPath}\"";

                await RunProcessAsync(ffmpegPath, args, BasePath);

                clip.TrimmedFilePath = trimmedPath;
                clip.Status = ClipStatus.Trimmed;
                Log?.Invoke($"[INFO] Trimmed clip {clip.Id}");
            }
            catch (Exception ex)
            {
                clip.Status = ClipStatus.Failed;
                clip.ErrorMessage = ex.Message;
                Log?.Invoke($"[ERROR] Failed to trim clip {clip.Id}: {ex.Message}");
            }
            finally
            {
                _trimSemaphore.Release();
                Interlocked.Increment(ref completed);
                ReportProgress(completed, clipsToTrim.Count);
            }
        }).ToList();

        await Task.WhenAll(tasks);

        var successCount = clips.Count(c => c.Status == ClipStatus.Trimmed);
        Log?.Invoke($"[INFO] Trim complete. {successCount}/{clips.Count} clips trimmed.");
    }

    #endregion

    #region Merge

    public async Task MergeAsync(List<ClipInfo> orderedClips, TransitionSettings transitions, string outputFilePath)
    {
        Log?.Invoke("[INFO] Starting merge...");
        var ffmpegPath = GetFfmpegPath();
        var trimmedDir = Path.Combine(BasePath, "songs", "trimmed");

        var validClips = orderedClips
            .Where(c => c.Status == ClipStatus.Trimmed &&
                        !string.IsNullOrEmpty(c.TrimmedFilePath) &&
                        File.Exists(c.TrimmedFilePath))
            .ToList();

        if (validClips.Count == 0)
        {
            Log?.Invoke("[ERROR] No valid trimmed clips found to merge.");
            return;
        }

        Log?.Invoke($"[INFO] Merging {validClips.Count} clips...");

        List<string> transitionFiles = new();
        if (transitions.Mode != TransitionMode.None)
        {
            transitionFiles = await PrepareTransitionsAsync(transitions, ffmpegPath);
            if (transitionFiles.Count == 0)
            {
                Log?.Invoke("[WARN] No transition files available. Merging without transitions.");
            }
        }

        bool useTransitions = transitions.Mode != TransitionMode.None && transitionFiles.Count > 0;
        var random = new Random();

        var concatListPath = Path.Combine(trimmedDir, "concat_list.txt");
        var lines = new List<string>();

        if (useTransitions && transitions.AddAtStart)
        {
            var t = GetTransitionFile(transitions, transitionFiles, random);
            lines.Add(FormatConcatLine(t));
        }

        for (int i = 0; i < validClips.Count; i++)
        {
            var clip = validClips[i];
            lines.Add(FormatConcatLine(clip.TrimmedFilePath!));

            bool isLast = i == validClips.Count - 1;
            if (useTransitions && !isLast)
            {
                var t = GetTransitionFile(transitions, transitionFiles, random);
                lines.Add(FormatConcatLine(t));
            }
        }

        if (useTransitions && transitions.AddAtEnd)
        {
            var t = GetTransitionFile(transitions, transitionFiles, random);
            lines.Add(FormatConcatLine(t));
        }

        File.WriteAllLines(concatListPath, lines);
        Log?.Invoke($"[INFO] Concat list written with {lines.Count} entries.");

        ReportProgress(0, 2);

        Log?.Invoke("[INFO] Validating audio parameters...");
        var allFiles = validClips.Select(c => c.TrimmedFilePath!).ToList();
        allFiles.AddRange(transitionFiles);
        foreach (var f in allFiles)
        {
            var probeArgs = $"-v error -show_entries stream=codec_name,sample_rate,channels,bit_rate -of default=noprint_wrappers=1 \"{f}\"";
            try
            {
                var (probeOut, _, probeCode) = await RunProcessCapturedAsync(ffmpegPath, probeArgs, BasePath);
                if (probeCode == 0 && !string.IsNullOrWhiteSpace(probeOut))
                    Log?.Invoke($"[INFO] {Path.GetFileName(f)}: {probeOut.Trim().Replace("\n", ", ")}");
            }
            catch { }
        }

        var args = $"-f concat -safe 0 -i \"{concatListPath}\" -c:a libmp3lame -ar 44100 -ac 2 -ab 192k \"{outputFilePath}\"";
        Log?.Invoke("[INFO] Running ffmpeg merge (re-encoding for consistency)...");

        await RunProcessAsync(ffmpegPath, args, BasePath);
        ReportProgress(1, 2);
        Log?.Invoke($"[INFO] Merge complete: {outputFilePath}");

        ReportProgress(2, 2);
        try { File.Delete(concatListPath); } catch { }
    }

    private async Task<List<string>> PrepareTransitionsAsync(TransitionSettings transitions, string ffmpegPath)
    {
        var transitionFiles = new List<string>();
        var transitionDir = Path.Combine(BasePath, "songs", "transitions");
        Directory.CreateDirectory(transitionDir);

        List<string> sourceFiles = new();

        if (transitions.Mode == TransitionMode.SingleFile &&
            !string.IsNullOrEmpty(transitions.SingleFilePath) &&
            File.Exists(transitions.SingleFilePath))
        {
            sourceFiles.Add(transitions.SingleFilePath);
        }
        else if (transitions.Mode == TransitionMode.RandomFolder &&
                 !string.IsNullOrEmpty(transitions.FolderPath) &&
                 Directory.Exists(transitions.FolderPath))
        {
            sourceFiles = Directory.GetFiles(transitions.FolderPath)
                .Where(f => f.EndsWith(".mp3") || f.EndsWith(".wav") || f.EndsWith(".m4a") ||
                            f.EndsWith(".webm") || f.EndsWith(".ogg") || f.EndsWith(".aac") ||
                            f.EndsWith(".flac"))
                .ToList();
        }

        foreach (var sourceFile in sourceFiles)
        {
            var encodedPath = Path.Combine(transitionDir,
                Path.GetFileNameWithoutExtension(sourceFile) + ".mp3");
            var args = $"-i \"{sourceFile}\" -vn -acodec libmp3lame -ar 44100 -ac 2 -ab 192k -y \"{encodedPath}\"";
            try
            {
                await RunProcessAsync(ffmpegPath, args, BasePath);
                transitionFiles.Add(encodedPath);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[WARN] Failed to encode transition '{Path.GetFileName(sourceFile)}': {ex.Message}");
            }
        }

        Log?.Invoke($"[INFO] Prepared {transitionFiles.Count} transition file(s).");
        return transitionFiles;
    }

    private static string GetTransitionFile(TransitionSettings transitions, List<string> transitionFiles, Random random)
    {
        if (transitions.Mode == TransitionMode.SingleFile)
            return transitionFiles[0];
        return transitionFiles[random.Next(transitionFiles.Count)];
    }

    internal static string FormatConcatLine(string filePath)
        => $"file '{filePath.Replace("'", "'\\''")}'";

    #endregion

    #region Process Execution

    private async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var (stdout, stderr, exitCode) = await RunProcessCapturedAsync(fileName, arguments, workingDirectory);

        if (!string.IsNullOrWhiteSpace(stdout))
            Log?.Invoke($"[PROCESS] {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr))
            Log?.Invoke($"[PROCESS] {stderr.Trim()}");

        if (exitCode != 0)
        {
            Log?.Invoke($"[ERROR] Process exited with code {exitCode}");
            throw new Exception($"Process exited with code {exitCode}. STDERR: {stderr}");
        }
    }

    private async Task<(string stdout, string stderr, int exitCode)> RunProcessCapturedAsync(
        string fileName, string arguments, string workingDirectory)
    {
        var dir = Path.GetDirectoryName(fileName);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = !string.IsNullOrEmpty(dir) ? dir : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (await stdoutTask, await stderrTask, process.ExitCode);
    }

    #endregion

    private void ReportProgress(int current, int total)
        => ProgressChanged?.Invoke(current, total);
}
