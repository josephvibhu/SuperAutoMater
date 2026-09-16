using System;
using System.Diagnostics;
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
using System.Windows.Media.Imaging;
using QRCoder;
using SuperAutoMater.Wpf.Core;
using SuperManager.Models;

namespace SuperManager.Services
{
    public class ManagerWebServer
    {
        private static readonly Lazy<ManagerWebServer> _instance =
            new Lazy<ManagerWebServer>(() => new ManagerWebServer());
        public static ManagerWebServer Instance => _instance.Value;

        private const int DEFAULT_PORT = 9000;
        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private byte[] _qrPngBytes;
        private readonly string _adminSessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        public int Port { get; private set; } = DEFAULT_PORT;
        public string LocalIpAddress { get; private set; } = "127.0.0.1";
        /// <summary>
        /// Bind address for the TCP listener. Defaults to IPAddress.Any so the HUD is reachable
        /// from other machines on the same LAN. Set to "127.0.0.1" to restrict to local only.
        /// </summary>
        public string BindHost { get; set; } = "0.0.0.0";
        public string AdminToken => _adminSessionToken;
        public string DashboardUrl => $"http://{LocalIpAddress}:{Port}/?token={_adminSessionToken}";
        public BitmapSource QrCodeBitmap { get; private set; }

        private ManagerWebServer() { }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                LocalIpAddress = DetectLanIp();
                GenerateQrCode();

                for (int p = DEFAULT_PORT; p <= DEFAULT_PORT + 10; p++)
                {
                    try
                    {
                        // Bind to any/all adapters (0.0.0.0) or a specific configured IP
                        IPAddress bindAddress = (BindHost == "0.0.0.0" || BindHost == "*")
                            ? IPAddress.Any
                            : (IPAddress.TryParse(BindHost, out var parsed) ? parsed : IPAddress.Any);
                        _tcpListener = new TcpListener(bindAddress, p);
                        _tcpListener.Start();
                        Port = p;
                        AppLogger.Info($"[ManagerWebServer] Listening on {bindAddress}:{p} (LAN accessible).");
                        break;
                    }
                    catch
                    {
                        try { _tcpListener?.Stop(); } catch { }
                        _tcpListener = null;
                    }
                }

                if (_tcpListener == null) return;

                GenerateQrCode(); // Rebuild QR with confirmed port & IP

