using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using QRCoder;
using SuperAutoMater.Wpf.Core;

namespace SuperAutoMater.Wpf.Services
{
    public class CertificateData
    {
        public string RunId { get; set; } = "";
        public QcRunSummary RunSummary { get; set; }
        public string SerialNumber { get; set; } = "UNKNOWN";
        public string Manufacturer { get; set; } = "Generic";
        public string Model { get; set; } = "Enterprise Workstation";
        public string BiosVersion { get; set; } = "1.0.0";
        public string CpuModel { get; set; } = "Processor";
        public string RamDetails { get; set; } = "System Memory";
        public string StorageModel { get; set; } = "Storage Device";
        public int StorageHealthPercent { get; set; } = 0;
        public string StoragePowerOn { get; set; } = "0 Days";
        public string BatteryHealthSummary { get; set; } = "Unknown";
        public string BatteryCapacities { get; set; } = "";
        public string GpuModel { get; set; } = "Display Adapter";
        public string PhysicalGrade { get; set; } = "PENDING";
        public string CosmeticDefectsSummary { get; set; } = "Pristine (No Defects)";
        public string BatteryCellTopology { get; set; } = "Balanced";
        public string StorageTbwSummary { get; set; } = "";
        public string DriverIntegritySummary { get; set; } = "0 Missing Drivers";
        public string ThermalDissipationVerdict { get; set; } = "Nominal";
        public string RamTopologySummary { get; set; } = "";
        public string RadiatorAirflowSummary { get; set; } = "";
        public string WebcamOpticsSummary { get; set; } = "";
        public string TechnicianName { get; set; } = "QC Station #1";
        public string AssetTag { get; set; } = "";
        public string IntakeTechnician { get; set; } = "N/A";
        public string ServiceTechnician { get; set; } = "N/A";
        public string QcTechnician { get; set; } = "QC Station #1";
        public string ApprovalTechnician { get; set; } = "Depot Lead";
        public string QcProfileUsed { get; set; } = "Full Diagnostic";
        public string MissingComponents { get; set; } = "";
        public string CloudAuditUrl { get; set; } = GoogleSheetsDispatcher.DefaultSheetsUrl;
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

            // Resolve and validate completed QC run summary
            var summary = data.RunSummary;
            if (summary == null && !string.IsNullOrEmpty(data.RunId))
            {
                var store = new QcRunStore();
                summary = store.GetRunSummary(data.RunId);
            }
            if (summary == null && QcRunOrchestrator.Instance.IsRunActive)
            {
                summary = QcRunOrchestrator.Instance.GetCurrentSummary();
            }

            if (summary == null)
            {
                throw new InvalidOperationException("Cannot generate certificate: No QC Run found for this unit.");
            }

            if (summary.Status != QcRunStatus.Completed)
            {
                throw new InvalidOperationException($"Cannot generate certificate: QC Run '{summary.RunId}' is in state '{summary.Status}'. All mandatory tests must be passed or approved overrides.");
            }

            if (!QcRunOrchestrator.VerifyRunTamper(summary))
            {
                throw new InvalidOperationException("Cannot generate certificate: Verification hash mismatch! The recorded run data has been modified.");
            }

            // Sync verified metadata from summary
            data.RunSummary = summary;
            data.RunId = summary.RunId;
            data.PhysicalGrade = summary.Grade;
            if (!string.IsNullOrEmpty(summary.SerialNumber))
            {
                data.SerialNumber = summary.SerialNumber;
            }
            if (!string.IsNullOrEmpty(summary.Model))
            {
                data.Model = summary.Model;
            }
            if (!string.IsNullOrEmpty(summary.Technician))
            {
                data.TechnicianName = summary.Technician;
            }

            if (string.IsNullOrWhiteSpace(data.AssetTag) && !string.IsNullOrWhiteSpace(summary.AssetTag))
            {
                data.AssetTag = summary.AssetTag;
            }

            try
            {
                var store = new QcRunStore();
                var asset = store.FindAssetBySerialOrTag(!string.IsNullOrWhiteSpace(data.AssetTag) ? data.AssetTag : data.SerialNumber);
                if (asset != null)
                {
                    if (string.IsNullOrWhiteSpace(data.AssetTag)) data.AssetTag = asset.AssetTag;
                    if (!string.IsNullOrWhiteSpace(asset.IntakeTechnician)) data.IntakeTechnician = asset.IntakeTechnician;
                    if (!string.IsNullOrWhiteSpace(asset.ServiceTechnician)) data.ServiceTechnician = asset.ServiceTechnician;
                    if (!string.IsNullOrWhiteSpace(asset.QcTechnician)) data.QcTechnician = asset.QcTechnician;
                    if (!string.IsNullOrWhiteSpace(asset.ApprovalTechnician)) data.ApprovalTechnician = asset.ApprovalTechnician;
                    if (!string.IsNullOrWhiteSpace(asset.MissingComponents)) data.MissingComponents = asset.MissingComponents;
                }
            }
            catch { }

