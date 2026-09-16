using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SuperManager.Models;

namespace SuperManager.Services
{
    public class PingDiagnosticsResult
    {
        public bool Success { get; set; }
        public bool IsFirewallBlocked { get; set; }
        public bool IsHostReachableIcmp { get; set; }
        public long RoundtripMs { get; set; }
        public string Message { get; set; } = "";
    }

    public class FleetCommandService
    {
        private static readonly Lazy<FleetCommandService> _instance =
            new Lazy<FleetCommandService>(() => new FleetCommandService());
        public static FleetCommandService Instance => _instance.Value;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private readonly HttpClient _pingHttp = new HttpClient { Timeout = TimeSpan.FromMilliseconds(2500) };

        private FleetCommandService() { }

        private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, BenchDevice bench, string path)
        {
            var request = new HttpRequestMessage(method, $"http://{bench.IpAddress}:{bench.Port}{path}");
            if (!string.IsNullOrWhiteSpace(bench.SessionAccessToken))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bench.SessionAccessToken);
            return request;
        }

        public async Task<PingDiagnosticsResult> SendPingWithDiagnosticsAsync(BenchDevice bench)
        {
            if (bench == null)
                return new PingDiagnosticsResult { Success = false, Message = "Bench device is null." };

            bench.IsIdentified = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
                using var request = CreateAuthorizedRequest(HttpMethod.Get, bench, "/api/ping");
                var response = await _pingHttp.SendAsync(request, cts.Token);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    _ = Task.Delay(3000).ContinueWith(_ => bench.IsIdentified = false);
                    return new PingDiagnosticsResult
                    {
                        Success = true,
                        RoundtripMs = sw.ElapsedMilliseconds,
                        Message = $"✓ Ping confirmed by {bench.MachineName} ({sw.ElapsedMilliseconds}ms) — acoustic chime triggered!"
                    };
                }
            }
            catch
            {
                // HTTP failed or timed out — test ICMP reachability to diagnose root cause
            }

            bench.IsIdentified = false;

            // Perform ICMP Echo Ping to verify if the physical host is alive on the LAN
            bool icmpAlive = false;
            long icmpMs = 0;
            try
            {
                using var pingSender = new System.Net.NetworkInformation.Ping();
                var reply = await pingSender.SendPingAsync(bench.IpAddress, 1500);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    icmpAlive = true;
                    icmpMs = reply.RoundtripTime;
                }
            }
            catch { }

            if (icmpAlive)
            {
                // Host is alive, but Port 8443 is blocked!
                return new PingDiagnosticsResult
                {
                    Success = false,
                    IsHostReachableIcmp = true,
                    IsFirewallBlocked = true,
                    RoundtripMs = icmpMs,
                    Message = $"⚠️ {bench.MachineName} ({bench.IpAddress}) is ONLINE ({icmpMs}ms), but Port {bench.Port} is blocked by Windows Firewall! Run Fix-Firewall.bat on {bench.MachineName}."
                };
            }
            else
            {
                // Host is completely unreachable
                return new PingDiagnosticsResult
                {
                    Success = false,
                    IsHostReachableIcmp = false,
                    IsFirewallBlocked = false,
                    Message = $"❌ Failed to reach {bench.MachineName} at {bench.IpAddress}:{bench.Port} (Host unreachable on LAN/Wi-Fi)."
                };
            }
        }

        public async Task<bool> SendPingAsync(BenchDevice bench)
        {
            var result = await SendPingWithDiagnosticsAsync(bench);
            return result.Success;
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
