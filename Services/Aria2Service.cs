using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GreedyDownloader.Services;

public class Aria2Service : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DownloadProcess> _downloads = new();
    private bool _disposed;
    private string? _enginePath;

    public bool IsRunning => !_disposed && GetEnginePath() != null;

    public Task<bool> StartAsync()
    {
        _enginePath = GetEnginePath();
        var ready = _enginePath != null;
        AppLogger.Info(ready
            ? $"Download engine mode ready: {_enginePath}"
            : "Download engine mode failed: executable unavailable");
        return Task.FromResult(ready);
    }

    public Task<string> AddUriAsync(string uri, string? downloadDir = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Aria2Service));

        _enginePath ??= GetEnginePath();
        if (_enginePath == null)
            throw new InvalidOperationException("Download engine is unavailable.");

        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("A download URL is required.", nameof(uri));

        var directory = string.IsNullOrWhiteSpace(downloadDir)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : downloadDir;

        Directory.CreateDirectory(directory);

        var gid = Guid.NewGuid().ToString("N")[..16];
        var item = new DownloadProcess(gid, uri.Trim(), directory);

        StartDownloadProcess(item);

        lock (_sync)
            _downloads[gid] = item;

        AppLogger.Info($"Queued direct engine process download {gid}: {uri}");
        return Task.FromResult(JsonSerializer.Serialize(new RpcEnvelope<string>(gid)));
    }

    public Task<string> PauseAsync(string gid)
    {
        lock (_sync)
        {
            if (_downloads.TryGetValue(gid, out var item))
            {
                KillProcess(item);
                item.Status = "paused";
                item.CompletedAt = null;
                AppLogger.Info($"Paused download {gid}");
            }
        }

        return Task.FromResult(Ok());
    }

    public Task<string> UnpauseAsync(string gid)
    {
        lock (_sync)
        {
            if (_downloads.TryGetValue(gid, out var item) && item.Status == "paused")
            {
                StartDownloadProcess(item);
                AppLogger.Info($"Resumed download {gid}");
            }
        }

        return Task.FromResult(Ok());
    }

    public Task<string> RemoveAsync(string gid) => ForceRemoveAsync(gid);

    public Task<string> ForceRemoveAsync(string gid)
    {
        lock (_sync)
        {
            if (_downloads.Remove(gid, out var item))
            {
                KillProcess(item);
                item.Status = "removed";
                item.CompletedAt = DateTimeOffset.Now;
                AppLogger.Info($"Removed download {gid}");
            }
        }

        return Task.FromResult(Ok());
    }

    public Task<string> RemoveDownloadResultAsync(string gid)
    {
        lock (_sync)
            _downloads.Remove(gid);

        return Task.FromResult(Ok());
    }

    public Task<string> GetGlobalStatAsync()
    {
        RefreshProcessStates();

        int active;
        int stopped;
        lock (_sync)
        {
            active = _downloads.Values.Count(x => x.Status == "active");
            stopped = _downloads.Values.Count(x => x.Status is "complete" or "error");
        }

        var result = new
        {
            downloadSpeed = "0",
            uploadSpeed = "0",
            numActive = active.ToString(),
            numWaiting = "0",
            numStopped = stopped.ToString()
        };

        return Task.FromResult(JsonSerializer.Serialize(new RpcEnvelope<object>(result)));
    }

    public Task<string> TellActiveAsync()
    {
        RefreshProcessStates();
        return Task.FromResult(BuildItemsJson(x => x.Status == "active"));
    }

    public Task<string> TellWaitingAsync(int offset, int num)
    {
        RefreshProcessStates();
        return Task.FromResult(JsonSerializer.Serialize(new RpcEnvelope<object[]>(Array.Empty<object>())));
    }

    public Task<string> TellStoppedAsync(int offset, int num)
    {
        RefreshProcessStates();
        return Task.FromResult(BuildItemsJson(x => x.Status is "paused" or "complete" or "error"));
    }

    public Task<string> PurgeDownloadResultAsync()
    {
        lock (_sync)
        {
            foreach (var gid in _downloads
                         .Where(x => x.Value.Status is "complete" or "error" or "removed")
                         .Select(x => x.Key)
                         .ToList())
            {
                _downloads.Remove(gid);
            }
        }

        return Task.FromResult(Ok());
    }

    public Task GracefulShutdownAsync()
    {
        StopAllProcesses();
        return Task.CompletedTask;
    }

    private async void StartDownloadProcess(DownloadProcess item)
    {
        item.Status = "active";
        item.CompletedAt = null;
        item.Cts = new System.Threading.CancellationTokenSource();

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            
            using var response = await client.GetAsync(item.Uri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, item.Cts.Token);
            response.EnsureSuccessStatusCode();

            // Try to extract filename from Content-Disposition
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar 
                           ?? response.Content.Headers.ContentDisposition?.FileName 
                           ?.Trim('"');

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = Path.GetFileName(new Uri(item.Uri).LocalPath);
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"download-{item.Gid}";
            }

            item.FileName = fileName;
            item.TotalLength = response.Content.Headers.ContentLength ?? 0;

            var filePath = Path.Combine(item.Directory, fileName);
            using var stream = await response.Content.ReadAsStreamAsync(item.Cts.Token);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, item.Cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, item.Cts.Token);
                item.CompletedLength += read;
                
                // Track simple speed based on time
                var now = DateTimeOffset.Now;
                var elapsed = (now - item.LastSpeedUpdate).TotalSeconds;
                if (elapsed >= 1.0)
                {
                    item.DownloadSpeed = (long)((item.CompletedLength - item.LastCompletedLength) / elapsed);
                    item.LastCompletedLength = item.CompletedLength;
                    item.LastSpeedUpdate = now;
                }
            }

            lock (_sync)
            {
                if (item.Status == "active")
                {
                    item.Status = "complete";
                    item.CompletedAt = DateTimeOffset.Now;
                    item.DownloadSpeed = 0;
                }
            }
            AppLogger.Info($"Download {item.Gid} completed successfully.");
        }
        catch (OperationCanceledException)
        {
            AppLogger.Info($"Download {item.Gid} cancelled.");
            item.DownloadSpeed = 0;
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                item.Status = "error";
                item.CompletedAt = DateTimeOffset.Now;
                item.DownloadSpeed = 0;
            }
            AppLogger.Error($"Download {item.Gid} error", ex);
        }
    }

    private string BuildItemsJson(Func<DownloadProcess, bool> predicate)
    {
        List<object> result;
        lock (_sync)
        {
            result = _downloads.Values
                .Where(predicate)
                .Select(ToEngineLikeItem)
                .ToList();
        }

        return JsonSerializer.Serialize(new RpcEnvelope<List<object>>(result));
    }

    private object ToEngineLikeItem(DownloadProcess item)
    {
        var fileName = item.FileName ?? GetDisplayFileName(item);
        var filePath = Path.Combine(item.Directory, fileName);

        return new
        {
            gid = item.Gid,
            status = item.Status,
            totalLength = item.TotalLength.ToString(),
            completedLength = item.CompletedLength.ToString(),
            downloadSpeed = item.DownloadSpeed.ToString(),
            files = new[]
            {
                new
                {
                    path = filePath,
                    uris = new[]
                    {
                        new { uri = item.Uri }
                    }
                }
            }
        };
    }

    private static string GetDisplayFileName(DownloadProcess item)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(item.Uri).LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
        }
        catch
        {
            // Fall through to a stable generated name.
        }

        return $"download-{item.Gid}";
    }

    private void RefreshProcessStates()
    {
        // No-op for HttpClient logic
    }

    private static void KillProcess(DownloadProcess item)
    {
        if (item.Cts != null)
        {
            try
            {
                item.Cts.Cancel();
                item.Cts.Dispose();
            }
            catch { }
            item.Cts = null;
        }
    }

    private void StopAllProcesses()
    {
        lock (_sync)
        {
            foreach (var item in _downloads.Values)
                KillProcess(item);
        }
    }

    private static string? GetEnginePath()
    {
        var exeName = "aria2c.exe";
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
        if (File.Exists(localPath)) return localPath;
        
        var assemblyPath = AppContext.BaseDirectory;
        if (assemblyPath != null)
        {
            var combinedPath = Path.Combine(assemblyPath, exeName);
            if (File.Exists(combinedPath)) return combinedPath;
        }

        return null;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string Ok() => JsonSerializer.Serialize(new RpcEnvelope<string>("OK"));

    public void Stop() => StopAllProcesses();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAllProcesses();
    }

    private sealed class DownloadProcess
    {
        public DownloadProcess(string gid, string uri, string directory)
        {
            Gid = gid;
            Uri = uri;
            Directory = directory;
            LastSpeedUpdate = DateTimeOffset.Now;
        }

        public string Gid { get; }
        public string Uri { get; }
        public string Directory { get; }
        public string Status { get; set; } = "waiting";
        public System.Threading.CancellationTokenSource? Cts { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? FileName { get; set; }
        public long TotalLength { get; set; }
        public long CompletedLength { get; set; }
        public long LastCompletedLength { get; set; }
        public long DownloadSpeed { get; set; }
        public DateTimeOffset LastSpeedUpdate { get; set; }
    }

    private sealed class RpcEnvelope<T>
    {
        public RpcEnvelope(T result)
        {
            Result = result;
        }

        [JsonPropertyName("result")]
        public T Result { get; init; }

        [JsonPropertyName("jsonrpc")]
        public string Jsonrpc { get; init; } = "2.0";

        [JsonPropertyName("id")]
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
    }
}
