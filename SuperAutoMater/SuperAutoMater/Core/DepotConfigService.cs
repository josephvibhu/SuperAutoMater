using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Configuration model for Depot OS site identity, security, and operations.
    /// </summary>
    public sealed class DepotConfig
    {
        public string SiteId { get; set; } = "DEPOT-US-EAST-01";
        public string FacilityName { get; set; } = "Newark Refurbishment Depot";
        public string BenchPrefix { get; set; } = "BENCH-";
        public string HmacSecretKey { get; set; } = "depot_secret_key_8f3a9e2d1c7b4a5f";
        public int EvidenceRetentionDays { get; set; } = 90;
        public string BackupStorageDirectory { get; set; } = "";
        public string DefaultWebhookUrl { get; set; } = "https://script.google.com/macros/s/AKfycbxh1-pzrBC1DieKrlM55_TsIfjP5sKoTfCdPuJ_PkMDe5E1HXwY1przZkejTBnuWm2_DQ/exec";
        public bool RequireSupervisorOverride { get; set; } = true;
        public DateTimeOffset LastModifiedUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Site configuration management service for depot_config.json.
    /// </summary>
    public sealed class DepotConfigService
    {
        private static readonly Lazy<DepotConfigService> _instance =
            new Lazy<DepotConfigService>(() => new DepotConfigService());

        public static DepotConfigService Instance => _instance.Value;

        private readonly string _configFilePath;
        private readonly object _lock = new object();
        private DepotConfig _currentConfig;

        public DepotConfig Config
        {
            get
            {
                lock (_lock)
                {
                    if (_currentConfig == null)
                    {
                        _currentConfig = LoadConfig();
                    }
                    return _currentConfig;
                }
            }
        }

        public DepotConfigService(string configFilePath = null)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }
            _configFilePath = configFilePath ?? Path.Combine(root, "depot_config.json");
            _currentConfig = LoadConfig();
        }

        public DepotConfig LoadConfig()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_configFilePath))
                    {
                        string json = File.ReadAllText(_configFilePath, Encoding.UTF8);
                        var cfg = JsonSerializer.Deserialize<DepotConfig>(json);
                        if (cfg != null)
                        {
                            if (string.IsNullOrWhiteSpace(cfg.BackupStorageDirectory))
                            {
                                string root = Path.GetDirectoryName(_configFilePath);
                                cfg.BackupStorageDirectory = Path.Combine(root, "Backups");
                            }
                            return cfg;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to read {_configFilePath}, using defaults.", ex);
                }

                var defaultConfig = new DepotConfig();
                string dir = Path.GetDirectoryName(_configFilePath);
                defaultConfig.BackupStorageDirectory = Path.Combine(dir, "Backups");
                SaveConfig(defaultConfig);
                return defaultConfig;
            }
        }

        public void SaveConfig(DepotConfig config)
        {
            if (config == null) return;
            lock (_lock)
            {
                try
                {
                    config.LastModifiedUtc = DateTimeOffset.UtcNow;
                    string dir = Path.GetDirectoryName(_configFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(config, options);
                    File.WriteAllText(_configFilePath, json, Encoding.UTF8);
                    _currentConfig = config;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to save depot_config.json", ex);
                }
            }
        }
    }
}
