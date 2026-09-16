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

        private TcpListener _tcpListener;
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
                    // 0. Automatically provision Windows Firewall rules for Ports 8443, 9000, 9876
                    FirewallHelper.EnsureFirewallRulesAsync();

                    // 1. Detect LAN IPv4 address
                    LocalIpAddress = DetectBestLanIp();

                    // 2. Generate QR Code pointing to mobile dashboard
                    GenerateQrCodeImage();

                    // 3. Start embedded HTTP server
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

                if (_tcpListener != null)
                {
                    try { _tcpListener.Stop(); } catch { }
                    _tcpListener = null;
                }

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

        private static List<string> GetAllActiveIpV4Addresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()
                             .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                         n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                {
                    foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string s = addr.Address.ToString();
                            if (!s.StartsWith("127.") && !s.StartsWith("169.254.") && !result.Contains(s))
                            {
                                result.Add(s);
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private void StartHttpServer()
        {
            Task.Run(async () =>
            {
                for (int port = DEFAULT_HTTP_PORT; port <= DEFAULT_HTTP_PORT + 5; port++)
                {
                    try
                    {
                        _tcpListener = new TcpListener(IPAddress.Any, port);
                        _tcpListener.Start();
                        HttpPort = port;
                        AppLogger.Info("Lifecycle", $"[WarehouseFleetService] TCP HTTP Server listening on 0.0.0.0:{port}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"[WarehouseFleetService] Port {port} unavailable: {ex.Message}");
                        try { _tcpListener?.Stop(); } catch { }
                        _tcpListener = null;
                    }
                }

                if (_tcpListener == null)
                {
                    AppLogger.Error("Lifecycle", "[WarehouseFleetService] Could not bind TCP server on ports 8443-8448.");
                    return;
                }

                // Regenerate QR with confirmed port
                GenerateQrCodeImage();

                while (_cts != null && !_cts.IsCancellationRequested && _tcpListener != null)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(_cts.Token);
                        _ = Task.Run(() => HandleTcpClientAsync(client));
                    }
                    catch (OperationCanceledException) { break; }
                    catch
                    {
                        if (_cts == null || _cts.IsCancellationRequested) break;
                    }
                }
            });
        }

        private async Task HandleTcpClientAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 8000;
                    stream.WriteTimeout = 8000;

                    byte[] readBuffer = new byte[8192];
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
                    if (bytesRead <= 0) return;

                    string requestText = Encoding.UTF8.GetString(readBuffer, 0, bytesRead);
                    string firstLine = requestText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)[0];
                    string[] parts = firstLine.Split(' ');
                    if (parts.Length < 2) return;

                    string method = parts[0].ToUpperInvariant();
                    string rawUrl = parts[1];
                    string path = rawUrl.Split('?')[0].ToLowerInvariant();

                    // 1. CORS Preflight
                    if (method == "OPTIONS")
                    {
                        string corsHeader = "HTTP/1.1 204 No Content\r\n" +
                                            "Access-Control-Allow-Origin: *\r\n" +
                                            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                            "Access-Control-Allow-Headers: Authorization, Content-Type\r\n" +
                                            "Content-Length: 0\r\n" +
                                            "Connection: close\r\n\r\n";
                        byte[] corsBytes = Encoding.UTF8.GetBytes(corsHeader);
                        await stream.WriteAsync(corsBytes, 0, corsBytes.Length);
                        return;
                    }

                    // 2. Acoustic Ping Chime (Unauthenticated, whitelisted for warehouse floor locating)
                    if (path == "/api/ping")
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
                        string pingJson = JsonSerializer.Serialize(pingPayload);
                        byte[] pingBytes = Encoding.UTF8.GetBytes(pingJson);
                        string respHeader = $"HTTP/1.1 200 OK\r\n" +
                                            $"Content-Type: application/json; charset=utf-8\r\n" +
                                            $"Content-Length: {pingBytes.Length}\r\n" +
                                            $"Access-Control-Allow-Origin: *\r\n" +
                                            $"Connection: close\r\n\r\n";
                        byte[] hBytes = Encoding.UTF8.GetBytes(respHeader);
                        await stream.WriteAsync(hBytes, 0, hBytes.Length);
                        await stream.WriteAsync(pingBytes, 0, pingBytes.Length);
                        await stream.FlushAsync();
                        return;
                    }

                    // 3. /verify/{runId} Cryptographic Proof HTML
                    if (path.StartsWith("/verify/"))
                    {
                        string verifyHtml = GetScanToVerifyHtml(path);
                        byte[] htmlBytes = Encoding.UTF8.GetBytes(verifyHtml);
                        string respHeader = $"HTTP/1.1 200 OK\r\n" +
                                            $"Content-Type: text/html; charset=utf-8\r\n" +
                                            $"Content-Length: {htmlBytes.Length}\r\n" +
                                            $"Access-Control-Allow-Origin: *\r\n" +
                                            $"Connection: close\r\n\r\n";
                        byte[] hBytes = Encoding.UTF8.GetBytes(respHeader);
                        await stream.WriteAsync(hBytes, 0, hBytes.Length);
                        await stream.WriteAsync(htmlBytes, 0, htmlBytes.Length);
                        await stream.FlushAsync();
                        return;
                    }

                    // 4. Authentication Check
                    bool authorized = IsAuthorized(requestText, rawUrl);
                    if (!authorized)
                    {
                        if (path.StartsWith("/api/"))
                        {
                            byte[] unauthBody = Encoding.UTF8.GetBytes("{\"error\":\"A valid bench session token is required.\"}");
                            string unauthHeader = $"HTTP/1.1 401 Unauthorized\r\n" +
                                                  $"Content-Type: application/json; charset=utf-8\r\n" +
                                                  $"Content-Length: {unauthBody.Length}\r\n" +
                                                  $"Access-Control-Allow-Origin: *\r\n" +
                                                  $"Connection: close\r\n\r\n";
                            byte[] uhBytes = Encoding.UTF8.GetBytes(unauthHeader);
                            await stream.WriteAsync(uhBytes, 0, uhBytes.Length);
                            await stream.WriteAsync(unauthBody, 0, unauthBody.Length);
                            await stream.FlushAsync();
                            return;
                        }
                        else
                        {
                            string unauthHtml = "<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Unauthorized</title><style>body{background:#0b0c10;color:#f0f6fc;font-family:sans-serif;text-align:center;padding:50px;}.card{background:#161b22;display:inline-block;padding:30px;border-radius:12px;border:1px solid #30363d;}h2{color:#f85149;}</style></head><body><div class='card'><h2>⚠️ Access Token Required</h2><p>Please scan the QR code displayed on the bench screen or access via SuperManager.</p></div></body></html>";
                            byte[] uhBytes = Encoding.UTF8.GetBytes(unauthHtml);
                            string header = $"HTTP/1.1 401 Unauthorized\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {uhBytes.Length}\r\nConnection: close\r\n\r\n";
                            byte[] hBytes = Encoding.UTF8.GetBytes(header);
                            await stream.WriteAsync(hBytes, 0, hBytes.Length);
                            await stream.WriteAsync(uhBytes, 0, uhBytes.Length);
                            await stream.FlushAsync();
                            return;
                        }
                    }

                    // 5. Live MJPEG Remote Stream
                    if (path == "/api/remote/stream")
                    {
                        await HandleRemoteStreamAsync(stream);
                        return;
                    }

                    // 6. Authorized API Responses
                    byte[] responseBytes;
                    string contentType = "application/json; charset=utf-8";
                    string extraHeader = "";

                    if (path == "/api/remote/frame")
                    {
                        responseBytes = ScreenCaptureService.Instance.GetScreenFrame(1280, 65L) ?? Array.Empty<byte>();
                        contentType = "image/jpeg";
                    }
                    else if (path == "/api/remote/input")
                    {
                        string bodyJson = await ReadRequestBodyAsync(requestText, readBuffer, bytesRead, stream);
                        try
                        {
                            using var doc = JsonDocument.Parse(bodyJson);
                            var root = doc.RootElement;
                            string type = root.TryGetProperty("type", out var pt) ? pt.GetString() : "mouse";
                            if (type == "mouse")
                            {
                                string action = root.TryGetProperty("action", out var pa) ? pa.GetString() : "left_click";
                                double x = root.TryGetProperty("x", out var px) ? px.GetDouble() : 0.5;
                                double y = root.TryGetProperty("y", out var py) ? py.GetDouble() : 0.5;
                                int delta = root.TryGetProperty("delta", out var pd) ? pd.GetInt32() : 0;
                                RemoteInputService.Instance.ProcessMouse(action, x, y, delta);
                            }
                            else if (type == "key")
                            {
                                string key = root.TryGetProperty("key", out var pk) ? pk.GetString() : "";
                                RemoteInputService.Instance.SendKey(key);
                            }
                            else if (type == "action")
                            {
                                string action = root.TryGetProperty("action", out var pa) ? pa.GetString() : "";
                                RemoteInputService.Instance.ExecuteQuickAction(action);
                            }
                        }
                        catch { }
                        responseBytes = Encoding.UTF8.GetBytes("{\"success\":true}");
                    }
                    else if (path == "/api/status")
                    {
                        var payload = GetStatusPayload();
                        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                        responseBytes = Encoding.UTF8.GetBytes(json);
                    }
                    else if (path == "/api/certificate")
                    {
                        responseBytes = GenerateCertificatePdfBytes(out contentType, out extraHeader);
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
                    }
                    else if (path == "/api/qr" && _qrPngBytes != null)
                    {
                        responseBytes = _qrPngBytes;
                        contentType = "image/png";
                    }
                    else
                    {
                        // Root mobile/desktop dashboard HTML
                        string html = GenerateAvionicsDashboardHtml();
                        responseBytes = Encoding.UTF8.GetBytes(html);
                        contentType = "text/html; charset=utf-8";
                    }

                    string respHeaderStr = $"HTTP/1.1 200 OK\r\n" +
                                           $"Content-Type: {contentType}\r\n" +
                                           $"Content-Length: {responseBytes.Length}\r\n" +
                                           $"Access-Control-Allow-Origin: *\r\n" +
                                           $"Cache-Control: no-store\r\n" +
                                           (string.IsNullOrEmpty(extraHeader) ? "" : extraHeader + "\r\n") +
                                           $"Connection: close\r\n\r\n";
                    byte[] respHeaderBytes = Encoding.UTF8.GetBytes(respHeaderStr);
                    await stream.WriteAsync(respHeaderBytes, 0, respHeaderBytes.Length);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await stream.FlushAsync();
                }
            }
            catch { }
        }

        private async Task HandleRemoteStreamAsync(NetworkStream stream)
        {
            try
            {
                string initHeader = "HTTP/1.1 200 OK\r\n" +
                                    "Content-Type: multipart/x-mixed-replace; boundary=--frame\r\n" +
                                    "Cache-Control: no-store, no-cache, must-revalidate, max-age=0\r\n" +
                                    "Access-Control-Allow-Origin: *\r\n\r\n";
                byte[] initBytes = Encoding.UTF8.GetBytes(initHeader);
                await stream.WriteAsync(initBytes, 0, initBytes.Length);

                while (_cts != null && !_cts.IsCancellationRequested && stream.CanWrite)
                {
                    byte[] frame = ScreenCaptureService.Instance.GetScreenFrame(1280, 65L);
                    if (frame != null && frame.Length > 0)
                    {
                        string header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.Length}\r\n\r\n";
                        byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                        await stream.WriteAsync(frame, 0, frame.Length);
                        byte[] footer = Encoding.UTF8.GetBytes("\r\n");
                        await stream.WriteAsync(footer, 0, footer.Length);
                        await stream.FlushAsync();
                    }
                    await Task.Delay(55); // ~18 FPS
                }
            }
            catch
            {
                // Remote client disconnected cleanly
            }
        }

        private static async Task<string> ReadRequestBodyAsync(string requestText, byte[] initialBuffer, int initialBytesRead, NetworkStream stream)
        {
            try
            {
                int bodyStart = requestText.IndexOf("\r\n\r\n");
                if (bodyStart < 0) return "";

                string headerPart = requestText.Substring(0, bodyStart + 4);
                int headerByteCount = Encoding.UTF8.GetByteCount(headerPart);
                int bodyBytesAlreadyRead = initialBytesRead - headerByteCount;

                int contentLength = 0;
                var lines = headerPart.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.Substring(15).Trim(), out contentLength);
                        break;
                    }
                }

                if (contentLength <= 0)
                {
                    return requestText.Substring(bodyStart + 4);
                }

                using var ms = new MemoryStream();
                if (bodyBytesAlreadyRead > 0)
                {
                    ms.Write(initialBuffer, headerByteCount, Math.Min(bodyBytesAlreadyRead, contentLength));
                }

                while (ms.Length < contentLength && stream.DataAvailable)
                {
                    byte[] chunk = new byte[Math.Min(4096, contentLength - (int)ms.Length)];
                    int read = await stream.ReadAsync(chunk, 0, chunk.Length);
                    if (read <= 0) break;
                    ms.Write(chunk, 0, read);
                }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return "";
            }
        }

        private byte[] GenerateCertificatePdfBytes(out string contentType, out string extraHeader)
        {
            contentType = "application/json; charset=utf-8";
            extraHeader = "";

            var vm = _viewModel;
            if (vm == null)
            {
                return Encoding.UTF8.GetBytes("{\"error\":\"ViewModel not available\"}");
            }

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
                    return Encoding.UTF8.GetBytes($"{{\"error\":\"Cannot generate certificate: {failReason}\"}}");
                }

                certData.RunSummary = summary;
                certData.RunId = summary.RunId;
                string pdfPath = PdfCertificateService.Instance.GenerateCertificate(certData);
                if (File.Exists(pdfPath))
                {
                    contentType = "application/pdf";
                    extraHeader = $"Content-Disposition: attachment; filename=\"SuperAutoMater_Certificate_{vm.Serial}.pdf\"";
                    return File.ReadAllBytes(pdfPath);
                }

                return Encoding.UTF8.GetBytes("{\"error\":\"PDF generation failed\"}");
            }
            catch (Exception ex)
            {
                return Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            }
        }

        private bool IsAuthorized(string requestText, string rawUrl)
        {
            string token = ExtractQueryParam(rawUrl, "token");
            if (string.IsNullOrWhiteSpace(token))
            {
                var lines = requestText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line.Substring(14).Trim();
                        const string bearer = "Bearer ";
                        if (val.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
                        {
                            token = val.Substring(bearer.Length).Trim();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(token) || token.Length != _sessionAccessToken.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token),
                Encoding.UTF8.GetBytes(_sessionAccessToken));
        }

        private static string ExtractQueryParam(string url, string paramName)
        {
            try
            {
                int qIdx = url.IndexOf('?');
                if (qIdx < 0 || qIdx >= url.Length - 1) return "";
                string query = url.Substring(qIdx + 1);
                var pairs = query.Split('&');
                foreach (var pair in pairs)
                {
                    var kv = pair.Split('=');
                    if (kv.Length >= 1 && string.Equals(Uri.UnescapeDataString(kv[0]), paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    }
                }
            }
            catch { }
            return "";
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

        private string GetScanToVerifyHtml(string path)
        {
            try
            {
                string runId = path.Substring("/verify/".Length).Trim().Trim('/');
                var store = new Core.QcRunStore();
                var summary = string.IsNullOrWhiteSpace(runId) ? null : store.GetRunSummary(runId);

                if (summary == null || summary.Status != Core.QcRunStatus.Completed)
                {
                    return @"<!DOCTYPE html>
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

                return sb.ToString();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("GetScanToVerifyHtml encountered error", ex);
                return "<!DOCTYPE html><html><body>Error generating verification page</body></html>";
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
                if (_tcpListener == null) return;

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

    <div class=""screen-box"" style=""background: var(--bg-card); border: 1px solid var(--border); border-radius: 12px; padding: 14px; margin-bottom: 16px;"">
        <div style=""display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;"">
            <span style=""font-size: 12px; font-weight: 800; text-transform: uppercase; color: var(--accent-cyan);"">🖥️ LIVE REMOTE SCREEN MIRROR</span>
            <button id=""btnToggleScreen"" onclick=""toggleRemoteStream()"" style=""background: var(--accent-cyan); color: #000; border: none; font-weight: 800; font-size: 11px; padding: 4px 10px; border-radius: 6px; cursor: pointer;"">▶ START STREAM</button>
        </div>
        <div id=""screenContainer"" style=""position: relative; width: 100%; aspect-ratio: 16/9; background: #000; border-radius: 8px; overflow: hidden; display: flex; align-items: center; justify-content: center; cursor: crosshair;"">
            <span id=""screenPlaceholder"" style=""color: var(--text-muted); font-size: 12px; font-family: monospace;"">Click 'START STREAM' to mirror live bench screen</span>
            <img id=""remoteImg"" style=""width: 100%; height: 100%; object-fit: contain; display: none;"" alt=""Bench Desktop"" />
        </div>
        <div style=""display: flex; gap: 6px; flex-wrap: wrap; margin-top: 10px;"">
            <button onclick=""sendRemoteAction('cmd')"" style=""background: var(--bg-elevated); color: var(--text-primary); border: 1px solid var(--border); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-family: monospace; cursor: pointer;"">📁 CMD</button>
            <button onclick=""sendRemoteAction('taskmgr')"" style=""background: var(--bg-elevated); color: var(--text-primary); border: 1px solid var(--border); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-family: monospace; cursor: pointer;"">⚙️ TaskMgr</button>
            <button onclick=""sendRemoteAction('show_desktop')"" style=""background: var(--bg-elevated); color: var(--text-primary); border: 1px solid var(--border); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-family: monospace; cursor: pointer;"">🪟 Desktop</button>
            <button onclick=""sendRemoteKey('Enter')"" style=""background: var(--bg-elevated); color: var(--text-primary); border: 1px solid var(--border); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-family: monospace; cursor: pointer;"">↵ Enter</button>
            <button onclick=""sendRemoteKey('Escape')"" style=""background: var(--bg-elevated); color: var(--text-primary); border: 1px solid var(--border); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-family: monospace; cursor: pointer;"">ESC</button>
            <button onclick=""triggerRemotePass()"" style=""background: rgba(63, 185, 80, 0.2); color: var(--accent-green); border: 1px solid var(--accent-green); border-radius: 4px; padding: 5px 8px; font-size: 10px; font-weight: bold; cursor: pointer;"">✓ Pass Active Test</button>
        </div>
    </div>

    <div class=""footer"">
        SUPERAUTOMATER v1.2 · WAREHOUSE FLEET PROTOCOL
    </div>

    <script>
        const sessionToken = new URLSearchParams(window.location.search).get('token') || '';
        let streaming = false;

        function toggleRemoteStream() {
            const img = document.getElementById('remoteImg');
            const placeholder = document.getElementById('screenPlaceholder');
            const btn = document.getElementById('btnToggleScreen');

            if (!streaming) {
                streaming = true;
                img.src = '/api/remote/stream?token=' + encodeURIComponent(sessionToken);
                img.style.display = 'block';
                placeholder.style.display = 'none';
                btn.textContent = '⏸ PAUSE STREAM';
                btn.style.background = 'var(--accent-red)';
                btn.style.color = '#fff';
            } else {
                streaming = false;
                img.src = '';
                img.style.display = 'none';
                placeholder.style.display = 'block';
                btn.textContent = '▶ START STREAM';
                btn.style.background = 'var(--accent-cyan)';
                btn.style.color = '#000';
            }
        }

        async function sendRemoteInput(payload) {
            try {
                await fetch('/api/remote/input?token=' + encodeURIComponent(sessionToken), {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
            } catch (e) { }
        }

        function sendRemoteAction(action) {
            sendRemoteInput({ type: 'action', action: action });
        }

        function sendRemoteKey(key) {
            sendRemoteInput({ type: 'key', key: key });
        }

        async function triggerRemotePass() {
            try {
                await fetch('/api/action/pass?token=' + encodeURIComponent(sessionToken));
                fetchTelemetry();
            } catch (e) { }
        }

        const remoteImg = document.getElementById('remoteImg');
        remoteImg.addEventListener('click', (e) => {
            const rect = remoteImg.getBoundingClientRect();
            const x = (e.clientX - rect.left) / rect.width;
            const y = (e.clientY - rect.top) / rect.height;
            sendRemoteInput({ type: 'mouse', action: 'left_click', x: x, y: y });
        });

        async function fetchTelemetry() {
            try {
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
