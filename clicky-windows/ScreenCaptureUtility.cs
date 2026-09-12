using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Clicky;

/// <summary>
/// Captures screenshots of all connected monitors using GDI+ (System.Drawing).
/// Returns one ScreenCapture per monitor labeled Screen 1, Screen 2, etc.,
/// matching the pattern in CompanionScreenCaptureUtility.swift.
///
/// Note: Windows.Graphics.Capture (WinRT) requires a window handle and cannot be
/// called headlessly in a tray-only app without additional bootstrapping.
/// GDI+ CopyFromScreen is the correct approach for a background system-tray app
/// on Windows — it does not require explicit screen capture permission prompts
/// (the user grants access at the OS level to the whole application).
/// </summary>
public static class ScreenCaptureUtility
{
    // Maximum image dimension for any single monitor capture before JPEG compression.
    // Limits payload size sent to Claude while keeping UI elements legible.
    private const int MaxCaptureDimension = 1920;

    /// <summary>
    /// Captures all connected monitors and returns them as ScreenCapture records
    /// ready to include in a Claude API request.
    /// </summary>
    public static IReadOnlyList<ScreenCapture> CaptureAllMonitors()
    {
        var captures = new List<ScreenCapture>();

        for (int monitorIndex = 0; monitorIndex < Screen.AllScreens.Length; monitorIndex++)
        {
            var screen = Screen.AllScreens[monitorIndex];
            var capture = CaptureMonitor(screen, monitorIndex);
            if (capture != null) captures.Add(capture);
        }

        return captures;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ScreenCapture? CaptureMonitor(Screen screen, int monitorIndex)
    {
        try
        {
            var bounds = screen.Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.CopyFromScreen(
                sourceX: bounds.X,
                sourceY: bounds.Y,
                destinationX: 0,
                destinationY: 0,
                blockRegionSize: bounds.Size,
                copyPixelOperation: CopyPixelOperation.SourceCopy);

            // Downsample large monitors to stay within a reasonable payload size
            var resizedBitmap = ResizeIfNeeded(bitmap, MaxCaptureDimension);

            var jpegBytes = EncodeBitmapAsJpeg(resizedBitmap, quality: 85);

            if (resizedBitmap != bitmap) resizedBitmap.Dispose();

            return new ScreenCapture(jpegBytes, "image/jpeg", monitorIndex);
        }
        catch (Exception)
        {
            // Capture failure on a specific monitor is non-fatal; skip it
            return null;
        }
    }

    private static Bitmap ResizeIfNeeded(Bitmap source, int maxDimension)
    {
        if (source.Width <= maxDimension && source.Height <= maxDimension)
            return source;

        var scaleFactor = Math.Min(
            (double)maxDimension / source.Width,
            (double)maxDimension / source.Height);

        var newWidth = (int)(source.Width * scaleFactor);
        var newHeight = (int)(source.Height * scaleFactor);

        var resizedBitmap = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(resizedBitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, newWidth, newHeight);
        return resizedBitmap;
    }

    private static byte[] EncodeBitmapAsJpeg(Bitmap bitmap, long quality)
    {
        using var outputStream = new MemoryStream();

        var jpegEncoder = GetJpegEncoder();
        var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        bitmap.Save(outputStream, jpegEncoder, encoderParameters);
        return outputStream.ToArray();
    }

    private static ImageCodecInfo GetJpegEncoder()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
        }
        throw new InvalidOperationException("JPEG encoder not available.");
    }
}
