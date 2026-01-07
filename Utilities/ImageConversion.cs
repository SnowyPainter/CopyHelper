using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CopyHelper.Utilities
{
    public static class ImageConversion
    {
        public static Mat ToMat(BitmapSource source)
        {
            if (source.Format != PixelFormats.Bgra32)
            {
                source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            }

            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            source.CopyPixels(pixels, stride, 0);

            Mat mat = new Mat(height, width, MatType.CV_8UC4);
            Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
            return mat;
        }

        public static BitmapSource ToBitmapSource(Mat mat)
        {
            if (mat.Empty())
            {
                throw new ArgumentException("Mat is empty.");
            }

            Mat bgra = mat;
            if (mat.Type() != MatType.CV_8UC4)
            {
                bgra = new Mat();
                if (mat.Channels() == 1)
                {
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
                }
                else
                {
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                }
            }

            int width = bgra.Width;
            int height = bgra.Height;
            int stride = width * 4;
            if (!bgra.IsContinuous() || bgra.Step() != stride)
            {
                Mat contiguous = bgra.Clone();
                if (!ReferenceEquals(bgra, mat))
                {
                    bgra.Dispose();
                }

                bgra = contiguous;
            }

            byte[] pixels = new byte[height * stride];
            Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);

            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);

            if (!ReferenceEquals(bgra, mat))
            {
                bgra.Dispose();
            }

            return bitmap;
        }

        public static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        public static SoftwareBitmap ToSoftwareBitmap(Mat mat)
        {
            if (mat.Empty())
            {
                throw new ArgumentException("Mat is empty.");
            }

            Mat bgra = mat;
            if (mat.Type() != MatType.CV_8UC4)
            {
                bgra = new Mat();
                if (mat.Channels() == 1)
                {
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA);
                }
                else
                {
                    Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA);
                }
            }

            int width = bgra.Width;
            int height = bgra.Height;
            int stride = width * 4;
            if (!bgra.IsContinuous() || bgra.Step() != stride)
            {
                Mat contiguous = bgra.Clone();
                if (!ReferenceEquals(bgra, mat))
                {
                    bgra.Dispose();
                }

                bgra = contiguous;
            }

            byte[] pixels = new byte[height * stride];
            Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);

            IBuffer buffer = pixels.AsBuffer();
            SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Ignore);

            if (!ReferenceEquals(bgra, mat))
            {
                bgra.Dispose();
            }

            return bitmap;
        }

        public static BitmapSource FromEncodedBytes(byte[] bytes)
        {
            using System.IO.MemoryStream stream = new System.IO.MemoryStream(bytes);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public static Bitmap ToBitmap(Mat mat)
        {
            if (mat.Empty())
            {
                throw new ArgumentException("Mat is empty.");
            }

            Mat bgr = mat;
            if (mat.Channels() == 1)
            {
                bgr = new Mat();
                Cv2.CvtColor(mat, bgr, ColorConversionCodes.GRAY2BGR);
            }
            else if (mat.Channels() == 4)
            {
                bgr = new Mat();
                Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
            }

            Bitmap bitmap = new Bitmap(bgr.Width, bgr.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                bitmap.PixelFormat);

            try
            {
                int bytesPerPixel = 3;
                int stride = data.Stride;
                int width = bgr.Width;
                int height = bgr.Height;
                int sourceStride = width * bytesPerPixel;
                byte[] buffer = new byte[height * sourceStride];
                Marshal.Copy(bgr.Data, buffer, 0, buffer.Length);

                for (int y = 0; y < height; y++)
                {
                    IntPtr dest = data.Scan0 + y * stride;
                    Marshal.Copy(buffer, y * sourceStride, dest, sourceStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
                if (!ReferenceEquals(bgr, mat))
                {
                    bgr.Dispose();
                }
            }

            return bitmap;
        }
    }
}
