using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CopyHelper.Services;

namespace CopyHelper.Views
{
    public partial class CaptureOverlayWindow : Window
    {
        private readonly TaskCompletionSource<BitmapSource?> _tcs = new TaskCompletionSource<BitmapSource?>();
        private Point _start;
        private bool _isDragging;

        public CaptureOverlayWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => Focus();
        }

        public Task<BitmapSource?> CaptureAsync()
        {
            return _tcs.Task;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _start = e.GetPosition(this);
            _isDragging = true;
            SelectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionRect, _start.X);
            Canvas.SetTop(SelectionRect, _start.Y);
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            Point current = e.GetPosition(this);
            double x = Math.Min(current.X, _start.X);
            double y = Math.Min(current.Y, _start.Y);
            double width = Math.Abs(current.X - _start.X);
            double height = Math.Abs(current.Y - _start.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = width;
            SelectionRect.Height = height;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            ReleaseMouseCapture();

            Point end = e.GetPosition(this);
            double x = Math.Min(_start.X, end.X);
            double y = Math.Min(_start.Y, end.Y);
            double width = Math.Abs(end.X - _start.X);
            double height = Math.Abs(end.Y - _start.Y);

            Point screenStart = PointToScreen(new Point(x, y));
            Point screenEnd = PointToScreen(new Point(x + width, y + height));

            Rect region = new Rect(
                screenStart.X,
                screenStart.Y,
                Math.Abs(screenEnd.X - screenStart.X),
                Math.Abs(screenEnd.Y - screenStart.Y));

            if (region.Width < 4 || region.Height < 4)
            {
                Cancel();
                return;
            }

            Hide();
            BitmapSource capture = ScreenCaptureService.Capture(region);
            _tcs.TrySetResult(capture);
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
            }
        }

        private void Cancel()
        {
            _tcs.TrySetResult(null);
            Close();
        }
    }
}
