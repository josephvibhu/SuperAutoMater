using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SuperManager.Models;

namespace SuperManager.Services
{
    public class FleetDiscoveryService
    {
        private static readonly Lazy<FleetDiscoveryService> _instance =
            new Lazy<FleetDiscoveryService>(() => new FleetDiscoveryService());
        public static FleetDiscoveryService Instance => _instance.Value;

        private const int UDP_PORT = 9876;
        private UdpClient _udpReceiver;
        private CancellationTokenSource _cts;
        private Timer _heartbeatTimer;
        private readonly ConcurrentDictionary<string, BenchDevice> _devices =
            new ConcurrentDictionary<string, BenchDevice>(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<BenchDevice> Devices { get; } = new ObservableCollection<BenchDevice>();

        public event Action FleetUpdated;
        public event Action<BenchDevice> BenchDiscovered;

        private FleetDiscoveryService() { }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();

            try
            {
                _udpReceiver = new UdpClient();
                _udpReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpReceiver.ExclusiveAddressUse = false;
                _udpReceiver.Client.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));

                Task.Run(ReceiveBeaconsAsync, _cts.Token);

                // Run periodic heartbeat and cleanup timer every 2 seconds
                _heartbeatTimer = new Timer(HeartbeatCheck, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP Listener Error] {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
                _udpReceiver?.Close();
                _udpReceiver = null;
                _cts = null;
            }
            catch { }
        }

        private async Task ReceiveBeaconsAsync()
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

                    // If the bench payload reports a loopback/APIPA/blank IP (multi-NIC issue),
                    // fall back to the actual UDP socket remote endpoint — the real LAN address.
                    string senderIp = result.RemoteEndPoint?.Address?.ToString() ?? ip;
                    bool payloadIpIsUsable = !string.IsNullOrWhiteSpace(ip)
                        && !ip.StartsWith("127.")
                        && !ip.StartsWith("169.254.")
                        && ip != "::1";
                    string resolvedIp = payloadIpIsUsable ? ip : senderIp;

                    bool isNew = !_devices.TryGetValue(id, out var dev);
                    if (isNew)
                    {
                        dev = new BenchDevice { Id = id };
                        _devices[id] = dev;
                    }

                    dev.MachineName = root.GetProperty("name").GetString();
                    dev.IpAddress = resolvedIp;
                    dev.Port = root.GetProperty("port").GetInt32();
                    dev.Model = root.GetProperty("model").GetString();
                    dev.Serial = root.GetProperty("serial").GetString();
                    dev.PassedCount = root.GetProperty("passed").GetInt32();
                    dev.TotalCount = root.GetProperty("total").GetInt32();
                    dev.Grade = root.GetProperty("grade").GetString();
                    dev.Status = root.GetProperty("status").GetString();
                    dev.Alert = root.GetProperty("alert").GetBoolean();
                    if (root.TryGetProperty("token", out var token))
                        dev.SessionAccessToken = token.GetString() ?? "";
                    dev.LastSeenUtc = DateTime.UtcNow;

                    SyncToObservableCollection(dev, isNew);
                }
                catch
                {
                    if (_cts == null || _cts.IsCancellationRequested) break;
                }
            }
        }

        private void SyncToObservableCollection(BenchDevice dev, bool isNew)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (isNew)
                {
                    Devices.Add(dev);
                    BenchDiscovered?.Invoke(dev);
                }
                FleetUpdated?.Invoke();
            }));
        }

        private void HeartbeatCheck(object state)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(15);
                foreach (var dev in Devices)
                {
                    dev.LastSeenUtc = dev.LastSeenUtc; // Triggers UI re-evaluation
                }
                FleetUpdated?.Invoke();
            }));
        }

        public void RegisterDeviceManually(BenchDevice dev)
        {
            _devices[dev.Id] = dev;
            SyncToObservableCollection(dev, true);
        }
    }
}
