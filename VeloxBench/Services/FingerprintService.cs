using System;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Services
{
    public class FingerprintSensorInfo
    {
        public bool HasSensor { get; set; }
        public string Name { get; set; } = "Unknown Sensor";
        public string Manufacturer { get; set; } = "Generic";
        public string Model { get; set; } = "N/A";
        public string DeviceId { get; set; } = "N/A";
        public uint UnitId { get; set; } = 0;
        public string Source { get; set; } = "None";
    }

    public sealed class FingerprintService
    {
        private static readonly Lazy<FingerprintService> _instance =
            new Lazy<FingerprintService>(() => new FingerprintService());

        public static FingerprintService Instance => _instance.Value;

        private const uint WINBIO_TYPE_FINGERPRINT = 0x00000008;
        private const uint WINBIO_POOL_SYSTEM = 1;
        private const uint WINBIO_FLAG_DEFAULT = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINBIO_UNIT_SCHEMA
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
        private static extern int WinBioEnumBiometricUnits(
            uint factor,
            out IntPtr unitSchemaArray,
            out int unitCount);

        [DllImport("winbio.dll")]
        private static extern int WinBioFree(IntPtr address);

        [DllImport("winbio.dll")]
        private static extern int WinBioOpenSession(
            uint factor,
            uint poolType,
            uint flags,
            IntPtr unitArray,
            uint unitCount,
            IntPtr databaseId,
            out IntPtr sessionHandle);

        [DllImport("winbio.dll")]
        private static extern int WinBioCloseSession(IntPtr sessionHandle);

        [DllImport("winbio.dll")]
        private static extern int WinBioLocateSensor(IntPtr sessionHandle, out uint unitId);

        [DllImport("winbio.dll")]
        private static extern int WinBioCancel(IntPtr sessionHandle);

        private IntPtr _currentSession = IntPtr.Zero;
        private readonly object _lock = new object();

        private FingerprintService() { }

        public async Task<FingerprintSensorInfo> ProbeSensorAsync()
        {
            return await Task.Run(() =>
            {
                var info = new FingerprintSensorInfo();

                // 1. First probe Windows Biometric Framework (WBF)
                try
                {
                    IntPtr unitSchemaArray = IntPtr.Zero;
                    int unitCount = 0;
                    int hr = WinBioEnumBiometricUnits(WINBIO_TYPE_FINGERPRINT, out unitSchemaArray, out unitCount);

                    if (hr == 0 && unitCount > 0 && unitSchemaArray != IntPtr.Zero)
                    {
                        var schema = (WINBIO_UNIT_SCHEMA)Marshal.PtrToStructure(unitSchemaArray, typeof(WINBIO_UNIT_SCHEMA));
                        info.HasSensor = true;
                        info.UnitId = schema.UnitId;
                        info.Name = string.IsNullOrWhiteSpace(schema.Description) ? "WBF Fingerprint Sensor" : schema.Description.Trim();
                        info.Manufacturer = string.IsNullOrWhiteSpace(schema.Manufacturer) ? "Biometric Device" : schema.Manufacturer.Trim();
                        info.Model = string.IsNullOrWhiteSpace(schema.Model) ? "Generic Fingerprint Sensor" : schema.Model.Trim();
                        info.DeviceId = string.IsNullOrWhiteSpace(schema.DeviceInstanceId) ? "WBF_UNIT_" + schema.UnitId : schema.DeviceInstanceId.Trim();
                        info.Source = "Windows Biometric Framework (WBF)";

                        WinBioFree(unitSchemaArray);
                        return info;
                    }

                    if (unitSchemaArray != IntPtr.Zero)
                    {
                        try { WinBioFree(unitSchemaArray); } catch { }
                    }
                }
                catch
                {
                    // WBF unavailable
                }

                // 2. Fallback probe via WMI Win32_PnPEntity
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Biometric' OR Name LIKE '%Fingerprint%' OR Caption LIKE '%Fingerprint%'"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            string name = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString();
                            string mfg = obj["Manufacturer"]?.ToString() ?? "Biometric Vendor";
                            string devId = obj["DeviceID"]?.ToString() ?? "PNP_BIOMETRIC";

                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                info.HasSensor = true;
                                info.Name = name.Trim();
                                info.Manufacturer = mfg.Trim();
                                info.Model = "Biometric PnP Hardware";
                                info.DeviceId = devId.Trim();
                                info.UnitId = 1;
                                info.Source = "PnP Biometric Sensor";
                                return info;
                            }
                        }
                    }
                }
                catch
                {
                    // WMI query error
                }

                info.HasSensor = false;
                info.Name = "No Biometric Fingerprint Sensor Found";
                info.Manufacturer = "N/A";
                info.Model = "No WBF/PnP Unit Detected";
                info.DeviceId = "N/A";
                info.Source = "Scan Complete (0 Devices)";
                return info;
            });
        }

        public async Task<bool> ListenForTouchAsync(CancellationToken ct, Action<uint> onTouchDetected)
        {
            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    CleanupSession();
                }

                try
                {
                    IntPtr session;
                    int openHr = WinBioOpenSession(
                        WINBIO_TYPE_FINGERPRINT,
                        WINBIO_POOL_SYSTEM,
                        WINBIO_FLAG_DEFAULT,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        out session);

                    if (openHr != 0 || session == IntPtr.Zero)
                    {
                        return false;
                    }

                    lock (_lock)
                    {
                        _currentSession = session;
                    }

                    uint touchedUnitId = 0;
                    int locateHr = WinBioLocateSensor(session, out touchedUnitId);

                    if (locateHr == 0 && !ct.IsCancellationRequested)
                    {
                        onTouchDetected?.Invoke(touchedUnitId);
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    lock (_lock)
                    {
                        CleanupSession();
                    }
                }
            });
        }

        public void CleanupSession()
        {
            if (_currentSession != IntPtr.Zero)
            {
                try
                {
                    WinBioCancel(_currentSession);
                    WinBioCloseSession(_currentSession);
                }
                catch { }
                finally
                {
                    _currentSession = IntPtr.Zero;
                }
            }
        }
    }
}
