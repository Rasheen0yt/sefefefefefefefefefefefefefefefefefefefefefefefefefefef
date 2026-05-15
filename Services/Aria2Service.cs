using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GreedyDownloader.Services;

public class Aria2Service : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _rpcUrl = "http://127.0.0.1:6800/jsonrpc";
    private Process? _aria2Process;
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
            throw;
        }
    }

    public async Task<bool> StartAsync()
    {
        if (IsRunning) return true;

        try
        {
            // 1. Try to see if it's already running
            await CallRpcAsync("aria2.getVersion");
            IsRunning = true;
            return true;
        }
        catch
        {
            // 2. Start it ourselves
            return await LaunchEngineAsync();
        }
    }

    private async Task<bool> LaunchEngineAsync()
    {
        try
        {
            string enginePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Engine", "aria2c.exe");
            
            // Fallback for development/published paths
            if (!File.Exists(enginePath))
                enginePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aria2c.exe");

            if (!File.Exists(enginePath))
            {
                AppLogger.Error($"Engine not found at: {enginePath}");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = enginePath,
                Arguments = "--enable-rpc --rpc-listen-all=false --rpc-listen-port=6800 --max-connection-per-server=16 --split=16 --min-split-size=1M --daemon=false",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _aria2Process = Process.Start(startInfo);
            
            // Wait for it to wake up
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await Task.Delay(500);
                    await CallRpcAsync("aria2.getVersion");
                    IsRunning = true;
                    return true;
                }
                catch { /* Ignore and retry */ }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to launch engine", ex);
        }

        return false;
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

    public Task<string> PauseAsync(string gid) => CallRpcAsync("aria2.pause", gid);
    public Task<string> UnpauseAsync(string gid) => CallRpcAsync("aria2.unpause", gid);
    public Task<string> RemoveAsync(string gid) => CallRpcAsync("aria2.remove", gid);
    public Task<string> ForceRemoveAsync(string gid) => CallRpcAsync("aria2.forceRemove", gid);
    public Task<string> RemoveDownloadResultAsync(string gid) => CallRpcAsync("aria2.removeDownloadResult", gid);
    public Task<string> GetGlobalStatAsync() => CallRpcAsync("aria2.getGlobalStat");
    public Task<string> TellActiveAsync() => CallRpcAsync("aria2.tellActive");
    public Task<string> TellWaitingAsync(int offset, int num) => CallRpcAsync("aria2.tellWaiting", offset, num);
    public Task<string> TellStoppedAsync(int offset, int num) => CallRpcAsync("aria2.tellStopped", offset, num);
    public Task<string> PurgeDownloadResultAsync() => CallRpcAsync("aria2.purgeDownloadResult");

    public async Task GracefulShutdownAsync()
    {
        if (!IsRunning) return;
        try { await CallRpcAsync("aria2.shutdown"); } catch { }
        
        if (_aria2Process != null && !_aria2Process.HasExited)
        {
            _aria2Process.Kill();
        }
        IsRunning = false;
    }

    public void Stop() 
    {
        if (_aria2Process != null && !_aria2Process.HasExited)
        {
            _aria2Process.Kill();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _httpClient.Dispose();
    }
}