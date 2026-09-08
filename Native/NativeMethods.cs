using System;
using System.Runtime.InteropServices;

namespace ITAS_QC_Tool
{
    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    internal static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // --- HDSentinel nag screen automation ---
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public const int BM_CLICK = 0x00F5;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        // --- Windows Biometric Framework (WinBio) ---
        public const uint WINBIO_TYPE_FINGERPRINT = 0x00000008;
        public const uint WINBIO_POOL_SYSTEM = 1;
        public const uint WINBIO_FLAG_DEFAULT = 0;
        public const int WINBIO_E_CANCELED = unchecked((int)0x80098004);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WINBIO_UNIT_SCHEMA
        {
            public uint UnitId;
            public uint PoolType;
            public uint BiometricFactor;
            public uint SensorSubType;
            public uint Capabilities;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DeviceInstanceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Description;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Manufacturer;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Model;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string SerialNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string FirmwareVersion;
        }

        [DllImport("winbio.dll", CharSet = CharSet.Unicode)]
        public static extern int WinBioEnumBiometricUnits(
            uint factor,
            out IntPtr unitSchemaArray,
            out int unitCount);

        [DllImport("winbio.dll")]
        public static extern int WinBioFree(IntPtr address);

        [DllImport("winbio.dll")]
        public static extern int WinBioOpenSession(
            uint factor,
            uint poolType,
            uint flags,
            IntPtr unitArray,
            uint unitCount,
            IntPtr databaseId,
            out IntPtr sessionHandle);

        [DllImport("winbio.dll")]
        public static extern int WinBioCloseSession(IntPtr sessionHandle);

        [DllImport("winbio.dll")]
        public static extern int WinBioLocateSensor(IntPtr sessionHandle, out uint unitId);

        [DllImport("winbio.dll")]
        public static extern int WinBioCancel(IntPtr sessionHandle);

        // --- Touch / Digitizer System Metrics ---
        public const int SM_DIGITIZER = 94;
        public const int SM_MAXIMUMTOUCHES = 95;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        // --- Windows Power & Thermal Telemetry (powrprof.dll) ---
        public const int POWER_SYSTEM_BATTERY_STATE = 5;
        public const int POWER_PROCESSOR_INFORMATION = 11;

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_BATTERY_STATE
        {
            [MarshalAs(UnmanagedType.I1)] public bool AcOnLine;
            [MarshalAs(UnmanagedType.I1)] public bool BatteryPresent;
            [MarshalAs(UnmanagedType.I1)] public bool Charging;
            [MarshalAs(UnmanagedType.I1)] public bool Discharging;
            public byte Tag;
            public uint MaxCapacity;
            public uint RemainingCapacity;
            public int Rate; // Discharge / Charge rate in milliwatts (mW)
            public uint EstimatedTime; // Estimated seconds of runtime remaining
            public uint DefaultAlert1;
            public uint DefaultAlert2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport("powrprof.dll")]
        public static extern int CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            out SYSTEM_BATTERY_STATE outputBuffer,
            uint outputBufferLength
        );

        [DllImport("powrprof.dll")]
        public static extern int CallNtPowerInformation(
            int informationLevel,
            IntPtr inputBuffer,
            uint inputBufferLength,
            [Out] PROCESSOR_POWER_INFORMATION[] outputBuffer,
            uint outputBufferLength
        );

        public static bool TryGetBatteryState(out SYSTEM_BATTERY_STATE state)
        {
            state = default;
            try
            {
                int size = Marshal.SizeOf(typeof(SYSTEM_BATTERY_STATE));
                int ret = CallNtPowerInformation(POWER_SYSTEM_BATTERY_STATE, IntPtr.Zero, 0, out state, (uint)size);
                return ret == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetProcessorPowerInfo(out PROCESSOR_POWER_INFORMATION[] info)
        {
            info = null;
            try
            {
                int count = Math.Max(1, Environment.ProcessorCount);
                info = new PROCESSOR_POWER_INFORMATION[count];
                int size = Marshal.SizeOf(typeof(PROCESSOR_POWER_INFORMATION)) * count;
                int ret = CallNtPowerInformation(POWER_PROCESSOR_INFORMATION, IntPtr.Zero, 0, info, (uint)size);
                return ret == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}