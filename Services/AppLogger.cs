using System;
using System.IO;

namespace GreedyDownloader.Services;

public static class AppLogger
{
    private static readonly object Sync = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GreedyDownloader");

    public static string LogPath { get; } = Path.Combine(DirectoryPath, "app.log");

    public static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Info("Application starting");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTimeOffset.Now:O}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
