using System;
using System.IO;
using System.Text.Json;

namespace VeloxBench.Core;

public sealed class BenchConfig
{
    public string ApiUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf";
    public string LicenseKey { get; set; } = "VLX-ALPHA-TEST-2026";
    public string TenantId { get; set; } = "ten-d2428a4cb3da";
    public string BenchName { get; set; } = Environment.MachineName;
    public string ManagerLanIp { get; set; } = "127.0.0.1";
    public int ManagerLanPort { get; set; } = 9000;
    public string UpdateChannel { get; set; } = "alpha";
    public bool AutoUpdateEnabled { get; set; } = true;
}

public static class BenchConfigService
{
    private static readonly string PortableConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private static readonly string LocalAppDataConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Velox", "VeloxBench", "config.json");

    private static BenchConfig? _current;
    private static readonly object _lock = new();

    public static BenchConfig Current
    {
        get
        {
            lock (_lock)
            {
                if (_current != null) return _current;
                _current = Load();
                return _current;
            }
        }
    }

    public static BenchConfig Load()
    {
        // 1. Check portable directory beside the executable (ideal for USB sticks)
        try
        {
            if (File.Exists(PortableConfigPath))
            {
                var json = File.ReadAllText(PortableConfigPath);
                var cfg = JsonSerializer.Deserialize<BenchConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch { }

        // 2. Check local application data
        try
        {
            if (File.Exists(LocalAppDataConfigPath))
            {
                var json = File.ReadAllText(LocalAppDataConfigPath);
                var cfg = JsonSerializer.Deserialize<BenchConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch { }

        var def = new BenchConfig();
        Save(def);
        return def;
    }

    public static void Save(BenchConfig config)
    {
        lock (_lock)
        {
            try
            {
                // Prefer saving beside executable if directory exists and is writable, otherwise local app data
                string targetPath = LocalAppDataConfigPath;
                if (File.Exists(PortableConfigPath))
                {
                    targetPath = PortableConfigPath;
                }

                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(targetPath, json);
                _current = config;
            }
            catch { }
        }
    }
}
