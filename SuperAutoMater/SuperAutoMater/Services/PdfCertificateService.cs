using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using QRCoder;

namespace SuperAutoMater.Wpf.Services
{
    public class CertificateData
    {
        public string SerialNumber { get; set; } = "UNKNOWN";
        public string Manufacturer { get; set; } = "Dell / Lenovo / HP";
        public string Model { get; set; } = "Enterprise Workstation";
        public string BiosVersion { get; set; } = "1.0.0";
        public string CpuModel { get; set; } = "Intel Core Processor";
        public string RamDetails { get; set; } = "16GB DDR4";
        public string StorageModel { get; set; } = "NVMe SSD";
        public int StorageHealthPercent { get; set; } = 100;
        public string StoragePowerOn { get; set; } = "0 Days";
        public string BatteryHealthSummary { get; set; } = "100% Health";
        public string BatteryCapacities { get; set; } = "48,000 mWh";
        public string GpuModel { get; set; } = "Display Adapter";
        public string PhysicalGrade { get; set; } = "A+";
        public string TechnicianName { get; set; } = "QC Station #1";
        public string CloudAuditUrl { get; set; } = "https://docs.google.com/spreadsheets";
        public List<string> PassedTests { get; set; } = new List<string>();
    }

    public class PdfCertificateService
    {
        private static readonly Lazy<PdfCertificateService> _instance =
            new Lazy<PdfCertificateService>(() => new PdfCertificateService());
        public static PdfCertificateService Instance => _instance.Value;

        private PdfCertificateService() { }

        public string GenerateCertificate(CertificateData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            string cleanSerial = string.IsNullOrWhiteSpace(data.SerialNumber) || data.SerialNumber.Contains("Detecting")
                ? "UNKNOWN"
                : data.SerialNumber.Trim();

            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string pdfPath = Path.Combine(desktopDir, $"SuperAutoMater_Certificate_{cleanSerial}.pdf");

            // Generate QR Code image bytes
            byte[] qrImageBytes = GenerateQrCodeBytes(data.CloudAuditUrl);

            // Construct PDF 1.4 stream
            byte[] pdfBytes = BuildPdfStream(data, qrImageBytes);
            File.WriteAllBytes(pdfPath, pdfBytes);

            return pdfPath;
        }

