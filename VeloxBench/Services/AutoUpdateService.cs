using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SuperAutoMater.Wpf.Core;
using Velox.Shared.Dtos;
using VeloxBench.Core;

namespace VeloxBench.Services;

public sealed class AutoUpdateService
{
    private static readonly Lazy<AutoUpdateService> _lazy = new(() => new AutoUpdateService());
    public static AutoUpdateService Instance => _lazy.Value;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private volatile bool _isChecking;
    private volatile bool _isDownloading;
    private double _downloadProgressPercent;

    public bool IsChecking => _isChecking;
    public bool IsDownloading => _isDownloading;
    public double DownloadProgressPercent => _downloadProgressPercent;
    public UpdateCheckResponse? LatestUpdate { get; private set; }

    public event Action<string>? UpdateStatusChanged;
    public event Action<double>? DownloadProgressChanged;

    public string CurrentVersion
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.6.10";
        }
    }

    public void CleanupOldVersions()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var currentExe = Environment.ProcessPath ?? Path.Combine(baseDir, "VeloxBench.exe");
            var exeName = Path.GetFileName(currentExe);

            // Clean .old files
            var oldFiles = Directory.GetFiles(baseDir, $"{exeName}*.old*");
            foreach (var f in oldFiles)
            {
                try { File.Delete(f); AppLogger.Info("Update", $"Cleaned up legacy file: {f}"); } catch { }
            }

            // Clean .update.tmp files
            var tmpFiles = Directory.GetFiles(baseDir, "*.update.tmp");
            foreach (var f in tmpFiles)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public async Task<UpdateCheckResponse?> CheckForUpdatesAsync(bool autoApplyIfAvailable = false)
    {
        if (_isChecking || _isDownloading) return LatestUpdate;
        _isChecking = true;

        try
        {
            var cfg = BenchConfigService.Current;
            var url = $"{cfg.ApiUrl.TrimEnd('/')}/api/v1/update/check?appName=VeloxBench&currentVersion={Uri.EscapeDataString(CurrentVersion)}&channel={Uri.EscapeDataString(cfg.UpdateChannel)}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var resp = await _http.GetAsync(url, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var update = JsonSerializer.Deserialize<UpdateCheckResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (update != null && update.UpdateAvailable)
                {
                    LatestUpdate = update;
                    AppLogger.Info("Update", $"New update discovered: v{update.LatestVersion} (Current: v{CurrentVersion})");
                    UpdateStatusChanged?.Invoke($"New update available: v{update.LatestVersion}");

                    if (autoApplyIfAvailable && cfg.AutoUpdateEnabled)
                    {
                        _ = Task.Run(async () => await DownloadAndApplyUpdateAsync(update));
                    }

                    return update;
                }
                else
                {
                    LatestUpdate = null;
                    UpdateStatusChanged?.Invoke("VeloxBench is up to date.");
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Update", $"Update check skipped: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
        }

        return null;
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(UpdateCheckResponse update)
    {
        if (_isDownloading) return false;
        _isDownloading = true;

        try
        {
            var currentExe = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VeloxBench.exe");
            var baseDir = Path.GetDirectoryName(currentExe) ?? AppDomain.CurrentDomain.BaseDirectory;
            var updateTmpPath = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(currentExe)}.update.tmp");
            var backupOldPath = Path.Combine(baseDir, $"{Path.GetFileName(currentExe)}.old");

            AppLogger.Info("Update", $"Downloading v{update.LatestVersion} from {update.DownloadUrl}...");
            UpdateStatusChanged?.Invoke($"Downloading v{update.LatestVersion}...");

            using (var resp = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var totalBytes = resp.Content.Headers.ContentLength ?? update.FileSizeBytes;

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(updateTmpPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        _downloadProgressPercent = Math.Round((double)totalRead / totalBytes * 100.0, 1);
                        DownloadProgressChanged?.Invoke(_downloadProgressPercent);
                    }
                }
            }

            // Verify SHA256 checksum if provided by server
            if (!string.IsNullOrWhiteSpace(update.FileSha256))
            {
                using var sha = SHA256.Create();
                using var checkFs = File.OpenRead(updateTmpPath);
                var hashBytes = await sha.ComputeHashAsync(checkFs);
                var computedHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                if (!string.Equals(computedHash, update.FileSha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Error("Update", $"Checksum mismatch! Expected: {update.FileSha256}, Computed: {computedHash}");
                    try { File.Delete(updateTmpPath); } catch { }
                    UpdateStatusChanged?.Invoke("Update failed: Checksum verification mismatch.");
                    return false;
                }
            }

            UpdateStatusChanged?.Invoke("Applying update...");
            AppLogger.Info("Update", "Performing atomic executable swap...");

            // 1. Rename running executable to .old (Windows allows renaming open files!)
            if (File.Exists(backupOldPath))
            {
                try { File.Delete(backupOldPath); } catch { }
            }
            File.Move(currentExe, backupOldPath, true);

            // 2. Move new executable into place
            File.Move(updateTmpPath, currentExe, true);

            AppLogger.Info("Update", $"Update v{update.LatestVersion} applied successfully. Relaunching application...");

            // 3. Spawn new executable
            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                WorkingDirectory = baseDir,
                UseShellExecute = true,
                Arguments = "--updated"
            };
            Process.Start(psi);

            // 4. Exit current process
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Application.Current.Shutdown(0);
            });

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "Failed to apply auto update", ex);
            UpdateStatusChanged?.Invoke($"Update error: {ex.Message}");
            return false;
        }
        finally
        {
            _isDownloading = false;
        }
    }
}
