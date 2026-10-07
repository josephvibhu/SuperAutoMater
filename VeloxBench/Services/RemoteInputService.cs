using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SuperAutoMater.Wpf.Services
{
    /// <summary>
    /// Injects virtual mouse and keyboard events into the local Windows desktop session.
    /// Operates with Administrator privileges inherited from SuperAutoMater.
    /// </summary>
    public class RemoteInputService
    {
        private static readonly Lazy<RemoteInputService> _instance =
            new Lazy<RemoteInputService>(() => new RemoteInputService());
        public static RemoteInputService Instance => _instance.Value;

        private RemoteInputService() { }

        #region Win32 Interop

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
        private const uint MOUSEEVENTF_WHEEL      = 0x0800;

        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP   = 0x0002;

        #endregion

        /// <summary>
        /// Moves the cursor to normalized screen coordinates (0.0 to 1.0).
        /// </summary>
        public void MoveMouse(double xRatio, double yRatio)
        {
            try
            {
                var bounds = Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                int targetX = (int)Math.Round(Math.Clamp(xRatio, 0.0, 1.0) * (bounds.Width - 1)) + bounds.X;
                int targetY = (int)Math.Round(Math.Clamp(yRatio, 0.0, 1.0) * (bounds.Height - 1)) + bounds.Y;
                SetCursorPos(targetX, targetY);
            }
            catch { }
        }

        /// <summary>
        /// Executes a mouse action at normalized coordinates.
        /// Action can be: "move", "left_click", "right_click", "double_click", "left_down", "left_up", "right_down", "right_up", "wheel".
        /// </summary>
        public void ProcessMouse(string action, double xRatio, double yRatio, int wheelDelta = 0)
        {
            MoveMouse(xRatio, yRatio);

            switch (action?.ToLowerInvariant())
            {
                case "left_click":
                case "click":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "double_click":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(60);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "right_click":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(20);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "left_down":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "left_up":
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "right_down":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "right_up":
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    break;

                case "wheel":
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)wheelDelta, UIntPtr.Zero);
                    break;
            }
        }

        /// <summary>
        /// Presses a virtual key by name (e.g. "Enter", "Escape", "Space", "Tab", "F5", "Win", "Backspace").
        /// </summary>
        public void SendKey(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return;

            byte vk = keyName.ToLowerInvariant() switch
            {
                "enter" or "return" => 0x0D,
                "escape" or "esc"   => 0x1B,
                "space"             => 0x20,
                "tab"               => 0x09,
                "backspace" or "bksp" => 0x08,
                "delete" or "del"   => 0x2E,
                "up" or "arrowup"   => 0x26,
                "down" or "arrowdown" => 0x28,
                "left" or "arrowleft" => 0x25,
                "right" or "arrowright" => 0x27,
                "f5"                => 0x74,
                "f11"               => 0x7A,
                "win" or "lwin"     => 0x5B,
                _ => (byte)0
            };

            if (vk != 0)
            {
                keybd_event(vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                Thread.Sleep(25);
                keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            else if (keyName.Length == 1)
            {
                // Single alphanumeric character
                char c = keyName[0];
                if (char.IsLetterOrDigit(c))
                {
                    byte v = (byte)char.ToUpperInvariant(c);
                    keybd_event(v, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                    Thread.Sleep(20);
                    keybd_event(v, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
        }

        /// <summary>
        /// Executes a named system action on the bench PC.
        /// </summary>
        public bool ExecuteQuickAction(string action)
        {
            try
            {
                switch (action?.ToLowerInvariant())
                {
                    case "taskmgr":
                        Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                        return true;

                    case "cmd":
                        Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true });
                        return true;

                    case "explorer":
                        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                        return true;

                    case "show_desktop":
                        // Win+D
                        keybd_event(0x5B, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                        keybd_event(0x44, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                        Thread.Sleep(30);
                        keybd_event(0x44, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(0x5B, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        return true;

                    case "reboot":
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Reboot requested by Fleet Manager\"") { UseShellExecute = true });
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
