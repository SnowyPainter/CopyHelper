using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyHelper.Models;
using CopyHelper.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WpfApplication = System.Windows.Application;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CopyHelper.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ImagePipelineService _pipelineService;
        private readonly TypingInjector _typingInjector;
        private readonly KeyboardHook _keyboardHook;
        private readonly MouseHook _mouseHook;
        private readonly OcrService _ocrService;
        private readonly ClipEmbeddingService _clipEmbeddingService;
        private readonly PdfIndexService _pdfIndexService;
        private PdfIndexStore _pdfIndexStore;
        private readonly IAsyncRelayCommand _pasteFromClipboardCommand;
        private readonly IAsyncRelayCommand _openFileCommand;
        private readonly IAsyncRelayCommand _captureCommand;
        private readonly IAsyncRelayCommand _ingestPdfCommand;
        private readonly IRelayCommand _clearCommand;
        private readonly IAsyncRelayCommand _startTypingCommand;
        private readonly IRelayCommand _stopTypingCommand;
        private readonly IRelayCommand<BitmapSource> _copyPhotoCommand;
        private readonly IRelayCommand<BitmapSource> _savePhotoCommand;
        private readonly IRelayCommand _saveAllPhotosCommand;
        private CancellationTokenSource? _typingCts;
        private IntPtr _targetWindow = IntPtr.Zero;

        public MainViewModel()
        {
            ImageSegmentationService segmentation = new ImageSegmentationService();
            _ocrService = new OcrService();
            _pipelineService = new ImagePipelineService(segmentation, _ocrService);
            _typingInjector = new TypingInjector();
            _keyboardHook = new KeyboardHook();
            _keyboardHook.KeyPressed += OnKeyPressed;
            _mouseHook = new MouseHook();
            _mouseHook.LeftButtonDown += OnLeftButtonDown;
            _clipEmbeddingService = new ClipEmbeddingService(AppDomain.CurrentDomain.BaseDirectory);
            _pdfIndexService = new PdfIndexService(AppDomain.CurrentDomain.BaseDirectory, _clipEmbeddingService);
            _pdfIndexStore = _pdfIndexService.Load();

            Photos = new ObservableCollection<BitmapSource>();
            PdfSearchResults = new ObservableCollection<SearchResult>();
            StatusText = "Ready.";
            TypingDelayMs = 20;
            CountdownSeconds = 3;
            IndexedPdfCount = _pdfIndexStore.Documents.Count;

            _pasteFromClipboardCommand = new AsyncRelayCommand(PasteFromClipboardAsync, () => !IsBusy);
            _openFileCommand = new AsyncRelayCommand(OpenFileAsync, () => !IsBusy);
            _captureCommand = new AsyncRelayCommand(CaptureAsync, () => !IsBusy && CaptureProvider != null);
            _ingestPdfCommand = new AsyncRelayCommand(IngestPdfsAsync, () => !IsBusy);
            _clearCommand = new RelayCommand(Clear);
            _startTypingCommand = new AsyncRelayCommand(StartTypingAsync, () => !IsBusy);
            _stopTypingCommand = new RelayCommand(StopTyping);
            _copyPhotoCommand = new RelayCommand<BitmapSource>(CopyPhoto);
            _savePhotoCommand = new RelayCommand<BitmapSource>(SavePhoto);
            _saveAllPhotosCommand = new RelayCommand(SaveAllPhotos, () => Photos.Count > 0);
            Photos.CollectionChanged += (_, _) => _saveAllPhotosCommand.NotifyCanExecuteChanged();
        }

        public ObservableCollection<BitmapSource> Photos { get; }

        [ObservableProperty]
        private BitmapSource? _sourceImage;

        [ObservableProperty]
        private string _ocrText = string.Empty;

        [ObservableProperty]
        private string _answerText = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private int _typingDelayMs;

        [ObservableProperty]
        private int _countdownSeconds;

        private Func<Task<BitmapSource?>>? _captureProvider;

        public Func<Task<BitmapSource?>>? CaptureProvider
        {
            get => _captureProvider;
            set
            {
                _captureProvider = value;
                _captureCommand.NotifyCanExecuteChanged();
            }
        }

        public IAsyncRelayCommand PasteFromClipboardCommand => _pasteFromClipboardCommand;
        public IAsyncRelayCommand OpenFileCommand => _openFileCommand;
        public IAsyncRelayCommand CaptureCommand => _captureCommand;
        public IAsyncRelayCommand IngestPdfCommand => _ingestPdfCommand;
        public IRelayCommand ClearCommand => _clearCommand;
        public IAsyncRelayCommand StartTypingCommand => _startTypingCommand;
        public IRelayCommand StopTypingCommand => _stopTypingCommand;
        public IRelayCommand<BitmapSource> CopyPhotoCommand => _copyPhotoCommand;
        public IRelayCommand<BitmapSource> SavePhotoCommand => _savePhotoCommand;
        public IRelayCommand SaveAllPhotosCommand => _saveAllPhotosCommand;

        public ObservableCollection<SearchResult> PdfSearchResults { get; }

        [ObservableProperty]
        private int _indexedPdfCount;

        public PdfIndexService PdfIndexService => _pdfIndexService;

        public bool TryGetPdfPageText(string pdfPath, int pageNumber, out string text)
        {
            text = string.Empty;
            PdfDocumentIndex? doc = _pdfIndexStore.Documents.FirstOrDefault(d =>
                string.Equals(d.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase));
            PdfPageIndex? page = doc?.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
            if (page == null)
            {
                return false;
            }

            text = page.Text ?? string.Empty;
            return true;
        }

        public async Task LoadImageAsync(BitmapSource source)
        {
            SourceImage = source;
            Photos.Clear();
            OcrText = string.Empty;

            IsBusy = true;
            StatusText = "Processing image...";

            ProcessedResult result = await _pipelineService.ProcessAsync(source).ConfigureAwait(true);

            Photos.Clear();
            foreach (BitmapSource photo in result.Photos)
            {
                Photos.Add(photo);
            }

            OcrText = result.OcrText;
            StatusText = "Processing complete.";
            IsBusy = false;
            _saveAllPhotosCommand.NotifyCanExecuteChanged();

            await RunPdfSearchAsync(result.OcrText, result.Photos).ConfigureAwait(true);
        }

        partial void OnIsBusyChanged(bool value)
        {
            _pasteFromClipboardCommand.NotifyCanExecuteChanged();
            _openFileCommand.NotifyCanExecuteChanged();
            _captureCommand.NotifyCanExecuteChanged();
            _ingestPdfCommand.NotifyCanExecuteChanged();
            _startTypingCommand.NotifyCanExecuteChanged();
        }

        public async Task LoadImageFromFileAsync(string filePath)
        {
            BitmapImage image = new BitmapImage();
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
            }

            await LoadImageAsync(image).ConfigureAwait(true);
        }

        private async Task PasteFromClipboardAsync()
        {
            if (!Clipboard.ContainsImage())
            {
                StatusText = "Clipboard does not contain an image.";
                return;
            }

            BitmapSource? source = Clipboard.GetImage();
            if (source != null)
            {
                source.Freeze();
                await LoadImageAsync(source).ConfigureAwait(true);
            }
        }

        private async Task OpenFileAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All Files|*.*"
            };

            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                await LoadImageFromFileAsync(dialog.FileName).ConfigureAwait(true);
            }
        }

        private async Task IngestPdfsAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|All Files|*.*",
                Multiselect = true
            };

            bool? result = dialog.ShowDialog();
            if (result == true && dialog.FileNames.Length > 0)
            {
                StatusText = "Indexing PDFs...";
                IsBusy = true;
                _pdfIndexStore = await _pdfIndexService.IngestAsync(dialog.FileNames, _pdfIndexStore).ConfigureAwait(true);
                IndexedPdfCount = _pdfIndexStore.Documents.Count;
                StatusText = $"Indexed {IndexedPdfCount} PDF(s).";
                IsBusy = false;
            }
        }

        public void ReloadPdfIndex()
        {
            _pdfIndexStore = _pdfIndexService.Load();
            IndexedPdfCount = _pdfIndexStore.Documents.Count;
        }

        private async Task CaptureAsync()
        {
            if (CaptureProvider == null)
            {
                return;
            }

            BitmapSource? capture = await CaptureProvider().ConfigureAwait(true);
            if (capture != null)
            {
                capture.Freeze();
                await LoadImageAsync(capture).ConfigureAwait(true);
            }
        }

        private void Clear()
        {
            SourceImage = null;
            Photos.Clear();
            OcrText = string.Empty;
            AnswerText = string.Empty;
            StatusText = "Cleared.";
            _saveAllPhotosCommand.NotifyCanExecuteChanged();
            PdfSearchResults.Clear();
        }

        private async Task RunPdfSearchAsync(string ocrText, IReadOnlyList<BitmapSource> photos)
        {
            PdfSearchResults.Clear();
            if (_pdfIndexStore.Documents.Count == 0)
            {
                return;
            }

            List<float[]> imageEmbeddings = new List<float[]>();
            foreach (BitmapSource photo in photos)
            {
                float[] embedding = _clipEmbeddingService.EncodeImage(photo);
                if (embedding.Length > 0)
                {
                    imageEmbeddings.Add(embedding);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[PDFSearch] ocrLen={ocrText.Length}, photos={photos.Count}, imageEmb={imageEmbeddings.Count}");

            List<SearchResult> results = await Task.Run(() =>
                _pdfIndexService.Search(_pdfIndexStore, ocrText, imageEmbeddings)).ConfigureAwait(true);

            foreach (SearchResult result in results)
            {
                PdfSearchResults.Add(result);
            }
        }

        private async Task StartTypingAsync()
        {
            if (string.IsNullOrWhiteSpace(AnswerText))
            {
                StatusText = "Answer text is empty.";
                return;
            }

            StopTyping();
            _typingCts = new CancellationTokenSource();

            try
            {
                StatusText = "Click target window. Waiting for click...";
                Task clickTask = WaitForTargetClickAsync(_typingCts.Token);
                await clickTask.ConfigureAwait(true);

                if (CountdownSeconds > 0)
                {
                    for (int i = CountdownSeconds; i > 0; i--)
                    {
                        StatusText = $"Typing starts in {i}s...";
                        await Task.Delay(1000).ConfigureAwait(true);
                    }
                }

                StatusText = "Typing in progress... Press ESC to stop.";
                await _typingInjector.TypeTextAsync(AnswerText, TypingDelayMs, _typingCts.Token, _targetWindow).ConfigureAwait(true);
                StatusText = "Typing finished.";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Typing canceled.";
            }
        }

        private void StopTyping()
        {
            _typingCts?.Cancel();
            _typingCts = null;
        }

        private void CopyPhoto(BitmapSource? photo)
        {
            if (photo == null)
            {
                return;
            }

            Clipboard.SetImage(photo);
            StatusText = "Photo copied to clipboard.";
        }

        private void SavePhoto(BitmapSource? photo)
        {
            if (photo == null)
            {
                return;
            }

            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|All Files|*.*",
                FileName = "photo.png"
            };

            bool? result = dialog.ShowDialog();
            if (result == true)
            {
                SaveBitmapSource(photo, dialog.FileName);
                StatusText = "Photo saved.";
            }
        }

        private void SaveAllPhotos()
        {
            if (Photos.Count == 0)
            {
                return;
            }

            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder"
            };

            bool? result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            string? folder = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            int index = 1;
            foreach (BitmapSource photo in Photos)
            {
                string fileName = $"photo_{index:000}.png";
                string path = Path.Combine(folder, fileName);
                SaveBitmapSource(photo, path);
                index++;
            }

            StatusText = $"Saved {Photos.Count} photos.";
        }

        private static void SaveBitmapSource(BitmapSource source, string filePath)
        {
            BitmapEncoder encoder = CreateEncoder(filePath);
            encoder.Frames.Add(BitmapFrame.Create(source));
            using FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
        }

        private static BitmapEncoder CreateEncoder(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
        }

        private void OnKeyPressed(object? sender, int keyCode)
        {
            if (keyCode == 0x1B)
            {
                StopTyping();
            }
        }

        private TaskCompletionSource<bool>? _clickTcs;

        private Task WaitForTargetClickAsync(CancellationToken token)
        {
            _clickTcs?.TrySetCanceled();
            _clickTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => _clickTcs.TrySetCanceled());
            return _clickTcs.Task;
        }


        private void OnLeftButtonDown(object? sender, EventArgs e)
        {
            if (_clickTcs == null) return;

            IntPtr foreground = GetForegroundWindow();
            IntPtr mainHandle = GetMainWindowHandle();

            if (foreground != IntPtr.Zero && foreground != mainHandle)
            {
                _targetWindow = foreground;
                _clickTcs.TrySetResult(true);
            }
        }


        private static IntPtr GetMainWindowHandle()
        {
            Window? window = WpfApplication.Current?.MainWindow;
            if (window == null)
            {
                return IntPtr.Zero;
            }

            return new WindowInteropHelper(window).Handle;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public void Dispose()
        {
            StopTyping();
            _keyboardHook.KeyPressed -= OnKeyPressed;
            _keyboardHook.Dispose();
            _mouseHook.LeftButtonDown -= OnLeftButtonDown;
            _mouseHook.Dispose();
            _clipEmbeddingService.Dispose();
        }

    }
}
