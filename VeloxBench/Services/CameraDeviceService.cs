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
    /// Unified Camera Detection, Hardware Probe & Dell Precision Workstation Driver Service:
    /// 1. Prioritizes Dell RGB-IR / standard RGB color webcams over pure IR / Windows Hello sensors.
    /// 2. Specifically handles Dell Precision 5540, 5550, and 5560 dual-sensor RGB-IR camera architectures.
    /// 3. Negotiates optimal native resolution (720p @ 30 FPS native for Precision 5540/5550/5560) to avoid DirectShow pin failures.
    /// 4. Provides safe fallback to driver default stream formats if custom pin negotiation fails.
    /// 5. Checks and offers 1-click unblock for Windows Camera Privacy toggles (User, Machine & NonPackaged desktop access).
    /// </summary>
    public static class CameraDeviceService
    {
        /// <summary>
        /// Returns true if the camera is an RGB color sensor (including dual RGB-IR modules on Dell Precision).
        /// </summary>
        public static bool IsRgbCamera(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();

            // Dell Precision 5540/5550/5560 use Realtek/Sunplus/OmniVision RGB-IR modules where RGB is primary
            if (lower.Contains("rgb")) return true;
            if (lower.Contains("integrated webcam") || lower.Contains("integrated camera"))
            {
                if (!lower.Contains("ir ") && !lower.Contains("infrared")) return true;
            }
            if (lower.Contains("hd webcam") || lower.Contains("fhd") || lower.Contains("usb video device")) return true;

            return false;
        }

        /// <summary>
        /// Returns true if the device is strictly an Infrared (IR) / Windows Hello sensor without RGB video output.
        /// Note: "RGB-IR" contains "rgb", so it is recognized as a color camera and returns false here.
        /// </summary>
        public static bool IsIrOrBiometricCamera(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();

            // Dual RGB-IR modules provide RGB stream - do NOT treat as IR-only!
            if (lower.Contains("rgb")) return false;

            return lower.Contains("ir camera") ||
                   lower.Contains("infrared") ||
                   lower.Contains("night vision") ||
                   lower.Contains("hello face") ||
                   lower.Contains("windows hello") ||
                   lower.Contains("ir sensor") ||
                   lower.Contains("depth") ||
                   lower.Contains("realsense") ||
                   lower.Contains("nir camera") ||
                   lower.Contains("dmft");
        }

        /// <summary>
        /// Returns true if the device is a virtual / software emulated camera.
        /// </summary>
        public static bool IsVirtualCamera(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("virtual") ||
                   lower.Contains("obs") ||
                   lower.Contains("manycam") ||
                   lower.Contains("droidcam") ||
                   lower.Contains("ndi") ||
                   lower.Contains("screen capture") ||
                   lower.Contains("vms") ||
                   lower.Contains("xsplit") ||
                   lower.Contains("unity");
        }

        /// <summary>
        /// Backward-compatible check: returns true if the device is either pure IR or virtual.
        /// </summary>
        public static bool IsIrOrNightVisionCamera(string name)
        {
            return IsIrOrBiometricCamera(name) || IsVirtualCamera(name);
        }

        /// <summary>
        /// Selects the best physical RGB camera index using multi-factor scoring.
        /// Specifically prioritizes Dell Precision 5540/5550/5560 RGB-IR sensors over Windows Hello IR devices.
        /// </summary>
        public static int SelectBestCameraIndex(FilterInfoCollection devices)
        {
            if (devices == null || devices.Count == 0) return -1;

            int bestIndex = 0;
            int highestScore = int.MinValue;

            for (int i = 0; i < devices.Count; i++)
            {
                string name = devices[i].Name ?? "";
                string lower = name.ToLowerInvariant();
                int score = 0;

                if (IsIrOrBiometricCamera(name))
                {
                    // Severe penalty for pure IR / Windows Hello devices (cannot output standard RGB frames)
                    score -= 1000;
                }
                else if (IsVirtualCamera(name))
                {
                    // Penalty for virtual software cameras
                    score -= 500;
                }
                else
                {
                    // Base score for genuine physical cameras
                    score += 100;

                    // 1. Integrated laptop cameras
                    if (lower.Contains("integrated webcam") || lower.Contains("integrated camera"))
                        score += 60;

                    // 2. Dell Precision 5540/5550/5560 RGB-IR camera module
                    if (lower.Contains("rgb-ir") || lower.Contains("rgb/ir") || (lower.Contains("rgb") && lower.Contains("camera")))
                        score += 55;

                    // 3. Known laptop OEM camera vendors
                    if (lower.Contains("dell") || lower.Contains("realtek") || lower.Contains("sunplus") || lower.Contains("intel") || lower.Contains("omnivid") || lower.Contains("ov01"))
                        score += 30;

                    // 4. HD / FHD / Webcam indicators
                    if (lower.Contains("fhd") || lower.Contains("hd camera") || lower.Contains("webcam"))
                        score += 20;

                    // 5. Standard UVC video
                    if (lower.Contains("usb video device"))
                        score += 15;
                }

                if (score > highestScore)
                {
                    highestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>
        /// Formats human-readable camera display names for dropdown selectors.
        /// </summary>
        public static string FormatCameraDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown Camera";
            if (IsIrOrBiometricCamera(name))
            {
                return $"{name} [IR / Biometric]";
            }
            if (IsVirtualCamera(name))
            {
                return $"{name} [Virtual]";
            }
            string lower = name.ToLowerInvariant();
            if (lower.Contains("rgb-ir") || lower.Contains("rgb/ir"))
            {
                return $"{name} [RGB-IR Color Sensor]";
            }
            return $"{name} [RGB Color]";
        }

        /// <summary>
        /// Configures optimal video stream resolution for laptop webcams.
        /// Native target: 1280x720 @ 30 FPS (hardware native sensor mode on Dell Precision 5540, 5550, 5560).
        /// Penalizes modes with < 15 FPS (snapshot pins) to avoid DirectShow pin negotiation crashes.
        /// </summary>
        public static VideoCapabilities ConfigureOptimalResolution(VideoCaptureDevice videoSource, int maxWidth = 1920, int maxHeight = 1080)
        {
            if (videoSource == null) return null;

            try
            {
                var caps = videoSource.VideoCapabilities;
                if (caps == null || caps.Length == 0) return null;

                VideoCapabilities bestCap = null;
                int bestScore = int.MinValue;

                foreach (var cap in caps)
                {
                    int w = cap.FrameSize.Width;
                    int h = cap.FrameSize.Height;
                    int fps = cap.AverageFrameRate;

                    // Ignore tiny sub-QVGA or oversized modes
                    if (w < 320 || h < 240) continue;
                    if (w > maxWidth || h > maxHeight) continue;

                    int score = 0;

                    // Heavily penalize low FPS (often snapshot/still pins or USB bus saturation modes)
                    if (fps < 15)
                    {
                        score -= 1000;
                    }
                    else
                    {
                        score += fps * 10;
                    }

                    // Dell Precision 5540/5550/5560 native hardware target: 1280x720 @ 30 FPS
                    if (w == 1280 && h == 720)
                    {
                        score += 500;
                    }
                    else if (w == 1920 && h == 1080 && fps >= 24)
                    {
                        score += 450;
                    }
                    else if (w == 960 && h == 540)
                    {
                        score += 300;
                    }
                    else if (w == 640 && h == 480)
                    {
                        score += 200;
                    }
                    else
                    {
                        score += (w * h) / 10000;
                    }

                    if (score > bestScore)
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
        /// Checks User, Machine, and NonPackaged (Win32 desktop apps) policy stores.
        /// </summary>
        public static bool IsWindowsCameraPrivacyRestricted(out string privacyDetail)
        {
            privacyDetail = "";

            try
            {
                // 1. Check Group Policy AllowCamera
                using var gpoKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Camera");
                if (gpoKey != null)
                {
                    var allow = gpoKey.GetValue("AllowCamera");
                    if (allow != null && Convert.ToInt32(allow) == 0)
                    {
                        privacyDetail = "Windows Camera is disabled via Corporate Group Policy (AllowCamera=0).";
                        return true;
                    }
                }

                // 2. Check HKCU ConsentStore (General Camera Access)
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

                // 3. Check HKCU NonPackaged (Desktop Win32 Apps Access)
                using var keyUserNonPackaged = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged");
                if (keyUserNonPackaged != null)
                {
                    var val = keyUserNonPackaged.GetValue("Value")?.ToString();
                    if (string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        privacyDetail = "'Let desktop apps access your camera' is turned OFF in Windows Settings.";
                        return true;
                    }
                }

                // 4. Check HKLM ConsentStore (Machine-wide Camera Access)
                using var keyLocal = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam");
                if (keyLocal != null)
                {
                    var val = keyLocal.GetValue("Value")?.ToString();
                    if (string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        privacyDetail = "Windows Camera Access is turned OFF system-wide (Machine Policy Deny).";
                        return true;
                    }
                }

                // 5. Check HKLM NonPackaged
                using var keyLocalNonPackaged = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged");
                if (keyLocalNonPackaged != null)
                {
                    var val = keyLocalNonPackaged.GetValue("Value")?.ToString();
                    if (string.Equals(val, "Deny", StringComparison.OrdinalIgnoreCase))
                    {
                        privacyDetail = "System-wide Desktop App Camera Access is turned OFF.";
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 1-Click Repair: Unblocks Windows Camera Privacy across User, Machine, and NonPackaged keys.
        /// Works because VeloxBench runs with Administrator elevation.
        /// </summary>
        public static bool TryUnblockWindowsCameraPrivacy(out string resultMessage)
        {
            try
            {
                // 1. Enable HKCU ConsentStore & NonPackaged
                using (var k1 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam"))
                {
                    k1?.SetValue("Value", "Allow", RegistryValueKind.String);
                }
                using (var k2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged"))
                {
                    k2?.SetValue("Value", "Allow", RegistryValueKind.String);
                }

                // 2. Enable HKLM ConsentStore & NonPackaged
                using (var k3 = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam"))
                {
                    k3?.SetValue("Value", "Allow", RegistryValueKind.String);
                }
                using (var k4 = Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged"))
                {
                    k4?.SetValue("Value", "Allow", RegistryValueKind.String);
                }

                // 3. Clear GPO blockage if present
                using (var k5 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Camera"))
                {
                    k5?.SetValue("AllowCamera", 1, RegistryValueKind.DWord);
                }

                resultMessage = "Windows Camera Access successfully enabled for all desktop applications.";
                return true;
            }
            catch (Exception ex)
            {
                resultMessage = "Failed to update camera privacy registry: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Launches the native Windows Camera UWP app using microsoft.windows.camera: protocol.
        /// Technicians can use this to quickly verify hardware-level sensor function.
        /// </summary>
        public static void LaunchWindowsCameraApp()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "microsoft.windows.camera:",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        /// <summary>
        /// Produces a human-readable diagnosis if DirectShow returns 0 devices.
        /// </summary>
        public static string GetDiagnosticsSummary()
        {
            if (IsWindowsCameraPrivacyRestricted(out string privacyMsg))
            {
                return $"⚠ {privacyMsg}\nClick [UNBLOCK CAMERA PRIVACY] or go to Windows Settings > Privacy > Camera.";
            }

            var pnpCams = GetPnpCameraHardware();
            if (pnpCams.Count > 0)
            {
                var cam = pnpCams[0];
                return $"ℹ Hardware Detected in Windows PnP: {cam.Name} ({cam.Manufacturer}).\n" +
                       "DirectShow filter unbridged or camera driver requires modern Media Foundation / UWP capture.\n" +
                       "Test via [OPEN WINDOWS CAMERA] button.";
            }

            return "No webcam capture devices detected by DirectShow or Windows PnP.\n" +
                   "Check Dell BIOS (F2 > System Configuration > Miscellaneous Devices > Camera = Enabled) or physical privacy shutter.";
        }
    }
}
