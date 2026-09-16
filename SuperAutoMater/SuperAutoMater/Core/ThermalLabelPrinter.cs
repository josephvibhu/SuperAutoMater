using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using QRCoder;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SuperAutoMater
{
    public enum LabelSizePreset
    {
        Compact2x1,
        Chassis3x2,
        Pallet4x2
    }

    public enum BarcodeMode
    {
        QrCode,
        Code128
    }

    /// <summary>
    /// Thermal Chassis Label & Sticker Printing Engine.
    /// Outputs high-contrast monochrome labels designed for standard 2"x1", 3"x2", and 4"x2"
    /// thermal printers (Zebra, Brother, Dymo, Xprinter, POS printers) and produces true-size PDF exports.
    /// </summary>
    public static class ThermalLabelPrinter
    {
        public static (int Width, int Height, int HundredthW, int HundredthH, int PtW, int PtH) GetPresetDimensions(LabelSizePreset preset)
        {
            return preset switch
            {
                LabelSizePreset.Compact2x1 => (500, 250, 200, 100, 144, 72),
                LabelSizePreset.Pallet4x2  => (800, 400, 400, 200, 288, 144),
                _                          => (600, 400, 300, 200, 216, 144) // Default Chassis 3"x2"
            };
        }

        public static void PrintLabel(
            AssetQueueRecord record,
            LabelSizePreset preset = LabelSizePreset.Chassis3x2,
            BarcodeMode barcodeMode = BarcodeMode.QrCode,
            IWin32Window owner = null)
        {
            if (record == null) return;

            try
            {
                var dims = GetPresetDimensions(preset);
                using (PrintDocument doc = new PrintDocument())
                {
                    doc.DefaultPageSettings.PaperSize = new PaperSize("ThermalLabel", dims.HundredthW, dims.HundredthH);
                    doc.DefaultPageSettings.Margins = new Margins(8, 8, 8, 8);

                    doc.PrintPage += (s, e) =>
                    {
                        // Render canonical high-resolution label bitmap
                        using (Bitmap labelBmp = RenderLabelBitmap(record, preset, barcodeMode))
                        {
                            // Detect if the chosen printer page is a dedicated thermal roll or full sheet (Letter, A4, Print to PDF)
                            bool isThermalRoll = e.PageBounds.Width <= dims.HundredthW + 80;

                            Rectangle destRect;
                            if (isThermalRoll)
                            {
                                // Thermal roll: fill the printable roll area neatly with small margin
                                destRect = new Rectangle(
                                    Math.Max(0, e.MarginBounds.Left),
                                    Math.Max(0, e.MarginBounds.Top),
                                    Math.Max(10, e.MarginBounds.Width),
                                    Math.Max(10, e.MarginBounds.Height));
                            }
                            else
                            {
                                // Full page / Microsoft Print to PDF:
                                // Draw the label at TRUE physical dimensions (e.g. 3"x2" = 300x200 hundredths of an inch)
                                destRect = new Rectangle(50, 50, dims.HundredthW, dims.HundredthH);

                                // Draw dashed outline cut-guide around the sticker boundary
                                using (Pen cutPen = new Pen(Color.FromArgb(170, 170, 170), 1))
                                {
                                    cutPen.DashStyle = DashStyle.Dash;
                                    e.Graphics.DrawRectangle(cutPen, destRect.X - 1, destRect.Y - 1, destRect.Width + 2, destRect.Height + 2);
                                }
                            }

                            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
                            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            e.Graphics.DrawImage(labelBmp, destRect);
                        }

                        e.HasMorePages = false;
                    };

                    using (PrintDialog dlg = new PrintDialog())
                    {
                        dlg.Document = doc;
                        dlg.UseEXDialog = true;
                        if (dlg.ShowDialog(owner) == DialogResult.OK)
                        {
                            doc.Print();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printing Error: " + ex.Message, "Label Printer Error");
            }
        }

        /// <summary>
        /// Generates a genuine standalone 1-page PDF document containing the thermal label
        /// at exact physical dimensions (e.g. 3"x2" / 216x144 pt) without requiring any printer driver.
        /// </summary>
        public static bool ExportPdfLabel(
            AssetQueueRecord record,
            LabelSizePreset preset,
            BarcodeMode barcodeMode,
            string targetPath)
        {
            if (record == null || string.IsNullOrWhiteSpace(targetPath)) return false;

            try
            {
                var dims = GetPresetDimensions(preset);
                using (Bitmap bmp = RenderLabelBitmap(record, preset, barcodeMode))
                using (MemoryStream jpegStream = new MemoryStream())
                {
                    // Save as high-quality JPEG for standard PDF DCTDecode embedding
                    bmp.Save(jpegStream, ImageFormat.Jpeg);
                    byte[] jpegBytes = jpegStream.ToArray();

                    byte[] pdfBytes = BuildSingleImagePdf(jpegBytes, dims.Width, dims.Height, dims.PtW, dims.PtH);
                    File.WriteAllBytes(targetPath, pdfBytes);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static byte[] BuildSingleImagePdf(byte[] jpegBytes, int imgW, int imgH, int ptW, int ptH)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                long[] objOffsets = new long[6];

                void WriteAscii(string s)
                {
                    byte[] b = Encoding.ASCII.GetBytes(s);
                    ms.Write(b, 0, b.Length);
                }

                WriteAscii("%PDF-1.4\r\n%\xE2\xE3\xCF\xD3\r\n");

                // Obj 1: Catalog
                objOffsets[1] = ms.Position;
                WriteAscii("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");

                // Obj 2: Pages
                objOffsets[2] = ms.Position;
                WriteAscii("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");

                // Obj 3: Page (exact physical dimensions in 72 pt/in)
                objOffsets[3] = ms.Position;
                WriteAscii($"3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {ptW} {ptH}] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>\r\nendobj\r\n");

                // Obj 4: Image XObject (JPEG DCTDecode)
                objOffsets[4] = ms.Position;
                WriteAscii($"4 0 obj\r\n<< /Type /XObject /Subtype /Image /Width {imgW} /Height {imgH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\r\nstream\r\n");
                ms.Write(jpegBytes, 0, jpegBytes.Length);
                WriteAscii("\r\nendstream\r\nendobj\r\n");

                // Obj 5: Contents
                objOffsets[5] = ms.Position;
                string streamContent = $"q {ptW} 0 0 {ptH} 0 0 cm /Im1 Do Q\r\n";
                byte[] contentBytes = Encoding.ASCII.GetBytes(streamContent);
                WriteAscii($"5 0 obj\r\n<< /Length {contentBytes.Length} >>\r\nstream\r\n{streamContent}endstream\r\nendobj\r\n");

                // Xref table
                long xrefPos = ms.Position;
                WriteAscii("xref\r\n0 6\r\n0000000000 65535 f \r\n");
                for (int i = 1; i <= 5; i++)
                {
                    WriteAscii($"{objOffsets[i]:D10} 00000 n \r\n");
                }

                // Trailer
                WriteAscii($"trailer\r\n<< /Size 6 /Root 1 0 R >>\r\nstartxref\r\n{xrefPos}\r\n%%EOF\r\n");

                return ms.ToArray();
            }
        }

        public static Bitmap RenderLabelBitmap(
            AssetQueueRecord record,
            LabelSizePreset preset = LabelSizePreset.Chassis3x2,
            BarcodeMode barcodeMode = BarcodeMode.QrCode)
        {
            var dims = GetPresetDimensions(preset);
            Bitmap bmp = new Bitmap(dims.Width, dims.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                RenderLabelGraphics(g, new Rectangle(8, 8, dims.Width - 16, dims.Height - 16), record, preset, barcodeMode);
            }
            return bmp;
        }

        public static Bitmap RenderLabelBitmapCustom(
            AssetQueueRecord record,
            int width,
            int height,
            LabelSizePreset preset = LabelSizePreset.Chassis3x2,
            BarcodeMode barcodeMode = BarcodeMode.QrCode)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                RenderLabelGraphics(g, new Rectangle(8, 8, width - 16, height - 16), record, preset, barcodeMode);
            }
            return bmp;
        }

        private static void RenderLabelGraphics(
            Graphics g,
            Rectangle bounds,
            AssetQueueRecord r,
            LabelSizePreset preset = LabelSizePreset.Chassis3x2,
            BarcodeMode barcodeMode = BarcodeMode.QrCode)
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            bool isCompact = preset == LabelSizePreset.Compact2x1;
            bool isPallet = preset == LabelSizePreset.Pallet4x2;

            // -------------------------------------------------------------
            // 1. INVERTED HEADER BADGE
            // -------------------------------------------------------------
            int headerH = isCompact ? 28 : (isPallet ? 40 : 34);
            Rectangle headerRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, headerH);
            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(blackBrush, headerRect);
            }

            float headerFontSize = isCompact ? 9.5f : (isPallet ? 12.5f : 10.5f);
            using (Font fontHeader = new Font("Arial", headerFontSize, FontStyle.Bold))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (StringFormat sfHeader = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                string headerTitle = isCompact
                    ? "SUPERAUTOMATER QC CERTIFIED"
                    : (isPallet
                        ? "SUPERAUTOMATER · REFURB LOGISTICS PALLET / MASTER TAG"
                        : "SUPERAUTOMATER · REFURB QC CERTIFIED");
                g.DrawString(headerTitle, fontHeader, whiteBrush, headerRect, sfHeader);
            }

            // -------------------------------------------------------------
            // 2. LEFT COLUMN: HIGH-CONTRAST BARCODE / QR CODE
            // -------------------------------------------------------------
            int contentY = headerRect.Bottom + 6;
            int contentH = bounds.Bottom - contentY - 4;

            int leftColW = isCompact ? 125 : (isPallet ? 210 : 180);
            Rectangle leftColRect = new Rectangle(bounds.X + 4, contentY, leftColW, contentH);

            string serialVal = string.IsNullOrWhiteSpace(r.Serial_Number) ? "UNKNOWN_SN" : r.Serial_Number.Trim();
            string tagVal = string.IsNullOrWhiteSpace(r.Asset_Tag) ? serialVal : r.Asset_Tag.Trim();
            string qrPayload = $"ASSET:{tagVal}|SN:{serialVal}|SPEC:{r.Processor}_{r.Memory}|BATT:{r.Battery_Health}%|GRADE:{r.Physical_Grade}|TECH:{r.Technician}";

            using (SolidBrush textBrush = new SolidBrush(Color.Black))
            using (StringFormat sfCenter = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            using (StringFormat sfLeft = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                if (barcodeMode == BarcodeMode.Code128)
                {
                    int barH = isCompact ? 60 : (isPallet ? 95 : 80);
                    Rectangle barRect = new Rectangle(leftColRect.X, leftColRect.Y + 6, leftColRect.Width, barH);
                    try
                    {
                        var writer = new BarcodeWriter
                        {
                            Format = BarcodeFormat.CODE_128,
                            Options = new EncodingOptions
                            {
                                Width = barRect.Width,
                                Height = barRect.Height,
                                Margin = 1,
                                PureBarcode = true
                            }
                        };
                        using (Bitmap barBmp = writer.Write(serialVal))
                        {
                            g.DrawImage(barBmp, barRect);
                        }
                    }
                    catch
                    {
                        DrawFallbackQr(g, barRect, qrPayload);
                    }

                    // Centered S/N under barcode
                    float snCodeFont = isCompact ? 8.5f : (isPallet ? 11f : 9.5f);
                    using (Font fontCodeSn = new Font("Consolas", snCodeFont, FontStyle.Bold))
                    {
                        Rectangle snRect = new Rectangle(leftColRect.X, barRect.Bottom + 2, leftColRect.Width, 18);
                        g.DrawString(serialVal, fontCodeSn, textBrush, snRect, sfCenter);
                    }

                    // Asset Tag under S/N
                    float tagFont = isCompact ? 8.5f : (isPallet ? 11f : 9.5f);
                    using (Font fontTag = new Font("Arial", tagFont, FontStyle.Bold))
                    {
                        Rectangle tagRect = new Rectangle(leftColRect.X, barRect.Bottom + 22, leftColRect.Width, 20);
                        g.DrawString($"TAG: {tagVal}", fontTag, textBrush, tagRect, sfCenter);
                    }
                }
                else
                {
                    // QR Code Mode
                    int qrSize = isCompact ? 115 : (isPallet ? 190 : 155);
                    Rectangle qrRect = new Rectangle(leftColRect.X + (leftColRect.Width - qrSize) / 2, leftColRect.Y + 2, qrSize, qrSize);
                    DrawFallbackQr(g, qrRect, qrPayload);

                    using (Pen borderPen = new Pen(Color.Black, 1))
                    {
                        g.DrawRectangle(borderPen, qrRect);
                    }

                    // Centered Tag under QR Code
                    float tagFont = isCompact ? 8.5f : (isPallet ? 11.5f : 10f);
                    using (Font fontTag = new Font("Arial", tagFont, FontStyle.Bold))
                    {
                        Rectangle tagRect = new Rectangle(leftColRect.X, qrRect.Bottom + 4, leftColRect.Width, 22);
                        g.DrawString($"TAG: {tagVal}", fontTag, textBrush, tagRect, sfCenter);
                    }

                    // Shelf Location if present
                    if (!string.IsNullOrWhiteSpace(r.Shelf_Location))
                    {
                        using (Font fontShelf = new Font("Arial", isCompact ? 7.5f : 8.5f, FontStyle.Regular))
                        {
                            Rectangle shelfRect = new Rectangle(leftColRect.X, qrRect.Bottom + 24, leftColRect.Width, 16);
                            g.DrawString($"LOC: {r.Shelf_Location}", fontShelf, textBrush, shelfRect, sfCenter);
                        }
                    }
                }

                // -------------------------------------------------------------
                // 3. RIGHT COLUMN: METADATA & TELEMETRY FIELDS
                // -------------------------------------------------------------
                int metaX = leftColRect.Right + 12;
                int metaW = bounds.Right - metaX - 6;
                int metaY = contentY + 2;

                // Line 1: S/N in High-Legibility Consolas Monospace
                float snFontSize = isCompact ? 10f : (isPallet ? 13.5f : 11.5f);
                using (Font fontSn = new Font("Consolas", snFontSize, FontStyle.Bold))
                {
                    Rectangle snLineRect = new Rectangle(metaX, metaY, metaW, (int)(snFontSize * 1.8f));
                    g.DrawString($"S/N: {serialVal}", fontSn, textBrush, snLineRect, sfLeft);
                    metaY += snLineRect.Height + 2;
                }

                // Thin Horizontal Divider
                using (Pen dividerPen = new Pen(Color.FromArgb(200, 200, 200), 1))
                {
                    g.DrawLine(dividerPen, metaX, metaY, bounds.Right - 8, metaY);
                }
                metaY += 6;

                // Field fonts
                float bodyFont = isCompact ? 8.5f : (isPallet ? 10.5f : 9.5f);
                float boldFont = isCompact ? 8.5f : (isPallet ? 10.5f : 9.5f);
                int lineH = (int)(bodyFont * 2.0f);

                using (Font fontBody = new Font("Arial", bodyFont, FontStyle.Regular))
                using (Font fontFieldBold = new Font("Arial", boldFont, FontStyle.Bold))
                {
                    // Line 2: Model
                    string modelDisplay = string.IsNullOrWhiteSpace(r.Model) ? "Generic PC / Laptop" : r.Model;
                    Rectangle modelRect = new Rectangle(metaX, metaY, metaW, lineH);
                    g.DrawString($"MDL: {modelDisplay}", fontFieldBold, textBrush, modelRect, sfLeft);
                    metaY += lineH;

                    if (!isCompact)
                    {
                        // Line 3: Hardware Specs (Pallet & Chassis 3x2)
                        string specDisplay = $"{r.Processor} | {r.Memory}";
                        if (string.IsNullOrWhiteSpace(r.Processor) && string.IsNullOrWhiteSpace(r.Memory))
                            specDisplay = "Hardware Audit Nominal";
                        Rectangle specRect = new Rectangle(metaX, metaY, metaW, lineH);
                        g.DrawString($"SPEC: {specDisplay}", fontBody, textBrush, specRect, sfLeft);
                        metaY += lineH;
                    }

                    // Line 4: Battery & Status
                    string statusDisplay = string.IsNullOrWhiteSpace(r.Status) ? "RTS" : r.Status;
                    Rectangle battRect = new Rectangle(metaX, metaY, metaW, lineH);
                    g.DrawString($"BATT: {r.Battery_Health}% | STATUS: {statusDisplay}", fontBody, textBrush, battRect, sfLeft);
                    metaY += lineH;

                    // Line 5: Technician & Date
                    string techStamp = string.IsNullOrWhiteSpace(r.Technician) ? "TECH-01" : r.Technician;
                    string dateStr = string.IsNullOrWhiteSpace(r.Timestamp) ? DateTime.Now.ToString("yyyy-MM-dd") : r.Timestamp.Split(' ')[0];
                    Rectangle techRect = new Rectangle(metaX, metaY, metaW, lineH);
                    g.DrawString($"TECH: {techStamp} · {dateStr}", fontBody, textBrush, techRect, sfLeft);
                    metaY += lineH + 4;
                }

                // -------------------------------------------------------------
                // 4. GRADE & RTS PILL BADGE
                // -------------------------------------------------------------
                int pillH = isCompact ? 22 : (isPallet ? 30 : 26);
                int pillW = Math.Min(metaW, isCompact ? 220 : 260);
                Rectangle pillRect = new Rectangle(metaX, bounds.Bottom - pillH - 6, pillW, pillH);

                using (SolidBrush blackBrush = new SolidBrush(Color.Black))
                {
                    g.FillRectangle(blackBrush, pillRect);
                }

                float pillFontSize = isCompact ? 8f : (isPallet ? 11f : 9.5f);
                using (Font fontPill = new Font("Arial", pillFontSize, FontStyle.Bold))
                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    string grade = string.IsNullOrWhiteSpace(r.Physical_Grade) ? "A+" : r.Physical_Grade.Trim();
                    string status = string.IsNullOrWhiteSpace(r.Status) ? "RTS" : r.Status.Trim();
                    string pillText = $"★ GRADE {grade} · PASSED {status} ★";
                    g.DrawString(pillText, fontPill, whiteBrush, pillRect, sfCenter);
                }
            }

            // -------------------------------------------------------------
            // 5. CRISP OUTER PERIMETER BORDER
            // -------------------------------------------------------------
            using (Pen borderPen = new Pen(Color.Black, 2))
            {
                g.DrawRectangle(borderPen, bounds);
            }
        }

        private static void DrawFallbackQr(Graphics g, Rectangle rect, string payload)
        {
            try
            {
                using (var qrGen = new QRCodeGenerator())
                using (var qrData = qrGen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M))
                using (var qrCode = new QRCode(qrData))
                using (Bitmap qrBmp = qrCode.GetGraphic(6, Color.Black, Color.White, false))
                {
                    g.DrawImage(qrBmp, rect);
                }
            }
            catch { }
        }
    }
}
