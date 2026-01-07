using System;
using System.Drawing;
using System.Windows.Media.Imaging;
using CopyHelper.Utilities;
using PdfiumViewer;

namespace CopyHelper.Services
{
    public sealed class PdfRenderService : IDisposable
    {
        private readonly PdfDocument _document;

        public PdfRenderService(string pdfPath)
        {
            _document = PdfDocument.Load(pdfPath);
        }

        public (BitmapSource bitmap, int width, int height) RenderPage(int pageNumber, int targetWidth)
        {
            int index = pageNumber - 1;
            var size = _document.PageSizes[index];
            double scale = targetWidth / size.Width;
            int width = Math.Max(1, (int)Math.Round(size.Width * scale));
            int height = Math.Max(1, (int)Math.Round(size.Height * scale));

            using Image image = _document.Render(index, width, height, 96, 96, PdfRenderFlags.Annotations);
            using Bitmap bitmap = new Bitmap(image);
            BitmapSource source = ImageConversion.ToBitmapSource(bitmap);
            source.Freeze();
            return (source, width, height);
        }

        public void Dispose()
        {
            _document.Dispose();
        }
    }
}
