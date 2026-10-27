using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace VeloxManager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        VeloxManager.Services.ThemeService.Instance.Initialize();

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogAndReportCrash("Dispatcher Exception", e.Exception);
        e.Handled = true; // Prevents process termination
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogAndReportCrash("Fatal Domain Exception", ex);
        }
    }

    private static void LogAndReportCrash(string category, Exception ex)
    {
        var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}]\nMessage: {ex.Message}\nType: {ex.GetType().FullName}\nStack Trace:\n{ex.StackTrace}\n";
        if (ex.InnerException != null)
        {
            log += $"\nInner Exception: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}\n";
        }

        try
        {
            var crashFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(crashFile, log + "\n--------------------------------------------------\n\n");
        }
        catch { }

        MessageBox.Show(
            $"An unexpected error occurred ({category}):\n\n{ex.Message}\n\nDetails have been logged to crash.log.",
            "VeloxManager Notification",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
