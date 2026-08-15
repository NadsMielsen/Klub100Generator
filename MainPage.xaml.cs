using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace Klub100Generator;

public partial class MainPage : ContentPage
{
    private readonly AudioGeneratorService _audioService = new();
    private readonly ObservableCollection<ClipInfo> _clips = new();
    private string? _selectedCsvPath;
    private string? _cookiesPath;
    private string? _transitionSingleFilePath;
    private string? _transitionFolderPath;
    private string? _lastOutputPath;
    private bool _isRunning;

    public MainPage()
    {
        InitializeComponent();
        _audioService.Log += OnLog;
        _audioService.ProgressChanged += OnProgressChanged;
        ClipList.BindingContext = _clips;
        _clips.CollectionChanged += OnClipsCollectionChanged;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        VersionLabel.Text = $"Klub100 Generator v{version}";
        Title = $"Klub100 Generator v{version}";
        Log($"Klub100 Generator v{version} started.");
    }

    #region Logging & Progress

    private void OnLog(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}\n";

            if (IsWarningOrError(message))
                WarnErrorTerminal.Text += line;
            else
                LogTerminal.Text += line;
        });
    }

    private void OnProgressChanged(int current, int total)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressBar.Progress = total > 0 ? (double)current / total : 0;
            ProgressLabel.Text = total > 0 ? $"{current}/{total}" : "";
        });
    }

    private void Log(string message) => OnLog(message);

    private static bool IsWarningOrError(string message)
    {
        var lower = message.ToLowerInvariant();
        return lower.Contains("warn") || lower.Contains("error") ||
               lower.Contains("fail") || lower.Contains("exception");
    }

    private void OnClipsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (ClipInfo clip in e.OldItems)
                clip.PropertyChanged -= OnClipPropertyChanged;

        if (e.NewItems != null)
            foreach (ClipInfo clip in e.NewItems)
                clip.PropertyChanged += OnClipPropertyChanged;

        UpdateStatusSummary();
    }

    private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipInfo.Status) || e.PropertyName == nameof(ClipInfo.ErrorMessage))
            UpdateStatusSummary();
    }

    private void UpdateStatusSummary()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_clips.Count == 0)
            {
                ClipCountLabel.Text = "";
                StatusSummaryLabel.Text = "";
                return;
            }

            ClipCountLabel.Text = $"({_clips.Count} clips)";

            var pending = _clips.Count(c => c.Status == ClipStatus.Pending);
            var downloading = _clips.Count(c => c.Status == ClipStatus.Downloading || c.Status == ClipStatus.Trimming);
            var downloaded = _clips.Count(c => c.Status == ClipStatus.Downloaded);
            var trimmed = _clips.Count(c => c.Status == ClipStatus.Trimmed);
            var failed = _clips.Count(c => c.Status == ClipStatus.Failed);

            var parts = new List<string>();
            if (pending > 0) parts.Add($"{pending} pending");
            if (downloading > 0) parts.Add($"{downloading} in progress");
            if (downloaded > 0) parts.Add($"{downloaded} downloaded");
            if (trimmed > 0) parts.Add($"{trimmed} trimmed");
            if (failed > 0) parts.Add($"{failed} failed");

            StatusSummaryLabel.Text = string.Join(", ", parts);
        });
    }

    #endregion

    #region Button State

    private void SetRunning(bool running)
    {
        _isRunning = running;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectCsvBtn.IsEnabled = !running;
            FetchTitlesBtn.IsEnabled = !running;
            DownloadBtn.IsEnabled = !running;
            TrimBtn.IsEnabled = !running;
            MergeBtn.IsEnabled = !running;
            RunAllBtn.IsEnabled = !running;
            ShuffleBtn.IsEnabled = !running;
        });
    }

    #endregion

    #region CSV Selection

    private async void OnSelectCsvClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;

        try
        {
            var csvTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".csv" } },
                { DevicePlatform.macOS, new[] { "public.comma-separated-values-text" } },
                { DevicePlatform.iOS, new[] { "public.comma-separated-values-text" } },
                { DevicePlatform.Android, new[] { "text/csv" } },
            });

            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a CSV file",
                FileTypes = csvTypes
            });

            if (result == null)
            {
                Log("File picking cancelled.");
                return;
            }

            _selectedCsvPath = result.FullPath;
            SelectedFileLabel.Text = $"Selected: {result.FileName}";
            _audioService.BasePath = Path.GetDirectoryName(_selectedCsvPath) ?? string.Empty;
            Log($"Selected file: {result.FileName}");

            _clips.Clear();
            var clips = await _audioService.ParseCsvAsync(_selectedCsvPath);
            foreach (var clip in clips)
                _clips.Add(clip);

            Log($"Loaded {clips.Count} clips from CSV.");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
    }

    private async void OnFetchTitlesClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (_clips.Count == 0)
        {
            Log("No clips loaded. Please choose a CSV file first.");
            return;
        }

        SetRunning(true);
        try
        {
            Log("Fetching video titles...");
            await _audioService.FetchTitlesAsync(_clips.ToList(), _cookiesPath);
        }
        catch (Exception ex)
        {
            Log($"Error fetching titles: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    #endregion

    #region Transition Settings

    private void OnTransitionModeChanged(object sender, EventArgs e)
    {
        var index = TransitionModePicker.SelectedIndex;
        TransitionFileBtn.IsVisible = index == 1;
        TransitionFolderBtn.IsVisible = index == 2;
    }

    private async void OnPickTransitionFileClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a transition audio file"
            });

            if (result != null)
            {
                _transitionSingleFilePath = result.FullPath;
                TransitionFileLabel.Text = result.FileName;
                Log($"Transition file: {result.FileName}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error picking transition file: {ex.Message}");
        }
    }

    private async void OnPickTransitionFolderClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Pick any audio file from your transition folder"
            });

            if (result != null)
            {
                var dir = Path.GetDirectoryName(result.FullPath);
                _transitionFolderPath = dir;
                TransitionFileLabel.Text = dir;
                Log($"Transition folder: {dir}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error picking transition folder: {ex.Message}");
        }
    }

    private TransitionSettings BuildTransitionSettings()
    {
        var mode = TransitionModePicker.SelectedIndex switch
        {
            1 => TransitionMode.SingleFile,
            2 => TransitionMode.RandomFolder,
            _ => TransitionMode.None
        };

        return new TransitionSettings
        {
            Mode = mode,
            SingleFilePath = _transitionSingleFilePath,
            FolderPath = _transitionFolderPath,
            AddAtStart = AddAtStartCheck.IsChecked,
            AddAtEnd = AddAtEndCheck.IsChecked
        };
    }

    #endregion

    #region Cookies

    private async void OnPickCookiesClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a cookies.txt file"
            });

            if (result != null)
            {
                _cookiesPath = result.FullPath;
                CookiesLabel.Text = result.FileName;
                Log($"Cookies file: {result.FileName}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error picking cookies file: {ex.Message}");
        }
    }

    private async void OnCookiesHelpClicked(object sender, EventArgs e)
    {
        await DisplayAlert("How to get a cookies.txt file",
            "A cookies.txt file lets yt-dlp access age-restricted or members-only videos.\n\n" +
            "Easiest method (Firefox):\n" +
            "1. Install the extension 'Get cookies.txt LOCALLY'\n" +
            "2. Log in to YouTube in Firefox\n" +
            "3. Click the extension icon and select 'Export'\n" +
            "4. Save the file as 'cookies.txt'\n\n" +
            "Alternative (Chrome):\n" +
            "1. Install 'Get cookies.txt LOCALLY' from Chrome Web Store\n" +
            "2. Log in to YouTube, click the extension, export\n\n" +
            "The file is only needed for age-restricted videos. Most clips work without it.",
            "Got it");
    }

    #endregion

    #region Clip Reordering

    private void OnShuffleClicked(object sender, EventArgs e)
    {
        if (_isRunning || _clips.Count < 2) return;

        var scrollY = ClipScrollView.ScrollY;
        var list = _clips.ToList();
        var random = new Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        _clips.Clear();
        foreach (var clip in list)
            _clips.Add(clip);

        ClipScrollView.ScrollToAsync(0, scrollY, false);
        Log("Clips shuffled.");
    }

    private void OnMoveUpClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (sender is BindableObject bindable && bindable.BindingContext is ClipInfo clip)
        {
            var index = _clips.IndexOf(clip);
            if (index > 0)
            {
                var scrollY = ClipScrollView.ScrollY;
                _clips.Move(index, index - 1);
                ClipScrollView.ScrollToAsync(0, scrollY, false);
            }
        }
    }

    private void OnMoveDownClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (sender is BindableObject bindable && bindable.BindingContext is ClipInfo clip)
        {
            var index = _clips.IndexOf(clip);
            if (index < _clips.Count - 1)
            {
                var scrollY = ClipScrollView.ScrollY;
                _clips.Move(index, index + 1);
                ClipScrollView.ScrollToAsync(0, scrollY, false);
            }
        }
    }

    #endregion

    #region Actions

    private int GetClipLength()
    {
        if (int.TryParse(ClipLengthEntry.Text, out var length) && length > 0)
            return length;
        return 60;
    }

    private async void OnDownloadClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (string.IsNullOrEmpty(_selectedCsvPath))
        {
            Log("No CSV file selected. Please choose a CSV file first.");
            return;
        }

        SetRunning(true);
        try
        {
            await _audioService.DownloadAsync(_clips.ToList(), _cookiesPath);
        }
        catch (Exception ex)
        {
            Log($"Error during download: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private async void OnTrimClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (_clips.Count == 0)
        {
            Log("No clips loaded. Please choose a CSV file first.");
            return;
        }

        SetRunning(true);
        try
        {
            var clipLength = GetClipLength();
            await _audioService.TrimAsync(_clips.ToList(), clipLength);
        }
        catch (Exception ex)
        {
            Log($"Error during trim: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private async void OnMergeClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (_clips.Count == 0)
        {
            Log("No clips loaded. Please choose a CSV file first.");
            return;
        }

        SetRunning(true);
        try
        {
            await DoMerge();
        }
        catch (Exception ex)
        {
            Log($"Error during merge: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private async void OnRunAllClicked(object sender, EventArgs e)
    {
        if (_isRunning) return;
        if (string.IsNullOrEmpty(_selectedCsvPath))
        {
            Log("No CSV file selected. Please choose a CSV file first.");
            return;
        }

        SetRunning(true);
        try
        {
            Log("Step 1/3: Downloading...");
            await _audioService.DownloadAsync(_clips.ToList(), _cookiesPath);

            Log("Step 2/3: Trimming...");
            var clipLength = GetClipLength();
            await _audioService.TrimAsync(_clips.ToList(), clipLength);

            Log("Step 3/3: Merging...");
            await DoMerge();

            Log("All steps complete!");
        }
        catch (Exception ex)
        {
            Log($"Error during run all: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private async Task DoMerge()
    {
        var csvDir = Path.GetDirectoryName(_selectedCsvPath);
        var outputFile = Path.Combine(csvDir ?? string.Empty, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
        Log($"Merging to: {outputFile}");

        var transitions = BuildTransitionSettings();
        await _audioService.MergeAsync(_clips.ToList(), transitions, outputFile);

        _lastOutputPath = outputFile;
        OutputLabel.Text = $"Output: {Path.GetFileName(outputFile)}";
        OpenOutputBtn.IsVisible = true;
    }

    private async void OnOpenOutputFolderClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastOutputPath)) return;

        var folder = Path.GetDirectoryName(_lastOutputPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        try
        {
            await Launcher.Default.OpenAsync(folder);
        }
        catch (Exception ex)
        {
            Log($"Error opening folder: {ex.Message}");
        }
    }

    #endregion
}
