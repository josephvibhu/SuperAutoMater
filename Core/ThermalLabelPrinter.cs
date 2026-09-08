using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Windows.Forms;
using QRCoder;

namespace ITAS_QC_Tool
{
    /// <summary>
    /// Thermal Chassis Label & Sticker Printing Engine.
    /// Outputs high-contrast monochrome labels designed for standard 2""x1"", 3""x2"", and 4""x2""
    /// thermal printers (Zebra, Brother, Dymo, Xprinter, POS printers).
    /// </summary>
    public static class ThermalLabelPrinter
    {
        public static void PrintLabel(AssetQueueRecord record, IWin32Window owner = null)
        {
            if (record == null) return;

            try
            {
                using (PrintDocument doc = new PrintDocument())
                {
                    // Default to 3" x 2" label (hundredths of an inch: 300 x 200)
                    doc.DefaultPageSettings.PaperSize = new PaperSize("ChassisLabel", 300, 200);
                    doc.DefaultPageSettings.Margins = new Margins(10, 10, 10, 10);

                    doc.PrintPage += (s, e) =>
                    {
                        RenderLabelGraphics(e.Graphics, e.MarginBounds, record);
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
                DarkMessageBox.Show("Printing Error: " + ex.Message, "Label Printer Error");
            }
        }

        public static Bitmap RenderLabelBitmap(AssetQueueRecord record, int width = 600, int height = 400)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                RenderLabelGraphics(g, new Rectangle(12, 12, width - 24, height - 24), record);
            }
            return bmp;
        }

        private static void RenderLabelGraphics(Graphics g, Rectangle bounds, AssetQueueRecord r)
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

            // 1. Inverted Header Badge
            int headerH = Math.Max(20, (int)(bounds.Height * 0.14f));
            Rectangle headerRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, headerH);
            using (SolidBrush blackBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(blackBrush, headerRect);
            }

            using (Font fontHeader = new Font("Arial", headerH * 0.45f, FontStyle.Bold))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (StringFormat sfHeader = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("AUTOMATER QC CERTIFIED", fontHeader, whiteBrush, headerRect, sfHeader);
            }

            // 2. Content Layout: Left QR Code | Right Metadata
            int contentY = headerRect.Bottom + 6;
            int contentH = bounds.Bottom - contentY;
            int qrSize = Math.Min(contentH - 4, (int)(bounds.Width * 0.38f));

            // Generate QR Code
            string qrPayload = $"ASSET:{r.Asset_Tag}|SN:{r.Serial_Number}|SPEC:{r.Processor}_{r.Memory}|BATT:{r.Battery_Health}%|GRADE:{r.Physical_Grade}|STATUS:{r.Status}";
            using (var qrGen = new QRCodeGenerator())
            using (var qrData = qrGen.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.M))
            using (var qrCode = new QRCode(qrData))
            using (Bitmap qrBmp = qrCode.GetGraphic(6, Color.Black, Color.White, false))
            {
                Rectangle qrRect = new Rectangle(bounds.X + 2, contentY + (contentH - qrSize) / 2, qrSize, qrSize);
                g.DrawImage(qrBmp, qrRect);
                using (Pen borderPen = new Pen(Color.Black, 1))
                    g.DrawRectangle(borderPen, qrRect);
            }

            // Right Metadata Block
            int metaX = bounds.X + qrSize + 12;
            int metaW = bounds.Right - metaX;
            Rectangle metaRect = new Rectangle(metaX, contentY, metaW, contentH);

            using (Font fontTag = new Font("Arial", Math.Max(9f, contentH * 0.12f), FontStyle.Bold))
            using (Font fontSn = new Font("Consolas", Math.Max(7.5f, contentH * 0.085f), FontStyle.Bold))
            using (Font fontBody = new Font("Arial", Math.Max(7f, contentH * 0.075f), FontStyle.Regular))
            using (Font fontBold = new Font("Arial", Math.Max(7f, contentH * 0.075f), FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.Black))
            {
                float lineY = metaRect.Y;
                float lineSpacing = contentH / 6.5f;

                // Line 1: Asset Tag
                string tagText = string.IsNullOrWhiteSpace(r.Asset_Tag) ? r.Serial_Number : r.Asset_Tag;
                g.DrawString($"TAG: {tagText}", fontTag, textBrush, metaX, lineY);
                lineY += lineSpacing * 1.15f;

                // Line 2: Serial Number
                g.DrawString($"S/N: {r.Serial_Number}", fontSn, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 3: Model (Truncated if long)
                string modelDisplay = r.Model.Length > 24 ? r.Model.Substring(0, 22) + ".." : r.Model;
                g.DrawString($"MDL: {modelDisplay}", fontBody, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 4: CPU & Memory
                string specDisplay = $"{r.Processor} | {r.Memory}";
                if (specDisplay.Length > 28) specDisplay = specDisplay.Substring(0, 26) + "..";
                g.DrawString($"SPEC: {specDisplay}", fontBody, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 5: Battery & Grade
                g.DrawString($"BATT: {r.Battery_Health}% | {r.Physical_Grade} · {r.Status}", fontBold, textBrush, metaX, lineY);
                lineY += lineSpacing * 0.95f;

                // Line 6: Date & Stamp
                string dateStr = string.IsNullOrWhiteSpace(r.Timestamp) ? DateTime.Now.ToString("yyyy-MM-dd") : r.Timestamp.Split(' ')[0];
                g.DrawString($"DATE: {dateStr} | POST CERTIFIED ✓", fontBody, textBrush, metaX, lineY);
            }

            // Outer Frame
            using (Pen borderPen = new Pen(Color.Black, 2))
            {
                g.DrawRectangle(borderPen, bounds);
            }
        }
    }
}
