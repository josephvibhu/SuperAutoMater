using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SuperAutoMater.Wpf.Services
{
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
    }
}
