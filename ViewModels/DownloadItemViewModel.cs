using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GreedyDownloader.Helpers;
using GreedyDownloader.Services;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace GreedyDownloader.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    private readonly Aria2Service? _engineService;
    private readonly Func<Task>? _requestRefresh;

    public DownloadItemViewModel() { }

    public DownloadItemViewModel(Aria2Service engineService, Func<Task> requestRefresh)
    {
        _engineService = engineService;
        _requestRefresh = requestRefresh;
    }

    [ObservableProperty]
    private string _gid = string.Empty;

    [ObservableProperty]
    private string _fileName = "Unknown File";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _totalLengthText = "0 B";

    [ObservableProperty]
    private string _completedLengthText = "0 B";

    [ObservableProperty]
    private string _status = "waiting";

    [ObservableProperty]
    private string _statusDisplay = "Waiting";

    [ObservableProperty]
    private string _statusColor = "#888888";

    [ObservableProperty]
    private string _progressText = "0%";

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private string _fileIconGlyph = "FILE";

    private long _totalBytes;
    private long _completedBytes;
    private long _speedBytes;

    public bool IsActive => Status == "active";
    public bool IsPaused => Status == "paused";
    public bool IsComplete => Status == "complete";
    public bool IsError => Status == "error";
    public bool IsWaiting => Status == "waiting";
    public bool CanPause => Status is "active" or "waiting";
    public bool CanResume => Status == "paused";
    public bool CanRetry => Status == "error";
    public bool IsTransferring => Status is "active";
    public bool ShowSpeed => Status == "active" && _speedBytes > 0;

    public void UpdateFromEngine(JsonNode node)
    {
        Gid = node["gid"]?.ToString() ?? "";

        var newStatus = node["status"]?.ToString() ?? "waiting";
        Status = newStatus;
        UpdateStatusDisplay(newStatus);

        // File name
        var files = node["files"] as JsonArray;
        if (files is { Count: > 0 })
        {
            var path = files[0]?["path"]?.ToString();
            if (!string.IsNullOrEmpty(path))
            {
                FileName = Path.GetFileName(path);
            }
            else
            {
                var uris = files[0]?["uris"] as JsonArray;
                if (uris is { Count: > 0 })
                {
                    var uri = uris[0]?["uri"]?.ToString();
                    if (!string.IsNullOrEmpty(uri))
                    {
                        try { FileName = Path.GetFileName(new Uri(uri).LocalPath); }
                        catch { FileName = uri.Length > 60 ? uri[..57] + "..." : uri; }
                        if (string.IsNullOrEmpty(FileName)) FileName = uri;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(FileName)) FileName = "Unknown File";

        // Set icon based on extension
        FileIconGlyph = GetFileIcon(FileName);

        // Size tracking
        if (long.TryParse(node["totalLength"]?.ToString(), out long total))
        {
            _totalBytes = total;
            TotalLengthText = ByteFormatter.Format(total);
        }

        if (long.TryParse(node["completedLength"]?.ToString(), out long completed))
        {
            _completedBytes = completed;
            CompletedLengthText = ByteFormatter.Format(completed);
            if (_totalBytes > 0)
            {
                Progress = (double)completed / _totalBytes * 100;
                ProgressText = $"{Progress:F1}%";
            }
        }

        if (long.TryParse(node["downloadSpeed"]?.ToString(), out long speed))
        {
            _speedBytes = speed;
            DownloadSpeed = ByteFormatter.FormatSpeed(speed);

            // ETA calc
            if (speed > 0 && _totalBytes > _completedBytes)
            {
                var remainingBytes = _totalBytes - _completedBytes;
                var seconds = remainingBytes / speed;
                EtaText = FormatEta(seconds);
            }
            else
            {
                EtaText = "";
            }
        }

        NotifyStateChanged();
    }

    private void UpdateStatusDisplay(string status)
    {
        (StatusDisplay, StatusColor) = status switch
        {
            "active" => ("Downloading", "#4CAF50"),
            "paused" => ("Paused", "#FF9800"),
            "waiting" => ("Queued", "#2196F3"),
            "complete" => ("Complete", "#00C853"),
            "error" => ("Error", "#F44336"),
            "removed" => ("Removed", "#888888"),
            _ => (status, "#888888")
        };
    }

    private static string FormatEta(long totalSeconds)
    {
        if (totalSeconds < 0) return "";
        if (totalSeconds < 60) return $"{totalSeconds}s left";
        if (totalSeconds < 3600) return $"{totalSeconds / 60}m {totalSeconds % 60}s left";
        var hours = totalSeconds / 3600;
        var mins = (totalSeconds % 3600) / 60;
        return $"{hours}h {mins}m left";
    }

    private static string GetFileIcon(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "ARCH",
            ".exe" or ".msi" or ".bat" => "APP",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" => "VID",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" => "AUD",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "IMG",
            ".pdf" => "PDF",
            ".doc" or ".docx" or ".txt" or ".rtf" => "DOC",
            ".iso" or ".bin" or ".img" => "DISK",
            ".torrent" => "TOR",
            _ => "FILE"
        };
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsTransferring));
        OnPropertyChanged(nameof(ShowSpeed));
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (_engineService == null || string.IsNullOrEmpty(Gid)) return;
        await _engineService.PauseAsync(Gid);
        if (_requestRefresh != null) await _requestRefresh();
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (_engineService == null || string.IsNullOrEmpty(Gid)) return;
        await _engineService.UnpauseAsync(Gid);
        if (_requestRefresh != null) await _requestRefresh();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_engineService == null || string.IsNullOrEmpty(Gid)) return;

        if (Status is "complete" or "error" or "removed")
            await _engineService.RemoveDownloadResultAsync(Gid);
        else
            await _engineService.ForceRemoveAsync(Gid);

        if (_requestRefresh != null) await _requestRefresh();
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        // For error state, remove the failed item and let the user re-add it.
        if (_engineService == null || string.IsNullOrEmpty(Gid)) return;
        await _engineService.RemoveDownloadResultAsync(Gid);
        if (_requestRefresh != null) await _requestRefresh();
    }
}
