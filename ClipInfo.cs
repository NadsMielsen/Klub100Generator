using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;

namespace Klub100Generator;

public class ClipInfo : INotifyPropertyChanged
{
    private string? _title;
    private ClipStatus _status = ClipStatus.Pending;
    private string? _errorMessage;

    public int Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;

    public string? Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? $"Clip {Id}" : Title;

    public ClipStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string StatusText => Status switch
    {
        ClipStatus.Pending => "Pending",
        ClipStatus.Downloading => "Downloading...",
        ClipStatus.Downloaded => "Downloaded",
        ClipStatus.Trimming => "Trimming...",
        ClipStatus.Trimmed => "Trimmed",
        ClipStatus.Failed => $"Failed: {ErrorMessage}",
        _ => "Unknown"
    };

    public Color StatusColor => Status switch
    {
        ClipStatus.Pending => Colors.Gray,
        ClipStatus.Downloading => Color.FromArgb("2563EB"),
        ClipStatus.Downloaded => Color.FromArgb("F59E0B"),
        ClipStatus.Trimming => Color.FromArgb("2563EB"),
        ClipStatus.Trimmed => Color.FromArgb("16A34A"),
        ClipStatus.Failed => Color.FromArgb("DC2626"),
        _ => Colors.Gray
    };

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string? DownloadedFilePath { get; set; }
    public string? TrimmedFilePath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public enum ClipStatus
{
    Pending,
    Downloading,
    Downloaded,
    Trimming,
    Trimmed,
    Failed
}
