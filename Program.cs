using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                    string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", assemblyName);
                    if (File.Exists(libPath))
                    {
                        return Assembly.LoadFrom(libPath);
                    }
                }
                catch { }
                return null;
            };

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
            {
                LogCrash("UI Thread Exception", e.Exception);
                DarkMessageBox.Show($"An unexpected UI error occurred:\n\n{e.Exception.Message}\n\nA crash report has been saved to the Logs folder.", "Application Warning");
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                LogCrash("AppDomain Unhandled Exception", ex);
                MessageBox.Show($"A critical error occurred:\n\n{ex?.Message ?? "Unknown Error"}\n\nA crash dump has been saved to the Logs folder.",
                    "AutoMater Diagnostic - Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogCrash("Unobserved Background Task Exception", e.Exception);
                e.SetObserved();
            };

            Application.Run(new Form1());
        }

        private static void LogCrash(string category, Exception ex)
        {
            try
            {
                string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logsDir);

                string filename = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                string fullPath = Path.Combine(logsDir, filename);

                string content = $"========================================\n" +
                                 $"AUTOMATER QC DIAGNOSTIC TOOL - CRASH LOG\n" +
                                 $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                                 $"Category : {category}\n" +
                                 $"OS       : {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})\n" +
                                 $".NET     : {Environment.Version}\n" +
                                 $"========================================\n\n" +
                                 $"EXCEPTION DETAILS:\n{ex}\n";

                File.WriteAllText(fullPath, content);
            }
            catch { }
        }
    }
}