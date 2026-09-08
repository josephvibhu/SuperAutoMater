using System;
using System.Collections.Generic;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITAS_QC_Tool
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

        public event Action<int, string> PortTested;
        public event Action<int> PortRemoved;

        public int TestedPortsCount => _insertionCount;

        public void ProcessWndProc(ref Message m)
        {
            if (m.Msg != WM_DEVICECHANGE) return;

            int wParam = m.WParam.ToInt32();

            if (wParam == DBT_DEVICEARRIVAL)
            {
                // Debounce rapid arrival interrupts (within 600ms)
                if ((DateTime.Now - _lastArrival).TotalMilliseconds < 600) return;
                _lastArrival = DateTime.Now;

                _insertionCount++;
                string portLabel = $"PORT {_insertionCount}";

                // Play confirmation chime
                Task.Run(() =>
                {
                    try { SystemSounds.Asterisk.Play(); } catch { }
                });

                PortTested?.Invoke(_insertionCount, portLabel);
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
                return "⟨ USB PORTS : PLUG FLASH DRIVE TO TEST ⟩";
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
