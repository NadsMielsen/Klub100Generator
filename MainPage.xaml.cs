using Microsoft.Maui.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Klub100Generator
{
	public partial class MainPage : ContentPage
	{
	private readonly AudioGeneratorService _audioService = new AudioGeneratorService();
	private string? _selectedCsvPath;

		public MainPage()
		{
			InitializeComponent();
			_audioService.Log += Log;
			Log("Application started.");
		}


		private async void OnGenerateClicked(object sender, EventArgs e)
		{
			if (string.IsNullOrEmpty(_selectedCsvPath))
			{
				// Prompt for file if not already selected
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
					}
					else
					{
						Log("File picking cancelled.");
						return;
					}
				}
				catch (Exception ex)
				{
					Log($"Error picking file: {ex.Message}");
					return;
				}
			}

			// Run the workflow
			await RunAudioGenerationWorkflow(_selectedCsvPath);
		}

		private async Task RunAudioGenerationWorkflow(string csvPath)
		{
			try
			{
				Log("Parsing CSV...");
				var (urls, timeStamps) = await _audioService.ParseCsvAsync(csvPath);
				var basePath = Path.GetDirectoryName(csvPath) ?? Environment.CurrentDirectory;
				Log($"Found {urls.Count} URLs.");

				Log("Downloading songs...");
				await _audioService.DownloadSongsAsync(urls, basePath);

				Log("Cutting audio...");
				await _audioService.CutAudioAsync(basePath, timeStamps);

				Log("Select output location for merged audio...");
				string outputFilePath = string.Empty;
#if WINDOWS
				try
				{
					var folderPicker = new Windows.Storage.Pickers.FolderPicker();
					var hwnd = ((MauiWinUIWindow)Microsoft.Maui.Controls.Application.Current.Windows[0].Handler.PlatformView).WindowHandle;
					WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
					folderPicker.FileTypeFilter.Add("*");
					var folder = await folderPicker.PickSingleFolderAsync();
					if (folder != null)
					{
						var outputFolderPath = folder.Path;
						outputFilePath = Path.Combine(outputFolderPath, $"klub100_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
						Log($"Output will be saved in: {outputFilePath}");
					}
					else
					{
						Log("Output folder selection cancelled.");
						return;
					}
				}
				catch (Exception ex)
				{
					Log($"Error selecting output folder: {ex.Message}");
					return;
				}
#elif MACCATALYST || MACOS
				try
				{
					var result = await FilePicker.PickAsync(new PickOptions
					{
						PickerTitle = "Select a folder (pick any file inside the desired folder)",
						FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
						{
							{ DevicePlatform.macOS, new[] { "*" } },
							{ DevicePlatform.MacCatalyst, new[] { "*" } }
						})
					});
					if (result != null)
					{
						var outputFolderPath = Path.GetDirectoryName(result.FullPath);
						outputFilePath = Path.Combine(outputFolderPath, $"klub100_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
						Log($"Output will be saved to: {outputFilePath}");
					}
					else
					{
						Log("Output folder selection cancelled.");
						return;
					}
				}
				catch (Exception ex)
				{
					Log($"Error selecting output folder: {ex.Message}");
					return;
				}
#else
				Log("Folder picking is only supported on Windows and file save on macOS. Using default output folder.");
				var outputFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				outputFilePath = Path.Combine(outputFolderPath, $"klub100_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
#endif

				Log("Merging audio...");
				await _audioService.MergeAsync(basePath, outputFilePath);

				Log("All done!");
			}
			catch (Exception ex)
			{
				Log($"Error in workflow: {ex.Message}");
			}
		}

		private void Log(string message)
		{
			var timestamp = DateTime.Now.ToString("HH:mm:ss");
			if (LogTerminal != null)
				LogTerminal.Text += $"[{timestamp}] {message}\n";
		}
	}
}

