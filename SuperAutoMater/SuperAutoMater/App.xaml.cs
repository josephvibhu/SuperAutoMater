using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using SuperAutoMater.Wpf.Core;

namespace SuperAutoMater.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            AppLogger.Info("Lifecycle", $"SuperAutoMater v1.6.3 starting on {Environment.MachineName} ({Environment.OSVersion}) [Elevated: {Environment.IsPrivilegedProcess}].");

            try
            {
                var mainWin = new Views.MainWindow();
                MainWindow = mainWin;

                if (e.Args.Contains("--smoke-test"))
                {
                    mainWin.Loaded += (s, ev) =>
                    {
                        Console.WriteLine("[WPF SMOKE TEST SUCCESS] MainWindow loaded with 0 XAML parsing errors.");
                        Shutdown(0);
                    };
                }

                mainWin.Show();
            }
            catch (Exception ex)
            {
                LogFatalCrash(ex);
                if (!e.Args.Contains("--smoke-test"))
                {
                    string details = GetExceptionDetails(ex);
                    MessageBox.Show($"Startup Exception:\n{details}", "SuperAutoMater Startup Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Shutdown(1);
            }
        }

        private bool _isHandlingException = false;
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (_isHandlingException) return;
            _isHandlingException = true;
            try
            {
                LogFatalCrash(e.Exception);
                string details = GetExceptionDetails(e.Exception);
                MessageBox.Show($"Runtime Exception:\n{details}", "SuperAutoMater Runtime Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            }
            finally
            {
                _isHandlingException = false;
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogFatalCrash(ex);
                string details = GetExceptionDetails(ex);
                MessageBox.Show($"Fatal Exception:\n{details}", "SuperAutoMater Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetExceptionDetails(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            var curr = ex;
            while (curr != null)
            {
                sb.AppendLine($"[{curr.GetType().Name}] {curr.Message}");
                curr = curr.InnerException;
            }
            sb.AppendLine();
            sb.AppendLine("Stack Trace:");
            sb.AppendLine(ex.StackTrace);
            return sb.ToString();
        }

        private void LogFatalCrash(Exception ex)
        {
            try
            {
                AppLogger.Error("Lifecycle", "Fatal crash or unhandled exception encountered.", ex);
                string details = GetExceptionDetails(ex);
                Console.WriteLine($"[FATAL CRASH]\n{details}");
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{details}\n----------------------------------------\n\n");
            }
            catch { }
        }
    }
}
