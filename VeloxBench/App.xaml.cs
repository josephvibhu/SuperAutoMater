using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace VeloxBench;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    static App()
    {
        try
        {
            // Ensure single-file extraction is always routed to the host machine's fast local SSD (%TEMP%)
            // rather than writing back to slow USB flash storage
            var tempExtract = Path.Combine(Path.GetTempPath(), "VeloxBench", "Bundle");
            Directory.CreateDirectory(tempExtract);
            Environment.SetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR", tempExtract);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Optimize WPF rendering tier: if software rendering is forced or old iGPU, maintain smooth 60fps
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        }
        catch { }
    }
}

