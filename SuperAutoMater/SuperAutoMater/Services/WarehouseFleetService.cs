using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
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

        public string LocalIpAddress { get; private set; } = "127.0.0.1";
        public int HttpPort { get; private set; } = DEFAULT_HTTP_PORT;
        public string LocalDashboardUrl => $"http://{LocalIpAddress}:{HttpPort}";
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
                    .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                                            n.NetworkInterfaceType == NetworkInterfaceType.Ethernet);

                foreach (var iface in interfaces)
                {
                    var props = iface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172."))
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
                byte[] responseBytes;
                string contentType;

                if (path == "/api/status")
                {
                    var payload = GetStatusPayload();
                    string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                    responseBytes = Encoding.UTF8.GetBytes(json);
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
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                await context.Response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private object GetStatusPayload()
        {
            var vm = _viewModel;
            var hw = HardwareDiagnosticsService.Instance;

            int passed = vm?.TestPipeline?.Count(t => t.IsPassed) ?? 0;
            int total = vm?.TestPipeline?.Count ?? 10;

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
                ram = vm?.RamSummary ?? "RAM Nominal",
                battery = $"{vm?.BatteryCharge ?? 100}%",
                batteryHealth = hw.BatteryTelemetry?.HealthPercent ?? 100,
                storage = vm?.StorageSummary ?? "Storage Nominal",
                storageHealth = vm?.HealthBadge ?? "100%",
                grade = vm?.Grade ?? "GRADE A+",
                passedCount = passed,
                totalCount = total,
                activeTest = vm?.TestPipeline?.FirstOrDefault(t => t.IsActive)?.Title ?? "Standby",
                alert = (hw.CpuTelemetry?.TemperatureC ?? 0) > 90 || (hw.BatteryTelemetry?.HealthPercent ?? 100) < 60,
                timestamp = DateTime.UtcNow.ToString("o")
            };
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
                    total = vm?.TestPipeline?.Count ?? 10,
                    grade = vm?.Grade ?? "GRADE A+",
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
                const res = await fetch('/api/status');
                if (!res.ok) return;
                const data = await res.json();

                document.getElementById('model').textContent = data.model || 'Diagnostic Bench';
                document.getElementById('serial').textContent = data.serial || 'N/A';
                document.getElementById('machine').textContent = data.machineName || 'HOST';
                document.getElementById('grade').textContent = data.grade || 'GRADE A+';

                document.getElementById('cpuUsage').textContent = data.cpuUsage + '%';
                document.getElementById('cpuTemp').textContent = data.cpuTemp + '°C Nominal';
                document.getElementById('batteryPct').textContent = data.battery || '--%';
                document.getElementById('batteryHealth').textContent = data.batteryHealth + '% Health';
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
