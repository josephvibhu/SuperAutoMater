using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VeloxDeploy.Core
{
    public class WimEditionInfo
    {
        public int Index { get; set; } = 1;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeFormatted => SizeBytes > 0 
            ? $"{(SizeBytes / (1024.0 * 1024.0 * 1024.0)):F1} GB" 
            : "Unknown";
        public bool IsPro => Name.Contains("Pro", StringComparison.OrdinalIgnoreCase) || 
                             Description.Contains("Pro", StringComparison.OrdinalIgnoreCase);
    }

    public class WimMetadata
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string EffectiveWimPath { get; set; } = string.Empty;
        public string? MountedIsoDriveLetter { get; set; }
        public List<WimEditionInfo> Editions { get; set; } = new();
        public WimEditionInfo? SelectedEdition { get; set; }
        public bool IsValid => File.Exists(EffectiveWimPath);
        public string DisplayName => SelectedEdition != null 
            ? $"{SelectedEdition.Name} ({Path.GetFileName(OriginalPath)})"
            : Path.GetFileName(OriginalPath);
    }

    public class WimImageService
    {
        private static string? _lastMountedIsoPath;

        public static async Task<WimMetadata> InspectImageAsync(string filePath)
        {
            var meta = new WimMetadata { OriginalPath = filePath };

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return meta;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".iso")
            {
                // If switching to a different ISO, clean up the previous one first
                if (!string.IsNullOrWhiteSpace(_lastMountedIsoPath) && 
                    !string.Equals(_lastMountedIsoPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    await DismountIsoAsync(_lastMountedIsoPath);
                    _lastMountedIsoPath = null;
                }

                // Mount ISO to inspect contents (or re-use existing mount)
                string? driveLetter = await MountIsoAsync(filePath);
                if (!string.IsNullOrWhiteSpace(driveLetter))
                {
                    meta.MountedIsoDriveLetter = driveLetter;
                    _lastMountedIsoPath = filePath;

                    string candidateWim = Path.Combine($"{driveLetter}:\\", "sources", "install.wim");
                    string candidateEsd = Path.Combine($"{driveLetter}:\\", "sources", "install.esd");

                    if (File.Exists(candidateWim))
                    {
                        meta.EffectiveWimPath = candidateWim;
                    }
                    else if (File.Exists(candidateEsd))
                    {
                        meta.EffectiveWimPath = candidateEsd;
                    }
                }

                // If this ISO is NOT a valid Windows OS installer (e.g. Office ISO), dismount immediately so Explorer stays clean
                if (string.IsNullOrWhiteSpace(meta.EffectiveWimPath))
                {
                    await DismountIsoAsync(filePath);
                    meta.MountedIsoDriveLetter = null;
                    if (string.Equals(_lastMountedIsoPath, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastMountedIsoPath = null;
                    }
                }
            }
            else if (ext == ".wim" || ext == ".esd")
            {
                meta.EffectiveWimPath = filePath;
            }

            if (File.Exists(meta.EffectiveWimPath))
            {
                meta.Editions = await QueryWimEditionsAsync(meta.EffectiveWimPath);

                // Auto-prefer Pro edition for refurbished depot machines
                meta.SelectedEdition = meta.Editions.FirstOrDefault(e => e.IsPro) 
                                       ?? meta.Editions.FirstOrDefault();
            }

            return meta;
        }

        private static async Task<List<WimEditionInfo>> QueryWimEditionsAsync(string wimPath)
        {
            var editions = new List<WimEditionInfo>();

            try
            {
                var psi = new ProcessStartInfo("dism.exe", $"/Get-WimInfo /WimFile:\"{wimPath}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    // Parse dism output:
                    // Index : 1
                    // Name : Windows 11 Home
                    // Index : 2
                    // Name : Windows 11 Pro
                    var indexMatches = Regex.Matches(output, @"Index\s*:\s*(\d+)", RegexOptions.IgnoreCase);
                    var nameMatches = Regex.Matches(output, @"Name\s*:\s*(.+)", RegexOptions.IgnoreCase);

                    int count = Math.Min(indexMatches.Count, nameMatches.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (int.TryParse(indexMatches[i].Groups[1].Value, out int idx))
                        {
                            string name = nameMatches[i].Groups[1].Value.Trim();
                            editions.Add(new WimEditionInfo
                            {
                                Index = idx,
                                Name = name,
                                Description = name,
                                SizeBytes = new FileInfo(wimPath).Length
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WimImageService] DISM query error: {ex.Message}");
            }

            // Fallback if DISM parsing didn't find multiple indexes
            if (editions.Count == 0 && File.Exists(wimPath))
            {
                editions.Add(new WimEditionInfo
                {
                    Index = 1,
                    Name = "Windows 11 Pro (Default)",
                    Description = Path.GetFileName(wimPath),
                    SizeBytes = new FileInfo(wimPath).Length
                });
            }

            return editions;
        }

        private static async Task<string?> MountIsoAsync(string isoPath)
        {
            try
            {
                // Check if already mounted first to avoid duplicate DVD drive letters in File Explorer
                string cmd = $"$p = '{isoPath}'; $img = Get-DiskImage -ImagePath $p -ErrorAction SilentlyContinue; if ($img -and $img.Attached) {{ ((Get-DiskImage -ImagePath $p | Get-Volume).DriveLetter | Select-Object -First 1) }} else {{ ((Mount-DiskImage -ImagePath $p -StorageType ISO -PassThru | Get-Volume).DriveLetter | Select-Object -First 1) }}";
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{cmd}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                    await proc.WaitForExitAsync();
                    string letter = output.Split(new[] { '\r', '\n', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(letter) && letter.Length == 1 && char.IsLetter(letter[0]))
                    {
                        return letter.ToUpperInvariant();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WimImageService] ISO mount error: {ex.Message}");
            }

            return null;
        }

        public static async Task DismountIsoAsync(string? isoPath)
        {
            if (string.IsNullOrWhiteSpace(isoPath)) return;
            try
            {
                string cmd = $"Dismount-DiskImage -ImagePath '{isoPath}' -ErrorAction SilentlyContinue";
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{cmd}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WimImageService] ISO dismount error: {ex.Message}");
            }
        }

        public static async Task DismountLastIsoAsync()
        {
            if (!string.IsNullOrWhiteSpace(_lastMountedIsoPath))
            {
                await DismountIsoAsync(_lastMountedIsoPath);
                _lastMountedIsoPath = null;
            }
        }
    }
}
