using System;
using System.IO;
using System.Text.Json;

namespace VeloxBench.Core;

public sealed class BenchConfig
{
    public string ApiUrl { get; set; } = "https://velox-api-production.up.railway.app";
    public string ApiKey { get; set; } = "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf";
    public string TenantId { get; set; } = "ten-d2428a4cb3da";
    public string BenchName { get; set; } = Environment.MachineName;
    public string ManagerLanIp { get; set; } = "127.0.0.1";
    public int ManagerLanPort { get; set; } = 9000;
}

public static class BenchConfigService
{
    private static readonly string ConfigPath = Path.Combine(
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
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
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
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                _current = config;
            }
            catch { }
        }
    }
}
