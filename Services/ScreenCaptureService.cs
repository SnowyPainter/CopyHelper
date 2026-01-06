using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;
using CopyHelper.Utilities;

namespace CopyHelper.Services
{
    public static class ScreenCaptureService
    {
        public static BitmapSource Capture(Rect region)
        {
            using Bitmap bitmap = new Bitmap((int)region.Width, (int)region.Height, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen((int)region.X, (int)region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            return ImageConversion.ToBitmapSource(bitmap);
        }
    }
}
