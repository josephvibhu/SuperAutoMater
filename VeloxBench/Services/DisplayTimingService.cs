using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace SuperAutoMater.Wpf.Services
{
    public class DisplayTimingStats
    {
        public int ConfiguredHz { get; set; } = 60;
        public double MeasuredHz { get; set; } = 60.0;
        public double TargetFrameIntervalMs => ConfiguredHz > 0 ? (1000.0 / ConfiguredHz) : 16.67;
        public double AverageFrameTimeMs { get; set; } = 16.67;
        public double JitterMs { get; set; } = 0.1;
        public int TotalFramesCounted { get; set; } = 0;
        public int DroppedFrames { get; set; } = 0;
        public double DroppedFramePercent => TotalFramesCounted > 0 ? Math.Round((double)DroppedFrames / TotalFramesCounted * 100.0, 2) : 0.0;
        public int ResolutionWidth { get; set; } = 1920;
        public int ResolutionHeight { get; set; } = 1080;
        public int ColorDepthBits { get; set; } = 32;
        public string SummaryText => $"{MeasuredHz:F1} Hz ({ResolutionWidth}x{ResolutionHeight} · {JitterMs:F2}ms Jitter · {DroppedFrames} Drops)";
        public string QualityBadge => DroppedFrames == 0 && JitterMs < 1.0 ? "✓ HARDWARE VSYNC NOMINAL" : (DroppedFrames > 5 ? "⚠ FRAME DROP / STUTTER" : "✓ VSYNC ACTIVE");
        public string AccentHex => DroppedFrames == 0 ? "#3FB950" : (DroppedFrames > 5 ? "#F85149" : "#D29922");
    }

    public class DisplayTimingService
    {
        private static readonly Lazy<DisplayTimingService> _instance =
            new Lazy<DisplayTimingService>(() => new DisplayTimingService());
        public static DisplayTimingService Instance => _instance.Value;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        private const int ENUM_CURRENT_SETTINGS = -1;

        private bool _isMeasuring = false;
        private long _lastTimestamp = 0;
        private readonly Queue<double> _intervalHistory = new Queue<double>();
        private const int HISTORY_SIZE = 90;
        private int _totalFrames = 0;
        private int _droppedFrames = 0;
        private int _configuredHz = 60;
        private int _width = 1920;
        private int _height = 1080;
        private int _bitsPerPel = 32;

        public event Action<DisplayTimingStats> TimingUpdated;
        public DisplayTimingStats CurrentStats { get; private set; } = new DisplayTimingStats();

        private DisplayTimingService()
        {
            QueryDisplayMode();
        }

        public void QueryDisplayMode()
        {
            try
            {
                var dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    _configuredHz = dm.dmDisplayFrequency > 0 ? dm.dmDisplayFrequency : 60;
                    _width = dm.dmPelsWidth > 0 ? dm.dmPelsWidth : 1920;
                    _height = dm.dmPelsHeight > 0 ? dm.dmPelsHeight : 1080;
                    _bitsPerPel = dm.dmBitsPerPel > 0 ? dm.dmBitsPerPel : 32;
                }
            }
            catch
            {
                _configuredHz = 60;
            }

            UpdateStats(0, 0);
        }

        public void StartMeasurement()
        {
            if (_isMeasuring) return;
            QueryDisplayMode();
            _intervalHistory.Clear();
            _totalFrames = 0;
            _droppedFrames = 0;
            _lastTimestamp = Stopwatch.GetTimestamp();
            _isMeasuring = true;

            CompositionTarget.Rendering += OnCompositionRendering;
        }

        public void StopMeasurement()
        {
            if (!_isMeasuring) return;
            _isMeasuring = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }

        private void OnCompositionRendering(object sender, EventArgs e)
        {
            if (!_isMeasuring) return;

            long now = Stopwatch.GetTimestamp();
            long diffTicks = now - _lastTimestamp;
            _lastTimestamp = now;

            double frameMs = (double)diffTicks / Stopwatch.Frequency * 1000.0;
            if (frameMs <= 0.1 || frameMs >= 500.0) return; // Skip startup glitch or window pause

            _totalFrames++;
            double targetMs = _configuredHz > 0 ? (1000.0 / _configuredHz) : 16.67;

            // Flag dropped frame if frame time exceeded 1.5x expected VSync interval
            if (frameMs > (targetMs * 1.58))
            {
                _droppedFrames++;
            }

            _intervalHistory.Enqueue(frameMs);
            if (_intervalHistory.Count > HISTORY_SIZE)
            {
                _intervalHistory.Dequeue();
            }

            // Calculate moving statistics every 6 frames to keep UI overhead imperceptible
            if (_totalFrames % 6 == 0 && _intervalHistory.Count >= 10)
            {
                double sum = 0.0;
                foreach (var interval in _intervalHistory)
                {
                    sum += interval;
                }
                double avgMs = sum / _intervalHistory.Count;
                double measuredHz = avgMs > 0.1 ? (1000.0 / avgMs) : _configuredHz;

                // Standard deviation (Jitter)
                double sumSquareDiff = 0.0;
                foreach (var interval in _intervalHistory)
                {
                    double d = interval - avgMs;
                    sumSquareDiff += d * d;
                }
                double jitter = Math.Sqrt(sumSquareDiff / _intervalHistory.Count);

                UpdateStats(measuredHz, jitter, avgMs);
            }
        }

        private void UpdateStats(double measuredHz, double jitterMs, double avgMs = 16.67)
        {
            var stats = new DisplayTimingStats
            {
                ConfiguredHz = _configuredHz,
                MeasuredHz = measuredHz > 0 ? Math.Round(measuredHz, 1) : _configuredHz,
                AverageFrameTimeMs = Math.Round(avgMs, 2),
                JitterMs = Math.Round(jitterMs, 2),
                TotalFramesCounted = _totalFrames,
                DroppedFrames = _droppedFrames,
                ResolutionWidth = _width,
                ResolutionHeight = _height,
                ColorDepthBits = _bitsPerPel
            };

            CurrentStats = stats;
            TimingUpdated?.Invoke(stats);
        }
    }
}