                while (_cts != null && !_cts.IsCancellationRequested)
                {
                    try
                    {
                        var client = await _tcpListener.AcceptTcpClientAsync(_cts.Token);
                        _ = Task.Run(() => HandleClientAsync(client));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        if (_cts == null || _cts.IsCancellationRequested) break;
                    }
                }
            });
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                if (_tcpListener != null)
                {
                    _tcpListener.Stop();
                    _tcpListener = null;
                }
                _cts = null;
            }
            catch { }
        }

        private async Task HandleClientAsync(TcpClient client)
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

                    if (method == "OPTIONS")
                    {
                        string corsHeader = $"HTTP/1.1 204 No Content\r\n" +
                                            $"Access-Control-Allow-Origin: *\r\n" +
                                            $"Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                                            $"Access-Control-Allow-Headers: Authorization, Content-Type\r\n" +
                                            $"Content-Length: 0\r\n" +
                                            $"Connection: close\r\n\r\n";
                        byte[] corsBytes = Encoding.UTF8.GetBytes(corsHeader);
                        await stream.WriteAsync(corsBytes, 0, corsBytes.Length);
                        return;
                    }

                    if (path.StartsWith("/api/"))
                    {
                        bool isAuthorized = IsAuthorized(requestText, rawUrl);
                        if (!isAuthorized)
                        {
                            AppLogger.Warn($"[Security] Unauthorized access attempt to {path} from {client.Client?.RemoteEndPoint}");
                            byte[] unauthBody = Encoding.UTF8.GetBytes("{\"error\":\"Valid admin authorization token is required.\"}");
                            string unauthHeader = $"HTTP/1.1 401 Unauthorized\r\n" +
                                                  $"Content-Type: application/json; charset=utf-8\r\n" +
                                                  $"Content-Length: {unauthBody.Length}\r\n" +
                                                  $"Access-Control-Allow-Origin: *\r\n" +
                                                  $"Connection: close\r\n\r\n";
                            byte[] unauthHeaderBytes = Encoding.UTF8.GetBytes(unauthHeader);
                            await stream.WriteAsync(unauthHeaderBytes, 0, unauthHeaderBytes.Length);
                            await stream.WriteAsync(unauthBody, 0, unauthBody.Length);
                            await stream.FlushAsync();
                            return;
                        }
                    }

                    byte[] body;
                    string contentType;

                    if (path == "/api/wip")
                    {
                        var journeyService = new WarehouseJourneyService();
                        var board = journeyService.GetWipBoard();
                        string json = JsonSerializer.Serialize(board, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/wip/asset")
                    {
                        string assetId = ExtractQueryParam(rawUrl, "id");
                        var journeyService = new WarehouseJourneyService();
                        var journey = journeyService.GetAssetJourney(assetId);
                        string json = JsonSerializer.Serialize(journey, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/fleet")
                    {
                        var benches = FleetDiscoveryService.Instance.Devices.ToList();
                        var kpis = InventoryStorageService.Instance.ComputeKpiSummary(benches);
                        var payload = new
                        {
                            timestamp = DateTime.UtcNow.ToString("o"),
                            kpis,
                            benches = benches.Select(b => new
                            {
                                b.Id,
                                b.MachineName,
                                b.IpAddress,
                                b.Port,
                                b.Model,
                                b.Serial,
                                b.PassedCount,
                                b.TotalCount,
                                b.ProgressText,
                                b.ProgressPercent,
                                b.Grade,
                                b.Status,
                                b.StatusBadge,
                                b.Alert,
                                b.CpuTemp,
                                b.CpuUsage,
                                b.Battery,
                                b.BatteryHealth,
                                b.Storage,
                                b.StorageHealth,
                                b.IsOnline,
                                b.LastSeenSummary,
                                b.AccentHex,
                                b.Url,
                                b.CertificateUrl
                            })
                        };
                        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/depot/analytics")
                    {
                        var kpis = DepotAnalyticsService.Instance.GetDepotKpis();
                        string json = JsonSerializer.Serialize(kpis, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/depot/wip/aging")
                    {
                        string limitStr = ExtractQueryParam(rawUrl, "limit");
                        int limit = int.TryParse(limitStr, out var lim) && lim > 0 ? lim : 10;
                        var aging = DepotAnalyticsService.Instance.GetAgingWip(limit);
                        string json = JsonSerializer.Serialize(aging, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/depot/audit")
                    {
                        string limitStr = ExtractQueryParam(rawUrl, "limit");
                        int limit = int.TryParse(limitStr, out var lim) && lim > 0 ? lim : 50;
                        var logs = DepotAnalyticsService.Instance.GetAuditLogs(limit);
                        string json = JsonSerializer.Serialize(logs, new JsonSerializerOptions { WriteIndented = true });
                        body = Encoding.UTF8.GetBytes(json);
                        contentType = "application/json; charset=utf-8";
                    }
                    else if (path == "/api/qr" && _qrPngBytes != null)
                    {
                        body = _qrPngBytes;
                        contentType = "image/png";
                    }
                    else
                    {
                        string html = GenerateWebDashboardHtml();
                        body = Encoding.UTF8.GetBytes(html);
                        contentType = "text/html; charset=utf-8";
                    }

                    string responseHeader = $"HTTP/1.1 200 OK\r\n" +
                                            $"Content-Type: {contentType}\r\n" +
                                            $"Content-Length: {body.Length}\r\n" +
                                            $"Connection: close\r\n" +
                                            $"Access-Control-Allow-Origin: *\r\n\r\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeader);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                    await stream.WriteAsync(body, 0, body.Length);
                    await stream.FlushAsync();
                }
            }
            catch { }
        }

        private bool IsAuthorized(string requestText, string rawUrl)
        {
            if (rawUrl.Contains($"token={_adminSessionToken}")) return true;
            if (requestText.Contains($"Authorization: Bearer {_adminSessionToken}", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ExtractQueryParam(string rawUrl, string key)
        {
            var parts = rawUrl.Split('?');
            if (parts.Length < 2) return "";
            var pairs = parts[1].Split('&');
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(kv[1]);
                }
            }
            return "";
        }

        private string DetectLanIp()
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

        private void GenerateQrCode()
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(DashboardUrl, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(qrCodeData);
                _qrPngBytes = qrCode.GetGraphic(12, new byte[] { 0x58, 0xA6, 0xFF }, new byte[] { 0x0E, 0x11, 0x16 });

                var bmp = new BitmapImage();
                using var ms = new MemoryStream(_qrPngBytes);
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                QrCodeBitmap = bmp;
            }
            catch { }
        }


        private string GenerateWebDashboardHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>SuperManager — Warehouse Fleet Cockpit</title>
    <style>
        * { margin:0; padding:0; box-sizing:border-box; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,monospace; }
        body { background:#0A0C10; color:#E1E4E8; padding:16px; }
        .header { display:flex; justify-content:space-between; align-items:center; background:#161B22; border:1px solid #30363D; border-radius:12px; padding:14px 20px; margin-bottom:16px; }
        .title { font-size:18px; font-weight:800; color:#FFF; display:flex; align-items:center; gap:8px; }
        .badge { background:#21262D; border:1px solid #388BFD; color:#58A6FF; font-size:11px; font-weight:700; padding:4px 8px; border-radius:6px; }
        .kpi-row { display:grid; grid-template-columns:repeat(auto-fit, minmax(160px, 1fr)); gap:12px; margin-bottom:16px; }
        .kpi-card { background:#161B22; border:1px solid #262C36; border-radius:10px; padding:12px; text-align:center; }
        .kpi-title { font-size:10px; color:#8B949E; text-transform:uppercase; letter-spacing:1px; margin-bottom:4px; }
        .kpi-val { font-size:24px; font-weight:800; color:#58A6FF; }
        .grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(320px, 1fr)); gap:14px; }
        .bench-card { background:#161B22; border:1px solid #30363D; border-radius:10px; padding:14px; position:relative; }
        .bench-top { display:flex; justify-content:space-between; align-items:center; margin-bottom:8px; }
        .bench-name { font-weight:700; font-size:14px; color:#FFF; }
        .bench-status { font-size:10px; font-weight:700; padding:3px 6px; border-radius:4px; }
        .bench-model { font-size:11px; color:#8B949E; margin-bottom:8px; }
        .prog-bar { background:#21262D; border-radius:4px; height:8px; overflow:hidden; margin-bottom:8px; }
        .prog-fill { background:#3FB950; height:100%; width:0%; transition:width 0.3s; }
        .metric-row { display:flex; justify-content:space-between; font-size:11px; color:#C9D1D9; margin-top:4px; }
        .action-row { margin-top:10px; display:flex; gap:8px; }
        .btn { background:#21262D; border:1px solid #30363D; color:#FFF; padding:6px 10px; border-radius:6px; font-size:10px; font-weight:700; cursor:pointer; text-decoration:none; display:inline-block; }
        .btn:hover { background:#30363D; border-color:#58A6FF; }
        .alert { border-color:#F85149 !important; }
    </style>
</head>
<body>
    <div class=""header"">
        <div class=""title"">
            <span>🛡️ SUPERMANAGER</span>
            <span class=""badge"">PORT 9000 MASTER FLEET</span>
        </div>
        <div id=""lastSync"" style=""font-size:11px; color:#8B949E;"">Syncing...</div>
    </div>

    <div class=""kpi-row"" id=""kpis"">
        <div class=""kpi-card""><div class=""kpi-title"">Active Benches</div><div class=""kpi-val"" id=""kpiActive"">0</div></div>
        <div class=""kpi-card""><div class=""kpi-title"">Testing Now</div><div class=""kpi-val"" id=""kpiTesting"">0</div></div>
        <div class=""kpi-card""><div class=""kpi-title"">Completed Today</div><div class=""kpi-val"" id=""kpiCompleted"">0</div></div>
        <div class=""kpi-card""><div class=""kpi-title"">Pass Rate</div><div class=""kpi-val"" style=""color:#3FB950"" id=""kpiPassRate"">100%</div></div>
        <div class=""kpi-card""><div class=""kpi-title"">Fleet Alerts</div><div class=""kpi-val"" style=""color:#F85149"" id=""kpiAlerts"">0</div></div>
    </div>

    <div class=""grid"" id=""benchGrid""></div>

    <script>
        async function refresh() {
            try {
                const res = await fetch('/api/fleet');
                const data = await res.json();
                document.getElementById('lastSync').innerText = 'Live · ' + new Date().toLocaleTimeString();
                
                document.getElementById('kpiActive').innerText = data.kpis.TotalActiveBenches;
                document.getElementById('kpiTesting').innerText = data.kpis.TotalTesting;
                document.getElementById('kpiCompleted').innerText = data.kpis.TotalCompletedToday;
                document.getElementById('kpiPassRate').innerText = data.kpis.FirstTimePassRate + '%';
                document.getElementById('kpiAlerts').innerText = data.kpis.TotalAlerts;

                const grid = document.getElementById('benchGrid');
                if (!data.benches || data.benches.length === 0) {
                    grid.innerHTML = '<div style=""color:#8B949E; grid-column:1/-1; text-align:center; padding:40px;"">Listening on UDP 9876. No laptops detected on this subnet yet.</div>';
                    return;
                }

                grid.innerHTML = data.benches.map(b => `
                    <div class=""bench-card ${b.Alert ? 'alert' : ''}"">
                        <div class=""bench-top"">
                            <div class=""bench-name"">${b.MachineName}</div>
                            <div class=""bench-status"" style=""background:${b.AccentHex}22; color:${b.AccentHex}; border:1px solid ${b.AccentHex};"">${b.StatusBadge}</div>
                        </div>
                        <div class=""bench-model"">${b.Model} · S/N: ${b.Serial}</div>
                        <div class=""prog-bar""><div class=""prog-fill"" style=""width:${b.ProgressPercent}%; background:${b.AccentHex};""></div></div>
                        <div class=""metric-row""><span>Progress:</span><strong style=""color:${b.AccentHex}"">${b.ProgressText}</strong></div>
                        <div class=""metric-row""><span>Grade:</span><strong>${b.Grade}</strong></div>
                        <div class=""metric-row""><span>Thermals:</span><span>${b.CpuTemp}°C (Load: ${b.CpuUsage}%)</span></div>
                        <div class=""metric-row""><span>Battery:</span><span>${b.Battery} (${b.BatteryHealth}% Health)</span></div>
                        <div class=""metric-row""><span>IP & Heartbeat:</span><span style=""color:#8B949E"">${b.IpAddress} · ${b.LastSeenSummary}</span></div>
                        <div class=""action-row"">
                            <a class=""btn"" href=""${b.Url}"" target=""_blank"">🌐 OPEN BENCH HUD</a>
                            <a class=""btn"" href=""${b.CertificateUrl}"" target=""_blank"">📄 CERTIFICATE</a>
                        </div>
                    </div>
                `).join('');
            } catch (err) {
                document.getElementById('lastSync').innerText = 'Reconnecting...';
            }
        }
        setInterval(refresh, 2500);
        refresh();
    </script>
</body>
</html>";
        }
    }
}
