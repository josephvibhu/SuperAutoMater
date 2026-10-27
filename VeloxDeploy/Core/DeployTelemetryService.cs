using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public class DeployTelemetryService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public async Task<bool> RecordDeployEventAsync(
            string apiEndpoint,
            HardwareProfile profile,
            TargetDiskInfo disk,
            TimeSpan duration,
            bool isSuccess,
            string? errorMessage = null)
        {
            var payload = new
            {
                serial_number = profile.BiosSerial,
                hardware_id = $"{profile.Manufacturer}-{profile.Model}-{profile.BiosSerial}",
                event_type = "os_deployed",
                status = isSuccess ? "completed" : "failed",
                details = new
                {
                    manufacturer = profile.Manufacturer,
                    model = profile.Model,
                    cpu = profile.CpuName,
                    ram_gb = profile.RamGb,
                    target_disk = disk.Model,
                    disk_size_gb = Math.Round(disk.SizeBytes / (1024.0 * 1024.0 * 1024.0), 1),
                    has_oem_license = profile.AcpiKey.HasKey,
                    license_key_masked = profile.AcpiKey.MaskedKey,
                    duration_seconds = Math.Round(duration.TotalSeconds, 1),
                    error = errorMessage,
                    deployed_at = DateTime.UtcNow.ToString("o")
                }
            };

            try
            {
                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string url = $"{apiEndpoint.TrimEnd('/')}/api/v1/assets/events";

                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Silently succeed for offline depot networks
                return false;
            }
        }
    }
}
