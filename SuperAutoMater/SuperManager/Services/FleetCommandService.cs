using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SuperManager.Models;

namespace SuperManager.Services
{
    public class FleetCommandService
    {
        private static readonly Lazy<FleetCommandService> _instance =
            new Lazy<FleetCommandService>(() => new FleetCommandService());
        public static FleetCommandService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private FleetCommandService() { }

        private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, BenchDevice bench, string path)
        {
            var request = new HttpRequestMessage(method, $"http://{bench.IpAddress}:{bench.Port}{path}");
            if (!string.IsNullOrWhiteSpace(bench.SessionAccessToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bench.SessionAccessToken);
            return request;
        }

        public async Task<bool> SendPingAsync(BenchDevice bench)
        {
            if (bench == null) return false;
            try
            {
                bench.IsIdentified = true;
                using var request = CreateAuthorizedRequest(HttpMethod.Get, bench, "/api/ping");
                var response = await _http.SendAsync(request);

                _ = Task.Delay(3000).ContinueWith(_ => bench.IsIdentified = false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                bench.IsIdentified = false;
                return false;
            }
        }

        public async Task<string> FetchCertificateAsync(BenchDevice bench, string destinationFolder)
        {
            if (bench == null) return null;
            try
            {
                Directory.CreateDirectory(destinationFolder);
                using var request = CreateAuthorizedRequest(HttpMethod.Get, bench, "/api/certificate");
                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                string fileName = $"QC_Cert_{bench.Serial}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                string fullPath = Path.Combine(destinationFolder, fileName);
                await File.WriteAllBytesAsync(fullPath, bytes);
                return fullPath;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> FetchDeepStatusAsync(BenchDevice bench)
        {
            if (bench == null) return false;
            try
            {
                using var request = CreateAuthorizedRequest(HttpMethod.Get, bench, "/api/status");
                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return false;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("cpuTemp", out var propTemp)) bench.CpuTemp = propTemp.GetInt32();
                if (root.TryGetProperty("cpuUsage", out var propUsage)) bench.CpuUsage = propUsage.GetInt32();
                if (root.TryGetProperty("cpu", out var propCpu)) bench.Cpu = propCpu.GetString();
                if (root.TryGetProperty("ram", out var propRam)) bench.Ram = propRam.GetString();
                if (root.TryGetProperty("battery", out var propBat)) bench.Battery = propBat.GetString();
                if (root.TryGetProperty("batteryHealth", out var propBatH)) bench.BatteryHealth = propBatH.GetInt32();
                if (root.TryGetProperty("storage", out var propSto)) bench.Storage = propSto.GetString();
                if (root.TryGetProperty("storageHealth", out var propStoH)) bench.StorageHealth = propStoH.GetString();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendPassActionAsync(BenchDevice bench)
        {
            if (bench == null) return false;
            try
            {
                using var request = CreateAuthorizedRequest(HttpMethod.Post, bench, "/api/action/pass");
                var response = await _http.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
