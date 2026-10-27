#nullable enable
using System;
using System.IO;
using System.Windows;
using VeloxHub.Services;

namespace VeloxHub;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogFatal("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"Velox Hub encountered an issue:\n\n{args.Exception?.Message}\n\nDetails saved to %localappdata%\\Velox\\hub_crash.log", "Velox Hub Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            ThemeService.Instance.Initialize("Dark");
        }
        catch { }

        base.OnStartup(e);
    }

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "hub_crash.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] [{source}] {ex?.ToString()}\n\n");
        }
        catch { }
    }
}
