using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
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
    /// thermal printers (Zebra, Brother, Dymo, Xprinter, POS printers).
    /// </summary>
    public static class ThermalLabelPrinter
    {
        public static (int Width, int Height, int HundredthW, int HundredthH) GetPresetDimensions(LabelSizePreset preset)
        {
            return preset switch
            {
                LabelSizePreset.Compact2x1 => (500, 250, 200, 100),
                LabelSizePreset.Pallet4x2 => (800, 400, 400, 200),
                _ => (600, 400, 300, 200) // Default Chassis 3"x2"
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
                        RenderLabelGraphics(e.Graphics, e.MarginBounds, record, preset, barcodeMode);
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
                RenderLabelGraphics(g, new Rectangle(10, 10, dims.Width - 20, dims.Height - 20), record, preset, barcodeMode);
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
                RenderLabelGraphics(g, new Rectangle(10, 10, width - 20, height - 20), record, preset, barcodeMode);
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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            bool isCompact = preset == LabelSizePreset.Compact2x1;
            bool isPallet = preset == LabelSizePreset.Pallet4x2;

            // 1. Inverted Header Badge
            int headerH = Math.Max(18, (int)(bounds.Height * (isCompact ? 0.16f : 0.13f)));
            Rectangle headerRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, headerH);
            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(blackBrush, headerRect);
            }

            using (Font fontHeader = new Font("Arial", headerH * 0.45f, FontStyle.Bold))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (StringFormat sfHeader = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                string headerTitle = isCompact ? "SUPERAUTOMATER QC" : "SUPERAUTOMATER QC CERTIFIED · REFURB AUDIT";
                g.DrawString(headerTitle, fontHeader, whiteBrush, headerRect, sfHeader);
            }

            // 2. Content Layout: Left Barcode/QR Code | Right Metadata
            int contentY = headerRect.Bottom + 4;
            int contentH = bounds.Bottom - contentY;
            int barcodeW = Math.Min(contentH - 4, (int)(bounds.Width * (isPallet ? 0.32f : isCompact ? 0.38f : 0.35f)));
            int barcodeH = isCompact ? contentH - 6 : (barcodeMode == BarcodeMode.Code128 ? (int)(contentH * 0.75f) : barcodeW);

            string qrPayload = $"ASSET:{r.Asset_Tag}|SN:{r.Serial_Number}|SPEC:{r.Processor}_{r.Memory}|BATT:{r.Battery_Health}%|GRADE:{r.Physical_Grade}|TECH:{r.Technician}";

            // Draw Barcode or QR
            Rectangle barcodeRect = new Rectangle(bounds.X + 2, contentY + (contentH - barcodeH) / 2, barcodeW, barcodeH);
            if (barcodeMode == BarcodeMode.Code128)
            {
                try
                {
                    var writer = new BarcodeWriter
                    {
                        Format = BarcodeFormat.CODE_128,
                        Options = new EncodingOptions
                        {
                            Width = barcodeW,
                            Height = barcodeH,
                            Margin = 1,
                            PureBarcode = false
                        }
                    };
                    string barcodeVal = !string.IsNullOrWhiteSpace(r.Serial_Number) ? r.Serial_Number : r.Asset_Tag;
                    using (Bitmap barBmp = writer.Write(barcodeVal))
                    {
                        g.DrawImage(barBmp, barcodeRect);
                    }
                }
                catch
                {
                    DrawFallbackQr(g, barcodeRect, qrPayload);
                }
            }
            else
            {
                DrawFallbackQr(g, barcodeRect, qrPayload);
            }

            using (Pen borderPen = new Pen(Color.Black, 1))
            {
                g.DrawRectangle(borderPen, barcodeRect);
            }

            // 3. Right Metadata Block
            int metaX = bounds.X + barcodeW + 10;
            int metaW = bounds.Right - metaX;
            Rectangle metaRect = new Rectangle(metaX, contentY, metaW, contentH);

            float fontScale = isCompact ? 0.85f : (isPallet ? 1.15f : 1.0f);
            using (Font fontTag = new Font("Arial", Math.Max(8f, contentH * 0.13f * fontScale), FontStyle.Bold))
            using (Font fontSn = new Font("Consolas", Math.Max(7f, contentH * 0.09f * fontScale), FontStyle.Bold))
            using (Font fontBody = new Font("Arial", Math.Max(6.5f, contentH * 0.08f * fontScale), FontStyle.Regular))
            using (Font fontBold = new Font("Arial", Math.Max(6.5f, contentH * 0.08f * fontScale), FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.Black))
            {
                float lineY = metaRect.Y;
                float lineSpacing = isCompact ? contentH / 5.2f : contentH / 6.3f;

                // Line 1: Asset Tag
                string tagText = string.IsNullOrWhiteSpace(r.Asset_Tag) ? r.Serial_Number : r.Asset_Tag;
                g.DrawString($"TAG: {tagText}", fontTag, textBrush, metaX, lineY);
                lineY += lineSpacing * 1.1f;

                // Line 2: Serial Number
                g.DrawString($"S/N: {r.Serial_Number}", fontSn, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 3: Model
                string modelDisplay = r.Model.Length > 22 ? r.Model.Substring(0, 20) + ".." : r.Model;
                g.DrawString($"MDL: {modelDisplay}", fontBody, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                if (!isCompact)
                {
                    // Line 4: CPU & Memory
                    string specDisplay = $"{r.Processor} | {r.Memory}";
                    if (specDisplay.Length > 26) specDisplay = specDisplay.Substring(0, 24) + "..";
                    g.DrawString($"SPEC: {specDisplay}", fontBody, textBrush, metaX, lineY);
                    lineY += lineSpacing * 0.95f;
                }

                // Line 5: Battery & Grade
                g.DrawString($"BATT: {r.Battery_Health}% | {r.Physical_Grade} · {r.Status}", fontBold, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 6: Technician & Date Stamp
                string techStamp = string.IsNullOrWhiteSpace(r.Technician) ? "TECH-01" : r.Technician;
                string dateStr = string.IsNullOrWhiteSpace(r.Timestamp) ? DateTime.Now.ToString("yyyy-MM-dd") : r.Timestamp.Split(' ')[0];
                g.DrawString($"TECH: {techStamp} · {dateStr}", fontBold, textBrush, metaX, lineY);
            }

            // Outer Frame
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
