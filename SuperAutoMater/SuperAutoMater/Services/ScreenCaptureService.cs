using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SuperAutoMater.Wpf.Services
{
    /// <summary>
    /// High-performance desktop screen capture engine for remote monitoring.
    /// Captures the screen, optionally downsamples to a target resolution, and encodes to JPEG in-memory.
    /// </summary>
    public class ScreenCaptureService
    {
        private static readonly Lazy<ScreenCaptureService> _instance =
            new Lazy<ScreenCaptureService>(() => new ScreenCaptureService());
        public static ScreenCaptureService Instance => _instance.Value;

        private readonly object _captureLock = new object();
        private byte[] _cachedJpeg = Array.Empty<byte>();
        private DateTime _lastCaptureTime = DateTime.MinValue;
        private const int MinCaptureIntervalMs = 45; // ~22 FPS cap

        private static ImageCodecInfo _jpegEncoder;
        private static EncoderParameters _encoderParams;

        private ScreenCaptureService()
        {
            _jpegEncoder = ImageCodecInfo.GetImageDecoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            _encoderParams = new EncoderParameters(1);
            _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 65L);
        }

        /// <summary>
        /// Captures the primary screen and returns the JPEG encoded byte array.
        /// Reuses frame if called within MinCaptureIntervalMs to conserve CPU.
        /// </summary>
        public byte[] GetScreenFrame(int maxWidth = 1280, long quality = 65L)
        {
            lock (_captureLock)
            {
                var now = DateTime.UtcNow;
                if (_cachedJpeg.Length > 0 && (now - _lastCaptureTime).TotalMilliseconds < MinCaptureIntervalMs)
                {
                    return _cachedJpeg;
                }

                try
                {
                    var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                    using (var rawBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(rawBitmap))
                        {
                            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                        }

                        // Determine scaled size
                        int targetW = bounds.Width;
                        int targetH = bounds.Height;
                        if (maxWidth > 0 && targetW > maxWidth)
                        {
                            double ratio = (double)maxWidth / targetW;
                            targetW = maxWidth;
                            targetH = (int)(targetH * ratio);
                        }

                        using (var finalBitmap = (targetW == bounds.Width && targetH == bounds.Height)
                                   ? rawBitmap
                                   : ResizeBitmap(rawBitmap, targetW, targetH))
                        {
                            using (var ms = new MemoryStream(targetW * targetH / 4))
                            {
                                var encoder = _jpegEncoder;
                                var prms = quality == 65L ? _encoderParams : CreateEncoderParams(quality);

                                if (encoder != null)
                                    finalBitmap.Save(ms, encoder, prms);
                                else
                                    finalBitmap.Save(ms, ImageFormat.Jpeg);

                                _cachedJpeg = ms.ToArray();
                                _lastCaptureTime = now;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to cached if capture fails (e.g. UAC secure desktop active)
                }

                return _cachedJpeg;
            }
        }

        private static Bitmap ResizeBitmap(Bitmap src, int width, int height)
        {
            var dest = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.DrawImage(src, 0, 0, width, height);
            }
            return dest;
        }

        private static EncoderParameters CreateEncoderParams(long quality)
        {
            var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 10L, 95L));
            return p;
        }
    }
}
