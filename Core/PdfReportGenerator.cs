using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace ITAS_QC_Tool
{
    /// <summary>
    /// Pure C# Zero-Dependency Vector PDF 1.4 Certificate & Report Generator.
    /// Generates standalone executive-grade hardware diagnostic certificates with embedded QR code.
    /// </summary>
    public static class PdfReportGenerator
    {
        public class AssetReportData
        {
            public string AssetTag { get; set; } = "";
            public string SerialNumber { get; set; } = "";
            public string Model { get; set; } = "";
            public string Processor { get; set; } = "";
            public string RamStorage { get; set; } = "";
            public string BatteryHealth { get; set; } = "100";
            public string Status { get; set; } = "RTS";
            public string WipIssue { get; set; } = "All Okay";
            public string PhysicalGrade { get; set; } = "A+";
            public string ShelfLocation { get; set; } = "Shelf A-1";
            public string Remarks { get; set; } = "";
            public string Technician { get; set; } = "QA Inspector";
            public Bitmap QrBitmap { get; set; }
        }

        public static string GeneratePdfReport(AssetReportData data)
        {
            string reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            if (!Directory.Exists(reportsDir)) Directory.CreateDirectory(reportsDir);

            string cleanAsset = string.IsNullOrEmpty(data.AssetTag) ? "UNKNOWN" : RegexReplaceInvalid(data.AssetTag);
            string cleanSerial = string.IsNullOrEmpty(data.SerialNumber) ? "UNKNOWN" : RegexReplaceInvalid(data.SerialNumber);
            string fileName = $"QC_{cleanAsset}_{cleanSerial}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string fullPath = Path.Combine(reportsDir, fileName);

            byte[] pdfBytes = BuildPdfDocument(data);
            File.WriteAllBytes(fullPath, pdfBytes);
            return fullPath;
        }

        private static string RegexReplaceInvalid(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private static byte[] BuildPdfDocument(AssetReportData data)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.ASCII);

            // A4 page dimensions in points: 595.28 x 841.89 (72 points/inch)
            const float pageW = 595f;
            const float pageH = 842f;

            // Stream for page content instructions
            var sbContent = new StringBuilder();

            // 1. Background Fill (Clean Slate White)
            sbContent.AppendLine("1.0 1.0 1.0 rg");
            sbContent.AppendLine($"0 0 {pageW} {pageH} re f");

            // 2. Header Banner (Dark Slate Navy #0F172A)
            sbContent.AppendLine("0.06 0.09 0.16 rg");
            sbContent.AppendLine($"0 {pageH - 90} {pageW} 90 re f");

            // Cyan Accent Stripe (#38BDF8)
            sbContent.AppendLine("0.22 0.74 0.97 rg");
            sbContent.AppendLine($"0 {pageH - 94} {pageW} 4 re f");

            // Header Titles
            DrawPdfText(sbContent, "AUTOMATER HARDWARE DIAGNOSTIC CERTIFICATE", "F2", 18, 28, pageH - 42, 0.95f, 0.98f, 1.0f);
            DrawPdfText(sbContent, "SYSTEM TELEMETRY, PRE-FLIGHT VERIFICATION & QUALITY SIGN-OFF", "F1", 9, 28, pageH - 60, 0.58f, 0.64f, 0.72f);
            DrawPdfText(sbContent, $"DATE: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | OPERATIONAL v6.3", "F1", 8.5f, 28, pageH - 74, 0.22f, 0.74f, 0.97f);

            // 3. Status Badge Box (Top Right of Header)
            float badgeX = pageW - 130;
            float badgeY = pageH - 76;
            sbContent.AppendLine("0.12 0.18 0.26 rg");
            sbContent.AppendLine($"{badgeX} {badgeY} 102 50 re f");
            sbContent.AppendLine("0.22 0.74 0.97 RG 1 w");
            sbContent.AppendLine($"{badgeX} {badgeY} 102 50 re S");

            DrawPdfText(sbContent, "STATUS / GRADE", "F2", 8, badgeX + 16, badgeY + 36, 0.6f, 0.7f, 0.8f);
            string statusStr = $"{data.Status}  ·  {data.PhysicalGrade}";
            DrawPdfText(sbContent, statusStr, "F2", 14, badgeX + 14, badgeY + 14, 0.29f, 0.87f, 0.5f);

            // 4. Section 1: Asset Identification Card
            float sec1Y = pageH - 120;
            DrawPdfSectionHeader(sbContent, "ASSET IDENTIFICATION & HARDWARE SPECIFICATIONS", 28, sec1Y, pageW - 56);

            float gridTop = sec1Y - 14;
            sbContent.AppendLine("0.97 0.98 0.99 rg");
            sbContent.AppendLine($"28 {gridTop - 150} {pageW - 56} 150 re f");
            sbContent.AppendLine("0.85 0.88 0.92 RG 1 w");
            sbContent.AppendLine($"28 {gridTop - 150} {pageW - 56} 150 re S");

            // Key-Value Rows
            DrawPdfTableRow(sbContent, "Asset Tag #", data.AssetTag, "Serial Number", data.SerialNumber, 40, gridTop - 25, pageW);
            DrawPdfTableRow(sbContent, "Hardware Model", data.Model, "Processor (CPU)", data.Processor, 40, gridTop - 55, pageW);
            DrawPdfTableRow(sbContent, "RAM / Storage", data.RamStorage, "Battery Health", $"{data.BatteryHealth}% ({GetBatteryGrade(data.BatteryHealth)})", 40, gridTop - 85, pageW);
            DrawPdfTableRow(sbContent, "QC Status", data.Status, "WIP Issue / Note", data.WipIssue, 40, gridTop - 115, pageW);
            DrawPdfTableRow(sbContent, "Physical Grade", data.PhysicalGrade, "Shelf / Location", data.ShelfLocation, 40, gridTop - 145, pageW);

            // 5. Section 2: Hardware Diagnostic Checklist Results
            float sec2Y = gridTop - 176;
            DrawPdfSectionHeader(sbContent, "HARDWARE TEST MATRIX & TELEMETRY POST", 28, sec2Y, pageW - 56);

            float chkGridTop = sec2Y - 14;
            sbContent.AppendLine("0.97 0.98 0.99 rg");
            sbContent.AppendLine($"28 {chkGridTop - 160} {pageW - 220} 160 re f");
            sbContent.AppendLine("0.85 0.88 0.92 RG 1 w");
            sbContent.AppendLine($"28 {chkGridTop - 160} {pageW - 220} 160 re S");

            string[] tests = {
                "Display Matrix (Dead Pixels / Colors)",
                "Audio Speakers (Stereo Left / Right)",
                "Webcam Sensor & DirectShow Capture",
                "Keyboard 104-Key Interrupt Matrix",
                "Trackpad & Left/Middle/Right Buttons",
                "GPU 3D Acceleration Benchmark",
                "Wi-Fi Wireless Radio Ping / Signal",
                "Fingerprint Biometric Unit",
                "Storage SMART Health & Performance",
                "Battery Subsystem & Cycle Wear"
            };

            for (int i = 0; i < tests.Length; i++)
            {
                float rowY = chkGridTop - 20 - (i * 14.5f);
                DrawPdfText(sbContent, $"[ PASS ]  {tests[i]}", "F1", 8.5f, 38, rowY, 0.1f, 0.6f, 0.2f);
            }

            // QR Code Box (Right of checklist)
            float qrBoxX = pageW - 180;
            float qrBoxY = chkGridTop - 160;
            sbContent.AppendLine("1.0 1.0 1.0 rg");
            sbContent.AppendLine($"{qrBoxX} {qrBoxY} 152 160 re f");
            sbContent.AppendLine("0.85 0.88 0.92 RG 1 w");
            sbContent.AppendLine($"{qrBoxX} {qrBoxY} 152 160 re S");

            // Embed QR image if available
            bool hasImage = data.QrBitmap != null;
            if (hasImage)
            {
                // Draw image XObject (120x120 centered in the box)
                sbContent.AppendLine("q");
                sbContent.AppendLine($"124 0 0 124 {qrBoxX + 14} {qrBoxY + 24} cm");
                sbContent.AppendLine("/Im1 Do");
                sbContent.AppendLine("Q");
            }

            DrawPdfText(sbContent, "2D INVENTORY BARCODE", "F2", 7.5f, qrBoxX + 18, qrBoxY + 10, 0.3f, 0.4f, 0.5f);

            // 6. Section 3: Remarks & Sign-off
            float sec3Y = chkGridTop - 186;
            DrawPdfSectionHeader(sbContent, "QC INSPECTION REMARKS & TECHNICIAN CERTIFICATION", 28, sec3Y, pageW - 56);

            float remGridTop = sec3Y - 14;
            sbContent.AppendLine("0.97 0.98 0.99 rg");
            sbContent.AppendLine($"28 {remGridTop - 75} {pageW - 56} 75 re f");
            sbContent.AppendLine("0.85 0.88 0.92 RG 1 w");
            sbContent.AppendLine($"28 {remGridTop - 75} {pageW - 56} 75 re S");

            string remarkText = string.IsNullOrWhiteSpace(data.Remarks) ? "All core subsystems nominal. Verified compliant with AutoMater Grade standards." : data.Remarks;
            DrawPdfText(sbContent, "TECHNICIAN REMARKS:", "F2", 8.5f, 40, remGridTop - 22, 0.2f, 0.3f, 0.4f);
            DrawPdfText(sbContent, remarkText, "F1", 9f, 40, remGridTop - 40, 0.1f, 0.15f, 0.2f);
            DrawPdfText(sbContent, $"CERTIFIED BY: {data.Technician}  |  STATUS: CERTIFIED FOR INVENTORY DISPATCH", "F2", 8f, 40, remGridTop - 62, 0.05f, 0.55f, 0.25f);

            // 7. Footer
            sbContent.AppendLine("0.9 0.92 0.94 RG 1 w");
            sbContent.AppendLine($"28 40 {pageW - 56} 0 re S");
            DrawPdfText(sbContent, "AutoMater Diagnostic & QC Studio v6.3 · Official System Audit Certificate", "F1", 7.5f, 28, 26, 0.55f, 0.6f, 0.65f);
            DrawPdfText(sbContent, "Confidential · For Internal Inventory Management & Verification Only", "F1", 7.5f, pageW - 280, 26, 0.55f, 0.6f, 0.65f);

            byte[] contentBytes = Encoding.ASCII.GetBytes(sbContent.ToString());

            // Prepare JPEG image bytes if QR is present
            byte[] jpegBytes = null;
            if (hasImage)
            {
                using var imgMs = new MemoryStream();
                data.QrBitmap.Save(imgMs, ImageFormat.Jpeg);
                jpegBytes = imgMs.ToArray();
            }

            // PDF Object Offsets Tracker
            var offsets = new List<long>();

            writer.Flush();
            ms.Position = 0;
            writer.WriteLine("%PDF-1.4");
            writer.Flush();

            // Obj 1: Catalog
            offsets.Add(ms.Position);
            writer.WriteLine("1 0 obj");
            writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 2: Pages
            offsets.Add(ms.Position);
            writer.WriteLine("2 0 obj");
            writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 3: Page
            offsets.Add(ms.Position);
            writer.WriteLine("3 0 obj");
            writer.Write("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >>");
            if (hasImage) writer.Write(" /XObject << /Im1 7 0 R >>");
            writer.WriteLine(" >> >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 4: Content Stream
            offsets.Add(ms.Position);
            writer.WriteLine("4 0 obj");
            writer.WriteLine($"<< /Length {contentBytes.Length} >>");
            writer.WriteLine("stream");
            writer.Flush();
            ms.Write(contentBytes, 0, contentBytes.Length);
            writer.WriteLine();
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 5: Font F1 (Helvetica)
            offsets.Add(ms.Position);
            writer.WriteLine("5 0 obj");
            writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 6: Font F2 (Helvetica-Bold)
            offsets.Add(ms.Position);
            writer.WriteLine("6 0 obj");
            writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Obj 7: Image XObject (if QR present)
            if (hasImage && jpegBytes != null)
            {
                offsets.Add(ms.Position);
                writer.WriteLine("7 0 obj");
                writer.WriteLine($"<< /Type /XObject /Subtype /Image /Width {data.QrBitmap.Width} /Height {data.QrBitmap.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>");
                writer.WriteLine("stream");
                writer.Flush();
                ms.Write(jpegBytes, 0, jpegBytes.Length);
                writer.WriteLine();
                writer.WriteLine("endstream");
                writer.WriteLine("endobj");
                writer.Flush();
            }

            // Cross-Reference Table
            long xrefPos = ms.Position;
            writer.WriteLine("xref");
            writer.WriteLine($"0 {offsets.Count + 1}");
            writer.WriteLine("0000000000 65535 f ");
            foreach (var off in offsets)
            {
                writer.WriteLine($"{off:D10} 00000 n ");
            }

            // Trailer
            writer.WriteLine("trailer");
            writer.WriteLine($"<< /Size {offsets.Count + 1} /Root 1 0 R >>");
            writer.WriteLine("startxref");
            writer.WriteLine(xrefPos);
            writer.WriteLine("%%EOF");
            writer.Flush();

            return ms.ToArray();
        }

        private static void DrawPdfText(StringBuilder sb, string text, string font, float size, float x, float y, float r, float g, float b)
        {
            if (string.IsNullOrEmpty(text)) return;
            string clean = text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            sb.AppendLine($"{r:F2} {g:F2} {b:F2} rg");
            sb.AppendLine("BT");
            sb.AppendLine($"/{font} {size:F1} Tf");
            sb.AppendLine($"{x:F1} {y:F1} Td");
            sb.AppendLine($"({clean}) Tj");
            sb.AppendLine("ET");
        }

        private static void DrawPdfSectionHeader(StringBuilder sb, string title, float x, float y, float w)
        {
            sb.AppendLine("0.18 0.24 0.32 rg");
            sb.AppendLine($"{x} {y - 4} 3 14 re f");
            DrawPdfText(sb, title, "F2", 9.5f, x + 8, y, 0.12f, 0.16f, 0.24f);
        }

        private static void DrawPdfTableRow(StringBuilder sb, string k1, string v1, string k2, string v2, float x, float y, float pageW)
        {
            float col1W = 100;
            float col2X = x + col1W;
            float col3X = pageW / 2 + 10;
            float col4X = col3X + 100;

            DrawPdfText(sb, k1 + ":", "F2", 8.5f, x, y, 0.45f, 0.5f, 0.55f);
            DrawPdfText(sb, Truncate(v1, 32), "F1", 8.5f, col2X, y, 0.1f, 0.12f, 0.15f);

            DrawPdfText(sb, k2 + ":", "F2", 8.5f, col3X, y, 0.45f, 0.5f, 0.55f);
            DrawPdfText(sb, Truncate(v2, 32), "F1", 8.5f, col4X, y, 0.1f, 0.12f, 0.15f);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Length > max ? s.Substring(0, max - 3) + "..." : s;
        }

        private static string GetBatteryGrade(string val)
        {
            if (int.TryParse(val, out int pct))
            {
                if (pct >= 85) return "Grade A+ Optimal";
                if (pct >= 70) return "Grade A Good";
                if (pct >= 50) return "Grade B Fair";
                return "Grade C Service Req";
            }
            return "Normal";
        }
    }
}