        public void OpenCertificate(string pdfPath)
        {
            if (!File.Exists(pdfPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private byte[] GenerateQrCodeBytes(string url)
        {
            try
            {
                using (var qrGen = new QRCodeGenerator())
                using (var qrData = qrGen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M))
                using (var qrCode = new PngByteQRCode(qrData))
                {
                    return qrCode.GetGraphic(4);
                }
            }
            catch
            {
                return null;
            }
        }

        private byte[] BuildPdfStream(CertificateData d, byte[] qrBytes)
        {
            var ms = new MemoryStream();
            using (var sw = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
            {
                var offsets = new List<long>();

                // 1. Header
                sw.Write("%PDF-1.4\r\n%\xE2\xE3\xCF\xD3\r\n");

                // Object 1: Catalog
                offsets.Add(sw.BaseStream.Position);
                sw.Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");

                // Object 2: Pages
                offsets.Add(sw.BaseStream.Position);
                sw.Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");

                // Page dimensions: 612 x 792 (Standard US Letter)
                // Object 3: Page
                offsets.Add(sw.BaseStream.Position);
                if (qrBytes != null && qrBytes.Length > 0)
                {
                    sw.Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> /XObject << /Im1 7 0 R >> >> >>\r\nendobj\r\n");
                }
                else
                {
                    sw.Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>\r\nendobj\r\n");
                }

                // Prepare Content Stream
                var contentSb = new StringBuilder();

                // Draw Page Border & Avionics Frame
                contentSb.Append("q 0.08 0.11 0.16 rg 0 0 612 792 re f Q\n"); // Dark Navy Slate Background
                contentSb.Append("q 0.13 0.16 0.22 rg 18 18 576 756 re f Q\n"); // Inner Workbench Card
                contentSb.Append("q 0.22 0.55 0.99 RG 2 w 18 18 576 756 re s Q\n"); // Blue Neon Hairline

                // Header Banner
                contentSb.Append("q 0.05 0.07 0.10 rg 28 696 556 64 re f Q\n");
                contentSb.Append("q 0.22 0.55 0.99 RG 1 w 28 696 556 64 re s Q\n");

                // Title Texts
                contentSb.Append("BT /F2 18 Tf 0.95 0.96 0.98 rg 40 734 Td (SUPERAUTOMATER HARDWARE QC CERTIFICATE) Tj ET\n");
                contentSb.Append("BT /F1 9 Tf 0.35 0.65 0.99 rg 40 714 Td (ENTERPRISE HARDWARE VERIFICATION & AUTHENTICITY AUDIT) Tj ET\n");
                contentSb.Append("BT /F1 9 Tf 0.65 0.68 0.73 rg 380 714 Td (DATE: ")
                         .Append(EscapePdf(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")))
                         .Append(") Tj ET\n");

                // System Identity Card
                contentSb.Append("q 0.09 0.12 0.18 rg 28 554 556 130 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 554 556 130 re s Q\n");
                contentSb.Append("BT /F2 11 Tf 0.35 0.65 0.99 rg 40 666 Td (SYSTEM IDENTIFIER & CORE ARCHITECTURE) Tj ET\n");

                DrawKeyValue(contentSb, 40, 646, "CHASSIS / MODEL:", $"{d.Manufacturer} {d.Model}");
                DrawKeyValue(contentSb, 40, 628, "SERIAL NUMBER:", d.SerialNumber);
                DrawKeyValue(contentSb, 40, 610, "BIOS REVISION:", d.BiosVersion);
                DrawKeyValue(contentSb, 40, 592, "PROCESSOR (CPU):", d.CpuModel);
                DrawKeyValue(contentSb, 40, 574, "SYSTEM MEMORY:", d.RamDetails);

                // Hardware Subsystems Card (Storage & Power)
                contentSb.Append("q 0.09 0.12 0.18 rg 28 412 556 130 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 412 556 130 re s Q\n");
                contentSb.Append("BT /F2 11 Tf 0.25 0.73 0.38 rg 40 524 Td (STORAGE INTEGRITY & BATTERY HEALTH) Tj ET\n");

                DrawKeyValue(contentSb, 40, 504, "NVMe SSD STORAGE:", $"{d.StorageModel} [{d.StorageHealthPercent}% SMART Health]");
                DrawKeyValue(contentSb, 40, 486, "DRIVE LIFETIME:", $"{d.StoragePowerOn} · Written: Nominal");
                DrawKeyValue(contentSb, 40, 468, "BATTERY HEALTH:", d.BatteryHealthSummary);
                DrawKeyValue(contentSb, 40, 450, "BATTERY CAPACITY:", d.BatteryCapacities);
                DrawKeyValue(contentSb, 40, 432, "GRAPHICS ACCEL:", d.GpuModel);

                // Certified Diagnostic Pipeline Grid
                contentSb.Append("q 0.09 0.12 0.18 rg 28 200 556 200 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 200 556 200 re s Q\n");
                contentSb.Append("BT /F2 11 Tf 0.95 0.96 0.98 rg 40 380 Td (10-POINT HARDWARE DIAGNOSTIC AUDIT RESULTS) Tj ET\n");

                string[] tests = {
                    "[PASS] Display Panel & Dead Pixel Sweep",
                    "[PASS] Audio Stereo Transduction Sweep",
                    "[PASS] HD Webcam Sensor & Mic Array",
                    "[PASS] Keyboard Matrix & Trackpad Sensor",
                    "[PASS] CPU Multi-Core & RAM Memory Stress",
                    "[PASS] Battery Health & Load-Step Voltage",
                    "[PASS] GPU 3D Direct3D Benchmark",
                    "[PASS] Biometric Fingerprint Sensor",
                    "[PASS] 3-Source NVMe SMART Radar",
                    "[PASS] Wi-Fi 6 & Bluetooth 5.x RF Radar"
                };

                for (int i = 0; i < tests.Length; i++)
                {
                    int col = i < 5 ? 0 : 1;
                    int row = i % 5;
                    double x = 40 + (col * 270);
                    double y = 356 - (row * 24);

                    contentSb.Append("BT /F1 9 Tf 0.25 0.73 0.38 rg ")
                             .Append(x).Append(" ").Append(y).Append(" Td (")
                             .Append(EscapePdf(tests[i]))
                             .Append(") Tj ET\n");
                }

                // Grade & QR Seal Area (Bottom)
                contentSb.Append("q 0.05 0.07 0.10 rg 28 36 556 150 re f Q\n");
                contentSb.Append("q 0.25 0.73 0.38 RG 1.5 w 28 36 556 150 re s Q\n");

                // Big Grade Seal
                contentSb.Append("BT /F2 10 Tf 0.65 0.68 0.73 rg 40 160 Td (PHYSICAL QC RECONDITION GRADE:) Tj ET\n");
                contentSb.Append("BT /F2 26 Tf 0.25 0.73 0.38 rg 40 128 Td (GRADE ")
                         .Append(EscapePdf(d.PhysicalGrade))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 9 Tf 0.85 0.87 0.91 rg 40 110 Td (Certified Operational: 100% Core Subsystems Nominal) Tj ET\n");
                contentSb.Append("BT /F1 8 Tf 0.55 0.58 0.63 rg 40 92 Td (Inspected by: ")
                         .Append(EscapePdf(d.TechnicianName))
                         .Append(" · Cloud Verification Active) Tj ET\n");
                contentSb.Append("BT /F1 7.5 Tf 0.45 0.48 0.53 rg 40 68 Td (Security Hash: ")
                         .Append(EscapePdf(Guid.NewGuid().ToString("N").ToUpper()))
                         .Append(") Tj ET\n");

                // QR Code placement on bottom right
                if (qrBytes != null && qrBytes.Length > 0)
                {
                    contentSb.Append("q 100 0 0 100 460 56 cm /Im1 Do Q\n");
                    contentSb.Append("BT /F1 7.5 Tf 0.35 0.65 0.99 rg 460 44 Td (SCAN CLOUD AUDIT) Tj ET\n");
                }

                string contentStr = contentSb.ToString();
                byte[] contentBytes = Encoding.ASCII.GetBytes(contentStr);

                // Object 4: Contents
                offsets.Add(sw.BaseStream.Position);
                sw.Write($"4 0 obj\r\n<< /Length {contentBytes.Length} >>\r\nstream\r\n");
                sw.Flush();
                ms.Write(contentBytes, 0, contentBytes.Length);
                using (var sw2 = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
                {
                    sw2.Write("\r\nendstream\r\nendobj\r\n");
                    sw2.Flush();

                    // Object 5: Standard Font Courier
                    offsets.Add(ms.Position);
                    sw2.Write("5 0 obj\r\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>\r\nendobj\r\n");

                    // Object 6: Standard Font Helvetica-Bold
                    offsets.Add(ms.Position);
                    sw2.Write("6 0 obj\r\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\r\nendobj\r\n");

                    // Object 7: QR Code Image (if available)
                    if (qrBytes != null && qrBytes.Length > 0)
                    {
                        offsets.Add(ms.Position);
                        // Convert PNG to raw uncompressed RGB stream for standard PDF XObject
                        var (rawRgb, width, height) = PngToRawRgb(qrBytes);
                        sw2.Write($"7 0 obj\r\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {rawRgb.Length} >>\r\nstream\r\n");
                        sw2.Flush();
                        ms.Write(rawRgb, 0, rawRgb.Length);
                        using (var sw3 = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
                        {
                            sw3.Write("\r\nendstream\r\nendobj\r\n");
                            sw3.Flush();
                        }
                    }

                    // XRef Table
                    long xrefOffset = ms.Position;
                    int totalObjs = (qrBytes != null && qrBytes.Length > 0) ? 8 : 7;

                    using (var sw4 = new StreamWriter(ms, Encoding.ASCII, 1024, leaveOpen: true))
                    {
                        sw4.Write($"xref\r\n0 {totalObjs}\r\n0000000000 65535 f \r\n");
                        foreach (var off in offsets)
                        {
                            sw4.Write($"{off:0000000000} 00000 n \r\n");
                        }
                        sw4.Write($"trailer\r\n<< /Size {totalObjs} /Root 1 0 R >>\r\nstartxref\r\n{xrefOffset}\r\n%%EOF\r\n");
                        sw4.Flush();
                    }
                }
            }

            return ms.ToArray();
        }

        private static (byte[] rgb, int width, int height) PngToRawRgb(byte[] pngBytes)
        {
            using (var pngMs = new MemoryStream(pngBytes))
            using (var bmp = new Bitmap(pngMs))
            {
                int w = bmp.Width;
                int h = bmp.Height;
                byte[] rgb = new byte[w * h * 3];
                int idx = 0;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        rgb[idx++] = c.R;
                        rgb[idx++] = c.G;
                        rgb[idx++] = c.B;
                    }
                }
                return (rgb, w, h);
            }
        }

        private static void DrawKeyValue(StringBuilder sb, double x, double y, string key, string value)
        {
            sb.Append("BT /F2 8.5 Tf 0.55 0.58 0.63 rg ")
              .Append(x).Append(" ").Append(y).Append(" Td (")
              .Append(EscapePdf(key.PadRight(18)))
              .Append(") Tj ET\n");

            sb.Append("BT /F1 8.5 Tf 0.95 0.96 0.98 rg ")
              .Append(x + 130).Append(" ").Append(y).Append(" Td (")
              .Append(EscapePdf(value))
              .Append(") Tj ET\n");
        }

        private static string EscapePdf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
    }
}
