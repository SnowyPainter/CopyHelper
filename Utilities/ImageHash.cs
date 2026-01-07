using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CopyHelper.Utilities
{
    public static class ImageHash
    {
        public static string ComputeDHash(BitmapSource source)
        {
            BitmapSource gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            BitmapSource resized = new TransformedBitmap(gray, new ScaleTransform(9.0 / gray.PixelWidth, 8.0 / gray.PixelHeight));

            int width = 9;
            int height = 8;
            int stride = width;
            byte[] pixels = new byte[height * stride];
            resized.CopyPixels(pixels, stride, 0);

            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width - 1; x++)
                {
                    byte left = pixels[row + x];
                    byte right = pixels[row + x + 1];
                    if (left > right)
                    {
                        hash |= 1UL << bit;
                    }
                    bit++;
                }
            }

            return hash.ToString("X16");
        }

        public static double Similarity(string hashA, string hashB)
        {
            if (!ulong.TryParse(hashA, System.Globalization.NumberStyles.HexNumber, null, out ulong a))
            {
                return 0;
            }

            if (!ulong.TryParse(hashB, System.Globalization.NumberStyles.HexNumber, null, out ulong b))
            {
                return 0;
            }

            ulong xor = a ^ b;
            int dist = 0;
            while (xor != 0)
            {
                dist++;
                xor &= xor - 1;
            }

            return 1.0 - dist / 64.0;
        }
    }
}
