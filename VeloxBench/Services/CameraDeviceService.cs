using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using AForge.Video;
using AForge.Video.DirectShow;
using Microsoft.Win32;

namespace SuperAutoMater.Wpf.Services
{
    public sealed class PnpCameraInfo
    {
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Status { get; set; } = "OK";
        public bool IsPresent { get; set; } = true;
    }

    /// <summary>
    /// Unified Camera Detection and Hardware Probe Service:
    /// 1. Prioritizes standard RGB color webcams over IR / Windows Hello / Virtual sensors.
    /// 2. Negotiates optimal native resolution (720p/1080p) to avoid driver stream failures.
    /// 3. Provides deep Windows PnP fallback for modern MIPI/Intel IPU cameras.
    /// 4. Checks Windows Camera Privacy toggles (ConsentStore).
    /// </summary>
    public static class CameraDeviceService
    {
        public static bool IsIrOrNightVisionCamera(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("ir camera") ||
                   lower.Contains("infrared") ||
                   lower.Contains("night vision") ||
                   lower.Contains("hello face") ||
                   lower.Contains("depth") ||
                   lower.Contains("realsense") ||
                   lower.Contains("ir sensor") ||
                   lower.Contains("rgb-ir") ||
                   lower.Contains("virtual") ||
                   lower.Contains("dmft") ||
                   lower.Contains("windows hello");
        }

        public static int SelectBestCameraIndex(FilterInfoCollection devices)
        {
            if (devices == null || devices.Count == 0) return -1;

            // 1. Prioritize non-IR, non-virtual standard color cameras
            for (int i = 0; i < devices.Count; i++)
            {
                string name = devices[i].Name ?? "";
                if (!IsIrOrNightVisionCamera(name))
                {
                    return i;
                }
            }

            // 2. Fallback to first device if all are marked IR
            return 0;
        }

        public static string FormatCameraDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown Camera";
            if (IsIrOrNightVisionCamera(name))
            {
                return $"{name} [IR / Biometric]";
            }
            return $"{name} [RGB Color]";
        }

        /// <summary>
        /// Selects the highest crisp resolution under 1080p (e.g. 1920x1080 or 1280x720)
        /// to ensure DirectShow driver stabilizes on a supported hardware pin format.
        /// </summary>
        public static VideoCapabilities ConfigureOptimalResolution(VideoCaptureDevice videoSource, int maxWidth = 1920, int maxHeight = 1080)
        {
            if (videoSource == null) return null;

            try
            {
                var caps = videoSource.VideoCapabilities;
                if (caps == null || caps.Length == 0) return null;

                VideoCapabilities bestCap = null;
                int bestScore = 0;

                foreach (var cap in caps)
                {
                    int w = cap.FrameSize.Width;
                    int h = cap.FrameSize.Height;
                    int score = w * h;

                    // Prefer standard resolutions up to 1080p (and at least 15 FPS)
                    if (w <= maxWidth && h <= maxHeight && score > bestScore)
                    {
                        bestScore = score;
                        bestCap = cap;
                    }
                }

                if (bestCap != null)
                {
                    videoSource.VideoResolution = bestCap;
                    return bestCap;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Queries Windows PnP (Win32_PnPEntity) for camera hardware detected by Windows kernel/driver.
        /// </summary>
        public static List<PnpCameraInfo> GetPnpCameraHardware()
        {
            var results = new List<PnpCameraInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Manufacturer, DeviceID, Status, Present FROM Win32_PnPEntity " +
                    "WHERE PNPClass = 'Camera' OR PNPClass = 'Image' OR Service = 'usbvideo' OR Description LIKE '%Camera%' OR Description LIKE '%Webcam%'");

                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = mo["Name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    results.Add(new PnpCameraInfo
                    {
                        Name = name,
                        Manufacturer = mo["Manufacturer"]?.ToString() ?? "Standard System",
                        DeviceId = mo["DeviceID"]?.ToString() ?? "",
                        Status = mo["Status"]?.ToString() ?? "OK",
                        IsPresent = Convert.ToBoolean(mo["Present"] ?? true)
                    });
                }
            }
            catch { }

            return results;
        }

        /// <summary>
        /// Checks whether Windows Camera Privacy is blocking desktop apps from accessing the camera.
        /// </summary>
        public static bool IsWindowsCameraPrivacyRestricted(out string privacyDetail)
        {
            privacyDetail = "";

            try
            {
                // Check HKCU
                using var keyUser = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam");
                if (keyUser != null)
                {
                    var val = keyUser.GetValue("Value")?.ToString();
                    if (string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        privacyDetail = "Windows Camera Access is turned OFF in Windows Settings (User Privacy Deny).";
                        return true;
                    }
                }

                // Check HKLM
                using var keyLocal = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam");
                if (keyLocal != null)
                {
                    var val = keyLocal.GetValue("Value")?.ToString();
                    if (string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        privacyDetail = "Windows Camera Access is turned OFF system-wide in Windows Settings (Machine Policy Deny).";
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Produces a human-readable diagnosis if DirectShow returns 0 devices.
        /// </summary>
        public static string GetDiagnosticsSummary()
        {
            if (IsWindowsCameraPrivacyRestricted(out string privacyMsg))
            {
                return $"⚠ {privacyMsg}\nGo to Windows Settings > Privacy & Security > Camera and toggle 'Camera access' to ON.";
            }

            var pnpCams = GetPnpCameraHardware();
            if (pnpCams.Count > 0)
            {
                var cam = pnpCams[0];
                return $"ℹ Hardware Detected in Windows PnP: {cam.Name} ({cam.Manufacturer}).\n" +
                       "DirectShow filter unbridged or camera driver requires modern Media Foundation / UWP capture.";
            }

            return "No webcam capture devices detected by DirectShow or Windows PnP.\n" +
                   "Check physical webcam kill-switch / privacy shutter or missing camera driver in Device Manager.";
        }
    }
}
