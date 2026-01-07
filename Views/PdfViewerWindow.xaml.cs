using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CopyHelper.Models;
using CopyHelper.Services;

namespace CopyHelper.Views
{
    public partial class PdfViewerWindow : Window
    {
        private readonly PdfRenderService _renderService;
        private readonly IReadOnlyList<PdfHighlight> _highlights;

        public PdfViewerWindow(string pdfPath, int pageNumber, IReadOnlyList<PdfHighlight> highlights)
        {
            InitializeComponent();
            _renderService = new PdfRenderService(pdfPath);
            _highlights = highlights;
            Loaded += (_, _) => RenderPage(pageNumber);
            Closed += (_, _) => _renderService.Dispose();
        }

        private void RenderPage(int pageNumber)
        {
            const int targetWidth = 1200;
            var (bitmap, width, height) = _renderService.RenderPage(pageNumber, targetWidth);
            PageImage.Source = bitmap;
            HighlightCanvas.Width = width;
            HighlightCanvas.Height = height;

            HighlightCanvas.Children.Clear();
            foreach (PdfHighlight highlight in _highlights)
            {
                PdfRect rect = highlight.Bounds;
                double x = rect.X * width;
                double y = rect.Y * height;
                double w = rect.Width * width;
                double h = rect.Height * height;

                Rectangle box = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Stroke = highlight.Kind == "image" ? Brushes.OrangeRed : Brushes.DeepSkyBlue,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255))
                };

                Canvas.SetLeft(box, x);
                Canvas.SetTop(box, y);
                HighlightCanvas.Children.Add(box);
            }
        }
    }
}
