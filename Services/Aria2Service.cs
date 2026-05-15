using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GreedyDownloader.Services;

public class Aria2Service : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl = "http://127.0.0.1:6800/jsonrpc";
    private bool _disposed;
    private int _requestId;

    public bool IsRunning { get; private set; }

    public Aria2Service()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    private async Task<string> CallRpcAsync(string method, params object[] parameters)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Aria2Service));

        _requestId++;
        var requestObj = new
        {
            jsonrpc = "2.0",
            id = _requestId.ToString(),
            method = method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(requestObj);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_rpcUrl, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"RPC Call Failed: {method}", ex);
            IsRunning = false;
            throw;
        }
    }

    public async Task<bool> StartAsync()
    {
        try
        {
            // Just ping the server to check if it's alive.
            await CallRpcAsync("aria2.getVersion");
            IsRunning = true;
            return true;
        }
        catch
        {
            IsRunning = false;
            return false;
        }
    }

    public Task<string> AddUriAsync(string uri, string? downloadDir = null)
    {
        var options = new System.Collections.Generic.Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(downloadDir))
        {
            options["dir"] = downloadDir;
        }

        return CallRpcAsync("aria2.addUri", new[] { uri }, options);
    }

    public Task<string> PauseAsync(string gid)
    {
        return CallRpcAsync("aria2.pause", gid);
    }

    public Task<string> UnpauseAsync(string gid)
    {
        return CallRpcAsync("aria2.unpause", gid);
    }

    public Task<string> RemoveAsync(string gid)
    {
        return CallRpcAsync("aria2.remove", gid);
    }

    public Task<string> ForceRemoveAsync(string gid)
    {
        return CallRpcAsync("aria2.forceRemove", gid);
    }

    public Task<string> RemoveDownloadResultAsync(string gid)
    {
        return CallRpcAsync("aria2.removeDownloadResult", gid);
    }

    public Task<string> GetGlobalStatAsync()
    {
        return CallRpcAsync("aria2.getGlobalStat");
    }

    public Task<string> TellActiveAsync()
    {
        return CallRpcAsync("aria2.tellActive");
    }

    public Task<string> TellWaitingAsync(int offset, int num)
    {
        return CallRpcAsync("aria2.tellWaiting", offset, num);
    }

    public Task<string> TellStoppedAsync(int offset, int num)
    {
        return CallRpcAsync("aria2.tellStopped", offset, num);
    }

    public Task<string> PurgeDownloadResultAsync()
    {
        return CallRpcAsync("aria2.purgeDownloadResult");
    }

    public Task GracefulShutdownAsync()
    {
        if (!IsRunning) return Task.CompletedTask;
        try
        {
            return CallRpcAsync("aria2.shutdown");
        }
        catch
        {
            return Task.CompletedTask; // Best effort
        }
    }

    public void Stop() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}