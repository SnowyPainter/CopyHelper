using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using CopyHelper.Models;
using CopyHelper.Services;
using CopyHelper.Utilities;
using CopyHelper.ViewModels;

namespace CopyHelper.Views
{
    public partial class PdfViewerWindow : Window
    {
        private const int DisplayTargetWidth = 650;
        private readonly MainViewModel _mainViewModel;
        private readonly List<SearchResult> _results;
        private readonly OcrService _ocrService;
        private PdfRenderService? _renderService;
        private int _currentIndex;
        private string? _currentPdfPath;
        private IReadOnlyList<PdfHighlight> _highlights = Array.Empty<PdfHighlight>();
        private BitmapSource? _lastRenderedBitmap;
        private CancellationTokenSource? _ocrCts;

        public PdfViewerWindow(MainViewModel mainViewModel, IReadOnlyList<SearchResult> results, int initialIndex)
        {
            InitializeComponent();
            _mainViewModel = mainViewModel;
            _results = results.ToList();
            _ocrService = new OcrService();
            _currentIndex = Math.Clamp(initialIndex, 0, Math.Max(0, _results.Count - 1));
            Loaded += async (_, _) => await LoadResultAsync(_currentIndex).ConfigureAwait(true);
            Closed += (_, _) =>
            {
                _ocrCts?.Cancel();
                _renderService?.Dispose();
            };
        }

        private async Task LoadResultAsync(int index)
        {
            if (_results.Count == 0)
            {
                return;
            }

            SearchResult result = _results[index];
            _currentIndex = index;
            _highlights = result.Highlights ?? Array.Empty<PdfHighlight>();
            EnsureRenderService(result.PdfPath);

            RenderPage(result.PageNumber);
            UpdateHeader(result);
            await LoadTextAsync(result).ConfigureAwait(true);
        }

        private void EnsureRenderService(string pdfPath)
        {
            if (string.Equals(_currentPdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _renderService?.Dispose();
            _renderService = new PdfRenderService(pdfPath);
            _currentPdfPath = pdfPath;
        }

        private void RenderPage(int pageNumber)
        {
            if (_renderService == null)
            {
                return;
            }

            var (bitmap, width, height) = _renderService.RenderPage(pageNumber, DisplayTargetWidth);
            PageImage.Source = bitmap;
            _lastRenderedBitmap = bitmap;
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

        private void UpdateHeader(SearchResult result)
        {
            string fileName = System.IO.Path.GetFileName(result.PdfPath);
            PageInfoText.Text = $"{fileName}  •  Page {result.PageNumber}";
            ResultInfoText.Text = $"Result {(_currentIndex + 1)} / {_results.Count}";
        }

        private async Task LoadTextAsync(SearchResult result)
        {
            _ocrCts?.Cancel();
            _ocrCts = new CancellationTokenSource();
            CancellationToken token = _ocrCts.Token;

            if (_mainViewModel.TryGetPdfPageText(result.PdfPath, result.PageNumber, out string text) &&
                !string.IsNullOrWhiteSpace(text))
            {
                PageTextBox.Text = text.Trim();
                TextSourceText.Text = "Source: PDF text";
                return;
            }

            TextSourceText.Text = "Source: OCR (running...)";
            PageTextBox.Text = string.Empty;

            string ocrText = await RunOcrAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            PageTextBox.Text = ocrText.Trim();
            TextSourceText.Text = "Source: OCR";
        }

        private async Task<string> RunOcrAsync(CancellationToken token)
        {
            if (_lastRenderedBitmap == null)
            {
                return string.Empty;
            }

            try
            {
                using var mat = ImageConversion.ToMat(_lastRenderedBitmap);
                token.ThrowIfCancellationRequested();
                return await _ocrService.ReadTextAsync(mat).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                return;
            }

            int nextIndex = _currentIndex - 1;
            if (nextIndex < 0)
            {
                nextIndex = _results.Count - 1;
            }

            _ = LoadResultAsync(nextIndex);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                return;
            }

            int nextIndex = (_currentIndex + 1) % _results.Count;
            _ = LoadResultAsync(nextIndex);
        }

        private void Insert_Click(object sender, RoutedEventArgs e)
        {
            string text = PageTextBox.SelectedText;
            if (string.IsNullOrWhiteSpace(text))
            {
                text = PageTextBox.Text;
            }

            text = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _mainViewModel.AnswerText = text;
            _mainViewModel.StatusText = "Inserted PDF text to answer.";
            Close();
        }

        private void Ocr_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                return;
            }

            _ = ForceOcrAsync();
        }

        private async Task ForceOcrAsync()
        {
            _ocrCts?.Cancel();
            _ocrCts = new CancellationTokenSource();
            CancellationToken token = _ocrCts.Token;
            TextSourceText.Text = "Source: OCR (running...)";
            PageTextBox.Text = string.Empty;
            string ocrText = await RunOcrAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }

            PageTextBox.Text = ocrText.Trim();
            TextSourceText.Text = "Source: OCR";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleWindowState()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            if (e.Key == Key.Right || e.Key == Key.Down)
            {
                Next_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.Up)
            {
                Prev_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
