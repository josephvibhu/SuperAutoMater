using System;
using System.IO;
using System.Linq;
using System.Windows;
using SuperManager.Services;

namespace SuperManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Start background services
                FleetDiscoveryService.Instance.Start();
                ManagerWebServer.Instance.Start();

                var mainWin = new Views.MainWindow();
                MainWindow = mainWin;

                if (e.Args.Contains("--smoke-test"))
                {
                    mainWin.Loaded += (s, ev) =>
                    {
                        Console.WriteLine("[SUPERMANAGER SMOKE TEST SUCCESS] Loaded cleanly.");
                        Shutdown(0);
                    };
                }

                mainWin.Show();
            }
            catch (Exception ex)
            {
                string msg = $"Fatal Startup Error:\n[{ex.GetType().Name}] {ex.Message}\n{ex.StackTrace}";
                Console.WriteLine(msg);
                try
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manager_startup_error.log"), msg);
                }
                catch { }

                if (!e.Args.Contains("--smoke-test"))
                {
                    MessageBox.Show(msg, "SuperManager Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                FleetDiscoveryService.Instance.Stop();
                ManagerWebServer.Instance.Stop();
            }
            catch { }
            base.OnExit(e);
        }
    }
}
