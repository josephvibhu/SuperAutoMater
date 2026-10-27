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
                // Mount ISO to inspect contents
                string? driveLetter = await MountIsoAsync(filePath);
                if (!string.IsNullOrWhiteSpace(driveLetter))
                {
                    meta.MountedIsoDriveLetter = driveLetter;
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
                string cmd = $"(Mount-DiskImage -ImagePath '{isoPath}' -StorageType ISO -PassThru | Get-Volume).DriveLetter";
                var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{cmd}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string letter = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                    await proc.WaitForExitAsync();
                    if (!string.IsNullOrWhiteSpace(letter) && letter.Length == 1)
                    {
                        return letter;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WimImageService] ISO mount error: {ex.Message}");
            }

            return null;
        }
    }
}
