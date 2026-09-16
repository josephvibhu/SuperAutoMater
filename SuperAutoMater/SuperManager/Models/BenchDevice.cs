using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SuperManager.Models
{
    public class BenchDevice : INotifyPropertyChanged
    {
        private string _id = "";
        private string _machineName = "BENCH-01";
        private string _ipAddress = "127.0.0.1";
        private int _port = 8443;
        private string _model = "Detecting...";
        private string _serial = "Unknown";
        private int _passedCount = 0;
        private int _totalCount = 11;
        private string _grade = "GRADE A+";
        private string _status = "Standby";
        private bool _alert = false;
        private DateTime _lastSeenUtc = DateTime.UtcNow;

        // Extended deep telemetry
        private int _cpuTemp = 0;
        private int _cpuUsage = 0;
        private string _battery = "100%";
        private int _batteryHealth = 100;
        private string _storage = "Nominal";
        private string _storageHealth = "100%";
        private string _cpu = "CPU";
        private string _ram = "RAM";
        private bool _isIdentified = false;
        private string _sessionAccessToken = "";

        public string Id { get => _id; set => SetField(ref _id, value); }
        public string MachineName { get => _machineName; set => SetField(ref _machineName, value); }
        public string IpAddress { get => _ipAddress; set => SetField(ref _ipAddress, value); }
        public int Port { get => _port; set => SetField(ref _port, value); }
        public string Model { get => _model; set => SetField(ref _model, value); }
        public string Serial { get => _serial; set => SetField(ref _serial, value); }
        public int PassedCount { get => _passedCount; set { SetField(ref _passedCount, value); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ProgressPercent)); OnPropertyChanged(nameof(AccentHex)); } }
        public int TotalCount { get => _totalCount; set { SetField(ref _totalCount, value); OnPropertyChanged(nameof(ProgressText)); OnPropertyChanged(nameof(ProgressPercent)); } }
        public string Grade { get => _grade; set => SetField(ref _grade, value); }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public bool Alert { get => _alert; set { SetField(ref _alert, value); OnPropertyChanged(nameof(AccentHex)); } }
        public DateTime LastSeenUtc { get => _lastSeenUtc; set { SetField(ref _lastSeenUtc, value); OnPropertyChanged(nameof(LastSeenSummary)); OnPropertyChanged(nameof(IsOnline)); } }

        public int CpuTemp { get => _cpuTemp; set => SetField(ref _cpuTemp, value); }
        public int CpuUsage { get => _cpuUsage; set => SetField(ref _cpuUsage, value); }
        public string Battery { get => _battery; set => SetField(ref _battery, value); }
        public int BatteryHealth { get => _batteryHealth; set => SetField(ref _batteryHealth, value); }
        public string Storage { get => _storage; set => SetField(ref _storage, value); }
        public string StorageHealth { get => _storageHealth; set => SetField(ref _storageHealth, value); }
        public string Cpu { get => _cpu; set => SetField(ref _cpu, value); }
        public string Ram { get => _ram; set => SetField(ref _ram, value); }
        public bool IsIdentified { get => _isIdentified; set => SetField(ref _isIdentified, value); }
        /// <summary>Ephemeral token announced by the bench for the current local fleet session.</summary>
        public string SessionAccessToken { get => _sessionAccessToken; set => SetField(ref _sessionAccessToken, value); }

        public string Url => string.IsNullOrWhiteSpace(SessionAccessToken)
            ? $"http://{IpAddress}:{Port}"
            : $"http://{IpAddress}:{Port}/?token={SessionAccessToken}";

        public string CertificateUrl => string.IsNullOrWhiteSpace(SessionAccessToken)
            ? $"http://{IpAddress}:{Port}/api/certificate"
            : $"http://{IpAddress}:{Port}/api/certificate?token={SessionAccessToken}";
        public string ProgressText => $"{PassedCount}/{TotalCount} PASSED";
        public double ProgressPercent => TotalCount > 0 ? (double)PassedCount / TotalCount * 100.0 : 0.0;
        public bool IsOnline => (DateTime.UtcNow - LastSeenUtc).TotalSeconds < 10;
        public string LastSeenSummary => $"{(int)(DateTime.UtcNow - LastSeenUtc).TotalSeconds}s ago";

        public string AccentHex
        {
            get
            {
                if (Alert || CpuTemp >= 90) return "#F85149";
                if (PassedCount >= TotalCount && TotalCount > 0) return "#3FB950";
                if (PassedCount > 0) return "#58A6FF";
                return "#8B949E";
            }
        }

        public string StatusBadge
        {
            get
            {
                if (Alert) return "🚨 ALERT";
                if (PassedCount >= TotalCount && TotalCount > 0) return "✓ CERTIFIED";
                if (PassedCount > 0) return "⚡ TESTING";
                return "STANDBY";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
