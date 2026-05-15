using Avalonia;
using GreedyDownloader.Services;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GreedyDownloader;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppLogger.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLogger.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var logPath = WriteCrashLog(ex);
            ShowStartupError(logPath, ex);
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static string WriteCrashLog(Exception exception)
    {
        AppLogger.Error("Startup crash", exception);
        return AppLogger.LogPath;
    }

    private static void ShowStartupError(string logPath, Exception exception)
    {
        var message = "GreedyDownloader could not start." +
            Environment.NewLine + Environment.NewLine +
            exception.Message +
            Environment.NewLine + Environment.NewLine +
            "Log file:" +
            Environment.NewLine +
            logPath;

        _ = MessageBox(IntPtr.Zero, message, "GreedyDownloader", 0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
