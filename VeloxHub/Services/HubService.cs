using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Velox.Shared.Dtos;

namespace VeloxHub.Services;

public enum AppInstallState
{
    NotInstalled,
    Installing,
    Installed,
    UpdateAvailable
}

public class HubAppItem : INotifyPropertyChanged
{
    private AppInstallState _state = AppInstallState.NotInstalled;
    private int _progress = 0;
    private string _statusText = "Ready to install";
    private bool _isEntitled = true;

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconSymbol { get; set; } = "⚡";
    public string Category { get; set; } = "Core Suite";
    public string LocalVersion { get; set; } = "1.6.10";
    public string RequiredPlanTier { get; set; } = "All Plans";
    public string ActionType { get; set; } = "EXE"; // EXE, WEB

    public bool IsEntitled
    {
        get => _isEntitled;
        set
        {
            _isEntitled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstallOrLaunch));
            OnPropertyChanged(nameof(EntitlementBadge));
        }
    }

    public string EntitlementBadge => IsEntitled ? "INCLUDED IN PLAN" : "PLAN UPGRADE REQUIRED";

    public AppInstallState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionButtonText));
            OnPropertyChanged(nameof(CanInstallOrLaunch));
        }
    }

    public int Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionButtonText));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanInstallOrLaunch => IsEntitled && (ActionType == "WEB" || State != AppInstallState.Installing);

    public string ActionButtonText => ActionType == "WEB"
        ? "Launch Web"
        : State switch
        {
            AppInstallState.NotInstalled => "Install",
            AppInstallState.Installing => $"{Progress}%",
            AppInstallState.Installed => "Launch",
            AppInstallState.UpdateAvailable => "Update",
            _ => "Install"
        };

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class HubConfig
{
    public string ApiUrl { get; set; } = "http://13.206.106.146";
    public string LicenseKey { get; set; } = "";
    public string MachineGuid { get; set; } = "";
}

