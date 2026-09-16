using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SuperAutoMater.Wpf.Services
{
    public class WebcamOpticsResult
    {
        public bool IsShutterClosed { get; set; } = false;
        public bool IsLensHazy { get; set; } = false;
        public double MeanLuminance { get; set; } = 128.0;
        public double StandardDeviation { get; set; } = 35.0;
        public int ClarityScore { get; set; } = 100;
        public string StatusBadge { get; set; } = "✓ OPTICS CRISP & CLEAR";
        public string StatusDetail { get; set; } = "Lens clarity and sensor dynamic range nominal.";
        public string AccentHex { get; set; } = "#3FB950";
    }

    public class CosmeticPhotoRecord
    {
        public string PresetName { get; set; } = "Chassis";
        public string FilePath { get; set; } = "";
        public DateTime CapturedAt { get; set; } = DateTime.Now;
    }

    public class CosmeticCameraService
    {
        private static readonly Lazy<CosmeticCameraService> _instance =
            new Lazy<CosmeticCameraService>(() => new CosmeticCameraService());
        public static CosmeticCameraService Instance => _instance.Value;

        private readonly string _basePhotosDir;

        private CosmeticCameraService()
        {
            _basePhotosDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater", "Photos");
            try
            {
                if (!Directory.Exists(_basePhotosDir))
                    Directory.CreateDirectory(_basePhotosDir);
            }
            catch { }
        }

        public string SaveWatermarkedPhoto(Bitmap rawBitmap, string serial, string presetName, string grade)
        {
            if (rawBitmap == null) return null;

            try
            {
                string cleanSerial = string.IsNullOrWhiteSpace(serial) || serial.Contains("Detecting") ? "CHASSIS" : serial.Trim();
                string unitDir = Path.Combine(_basePhotosDir, cleanSerial);
                if (!Directory.Exists(unitDir))
                    Directory.CreateDirectory(unitDir);

                string fileName = $"{cleanSerial}_{presetName}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string fullPath = Path.Combine(unitDir, fileName);

                using (var cloned = new Bitmap(rawBitmap))
                using (var g = Graphics.FromImage(cloned))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    // Draw semi-transparent HUD banner at bottom
                    int bannerHeight = 36;
                    int yStart = cloned.Height - bannerHeight;
                    using (var bannerBrush = new SolidBrush(Color.FromArgb(200, 13, 17, 23)))
                    {
                        g.FillRectangle(bannerBrush, 0, yStart, cloned.Width, bannerHeight);
                    }

                    using (var borderPen = new Pen(Color.FromArgb(56, 139, 253), 1))
                    {
                        g.DrawLine(borderPen, 0, yStart, cloned.Width, yStart);
                    }

                    // Watermark Text
                    using (var fontBold = new Font("Consolas", 10, FontStyle.Bold))
                    using (var fontRegular = new Font("Consolas", 9, FontStyle.Regular))
                    using (var textWhite = new SolidBrush(Color.White))
                    using (var textBlue = new SolidBrush(Color.FromArgb(88, 166, 255)))
                    using (var textGreen = new SolidBrush(Color.FromArgb(63, 185, 80)))
                    {
                        string leftText = $"SN: {cleanSerial} · {presetName.ToUpper()} · GRADE: {grade}";
                        string rightText = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · SUPERAUTOMATER QC";

                        g.DrawString(leftText, fontBold, textGreen, 12, yStart + 8);
                        var sz = g.MeasureString(rightText, fontRegular);
                        g.DrawString(rightText, fontRegular, textBlue, cloned.Width - sz.Width - 12, yStart + 9);
                    }

                    // Save with high quality JPEG compression
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 88L);
                    cloned.Save(fullPath, encoder, encoderParams);
                }

                return fullPath;
            }
            catch
            {
                return null;
            }
        }

        public List<string> GetPhotosForUnit(string serial)
        {
            var list = new List<string>();
            try
            {
                string cleanSerial = string.IsNullOrWhiteSpace(serial) || serial.Contains("Detecting") ? "CHASSIS" : serial.Trim();
                string unitDir = Path.Combine(_basePhotosDir, cleanSerial);
                if (Directory.Exists(unitDir))
                {
                    list.AddRange(Directory.GetFiles(unitDir, "*.jpg"));
                }
            }
            catch { }
            return list;
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        public WebcamOpticsResult AnalyzeWebcamFrame(Bitmap rawBitmap)
        {
            if (rawBitmap == null)
                return new WebcamOpticsResult { StatusBadge = "NO FRAME", AccentHex = "#8B949E" };

            try
            {
                int w = rawBitmap.Width;
                int h = rawBitmap.Height;
                if (w < 16 || h < 16)
                    return new WebcamOpticsResult();

                int sampleCols = 32;
                int sampleRows = 24;
                int totalSamples = sampleCols * sampleRows;
                double sumLum = 0.0;
                double sumSqLum = 0.0;

                var rect = new Rectangle(0, 0, w, h);
                BitmapData bmpData = rawBitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = bmpData.Stride;
                    IntPtr scan0 = bmpData.Scan0;
                    int stepX = Math.Max(1, w / sampleCols);
                    int stepY = Math.Max(1, h / sampleRows);

                    for (int r = 0; r < sampleRows; r++)
                    {
                        int py = Math.Min(h - 1, r * stepY);
                        IntPtr rowPtr = IntPtr.Add(scan0, py * stride);

                        for (int c = 0; c < sampleCols; c++)
                        {
                            int px = Math.Min(w - 1, c * stepX);
                            int pixelOffset = px * 3;

                            byte b = Marshal.ReadByte(rowPtr, pixelOffset);
                            byte g = Marshal.ReadByte(rowPtr, pixelOffset + 1);
                            byte rVal = Marshal.ReadByte(rowPtr, pixelOffset + 2);

                            // Rec. 601 Luma
                            double lum = (0.299 * rVal) + (0.587 * g) + (0.114 * b);
                            sumLum += lum;
                            sumSqLum += lum * lum;
                        }
                    }
                }
                finally
                {
                    rawBitmap.UnlockBits(bmpData);
                }

                double mean = sumLum / totalSamples;
                double variance = Math.Max(0.0, (sumSqLum / totalSamples) - (mean * mean));
                double stdDev = Math.Sqrt(variance);

                var res = new WebcamOpticsResult
                {
                    MeanLuminance = Math.Round(mean, 1),
                    StandardDeviation = Math.Round(stdDev, 1),
                    ClarityScore = Math.Min(100, Math.Max(0, (int)(stdDev * 2.5)))
                };

                // 1. Shutter Closed: frame is pitch black, very low luminance and near-zero standard deviation
                if (mean < 8.0 && stdDev < 3.5)
                {
                    res.IsShutterClosed = true;
                    res.StatusBadge = "🔒 PRIVACY SHUTTER CLOSED";
                    res.StatusDetail = "Camera sensor is occluded. Slide the physical privacy shutter open on top bezel.";
                    res.AccentHex = "#F85149";
                    res.ClarityScore = 0;
                }
                // 2. Lens Hazy / Smudged: light is present (mean > 35) but contrast is severely suppressed (stdDev < 13.0)
                else if (mean > 35.0 && stdDev < 13.0)
                {
                    res.IsLensHazy = true;
                    res.StatusBadge = "⚠️ LENS HAZY / SMUDGED";
                    res.StatusDetail = $"Low optical contrast (std: {stdDev:F1}). Clean webcam lens cover with microfiber cloth.";
                    res.AccentHex = "#D29922";
                }
                // 3. Crisp optics
                else
                {
                    res.StatusBadge = "✓ OPTICS CRISP & CLEAR";
                    res.StatusDetail = $"Clarity {res.ClarityScore}% · Contrast & sensor dynamic range nominal.";
                    res.AccentHex = "#3FB950";
                }

                return res;
            }
            catch
            {
                return new WebcamOpticsResult();
            }
        }
    }
}
