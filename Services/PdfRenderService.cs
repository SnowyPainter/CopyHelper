using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFiumSharp;
using PDFiumSharp.Enums;

namespace CopyHelper.Services
{
    public sealed class PdfRenderService : IDisposable
    {
        private readonly PdfDocument _document;

        public PdfRenderService(string pdfPath)
        {
            _document = new PdfDocument(pdfPath);
        }

        public (BitmapSource bitmap, int width, int height) RenderPage(int pageNumber, int targetWidth)
        {
            PdfPage page = _document.Pages[pageNumber - 1];
            double scale = targetWidth / page.Width;
            int width = Math.Max(1, (int)Math.Round(page.Width * scale));
            int height = Math.Max(1, (int)Math.Round(page.Height * scale));

            using PDFiumBitmap bitmap = new PDFiumBitmap(width, height, true);
            page.Render(bitmap, PageOrientations.Normal, RenderingFlags.Annotations);

            int bufferSize = bitmap.Stride * height;
            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                bitmap.Scan0,
                bufferSize,
                bitmap.Stride);

            source.Freeze();
            return (source, width, height);
        }

        public void Dispose()
        {
            _document.Close();
        }
    }
}
