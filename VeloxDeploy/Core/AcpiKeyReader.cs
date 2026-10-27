using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using VeloxDeploy.Models;

namespace VeloxDeploy.Core
{
    public static class AcpiKeyReader
    {
        private const uint FIRMWARE_TABLE_PROVIDER_ACPI = 0x41435049; // 'ACPI'
        private const uint FIRMWARE_TABLE_MSDM = 0x4D53444D;          // 'MSDM'

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint FirmwareTableProviderSignature,
            uint FirmwareTableID,
            byte[]? pFirmwareTableBuffer,
            uint BufferSize);

        /// <summary>
        /// Reads the native Windows OEM digital product key embedded in the BIOS ACPI MSDM table.
        /// </summary>
        public static AcpiKeyInfo ReadEmbeddedKey()
        {
            var info = new AcpiKeyInfo();

            // Attempt 1: Direct ACPI Firmware Table extraction (Fastest & most direct in WinPE)
            try
            {
                uint bufferSize = GetSystemFirmwareTable(FIRMWARE_TABLE_PROVIDER_ACPI, FIRMWARE_TABLE_MSDM, null, 0);
                if (bufferSize > 0)
                {
                    byte[] buffer = new byte[bufferSize];
                    uint bytesRead = GetSystemFirmwareTable(FIRMWARE_TABLE_PROVIDER_ACPI, FIRMWARE_TABLE_MSDM, buffer, bufferSize);
                    if (bytesRead > 0 && buffer.Length >= 56)
                    {
                        // In MSDM ACPI tables, the 29-character product key string starts at offset 56
                        string keyCandidate = Encoding.ASCII.GetString(buffer, 56, buffer.Length - 56).Trim('\0', ' ', '\r', '\n');
                        var match = Regex.Match(keyCandidate, @"[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}");
                        if (match.Success)
                        {
                            info.HasKey = true;
                            info.ProductKey = match.Value;
                            info.SourceMethod = "ACPI MSDM Direct Firmware Table";
                            info.KeyEdition = DetermineEdition(info.ProductKey);
                            return info;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcpiKeyReader] Direct firmware table read failed: {ex.Message}");
            }

            // Attempt 2: WMI SoftwareLicensingService (OA3xOriginalProductKey)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var val = obj["OA3xOriginalProductKey"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var match = Regex.Match(val.Trim(), @"[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}");
                        if (match.Success)
                        {
                            info.HasKey = true;
                            info.ProductKey = match.Value;
                            info.SourceMethod = "WMI SoftwareLicensingService (OA 3.0)";
                            info.KeyEdition = DetermineEdition(info.ProductKey);
                            return info;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AcpiKeyReader] WMI query failed: {ex.Message}");
            }

            // Attempt 3: PowerShell fallback
            try
            {
                var psi = new ProcessStartInfo("powershell", "-NoProfile -Command \"(Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    var match = Regex.Match(output, @"[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}");
                    if (match.Success)
                    {
                        info.HasKey = true;
                        info.ProductKey = match.Value;
                        info.SourceMethod = "CIM SoftwareLicensingService";
                        info.KeyEdition = DetermineEdition(info.ProductKey);
                        return info;
                    }
                }
            }
            catch
            {
                // Ignored
            }

            info.HasKey = false;
            info.ProductKey = string.Empty;
            info.KeyEdition = "No ACPI Key Found";
            return info;
        }

        private static string DetermineEdition(string key)
        {
            // Most commercial refurbishments carry Pro OEM keys
            return "Windows 10/11 Pro (OEM Digital License)";
        }
    }
}
