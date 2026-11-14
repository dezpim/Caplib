using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Caplib
{
    internal enum CaptureScope
    {
        AllMonitors = 0,
        PrimaryMonitor = 1,
        ActiveWindow = 2
    }

    internal enum SaveImageFormat
    {
        Png = 0,
        Jpeg = 1
    }

    internal static class ScreenCapturer
    {
        public static Rectangle GetTargetBounds(CaptureScope scope)
        {
            if (scope == CaptureScope.PrimaryMonitor)
            {
                return Screen.PrimaryScreen.Bounds;
            }

            if (scope == CaptureScope.ActiveWindow)
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.RECT rect;
                    int hr = NativeMethods.DwmGetWindowAttribute(
                        hwnd,
                        NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out rect,
                        Marshal.SizeOf(typeof(NativeMethods.RECT)));

                    if (hr != 0)
                    {
                        NativeMethods.GetWindowRect(hwnd, out rect);
                    }

                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    if (width > 0 && height > 0)
                    {
                        return new Rectangle(rect.Left, rect.Top, width, height);
                    }
                }
            }

            return SystemInformation.VirtualScreen;
        }

        public static void CaptureAndSave(
            Rectangle bounds,
            Point clickPoint,
            bool drawClickMarker,
            SaveImageFormat format,
            string outputPath)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

                if (drawClickMarker && bounds.Contains(clickPoint))
                {
                    DrawClickHighlight(g, clickPoint.X - bounds.X, clickPoint.Y - bounds.Y);
                }

                if (format == SaveImageFormat.Jpeg)
                {
                    SaveAsJpeg(bmp, outputPath, 90L);
                }
                else
                {
                    bmp.Save(outputPath, ImageFormat.Png);
                }
            }
        }

        private static void DrawClickHighlight(Graphics g, int x, int y)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Brush fillBrush = new SolidBrush(Color.FromArgb(65, 239, 68, 68)))
            using (Pen ringPen = new Pen(Color.FromArgb(220, 220, 38, 38), 2.5f))
            using (Brush centerBrush = new SolidBrush(Color.FromArgb(230, 220, 38, 38)))
            {
                g.FillEllipse(fillBrush, x - 16, y - 16, 32, 32);
                g.DrawEllipse(ringPen, x - 16, y - 16, 32, 32);
                g.FillEllipse(centerBrush, x - 3, y - 3, 6, 6);
            }
        }

        private static void SaveAsJpeg(Bitmap bmp, string outputPath, long quality)
        {
            ImageCodecInfo jpegCodec = GetEncoder(ImageFormat.Jpeg);
            if (jpegCodec == null)
            {
                bmp.Save(outputPath, ImageFormat.Jpeg);
                return;
            }

            using (EncoderParameters encoderParams = new EncoderParameters(1))
            using (EncoderParameter qualityParam = new EncoderParameter(Encoder.Quality, quality))
            {
                encoderParams.Param[0] = qualityParam;
                bmp.Save(outputPath, jpegCodec, encoderParams);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == format.Guid)
                {
                    return codecs[i];
                }
            }
            return null;
        }
    }
}
