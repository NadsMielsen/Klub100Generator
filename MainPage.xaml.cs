

	using Microsoft.Maui.ApplicationModel;
	using Microsoft.Maui.Controls;
	using System;
	using System.Collections.Generic;
using System.Threading.Tasks;
using FFMpegCore;

	namespace Klub100Generator
	{
		public partial class MainPage : ContentPage
		{
			private readonly AudioGeneratorService _audioService = new AudioGeneratorService();
			private string? _selectedCsvPath;
			private string? _selectedOutputFolder;
			private List<string>? _lastTimeStamps;

		public MainPage()
		{
			InitializeComponent();
			_audioService.Log += Log;
			ConfigureFfmpegCore();
			Log("Application started.");
		}

		private async void OnMergeAudioClicked(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(_selectedCsvPath))
			{
				Log("No CSV file selected. Please choose a CSV file first.");
				return;
			}
			try
			{
				var csvDir = Path.GetDirectoryName(_selectedCsvPath);
				var outputFile = Path.Combine(csvDir ?? string.Empty, $"merged_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
				Log($"Merging cut files to: {outputFile}");
				await _audioService.MergeAsync(outputFile);
				Log($"Merge complete: {outputFile}");
			}
			catch (Exception ex)
			{
				Log($"Error merging audio: {ex.Message}");
			}
		}

		private void ConfigureFfmpegCore()
		{
			try
			{
				// Use ffmpeg.exe from the same folder as the selected CSV file (BasePath)
				var binPath = _audioService.BasePath;
				FFMpegCore.GlobalFFOptions.Configure(new FFMpegCore.FFOptions { BinaryFolder = binPath });
				Log($"FFmpeg binaries configured at: {binPath}");
			}
			catch (Exception ex)
			{
				Log($"Error configuring FFmpegCore: {ex.Message}");
			}
		}


		private async void OnSelectCsvClicked(object sender, EventArgs e)
		{
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

				if (result != null)
				{
					_selectedCsvPath = result.FullPath;
					SelectedFileLabel.Text = $"Selected: {result.FileName}";
					Log($"Selected file: {result.FileName}");

					// Update ffmpeg binary location to match CSV location
					_audioService.BasePath = Path.GetDirectoryName(_selectedCsvPath);
					ConfigureFfmpegCore();

					// Parse CSV and set _lastTimeStamps for trimming
					try
					{
						var (_, timeStamps) = await _audioService.ParseCsvAsync(_selectedCsvPath);
						_lastTimeStamps = timeStamps;
						Log($"Loaded {timeStamps.Count} timestamps from CSV.");
					}
					catch (Exception ex)
					{
						Log($"Error parsing CSV: {ex.Message}");
					}
				}
				else
				{
					Log("File picking cancelled.");
				}
			}
			catch (Exception ex)
			{
				Log($"Error picking file: {ex.Message}");
			}
		}

		private async void OnDownloadClicked(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(_selectedCsvPath))
			{
				Log("No CSV file selected. Please choose a CSV file first.");
				return;
			}
			await RunAudioGenerationWorkflow(_selectedCsvPath);
		}


		private void Log(string message)
		{
			var timestamp = DateTime.Now.ToString("HH:mm:ss");
			if (IsWarningOrError(message))
			{
				if (WarnErrorTerminal != null)
				{
					WarnErrorTerminal.Text += $"[{timestamp}] {message}\n";
					MainThread.BeginInvokeOnMainThread(() => {
						WarnErrorTerminal.CursorPosition = WarnErrorTerminal.Text?.Length ?? 0;
					});
				}
			}
			else
			{
				if (LogTerminal != null)
				{
					LogTerminal.Text += $"[{timestamp}] {message}\n";
					MainThread.BeginInvokeOnMainThread(() => {
						LogTerminal.CursorPosition = LogTerminal.Text?.Length ?? 0;
					});
				}
			}
		}

		private bool IsWarningOrError(string message)
		{
			if (string.IsNullOrWhiteSpace(message)) return false;
			var lower = message.ToLowerInvariant();
			return lower.Contains("warn") || lower.Contains("error") || lower.Contains("fail") || lower.Contains("exception");
		}

		private async Task RunAudioGenerationWorkflow(string csvPath)
		{
			try
			{
				Log("Parsing CSV...");
				var (urls, timeStamps) = await _audioService.ParseCsvAsync(csvPath);
				Log($"Found {urls.Count} URLs.");
				_lastTimeStamps = timeStamps;
				Log("Downloading songs...");
				await _audioService.DownloadSongsAsync(urls);
				Log("Download step complete. You can now trim or merge manually.");
			}
			catch (Exception ex)
			{
				Log($"Error in workflow: {ex.Message}");
			}
		}

			private async void OnTrimAudioClicked(object sender, EventArgs e)
			{
				if (_lastTimeStamps == null || _lastTimeStamps.Count == 0)
				{
					Log("No timestamps loaded. Please generate or load a CSV first.");
					return;
				}
				Log("Trimming audio files...");
				try
				{
					await _audioService.CutAudioAsync(_lastTimeStamps);
					Log("Audio files trimmed and saved to the trimmed folder.");
				}
				catch (Exception ex)
				{
					Log($"Error trimming audio: {ex.Message}");
				}
			}
		}
	}