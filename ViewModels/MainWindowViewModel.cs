using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GreedyDownloader.Helpers;
using GreedyDownloader.Models;
using GreedyDownloader.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GreedyDownloader.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Aria2Service _engineService;
    private readonly PasteScraper _pasteScraper;
    private DispatcherTimer? _timer;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    private Avalonia.Styling.ThemeVariant _currentThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

    [RelayCommand]
    private void ToggleTheme()
    {
        CurrentThemeVariant = CurrentThemeVariant == Avalonia.Styling.ThemeVariant.Dark 
            ? Avalonia.Styling.ThemeVariant.Light 
            : Avalonia.Styling.ThemeVariant.Dark;
        
        if (Avalonia.Application.Current != null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = CurrentThemeVariant;
        }

        System.Diagnostics.Debug.WriteLine($"Theme toggled to: {CurrentThemeVariant}");
        OnPropertyChanged(nameof(ThemeIcon));
    }

    public string ThemeIcon => CurrentThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "🌙" : "☀️";

    [ObservableProperty]
    private string _globalDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _globalUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _downloadDirectory = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Starting...";

    [ObservableProperty]
    private string _connectionColor = "#FF9800";

    [ObservableProperty]
    private DownloadCategory _selectedCategory = DownloadCategory.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotal))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActive))]
    private int _activeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPaused))]
    private int _pausedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompleted))]
    private int _completedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private int _errorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDownloads))]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusMessage = "";

    // Computed bools for sidebar badge visibility
    public bool HasTotal => TotalCount > 0;
    public bool HasActive => ActiveCount > 0;
    public bool HasPaused => PausedCount > 0;
    public bool HasCompleted => CompletedCount > 0;
    public bool HasErrors => ErrorCount > 0;
    public bool HasDownloads => !IsEmpty;
    public bool IsAllSelected => SelectedCategory == DownloadCategory.All;
    public bool IsActiveSelected => SelectedCategory == DownloadCategory.Active;
    public bool IsPausedSelected => SelectedCategory == DownloadCategory.Paused;
    public bool IsCompletedSelected => SelectedCategory == DownloadCategory.Completed;
    public bool IsErrorSelected => SelectedCategory == DownloadCategory.Error;

    // All downloads (master list)
    public ObservableCollection<DownloadItemViewModel> AllDownloads { get; } = new();

    // Filtered view
    public ObservableCollection<DownloadItemViewModel> FilteredDownloads { get; } = new();

    // For the View to handle folder picking
    public event Func<Task<string?>>? RequestDirectoryPick;

    public MainWindowViewModel()
    {
        _engineService = new Aria2Service();
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _pasteScraper = new PasteScraper(_httpClient);

        // Default download dir
        DownloadDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        // Start polling timer immediately so UI is responsive
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _timer.Tick += async (_, _) =>
        {
            try { await PollEngineAsync(); }
            catch { /* swallow poll errors */ }
        };
        _timer.Start();

        // Start engine on a background thread so the window appears immediately.
        Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        const int maxRetries = 5;
        int[] delaysMs = { 0, 1000, 2000, 4000, 8000 };

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (_disposed) return;

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ConnectionStatus = attempt == 0
                        ? "Starting engine..."
                        : $"Retrying ({attempt + 1}/{maxRetries})...";
                    ConnectionColor = "#FF9800";
                    IsConnected = false;
                });

                if (attempt > 0)
                    await Task.Delay(delaysMs[Math.Min(attempt, delaysMs.Length - 1)]);

                bool started = await _engineService.StartAsync();

                if (started)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsConnected = true;
                        ConnectionStatus = "Connected";
                        ConnectionColor = "#3FB950";
                        ShowStatus("Ready - paste a URL and hit Add");
                    });
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Engine start attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        // All retries exhausted
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ConnectionStatus = "Engine failed";
            ConnectionColor = "#F85149";
            ShowStatus("Download engine failed to start - retrying in background");
        });
    }

    partial void OnSelectedCategoryChanged(DownloadCategory value)
    {
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsActiveSelected));
        OnPropertyChanged(nameof(IsPausedSelected));
        OnPropertyChanged(nameof(IsCompletedSelected));
        OnPropertyChanged(nameof(IsErrorSelected));
        RefreshFilteredDownloads();
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        if (Enum.TryParse<DownloadCategory>(category, out var cat))
            SelectedCategory = cat;
    }

    [RelayCommand]
    private async Task ChooseDirectoryAsync()
    {
        if (RequestDirectoryPick != null)
        {
            var dir = await RequestDirectoryPick();
            if (!string.IsNullOrWhiteSpace(dir))
            {
                DownloadDirectory = dir;
                ShowStatus($"Save folder: {dir}");
            }
        }
    }

    [RelayCommand]
    private async Task AddDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;

        if (!IsConnected)
        {
            ShowStatus("Engine not connected - restarting...");
            var started = await _engineService.StartAsync();
            if (started)
            {
                IsConnected = true;
                ConnectionStatus = "Connected";
                ConnectionColor = "#3FB950";
            }
            else
            {
                ShowStatus("Cannot start download engine.");
                return;
            }
        }

        var input = UrlInput.Trim();
        UrlInput = string.Empty;

        try
        {
            var urlRegex = new Regex(
                @"https?://[^\s<>""']+",
                RegexOptions.Compiled);

            var matches = urlRegex.Matches(input);

            // Single URL: try paste scraping / link extraction.
            if (matches.Count == 1 && input.TrimEnd() == matches[0].Value)
            {
                ShowStatus("Analyzing link...");
                var extractedUrls = await _pasteScraper.ExtractUrlsAsync(input);

                if (extractedUrls.Count > 0)
                {
                    int added = 0;
                    foreach (var url in extractedUrls)
                    {
                        await _engineService.AddUriAsync(url, DownloadDirectory);
                        added++;
                    }
                    ShowStatus($"Found and queued {added} downloads");
                    await PollEngineAsync();
                    return;
                }

                // No links extracted - download the URL itself.
                await _engineService.AddUriAsync(input, DownloadDirectory);
                ShowStatus("Download added");
                await PollEngineAsync();
                return;
            }

            // Multiple URLs pasted as text.
            int addedCount = 0;
            if (matches.Count > 1)
            {
                foreach (Match match in matches)
                {
                    await _engineService.AddUriAsync(match.Value.TrimEnd('.', ',', ';'), DownloadDirectory);
                    addedCount++;
                }
            }
            else
            {
                // Not even a URL - let engine validate it and report a useful error.
                await _engineService.AddUriAsync(input, DownloadDirectory);
                addedCount = 1;
            }

            ShowStatus(addedCount == 1 ? "Download added" : $"{addedCount} downloads added");
            await PollEngineAsync();
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        if (!IsConnected) return;
        await _engineService.PurgeDownloadResultAsync();
        ShowStatus("Cleared completed downloads");
        await PollEngineAsync();
    }

    private int _reconnectCooldown;

    private async Task PollEngineAsync()
    {
            // Auto-reconnect if the engine is down.
        if (!IsConnected)
        {
            // Rate-limit reconnect attempts (every ~10 poll ticks = ~10 seconds)
            _reconnectCooldown++;
            if (_reconnectCooldown < 10) return;
            _reconnectCooldown = 0;

            try
            {
                var started = await _engineService.StartAsync();
                if (started)
                {
                    IsConnected = true;
                    ConnectionStatus = "Connected";
                    ConnectionColor = "#3FB950";
                    ShowStatus("Engine reconnected");
                }
                else
                {
                    ConnectionStatus = "Reconnecting...";
                    ConnectionColor = "#FF9800";
                }
            }
            catch { /* will retry next cycle */ }
            return;
        }

        try
        {
            // Global stats
            var statsJson = await _engineService.GetGlobalStatAsync();
            if (string.IsNullOrEmpty(statsJson))
            {
                // Engine went away mid-session
                IsConnected = false;
                ConnectionStatus = "Disconnected";
                ConnectionColor = "#F85149";
                ShowStatus("Engine disconnected - reconnecting...");
                _reconnectCooldown = 9; // try reconnect on next tick
                return;
            }

            var result = JsonNode.Parse(statsJson)?["result"];
            if (result != null)
            {
                if (long.TryParse(result["downloadSpeed"]?.ToString(), out long dlSpeed))
                    GlobalDownloadSpeed = ByteFormatter.FormatSpeed(dlSpeed);
                if (long.TryParse(result["uploadSpeed"]?.ToString(), out long ulSpeed))
                    GlobalUploadSpeed = ByteFormatter.FormatSpeed(ulSpeed);
            }

            // Gather all downloads
            var allItems = new System.Collections.Generic.List<JsonNode>();
            await CollectItems("active", allItems);
            await CollectItems("waiting", allItems);
            await CollectItems("stopped", allItems);

            // Sync
            var activeGids = allItems
                .Select(x => x?["gid"]?.ToString())
                .Where(g => g != null)
                .ToHashSet();

            var toRemove = AllDownloads.Where(d => !activeGids.Contains(d.Gid)).ToList();
            foreach (var item in toRemove)
                AllDownloads.Remove(item);

            foreach (var itemNode in allItems)
            {
                var gid = itemNode?["gid"]?.ToString();
                if (string.IsNullOrEmpty(gid)) continue;

                var existing = AllDownloads.FirstOrDefault(d => d.Gid == gid);
                if (existing != null)
                {
                    existing.UpdateFromEngine(itemNode!);
                }
                else
                {
                    var newItem = new DownloadItemViewModel(_engineService, PollEngineAsync);
                    newItem.UpdateFromEngine(itemNode!);
                    AllDownloads.Add(newItem);
                }
            }

            IsEmpty = AllDownloads.Count == 0;
            UpdateCounts();
            RefreshFilteredDownloads();
        }
        catch
        {
            // RPC call failed - engine might have crashed.
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            ConnectionColor = "#F85149";
            _reconnectCooldown = 9; // try reconnect on next tick
        }
    }

    private async Task CollectItems(string type, System.Collections.Generic.List<JsonNode> list)
    {
        string json = type switch
        {
            "active" => await _engineService.TellActiveAsync(),
            "waiting" => await _engineService.TellWaitingAsync(0, 200),
            "stopped" => await _engineService.TellStoppedAsync(0, 200),
            _ => ""
        };

        if (string.IsNullOrEmpty(json)) return;
        var arr = JsonNode.Parse(json)?["result"] as JsonArray;
        if (arr != null) list.AddRange(arr.OfType<JsonNode>());
    }

    private void UpdateCounts()
    {
        TotalCount = AllDownloads.Count;
        ActiveCount = AllDownloads.Count(d => d.IsActive || d.IsWaiting);
        PausedCount = AllDownloads.Count(d => d.IsPaused);
        CompletedCount = AllDownloads.Count(d => d.IsComplete);
        ErrorCount = AllDownloads.Count(d => d.IsError);
    }

    private void RefreshFilteredDownloads()
    {
        var filtered = SelectedCategory switch
        {
            DownloadCategory.Active => AllDownloads.Where(d => d.IsActive || d.IsWaiting).ToList(),
            DownloadCategory.Paused => AllDownloads.Where(d => d.IsPaused).ToList(),
            DownloadCategory.Completed => AllDownloads.Where(d => d.IsComplete).ToList(),
            DownloadCategory.Error => AllDownloads.Where(d => d.IsError).ToList(),
            _ => AllDownloads.ToList()
        };

        var toRemove = FilteredDownloads.Where(d => !filtered.Contains(d)).ToList();
        foreach (var item in toRemove)
            FilteredDownloads.Remove(item);

        for (int i = 0; i < filtered.Count; i++)
        {
            var item = filtered[i];
            var currentIndex = FilteredDownloads.IndexOf(item);
            
            if (currentIndex == -1)
            {
                FilteredDownloads.Insert(Math.Min(i, FilteredDownloads.Count), item);
            }
            else if (currentIndex != i)
            {
                FilteredDownloads.Move(currentIndex, i);
            }
        }
    }

    private int _statusMessageId;

    private async void ShowStatus(string message)
    {
        StatusMessage = message;
        var currentId = ++_statusMessageId;
        
        await Task.Delay(5000);
        
        if (_statusMessageId == currentId && StatusMessage == message)
        {
            StatusMessage = "";
        }
    }

    public async void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Stop();
        _timer = null;

        try { await _engineService.GracefulShutdownAsync(); }
        catch { /* Best effort */ }

        await Task.Delay(500);
        _engineService.Dispose();
        _httpClient.Dispose();
    }
}