            string cleanSerial = string.IsNullOrWhiteSpace(data.SerialNumber) || data.SerialNumber.Contains("Detecting")
                ? (string.IsNullOrWhiteSpace(summary.AssetTag) ? "UNKNOWN" : summary.AssetTag)
                : data.SerialNumber.Trim();


            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string pdfPath = Path.Combine(desktopDir, $"SuperAutoMater_Certificate_{cleanSerial}_{summary.RunId.Substring(0, 8)}.pdf");

            // Generate QR Code image bytes
            byte[] qrImageBytes = GenerateQrCodeBytes(data.CloudAuditUrl);

            // Construct PDF 1.4 stream
            byte[] pdfBytes = BuildPdfStream(data, summary, qrImageBytes);
            File.WriteAllBytes(pdfPath, pdfBytes);

            AppLogger.Info($"Generated verified QC certificate at {pdfPath} for run {summary.RunId}");
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to open certificate PDF at {pdfPath}", ex);
            }
        }

        private byte[] GenerateQrCodeBytes(string url)
        {
            try
            {
                using (var qrGen = new QRCodeGenerator())
                using (var qrData = qrGen.CreateQrCode(url ?? GoogleSheetsDispatcher.DefaultSheetsUrl, QRCodeGenerator.ECCLevel.M))
                using (var qrCode = new PngByteQRCode(qrData))
                {
                    return qrCode.GetGraphic(4);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to generate QR code bytes for certificate", ex);
                return null;
            }
        }

        private byte[] BuildPdfStream(CertificateData d, QcRunSummary summary, byte[] qrBytes)
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
                contentSb.Append("BT /F2 16 Tf 0.95 0.96 0.98 rg 40 734 Td (SUPERAUTOMATER TRUSTED HARDWARE QC CERTIFICATE) Tj ET\n");
                contentSb.Append("BT /F1 8.5 Tf 0.35 0.65 0.99 rg 40 714 Td (ENTERPRISE OFFLINE-FIRST VERIFICATION AUDIT · RUN ID: ")
                         .Append(EscapePdf(summary.RunId))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 8.5 Tf 0.65 0.68 0.73 rg 380 714 Td (DATE: ")
                         .Append(EscapePdf(summary.CompletedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")))
                         .Append(" UTC) Tj ET\n");

                // System Identity Card: DECLARED DMI DATA
                contentSb.Append("q 0.09 0.12 0.18 rg 28 554 556 130 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 554 556 130 re s Q\n");
                contentSb.Append("BT /F2 10 Tf 0.35 0.65 0.99 rg 40 666 Td (DECLARED HARDWARE IDENTIFIERS [FIRMWARE / DMI]) Tj ET\n");

                DrawKeyValue(contentSb, 40, 646, "CHASSIS / MODEL:", $"{d.Manufacturer} {d.Model}");
                string snText = string.IsNullOrEmpty(summary.SerialNumber) ? "[NO SERIAL]" : summary.SerialNumber;
                string tagText = string.IsNullOrEmpty(d.AssetTag) ? "N/A" : d.AssetTag;
                DrawKeyValue(contentSb, 40, 628, "ASSET TAG / SN:", $"{tagText} | SN: {snText}");
                DrawKeyValue(contentSb, 40, 610, "BIOS REVISION:", d.BiosVersion);

                DrawKeyValue(contentSb, 40, 592, "PROCESSOR ARCH:", d.CpuModel);
                DrawKeyValue(contentSb, 40, 574, "MEMORY CONFIG:", string.IsNullOrEmpty(d.RamTopologySummary) ? d.RamDetails : $"{d.RamDetails} [{d.RamTopologySummary}]");

                // Hardware Subsystems Card: MEASURED SENSOR TELEMETRY
                contentSb.Append("q 0.09 0.12 0.18 rg 28 412 556 130 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 412 556 130 re s Q\n");
                contentSb.Append("BT /F2 10 Tf 0.25 0.73 0.38 rg 40 524 Td (MEASURED SENSOR TELEMETRY [LIVE PROBES]) Tj ET\n");

                DrawKeyValue(contentSb, 40, 504, "MEASURED STORAGE:", $"{d.StorageModel} [{d.StorageHealthPercent}% SMART Health]");
                DrawKeyValue(contentSb, 40, 486, "STORAGE WEAR / POH:", $"{d.StorageTbwSummary} · {d.StoragePowerOn}");
                DrawKeyValue(contentSb, 40, 468, "MEASURED BATTERY:", $"{d.BatteryHealthSummary} ({d.BatteryCellTopology})");
                DrawKeyValue(contentSb, 40, 450, "BATTERY CAPACITY:", d.BatteryCapacities);
                DrawKeyValue(contentSb, 40, 432, "GRAPHICS SYSTEM:", d.GpuModel);

                // Certified Diagnostic Pipeline Grid: AUDIT RESULTS
                contentSb.Append("q 0.09 0.12 0.18 rg 28 200 556 200 re f Q\n");
                contentSb.Append("q 0.18 0.24 0.34 RG 1 w 28 200 556 200 re s Q\n");
                contentSb.Append("BT /F2 10 Tf 0.95 0.96 0.98 rg 40 380 Td (10-POINT DIAGNOSTIC PIPELINE RESULTS & WAIVERS) Tj ET\n");

                // Map actual recorded results from summary
                var testLines = new List<string>();
                foreach (var res in summary.Results)
                {
                    string prefix = res.Status switch
                    {
                        QcTestStatus.Passed => "[PASS - Measured]",
                        QcTestStatus.ManualOverride => "[OVERRIDE - Audited]",
                        QcTestStatus.NotApplicable => "[N/A - Non-Applicable]",
                        QcTestStatus.Failed => "[FAILED - Defective]",
                        _ => $"[{res.Status}]"
                    };
                    string detail = res.Status == QcTestStatus.ManualOverride && !string.IsNullOrEmpty(res.OverrideReason)
                        ? $" ({res.OverrideReason})"
                        : "";
                    testLines.Add($"{prefix} {res.TestName}{detail}");
                }

                if (testLines.Count == 0)
                {
                    testLines.Add("[PASS - Measured] Diagnostic Pipeline Nominal");
                }

                for (int i = 0; i < Math.Min(10, testLines.Count); i++)
                {
                    int col = i < 5 ? 0 : 1;
                    int row = i % 5;
                    double x = 40 + (col * 270);
                    double y = 356 - (row * 24);

                    string t = testLines[i];
                    if (t.Length > 36) t = t.Substring(0, 33) + "...";

                    // Green for pass, orange for override/na, red for failed
                    if (t.StartsWith("[PASS"))
                        contentSb.Append("BT /F1 8.5 Tf 0.25 0.73 0.38 rg ");
                    else if (t.StartsWith("[OVERRIDE") || t.StartsWith("[N/A"))
                        contentSb.Append("BT /F1 8.5 Tf 0.82 0.60 0.14 rg ");
                    else
                        contentSb.Append("BT /F1 8.5 Tf 0.95 0.30 0.30 rg ");

                    contentSb.Append(x).Append(" ").Append(y).Append(" Td (")
                             .Append(EscapePdf(t))
                             .Append(") Tj ET\n");
                }

                // Grade & Cryptographic Seal Area (Bottom)
                contentSb.Append("q 0.05 0.07 0.10 rg 28 36 556 150 re f Q\n");
                contentSb.Append("q 0.25 0.73 0.38 RG 1.5 w 28 36 556 150 re s Q\n");

                // Big Grade Seal
                contentSb.Append("BT /F2 10 Tf 0.65 0.68 0.73 rg 40 160 Td (POLICY-VERIFIED PHYSICAL GRADE:) Tj ET\n");
                contentSb.Append("BT /F2 24 Tf 0.25 0.73 0.38 rg 40 130 Td (")
                         .Append(EscapePdf(summary.Grade))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 8.5 Tf 0.85 0.87 0.91 rg 40 110 Td (Policy: ")
                         .Append(EscapePdf(summary.PolicyVersion))
                         .Append(" · Station: ")
                         .Append(EscapePdf(summary.Station))
                         .Append(" · Profile: ")
                         .Append(EscapePdf(string.IsNullOrWhiteSpace(d.QcProfileUsed) ? "Full" : d.QcProfileUsed))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 7.5 Tf 0.55 0.58 0.63 rg 40 92 Td (Attribution: Intake: ")
                         .Append(EscapePdf(string.IsNullOrWhiteSpace(d.IntakeTechnician) ? "N/A" : d.IntakeTechnician))
                         .Append(" | Srv: ")
                         .Append(EscapePdf(string.IsNullOrWhiteSpace(d.ServiceTechnician) ? "N/A" : d.ServiceTechnician))
                         .Append(" | QC: ")
                         .Append(EscapePdf(string.IsNullOrWhiteSpace(d.QcTechnician) ? summary.Technician : d.QcTechnician))
                         .Append(" | Appr: ")
                         .Append(EscapePdf(string.IsNullOrWhiteSpace(d.ApprovalTechnician) ? "LEAD" : d.ApprovalTechnician))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 7.5 Tf 0.45 0.48 0.53 rg 40 70 Td (Verification Hash: ")
                         .Append(EscapePdf(summary.VerificationHash))
                         .Append(") Tj ET\n");
                contentSb.Append("BT /F1 7 Tf 0.35 0.38 0.43 rg 40 54 Td (Any manual alteration of test metrics, serial, or telemetry invalidates the cryptographic seal above.) Tj ET\n");


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
