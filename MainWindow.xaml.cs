using System.Windows;
using System.Windows.Input;
using CopyHelper.Models;
using CopyHelper.ViewModels;
using CopyHelper.Views;

namespace CopyHelper
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            MainViewModel vm = new MainViewModel();
            vm.CaptureProvider = async () =>
            {
                try
                {
                    Hide();
                    CaptureOverlayWindow overlay = new CaptureOverlayWindow
                    {
                        Owner = this
                    };
                    overlay.Show();
                    return await overlay.CaptureAsync().ConfigureAwait(true);
                }
                finally
                {
                    Show();
                    Activate();
                }
            };
            DataContext = vm;
            Closed += (_, _) => vm.Dispose();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.PasteFromClipboardCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void DropArea_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private async void DropArea_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    await vm.LoadImageFromFileAsync(files[0]).ConfigureAwait(true);
                }
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            this.WindowState = this.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void ManagePdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            PdfIndexManagerWindow window = new PdfIndexManagerWindow(new PdfIndexManagerViewModel(vm.PdfIndexService))
            {
                Owner = this
            };
            window.ShowDialog();
            vm.ReloadPdfIndex();
        }

        private void PdfResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            if (PdfResultsList.SelectedItem is SearchResult result)
            {
                int index = vm.PdfSearchResults.IndexOf(result);
                PdfViewerWindow window = new PdfViewerWindow(vm, vm.PdfSearchResults, index)
                {
                    Owner = this
                };
                window.Show();
            }
        }
    }
}
