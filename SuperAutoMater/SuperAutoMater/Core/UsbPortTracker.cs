using System;
using System.Collections.Generic;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoMater
{
    /// <summary>
    /// Live USB Port Plug & Pass Tracker.
    /// Hooks Windows WM_DEVICECHANGE to detect when flash drives or peripherals are plugged into physical ports.
    /// Tracks insertion history so technicians can plug & verify all laptop ports sequentially without clicking.
    /// </summary>
    public sealed class UsbPortTracker
    {
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private readonly HashSet<string> _detectedPortHistory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _insertionCount = 0;
        private DateTime _lastArrival = DateTime.MinValue;

        public event Action<int, string, string> PortTested;
        public event Action<int> PortRemoved;

        public int TestedPortsCount => _insertionCount;

        public static string GetActiveUsbDriveDetails()
        {
            try
            {
                var drives = System.IO.DriveInfo.GetDrives();
                foreach (var d in drives)
                {
                    if (d.DriveType == System.IO.DriveType.Removable && d.IsReady)
                    {
                        string label = !string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.VolumeLabel : "USB Drive";
                        double sizeGb = d.TotalSize / (1024.0 * 1024 * 1024);
                        return $"{label} ({d.Name.TrimEnd('\\')}) {sizeGb:0.#}GB";
                    }
                }
            }
            catch { }
            return null;
        }

        public void ProcessWndProc(ref Message m)
        {
            if (m.Msg != WM_DEVICECHANGE) return;

            int wParam = m.WParam.ToInt32();

            if (wParam == DBT_DEVICEARRIVAL)
            {
                // Debounce rapid arrival interrupts (within 700ms)
                if ((DateTime.Now - _lastArrival).TotalMilliseconds < 700) return;
                _lastArrival = DateTime.Now;

                _insertionCount++;
                string portLabel = $"PORT {_insertionCount}";
                string driveDetails = GetActiveUsbDriveDetails() ?? "USB Flash Drive";

                // Play confirmation chime
                Task.Run(() =>
                {
                    try { SystemSounds.Asterisk.Play(); } catch { }
                });

                PortTested?.Invoke(_insertionCount, portLabel, driveDetails);
            }
            else if (wParam == DBT_DEVICEREMOVECOMPLETE)
            {
                PortRemoved?.Invoke(_insertionCount);
            }
        }

        public string GetHudStatusText()
        {
            if (_insertionCount == 0)
            {
                return "⟨ USB: PLUG FLASH DRIVE TO TEST ⟩";
            }

            var parts = new List<string>();
            for (int i = 1; i <= Math.Min(6, _insertionCount); i++)
            {
                parts.Add($"[ P{i} ✓ ]");
            }

            if (_insertionCount > 6)
            {
                parts.Add($"(+{_insertionCount - 6} more)");
            }

            return $"⟨ USB PORTS TESTED ({_insertionCount}) : {string.Join(" ", parts)} ⟩";
        }

        public void Reset()
        {
            _insertionCount = 0;
            _detectedPortHistory.Clear();
            _lastArrival = DateTime.MinValue;
        }
    }
}