public class HubService : INotifyPropertyChanged
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _configDir;
    private readonly string _configFile;
    private readonly string _appsRootDir;

    public HubConfig Config { get; private set; } = new();
    public LicenseValidateResponse CurrentLicense { get; private set; }
    public ObservableCollection<HubAppItem> Apps { get; } = new();

    private bool _isBusy;
    private string _statusBanner = "Initializing Velox Hub...";

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string StatusBanner
    {
        get => _statusBanner;
        set { _statusBanner = value; OnPropertyChanged(); }
    }

    public HubService()
    {
        _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Velox");
        Directory.CreateDirectory(_configDir);
        _configFile = Path.Combine(_configDir, "hub_config.json");
        _appsRootDir = Path.Combine(_configDir, "Apps");
        Directory.CreateDirectory(_appsRootDir);

        LoadConfig();
        InitializeCatalog();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configFile))
            {
                var json = File.ReadAllText(_configFile);
                Config = JsonSerializer.Deserialize<HubConfig>(json) ?? new();
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(Config.MachineGuid))
        {
            Config.MachineGuid = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            SaveConfig();
        }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFile, json);
        }
        catch { }
    }

    private void InitializeCatalog()
    {
        Apps.Clear();

        // 1. VeloxBench
        Apps.Add(new HubAppItem
        {
            Id = "VeloxBench",
            Title = "VeloxBench",
            Tagline = "Laptop Diagnostics & Auto-Grading Cockpit",
            Description = "Full hardware automated testing suite. 60-second multi-threaded diagnostic run across Battery, Display, RAM, NVMe, Keyboard, Audio, and Motherboard components.",
            IconSymbol = "⚡",
            Category = "Core Testing",
            LocalVersion = "v1.6.10",
            RequiredPlanTier = "All Plans",
            ActionType = "EXE"
        });

        // 2. VeloxManager
        Apps.Add(new HubAppItem
        {
            Id = "VeloxManager",
            Title = "VeloxManager",
            Tagline = "Warehouse Fleet & Inventory Telemetry",
            Description = "Supervisor operations cockpit. Real-time godown intake, technician station throughput metrics, warranty holds, and CSV/Excel exports.",
            IconSymbol = "📊",
            Category = "Fleet Telemetry",
            LocalVersion = "v1.0.2",
            RequiredPlanTier = "Starter / Pro / Enterprise",
            ActionType = "EXE"
        });

        // 3. Velox Link Mobile Gateway
        Apps.Add(new HubAppItem
        {
            Id = "VeloxLink",
            Title = "Velox Link Mobile",
            Tagline = "Zero-Install Cosmetic Photo Capture",
            Description = "Dynamic QR pairing gateway. Open phone camera on technician device to stream chassis defect photos and serial scans directly into active test reports.",
            IconSymbol = "📱",
            Category = "Mobile Companion",
            LocalVersion = "Web Companion",
            RequiredPlanTier = "Starter / Pro / Enterprise",
            ActionType = "WEB"
        });

        RefreshInstalledStates();
    }

    public void RefreshInstalledStates()
    {
        foreach (var app in Apps)
        {
            // NEVER modify state of an app actively downloading in the background!
            if (app.State == AppInstallState.Installing)
            {
                continue;
            }

            if (app.ActionType == "WEB")
            {
                app.State = AppInstallState.Installed;
                app.StatusText = "Active Cloud Gateway";
                continue;
            }

            var appDir = Path.Combine(_appsRootDir, app.Id);
            var exeFile = Path.Combine(appDir, $"{app.Id}.exe");
            var downloadTempFile = Path.Combine(appDir, $"{app.Id}.exe.download");

            // Clean up any stale temp download file if not installing
            if (File.Exists(downloadTempFile))
            {
                try { File.Delete(downloadTempFile); } catch { }
            }

            if (File.Exists(exeFile))
            {
                app.State = AppInstallState.Installed;
                app.StatusText = "Installed • Ready to launch";
            }
            else
            {
                app.State = AppInstallState.NotInstalled;
                app.StatusText = "Ready to install";
            }
        }
    }

    public async Task<bool> ValidateLicenseAsync(string newKey = null)
    {
        IsBusy = true;
        StatusBanner = "Contacting Velox Cloud licensing authority...";

        if (!string.IsNullOrWhiteSpace(newKey))
        {
            Config.LicenseKey = newKey.Trim().ToUpperInvariant();
            SaveConfig();
        }

        if (string.IsNullOrWhiteSpace(Config.LicenseKey))
        {
            StatusBanner = "No active license. Click 'Activate Key' to enter your organization credentials.";
            CurrentLicense = null;
            ApplyPlanEntitlements();
            IsBusy = false;
            return false;
        }

        try
        {
            var req = new LicenseValidateRequest
            {
                LicenseKey = Config.LicenseKey,
                MachineName = Environment.MachineName,
                MachineGuid = Config.MachineGuid,
                AppVersion = "1.6.10"
            };

            var url = $"{Config.ApiUrl.TrimEnd('/')}/api/v1/license/validate";
            var resp = await Http.PostAsJsonAsync(url, req);

            if (resp.IsSuccessStatusCode)
            {
                CurrentLicense = await resp.Content.ReadFromJsonAsync<LicenseValidateResponse>();
                StatusBanner = $"Verified for {CurrentLicense.CompanyName} ({CurrentLicense.PlanTier} Plan)";
                ApplyPlanEntitlements();
                IsBusy = false;
                return true;
            }
            else
            {
                var errObj = await resp.Content.ReadFromJsonAsync<LicenseValidateResponse>();
                StatusBanner = errObj?.Message ?? "License validation failed (VLX-AUTH-200).";
                CurrentLicense = null;
                ApplyPlanEntitlements();
                IsBusy = false;
                return false;
            }
        }
        catch (Exception ex)
        {
            StatusBanner = $"Cloud unreachable: {ex.Message}";
            CurrentLicense = null;
            ApplyPlanEntitlements();
            IsBusy = false;
            return false;
        }
    }

    public void ApplyPlanEntitlements()
    {
        if (CurrentLicense == null || !CurrentLicense.IsValid)
        {
            foreach (var app in Apps)
            {
                app.IsEntitled = false;
            }
            return;
        }

        var tier = (CurrentLicense.PlanTier ?? "").ToLowerInvariant();

        foreach (var app in Apps)
        {
            switch (app.Id)
            {
                case "VeloxBench":
                    // VeloxBench is included in every valid plan
                    app.IsEntitled = true;
                    break;

                case "VeloxManager":
                    // Included in Starter, Pro, Enterprise (excluded from Bench Solo)
                    app.IsEntitled = !tier.Contains("solo");
                    break;

                case "VeloxLink":
                    // Included in Starter, Pro, Enterprise (excluded from Bench Solo)
                    app.IsEntitled = !tier.Contains("solo");
                    break;

                default:
                    app.IsEntitled = true;
                    break;
            }
        }
    }

    public async Task ExecuteActionAsync(HubAppItem app)
    {
        if (!app.IsEntitled) return;

        if (app.ActionType == "WEB")
        {
            // Open Velox Link in browser
            try
            {
                var linkUrl = $"{Config.ApiUrl.TrimEnd('/')}/link";
                Process.Start(new ProcessStartInfo(linkUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open browser: {ex.Message}", "Velox Link", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        var appDir = Path.Combine(_appsRootDir, app.Id);
        Directory.CreateDirectory(appDir);
        var exePath = Path.Combine(appDir, $"{app.Id}.exe");
        var downloadTempPath = Path.Combine(appDir, $"{app.Id}.exe.download");

        if (app.State == AppInstallState.Installed)
        {
            // Launch the application safely
            if (File.Exists(exePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exePath)
                    {
                        WorkingDirectory = appDir,
                        UseShellExecute = true
                    });
                    app.StatusText = "Running • Background diagnostic session";
                }
                catch (Exception ex)
                {
                    app.StatusText = $"Launch failed: {ex.Message}";
                    MessageBox.Show($"Could not launch {app.Title}:\n\n{ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                app.State = AppInstallState.NotInstalled;
                app.StatusText = "Binary missing. Please click Install.";
            }
            return;
        }

        if (app.State == AppInstallState.Installing)
        {
            return; // Ignore redundant clicks while downloading
        }

        // Install: Download to isolated temporary file
        app.State = AppInstallState.Installing;
        app.Progress = 5;
        app.StatusText = "Connecting to download server...";

        try
        {
            var downloadUrl = $"{Config.ApiUrl.TrimEnd('/')}/api/v1/update/download/latest?appName={app.Id}";
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 90_000_000;

            // Stream directly into temporary file
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(downloadTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    var pct = (int)Math.Min(98, (totalRead * 100) / totalBytes);
                    app.Progress = pct;
                    app.StatusText = $"Downloading {app.Title}... ({pct}%)";
                }

                await fileStream.FlushAsync();
            }

            // Atomic file move into production position
            app.StatusText = "Finalizing installation...";
            if (File.Exists(exePath))
            {
                try { File.Delete(exePath); } catch { }
            }
            File.Move(downloadTempPath, exePath, overwrite: true);

            // Auto-provision config.json in installed app directory
            var appConfig = new
            {
                ApiUrl = Config.ApiUrl,
                LicenseKey = Config.LicenseKey,
                DepotId = CurrentLicense?.CompanyName ?? "DEPOT-01",
                BenchId = Environment.MachineName,
                UpdateChannel = "alpha"
            };
            var configPath = Path.Combine(appDir, "config.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(appConfig, new JsonSerializerOptions { WriteIndented = true }));

            // Create Windows Start Menu shortcut
            try
            {
                var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Velox Suite");
                Directory.CreateDirectory(startMenuDir);
                var lnkPath = Path.Combine(startMenuDir, $"{app.Title}.lnk");
                CreateShortcut(lnkPath, exePath, appDir, app.Description);
            }
            catch { }

            app.Progress = 100;
            app.State = AppInstallState.Installed;
            app.StatusText = "Installed • Ready to launch";
        }
        catch (Exception ex)
        {
            try { if (File.Exists(downloadTempPath)) File.Delete(downloadTempPath); } catch { }
            app.State = AppInstallState.NotInstalled;
            app.StatusText = $"Download failed: {ex.Message}";
            MessageBox.Show($"Failed to download {app.Title}:\n\n{ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void UninstallApp(HubAppItem app)
    {
        if (app.ActionType == "WEB") return;

        var appDir = Path.Combine(_appsRootDir, app.Id);
        if (Directory.Exists(appDir))
        {
            try
            {
                Directory.Delete(appDir, recursive: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not remove all files for {app.Title}: {ex.Message}", "Uninstall Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Remove start menu shortcut
        try
        {
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Velox Suite");
            var lnkPath = Path.Combine(startMenuDir, $"{app.Title}.lnk");
            if (File.Exists(lnkPath)) File.Delete(lnkPath);
        }
        catch { }

        app.State = AppInstallState.NotInstalled;
        app.Progress = 0;
        app.StatusText = "Uninstalled • Ready to install";
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType);
            var shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = description;
            shortcut.Save();
        }
        catch { }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}