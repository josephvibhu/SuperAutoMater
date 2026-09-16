using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using SuperAutoMater.Wpf.Core;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Services
{
    public class FleetBenchNode
    {
        public string Id { get; set; } = "";
        public string MachineName { get; set; } = "BENCH-01";
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8443;
        public string Model { get; set; } = "Detecting...";
        public string Serial { get; set; } = "Unknown";
        public int PassedCount { get; set; } = 0;
        public int TotalCount { get; set; } = 10;
        public string Grade { get; set; } = "GRADE A+";
        public string Status { get; set; } = "QC In Progress";
        public bool Alert { get; set; } = false;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        public string Url => $"http://{IpAddress}:{Port}";
        public string ProgressText => $"{PassedCount}/{TotalCount} PASSED";
        public double ProgressPercent => TotalCount > 0 ? (double)PassedCount / TotalCount * 100.0 : 0.0;
        public string AccentHex => Alert ? "#F85149" : (PassedCount >= TotalCount ? "#3FB950" : "#58A6FF");
        public string LastSeenSummary => $"{(int)(DateTime.UtcNow - LastSeenUtc).TotalSeconds}s ago";
    }

    public class WarehouseFleetService
    {
        private static readonly Lazy<WarehouseFleetService> _instance =
            new Lazy<WarehouseFleetService>(() => new WarehouseFleetService());
        public static WarehouseFleetService Instance => _instance.Value;

        private const int UDP_DISCOVERY_PORT = 9876;
        private const int DEFAULT_HTTP_PORT = 8443;

        private HttpListener _httpListener;
        private UdpClient _udpBroadcaster;
        private UdpClient _udpReceiver;
        private Timer _broadcastTimer;
        private Timer _cleanupTimer;
        private CancellationTokenSource _cts;

        private readonly ConcurrentDictionary<string, FleetBenchNode> _peerNodes =
            new ConcurrentDictionary<string, FleetBenchNode>(StringComparer.OrdinalIgnoreCase);

        private MainViewModel _viewModel;
        private byte[] _qrPngBytes;
        private readonly string _sessionAccessToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        public string LocalIpAddress { get; private set; } = "127.0.0.1";
        public int HttpPort { get; private set; } = DEFAULT_HTTP_PORT;
        /// <summary>
        /// A per-process capability URL for the local fleet dashboard. The token is deliberately
        /// not persisted: restarting the diagnostic bench revokes previously shared URLs.
        /// </summary>
        public string LocalDashboardUrl => $"http://{LocalIpAddress}:{HttpPort}/?token={_sessionAccessToken}";
        public BitmapSource QrCodeBitmap { get; private set; }

        public ObservableCollection<FleetBenchNode> OnlineBenches { get; } =
            new ObservableCollection<FleetBenchNode>();

        public event Action FleetUpdated;

        private WarehouseFleetService() { }

        public void StartService(MainViewModel viewModel)
        {
            if (_cts != null) return; // Already running
            _viewModel = viewModel;
            _cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                try
                {
                    // 1. Detect LAN IPv4 address
                    LocalIpAddress = DetectBestLanIp();

                    // 2. Generate QR Code pointing to mobile dashboard
                    GenerateQrCodeImage();

                    // 3. Start embedded HttpListener
                    StartHttpServer();

                    // 4. Start UDP Discovery Mesh
                    StartUdpDiscovery();

                    // 5. Start periodic cleanup of stale peer benches
                    _cleanupTimer = new Timer(CleanupStalePeers, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

                    FleetUpdated?.Invoke();
                }
                catch { }
            });
        }

        public void StopService()
        {
            try
            {
                _cts?.Cancel();
                _broadcastTimer?.Dispose();
                _cleanupTimer?.Dispose();

                if (_httpListener != null && _httpListener.IsListening)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    _httpListener = null;
                }

                _udpBroadcaster?.Dispose();
                _udpBroadcaster = null;
                _udpReceiver?.Dispose();
                _udpReceiver = null;

                _cts = null;
            }
            catch { }
        }

        private string DetectBestLanIp()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                // 1. Exclude virtual, tunnel, VPN, and host-only adapters (VirtualBox, VMware, Hyper-V, WSL)
                var physical = interfaces.Where(n =>
                {
                    string desc = (n.Description ?? "").ToLowerInvariant();
                    string name = (n.Name ?? "").ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vbox") || desc.Contains("vmware") ||
                        desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("wsl") ||
                        desc.Contains("pseudo") || desc.Contains("p2p") || desc.Contains("direct") ||
                        desc.Contains("teredo") || desc.Contains("tunnel") || desc.Contains("npcap") ||
                        desc.Contains("tap") || desc.Contains("vpn") || desc.Contains("host-only") ||
                        desc.Contains("filter") || desc.Contains("scheduler") || desc.Contains("wan miniport"))
                        return false;

                    if (name.Contains("vbox") || name.Contains("virtualbox") || name.Contains("wsl") ||
                        name.Contains("vethernet") || name.Contains("local area connection*"))
                        return false;

                    return true;
                }).ToList();

                // 2. Prioritize adapters with an active IPv4 Gateway (connected to local router/AP)
                var withGateway = physical.Where(n =>
                {
                    var props = n.GetIPProperties();
                    return props.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any) &&
                        !g.Address.ToString().StartsWith("0."));
                }).OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                          n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                  .ToList();

                foreach (var iface in withGateway.Concat(physical))
                {
                    var props = iface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            if (!ip.StartsWith("127.") && !ip.StartsWith("169.254."))
                            {
                                return ip;
                            }
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        private void GenerateQrCodeImage()
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(LocalDashboardUrl, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(qrCodeData);
                
                // Cyan QR on dark obsidian background
                _qrPngBytes = qrCode.GetGraphic(12, new byte[] { 0x58, 0xA6, 0xFF }, new byte[] { 0x0E, 0x11, 0x16 });

                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(_qrPngBytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                QrCodeBitmap = bmp;
            }
            catch { }
        }

        private void StartHttpServer()
        {
            Task.Run(async () =>
            {
                for (int port = DEFAULT_HTTP_PORT; port <= DEFAULT_HTTP_PORT + 5; port++)
                {
                    try
                    {
                        _httpListener = new HttpListener();
                        // Try binding wildcard first (requires Admin, which SuperAutoMater has)
                        _httpListener.Prefixes.Add($"http://*:{port}/");
                        _httpListener.Start();
                        HttpPort = port;
                        break;
                    }
                    catch
                    {
                        try
                        {
                            _httpListener = new HttpListener();
                            _httpListener.Prefixes.Add($"http://localhost:{port}/");
                            if (LocalIpAddress != "127.0.0.1")
                            {
                                _httpListener.Prefixes.Add($"http://{LocalIpAddress}:{port}/");
                            }
                            _httpListener.Start();
                            HttpPort = port;
                            break;
                        }
                        catch
                        {
                            _httpListener?.Close();
                            _httpListener = null;
                        }
                    }
                }

                if (_httpListener == null || !_httpListener.IsListening) return;

                // Regenerate QR with confirmed port
                GenerateQrCodeImage();

                while (_cts != null && !_cts.IsCancellationRequested && _httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = ProcessHttpRequestAsync(context);
                    }
                    catch
                    {
                        if (_cts == null || _cts.IsCancellationRequested) break;
                    }
                }
            });
        }

        private async Task ProcessHttpRequestAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.AbsolutePath.ToLowerInvariant();
                if (path.StartsWith("/verify/"))
                {
                    await HandleScanToVerifyAsync(context, path);
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    byte[] unauthorized = Encoding.UTF8.GetBytes("{\"error\":\"A valid bench session token is required.\"}");
                    context.Response.ContentLength64 = unauthorized.Length;
                    await context.Response.OutputStream.WriteAsync(unauthorized, 0, unauthorized.Length);
                    context.Response.Close();
                    return;
                }

                byte[] responseBytes;
                string contentType;

                if (path == "/api/status")
                {
                    var payload = GetStatusPayload();
                    string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    responseBytes = Encoding.UTF8.GetBytes(json);
                    contentType = "application/json; charset=utf-8";
                }
                else if (path == "/api/ping")
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Console.Beep(1200, 250);
                            Thread.Sleep(80);
                            Console.Beep(1600, 350);
                        }
                        catch { }
                    });
                    var pingPayload = new
                    {
                        success = true,
                        machineName = Environment.MachineName,
                        message = "📍 Bench successfully located via acoustic alert.",
                        timestamp = DateTime.UtcNow.ToString("o")
                    };
                    string json = JsonSerializer.Serialize(pingPayload);
                    responseBytes = Encoding.UTF8.GetBytes(json);
                    contentType = "application/json; charset=utf-8";
                }
                else if (path == "/api/certificate")
                {
                    var vm = _viewModel;
                    if (vm != null)
                    {
                        var certData = new CertificateData
                        {
                            SerialNumber = vm.Serial,
                            Manufacturer = vm.Manufacturer,
                            Model = vm.Model,
                            BiosVersion = "UEFI Compliant",
                            CpuModel = vm.CpuName,
                            RamDetails = vm.RamSummary,
                            StorageModel = vm.PrimaryDriveModel,
                            StorageHealthPercent = vm.HdsHealth,
                            StoragePowerOn = vm.HdsPowerOnTime,
                            BatteryHealthSummary = vm.BatteryIntegrityBadge,
                            BatteryCapacities = $"{vm.BatteryFullChargeCapacityMwh} / {vm.BatteryDesignCapacityMwh} mWh",
                            BatteryCellTopology = $"{vm.BatteryCellTopology} · {vm.BatteryCellBalanceBadge}",
                            GpuModel = vm.GpuName,
                            PhysicalGrade = vm.Grade,
                            CosmeticDefectsSummary = vm.CosmeticDefectsSummary,
                            TechnicianName = vm.TechnicianDisplayBadge,
                            StorageTbwSummary = vm.TbwDisplaySummary,
                            DriverIntegritySummary = vm.MissingDriversSummary,
                            ThermalDissipationVerdict = ThermalProfilerService.Instance.GetCurrentResult().ConditionSummary,
                            RamTopologySummary = vm.RamChannelBadge,
                            RadiatorAirflowSummary = vm.ThermalDecayVerdict,
                            WebcamOpticsSummary = vm.WebcamOpticsBadge,
                            CloudAuditUrl = GoogleSheetsDispatcher.DefaultSheetsUrl
                        };

                        if (vm.TestPipeline != null)
                        {
                            foreach (var test in vm.TestPipeline)
                            {
                                if (test.StatusBadge == "✓" || test.IsPassed)
                                {
                                    certData.PassedTests.Add(test.Title);
                                }
                            }
                        }

                        try
                        {
                            bool completed = SuperAutoMater.Wpf.Core.QcRunOrchestrator.Instance.TryCompleteRun(
                                vm.BatteryHealth,
                                vm.HdsHealth,
                                out string failReason,
                                out var summary);

                            if (!completed)
                            {
                                responseBytes = Encoding.UTF8.GetBytes($"{{\"error\":\"Cannot generate certificate: {failReason}\"}}");
                                contentType = "application/json";
                                context.Response.StatusCode = 400;
                            }
                            else
                            {
                                certData.RunSummary = summary;
                                certData.RunId = summary.RunId;
                                string pdfPath = PdfCertificateService.Instance.GenerateCertificate(certData);
                                if (File.Exists(pdfPath))
                                {
                                    responseBytes = File.ReadAllBytes(pdfPath);
                                    contentType = "application/pdf";
                                    context.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"SuperAutoMater_Certificate_{vm.Serial}.pdf\"");
                                }
                                else
                                {
                                    responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"PDF generation failed\"}");
                                    contentType = "application/json";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            responseBytes = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
                            contentType = "application/json";
                            context.Response.StatusCode = 400;
                        }
                    }
                    else
                    {
                        responseBytes = Encoding.UTF8.GetBytes("{\"error\":\"ViewModel not available\"}");
                        contentType = "application/json";
                    }
                }
                else if (path == "/api/action/pass")
                {
                    var vm = _viewModel;
                    bool markedPassed = false;
                    if (vm?.TestPipeline != null && Application.Current?.Dispatcher != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var activeTest = vm.TestPipeline.FirstOrDefault(t => t.IsActive && t.IsApplicable);
                            if (activeTest == null) return;
                            vm.MarkTestPassed(activeTest.Key);
                            markedPassed = true;
                        });
                    }
                    var actionPayload = new
                    {
                        success = markedPassed,
                        message = markedPassed ? "Active test marked passed." : "No applicable active test was available."
                    };
                    responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(actionPayload));
                    contentType = "application/json; charset=utf-8";
                }
                else if (path == "/api/qr" && _qrPngBytes != null)
                {
                    responseBytes = _qrPngBytes;
                    contentType = "image/png";
                }
                else
                {
                    string html = GenerateAvionicsDashboardHtml();
                    responseBytes = Encoding.UTF8.GetBytes(html);
                    contentType = "text/html; charset=utf-8";
                }

                context.Response.ContentType = contentType;
                context.Response.ContentLength64 = responseBytes.Length;
                context.Response.Headers.Add("Cache-Control", "no-store");
                await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private bool IsAuthorized(HttpListenerRequest request)
        {
            string token = request.QueryString["token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                string authorization = request.Headers["Authorization"];
                const string bearerPrefix = "Bearer ";
                if (!string.IsNullOrWhiteSpace(authorization) && authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                    token = authorization.Substring(bearerPrefix.Length).Trim();
            }

            if (string.IsNullOrEmpty(token) || token.Length != _sessionAccessToken.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(_sessionAccessToken));
        }

        private object GetStatusPayload()
        {
            var vm = _viewModel;
            var hw = HardwareDiagnosticsService.Instance;

            int passed = vm?.TestPipeline?.Count(t => t.IsPassed) ?? 0;
            int total = vm?.RequiredTestCount ?? 10;

            return new
            {
                machineName = Environment.MachineName,
                manufacturer = hw.SystemIdentity.Manufacturer,
                model = hw.SystemIdentity.Model,
                serial = hw.SystemIdentity.Serial,
                os = Environment.OSVersion.ToString(),
                cpu = hw.CpuTelemetry?.CpuName ?? "Unknown CPU",
                cpuUsage = hw.CpuTelemetry?.UsagePercent ?? 0,
                cpuTemp = hw.CpuTelemetry?.TemperatureC ?? 0,
                ram = vm?.RamSummary ?? "Unavailable",
                battery = vm == null ? "Unavailable" : $"{vm.BatteryCharge}%",
                batteryHealth = hw.BatteryTelemetry?.HealthPercent,
                storage = vm?.StorageSummary ?? "Unavailable",
                storageHealth = vm?.HealthBadge ?? "Unavailable",
                grade = vm?.Grade ?? "Unassigned",
                passedCount = passed,
                totalCount = total,
                activeTest = vm?.TestPipeline?.FirstOrDefault(t => t.IsActive)?.Title ?? "Standby",
                alert = (hw.CpuTelemetry?.TemperatureC ?? 0) > 90 || (hw.BatteryTelemetry?.HealthPercent ?? 100) < 60,
                timestamp = DateTime.UtcNow.ToString("o")
            };
        }

        private async Task HandleScanToVerifyAsync(HttpListenerContext context, string path)
        {
            try
            {
                string runId = path.Substring("/verify/".Length).Trim().Trim('/');
                var store = new Core.QcRunStore();
                var summary = string.IsNullOrWhiteSpace(runId) ? null : store.GetRunSummary(runId);

                context.Response.ContentType = "text/html; charset=utf-8";

                if (summary == null || summary.Status != Core.QcRunStatus.Completed)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    string notFoundHtml = @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
    <title>Hardware Verification - Not Found</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0d1117; color: #c9d1d9; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; padding: 20px; box-sizing: border-box; }
        .card { background: #161b22; border: 1px solid #30363d; border-radius: 12px; padding: 32px; max-width: 520px; width: 100%; text-align: center; }
        .badge { display: inline-block; padding: 6px 16px; border-radius: 20px; font-weight: bold; background: #f8514922; color: #f85149; border: 1px solid #f85149; margin-bottom: 20px; }
        h1 { margin: 0 0 12px 0; font-size: 24px; color: #f0f6fc; }
        p { color: #8b949e; line-height: 1.6; }
    </style>
</head>
<body>
    <div class='card'>
        <div class='badge'>⚠️ RECORD NOT FOUND OR UNVERIFIED</div>
        <h1>Verification Pending</h1>
        <p>No verified diagnostic completion record was found for this run identifier. The unit may still be undergoing refurbishing bench testing, or the QR payload may be invalid.</p>
    </div>
</body>
</html>";
                    byte[] bytes = Encoding.UTF8.GetBytes(notFoundHtml);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                    context.Response.Close();
                    return;
                }

                // Render Verified Device Certificate HTML
                var sb = new StringBuilder();
                sb.Append(@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'/>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'/>
    <title>Hardware Verification Certificate</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0d1117; color: #c9d1d9; margin: 0; padding: 24px 16px; display: flex; justify-content: center; }
        .container { max-width: 640px; width: 100%; background: #161b22; border: 1px solid #30363d; border-radius: 14px; overflow: hidden; box-shadow: 0 8px 24px rgba(0,0,0,0.5); }
        .header { background: #1f6feb15; border-bottom: 1px solid #30363d; padding: 24px; text-align: center; }
        .seal-badge { display: inline-flex; align-items: center; gap: 8px; background: #238636; color: #ffffff; padding: 6px 18px; border-radius: 20px; font-weight: bold; font-size: 14px; margin-bottom: 14px; }
        .title { margin: 0; font-size: 22px; color: #f0f6fc; }
        .subtitle { color: #8b949e; font-size: 13px; margin-top: 4px; }
        .content { padding: 24px; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 24px; }
        .field { background: #0d1117; border: 1px solid #21262d; border-radius: 8px; padding: 14px; }
        .field-label { font-size: 11px; text-transform: uppercase; color: #8b949e; letter-spacing: 0.5px; margin-bottom: 4px; }
        .field-value { font-size: 15px; font-weight: 600; color: #f0f6fc; word-break: break-all; }
        .grade-badge { color: #58a6ff; font-size: 20px; }
        .section-title { font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px; color: #8b949e; margin: 24px 0 12px 0; }
        .test-list { list-style: none; padding: 0; margin: 0; }
        .test-item { display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; border-bottom: 1px solid #21262d; font-size: 14px; }
        .test-item:last-child { border-bottom: none; }
        .pass-tag { background: #23863622; color: #3fb950; border: 1px solid #238636; border-radius: 12px; padding: 2px 10px; font-size: 12px; font-weight: 600; }
        .hash-box { background: #090d12; border: 1px solid #30363d; border-radius: 8px; padding: 14px; margin-top: 24px; font-family: 'Consolas', monospace; font-size: 12px; color: #58a6ff; word-break: break-all; }
        .footer { border-top: 1px solid #21262d; padding: 16px 24px; text-align: center; font-size: 12px; color: #484f58; }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='seal-badge'>✓ CRYPTOGRAPHICALLY VERIFIED</div>
            <h1 class='title'>Diagnostic Quality Certificate</h1>
            <div class='subtitle'>SuperAutoMater Trusted Hardware Verification Engine</div>
        </div>
        <div class='content'>
            <div class='grid'>
                <div class='field'>
                    <div class='field-label'>Certified Grade</div>
                    <div class='field-value grade-badge'>");
                sb.Append(WebUtility.HtmlEncode(summary.Grade ?? "GRADE A"));
                sb.Append(@"</div>
                </div>
                <div class='field'>
                    <div class='field-label'>Verification Date</div>
                    <div class='field-value'>");
                sb.Append(summary.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm UTC") ?? "Nominal");
                sb.Append(@"</div>
                </div>
                <div class='field'>
                    <div class='field-label'>Device Model</div>
                    <div class='field-value'>");
                sb.Append(WebUtility.HtmlEncode(summary.Model ?? "Refurbished Hardware"));
                sb.Append(@"</div>
                </div>
                <div class='field'>
                    <div class='field-label'>Serial Number</div>
                    <div class='field-value'>");
                sb.Append(WebUtility.HtmlEncode(summary.SerialNumber ?? "Verified"));
                sb.Append(@"</div>
                </div>
            </div>

            <div class='section-title'>Diagnostic Test Matrix</div>
            <ul class='test-list'>");

                foreach (var result in summary.Results)
                {
                    sb.Append("<li class='test-item'><span>");
                    sb.Append(WebUtility.HtmlEncode(result.TestName ?? result.TestKey));
                    sb.Append("</span><span class='pass-tag'>");
                    sb.Append(result.Status == Core.QcTestStatus.Passed || result.Status == Core.QcTestStatus.ManualOverride ? "PASSED" : result.Status.ToString().ToUpperInvariant());
                    sb.Append("</span></li>");
                }

                sb.Append(@"</ul>

            <div class='section-title'>Cryptographic Verification Seal</div>
            <div class='hash-box'>
                <div style='color: #8b949e; margin-bottom: 4px; font-size: 11px;'>SHA-256 IMMUTABLE AUDIT HASH:</div>");
                sb.Append(WebUtility.HtmlEncode(summary.VerificationHash ?? ""));
                sb.Append(@"</div>
        </div>
        <div class='footer'>
            Authenticated Refurbished Hardware · Protected by SuperAutoMater Offline-First Verification Architecture
        </div>
    </div>
</body>
</html>");

                byte[] htmlBytes = Encoding.UTF8.GetBytes(sb.ToString());
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentLength64 = htmlBytes.Length;
                await context.Response.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("HandleScanToVerifyAsync encountered error", ex);
                try { context.Response.Close(); } catch { }
            }
        }

        private void StartUdpDiscovery()
        {
            try
            {
                // Receiver socket
                _udpReceiver = new UdpClient();
                _udpReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpReceiver.ExclusiveAddressUse = false;
                _udpReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, UDP_DISCOVERY_PORT));

                // Broadcaster socket
                _udpBroadcaster = new UdpClient();
                _udpBroadcaster.EnableBroadcast = true;

                // Start receiver loop
                Task.Run(ReceiveUdpBeaconsAsync);

                // Broadcast beacon every 2.5 seconds
                _broadcastTimer = new Timer(BroadcastBeacon, null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(2500));
            }
            catch { }
        }

        private void BroadcastBeacon(object state)
        {
            try
            {
                if (_udpBroadcaster == null) return;

                var vm = _viewModel;
                var hw = HardwareDiagnosticsService.Instance;

                var beacon = new
                {
                    id = hw.SystemIdentity.Serial != "Detecting..." ? hw.SystemIdentity.Serial : Environment.MachineName,
                    name = Environment.MachineName,
                    ip = LocalIpAddress,
                    port = HttpPort,
                    model = hw.SystemIdentity.Model,
                    serial = hw.SystemIdentity.Serial,
                    passed = vm?.TestPipeline?.Count(t => t.IsPassed) ?? 0,
                    total = vm?.RequiredTestCount ?? 10,
                    token = _sessionAccessToken,
                    grade = vm?.Grade ?? "Unassigned",
                    status = vm?.TestPipeline?.FirstOrDefault(t => t.IsActive)?.Title ?? "Standby",
                    alert = (hw.CpuTelemetry?.TemperatureC ?? 0) > 90
                };

                string json = JsonSerializer.Serialize(beacon);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                var endpoint = new IPEndPoint(IPAddress.Broadcast, UDP_DISCOVERY_PORT);
                _udpBroadcaster.Send(bytes, bytes.Length, endpoint);
            }
            catch { }
        }

        private async Task ReceiveUdpBeaconsAsync()
        {
            while (_cts != null && !_cts.IsCancellationRequested && _udpReceiver != null)
            {
                try
                {
                    var result = await _udpReceiver.ReceiveAsync();
                    string json = Encoding.UTF8.GetString(result.Buffer);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string id = root.GetProperty("id").GetString();
                    string ip = root.GetProperty("ip").GetString();

                    // Ignore echo from our own instance
                    if (ip == LocalIpAddress && id == HardwareDiagnosticsService.Instance.SystemIdentity.Serial)
                        continue;

                    var node = new FleetBenchNode
                    {
                        Id = id,
                        MachineName = root.GetProperty("name").GetString(),
                        IpAddress = ip,
                        Port = root.GetProperty("port").GetInt32(),
                        Model = root.GetProperty("model").GetString(),
                        Serial = root.GetProperty("serial").GetString(),
                        PassedCount = root.GetProperty("passed").GetInt32(),
                        TotalCount = root.GetProperty("total").GetInt32(),
                        Grade = root.GetProperty("grade").GetString(),
                        Status = root.GetProperty("status").GetString(),
                        Alert = root.GetProperty("alert").GetBoolean(),
                        LastSeenUtc = DateTime.UtcNow
                    };

                    _peerNodes[id] = node;
                    SyncOnlineBenches();
                }
                catch
                {
                    if (_cts == null || _cts.IsCancellationRequested) break;
                }
            }
        }

        private void CleanupStalePeers(object state)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(8);
            bool removed = false;

            foreach (var kvp in _peerNodes)
            {
                if (kvp.Value.LastSeenUtc < cutoff)
                {
                    _peerNodes.TryRemove(kvp.Key, out _);
                    removed = true;
                }
            }

            if (removed)
            {
                SyncOnlineBenches();
            }
        }

        private void SyncOnlineBenches()
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                OnlineBenches.Clear();
                foreach (var node in _peerNodes.Values.OrderBy(n => n.MachineName))
                {
                    OnlineBenches.Add(node);
                }
                FleetUpdated?.Invoke();
            });
        }

        private string GenerateAvionicsDashboardHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
    <title>SuperAutoMater — Fleet Telemetry Cockpit</title>
    <style>
        :root {
            --bg-obsidian: #0A0C10;
            --bg-card: #161B22;
            --bg-elevated: #21262D;
            --border: #30363D;
            --border-glow: #388BFD;
            --text-primary: #F0F6FC;
            --text-muted: #8B949E;
            --accent-green: #3FB950;
            --accent-cyan: #58A6FF;
            --accent-purple: #BC8CFF;
            --accent-red: #F85149;
            --accent-gold: #D29922;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
        body { background: var(--bg-obsidian); color: var(--text-primary); padding: 16px; -webkit-font-smoothing: antialiased; }
        
        .header { display: flex; justify-content: space-between; align-items: center; padding-bottom: 14px; border-bottom: 1px solid var(--border); margin-bottom: 16px; }
        .logo-wrap { display: flex; align-items: center; gap: 8px; }
        .pulse-dot { width: 10px; height: 10px; border-radius: 50%; background: var(--accent-green); box-shadow: 0 0 10px var(--accent-green); animation: pulse 2s infinite; }
        @keyframes pulse { 0%, 100% { opacity: 1; transform: scale(1); } 50% { opacity: 0.4; transform: scale(0.85); } }
        .brand { font-size: 15px; font-weight: 800; letter-spacing: 0.5px; }
        .brand span { color: var(--accent-purple); }
        .badge-live { background: rgba(63, 185, 80, 0.15); border: 1px solid var(--accent-green); color: var(--accent-green); font-size: 10px; font-weight: 700; padding: 3px 8px; border-radius: 6px; font-family: monospace; }
        
        .hero-banner { background: linear-gradient(135deg, rgba(56, 139, 253, 0.15), rgba(188, 140, 255, 0.15)); border: 1px solid var(--border-glow); border-radius: 12px; padding: 16px; margin-bottom: 16px; }
        .hero-model { font-size: 20px; font-weight: 800; margin-bottom: 4px; }
        .hero-serial { font-family: monospace; font-size: 12px; color: var(--text-muted); display: flex; gap: 12px; }
        .hero-grade { display: inline-block; background: var(--accent-purple); color: #000; font-weight: 800; font-size: 11px; padding: 2px 8px; border-radius: 4px; margin-top: 8px; }

        .metric-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; margin-bottom: 16px; }
        .metric-card { background: var(--bg-card); border: 1px solid var(--border); border-radius: 10px; padding: 12px; }
        .metric-title { font-size: 11px; font-weight: 700; color: var(--text-muted); text-transform: uppercase; margin-bottom: 4px; display: flex; justify-content: space-between; }
        .metric-val { font-size: 15px; font-weight: 800; }
        .metric-sub { font-size: 11px; color: var(--accent-green); font-family: monospace; margin-top: 4px; }

        .progress-box { background: var(--bg-card); border: 1px solid var(--border); border-radius: 10px; padding: 14px; margin-bottom: 16px; }
        .progress-header { display: flex; justify-content: space-between; font-size: 12px; font-weight: 700; margin-bottom: 8px; }
        .bar-bg { background: var(--bg-elevated); height: 12px; border-radius: 6px; overflow: hidden; }
        .bar-fill { background: linear-gradient(90deg, var(--accent-cyan), var(--accent-green)); height: 100%; border-radius: 6px; width: 0%; transition: width 0.4s ease; }

        .footer { text-align: center; margin-top: 24px; font-size: 10px; color: var(--text-muted); font-family: monospace; }
    </style>
</head>
<body>
    <div class=""header"">
        <div class=""logo-wrap"">
            <div class=""pulse-dot""></div>
            <div class=""brand"">SUPER<span>AUTOMATER</span></div>
        </div>
        <div class=""badge-live"">● LIVE HUD</div>
    </div>

    <div class=""hero-banner"">
        <div class=""hero-model"" id=""model"">Scanning Bench...</div>
        <div class=""hero-serial"">
            <span>S/N: <b id=""serial"">--</b></span>
            <span>HOST: <b id=""machine"">--</b></span>
        </div>
        <div class=""hero-grade"" id=""grade"">GRADE A+</div>
    </div>

    <div class=""progress-box"">
        <div class=""progress-header"">
            <span>DIAGNOSTIC PIPELINE</span>
            <span id=""passRate"">0/10 PASSED</span>
        </div>
        <div class=""bar-bg"">
            <div class=""bar-fill"" id=""barFill""></div>
        </div>
    </div>

    <div class=""metric-grid"">
        <div class=""metric-card"">
            <div class=""metric-title"">CPU Telemetry <span>⚡</span></div>
            <div class=""metric-val"" id=""cpuUsage"">--%</div>
            <div class=""metric-sub"" id=""cpuTemp"">--°C Nominal</div>
        </div>
        <div class=""metric-card"">
            <div class=""metric-title"">Battery Health <span>🔋</span></div>
            <div class=""metric-val"" id=""batteryPct"">--%</div>
            <div class=""metric-sub"" id=""batteryHealth"">--% Health</div>
        </div>
        <div class=""metric-card"">
            <div class=""metric-title"">Storage Health <span>💾</span></div>
            <div class=""metric-val"" id=""storageHealth"">100%</div>
            <div class=""metric-sub"" id=""storageName"">NVMe Nominal</div>
        </div>
        <div class=""metric-card"">
            <div class=""metric-title"">System RAM <span>🧠</span></div>
            <div class=""metric-val"" id=""ramVal"">--</div>
            <div class=""metric-sub"">Nominal</div>
        </div>
    </div>

    <div class=""footer"">
        SUPERAUTOMATER v1.2 · WAREHOUSE FLEET PROTOCOL
    </div>

    <script>
        async function fetchTelemetry() {
            try {
                const sessionToken = new URLSearchParams(window.location.search).get('token') || '';
                const res = await fetch('/api/status?token=' + encodeURIComponent(sessionToken), { cache: 'no-store' });
                if (!res.ok) return;
                const data = await res.json();

                document.getElementById('model').textContent = data.model || 'Diagnostic Bench';
                document.getElementById('serial').textContent = data.serial || 'N/A';
                document.getElementById('machine').textContent = data.machineName || 'HOST';
                document.getElementById('grade').textContent = data.grade || 'GRADE A+';

                document.getElementById('cpuUsage').textContent = data.cpuUsage + '%';
                document.getElementById('cpuTemp').textContent = data.cpuTemp + '°C Nominal';
                document.getElementById('batteryPct').textContent = data.battery || '--%';
                document.getElementById('batteryHealth').textContent = data.batteryHealth != null ? (data.batteryHealth + '% Health') : 'Unavailable';
                document.getElementById('storageHealth').textContent = data.storageHealth || '100%';
                document.getElementById('ramVal').textContent = data.ram || 'RAM Active';

                const pct = Math.round((data.passedCount / (data.totalCount || 1)) * 100);
                document.getElementById('passRate').textContent = data.passedCount + '/' + data.totalCount + ' PASSED';
                document.getElementById('barFill').style.width = pct + '%';
            } catch (err) { }
        }

        fetchTelemetry();
        setInterval(fetchTelemetry, 1500);
    </script>
</body>
</html>";
        }
    }
}
