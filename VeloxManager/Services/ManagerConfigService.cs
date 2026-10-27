using System;
using System.IO;
using System.Text.Json;

namespace VeloxManager.Services;

public class ManagerConfig
{
    public string ApiUrl { get; set; } = "https://velox-api-production.up.railway.app";
    public string ApiKey { get; set; } = "vx-2fe1479ad70dffd3017a0aa8c67b1c270fc9dd00bb9d4bdf";
    public string SiteCode { get; set; } = "MAIN";
    public int LanPort { get; set; } = 9000;
    public bool EnableLanServer { get; set; } = true;
    public string ManagerName { get; set; } = "Depot Operations Manager";
    public string Theme { get; set; } = "dark";
    public string GoogleSheetsWebhookUrl { get; set; } = "https://script.google.com/macros/s/AKfycbweLRaXur7tAluRq4IIDjC2RDGDhBU52iss-zAKMau_Sd-yt2NJjlWNZfKEJ2sfYphfOw/exec";
    public int AutoSyncSheetsMinutes { get; set; } = 30;
}

public static class ManagerConfigService
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    private static ManagerConfig? _current;

    public static ManagerConfig Current
    {
        get
        {
            if (_current == null) Load();
            return _current!;
        }
    }

    public static void Load()
    {
        try
        {
            // If production config exists in root or base dir, load it
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                _current = JsonSerializer.Deserialize<ManagerConfig>(json) ?? new ManagerConfig();
                return;
            }

            // Check if local dev production config exists
            var devProdPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "config.production.json");
            if (File.Exists(devProdPath))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(devProdPath));
                _current = new ManagerConfig
                {
                    ApiUrl = doc.RootElement.TryGetProperty("apiUrl", out var u) ? u.GetString() ?? "" : "https://velox-api-production.up.railway.app",
                    ApiKey = doc.RootElement.TryGetProperty("primaryApiKey", out var k) ? k.GetString() ?? "" : "",
                    SiteCode = doc.RootElement.TryGetProperty("siteCode", out var s) ? s.GetString() ?? "MAIN" : "MAIN"
                };
                Save();
                return;
            }
        }
        catch
        {
            // Fallback to default
        }

        _current = new ManagerConfig();
        Save();
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
