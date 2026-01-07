using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CopyHelper.Models;
using CopyHelper.Services;

namespace CopyHelper.ViewModels
{
    public sealed partial class PdfIndexManagerViewModel : ObservableObject
    {
        private readonly PdfIndexService _indexService;
        private PdfIndexStore _store;

        public PdfIndexManagerViewModel(PdfIndexService indexService)
        {
            _indexService = indexService;
            _store = _indexService.Load();
            Documents = new ObservableCollection<PdfDocumentIndex>(_store.Documents.OrderBy(d => d.PdfPath));

            AddCommand = new AsyncRelayCommand(AddAsync);
            RemoveCommand = new RelayCommand(Remove, () => SelectedDocument != null);
            ReindexCommand = new AsyncRelayCommand(ReindexAsync, () => SelectedDocument != null);
        }

        public ObservableCollection<PdfDocumentIndex> Documents { get; }

        [ObservableProperty]
        private PdfDocumentIndex? _selectedDocument;

        public IAsyncRelayCommand AddCommand { get; }
        public IRelayCommand RemoveCommand { get; }
        public IAsyncRelayCommand ReindexCommand { get; }

        partial void OnSelectedDocumentChanged(PdfDocumentIndex? value)
        {
            RemoveCommand.NotifyCanExecuteChanged();
            ReindexCommand.NotifyCanExecuteChanged();
        }

        private async Task AddAsync()
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|All Files|*.*",
                Multiselect = true
            };

            bool? result = dialog.ShowDialog();
            if (result != true || dialog.FileNames.Length == 0)
            {
                return;
            }

            _store = await _indexService.IngestAsync(dialog.FileNames, _store).ConfigureAwait(true);
            RefreshDocuments();
        }

        private void Remove()
        {
            if (SelectedDocument == null)
            {
                return;
            }

            PdfDocumentIndex doc = SelectedDocument;
            _store.Documents.Remove(doc);
            _indexService.Save(_store);
            RefreshDocuments();
        }

        private async Task ReindexAsync()
        {
            if (SelectedDocument == null)
            {
                return;
            }

            string path = SelectedDocument.PdfPath;
            if (!File.Exists(path))
            {
                Remove();
                return;
            }

            _store = await _indexService.IngestAsync(new[] { path }, _store).ConfigureAwait(true);
            RefreshDocuments();
        }

        private void RefreshDocuments()
        {
            Documents.Clear();
            foreach (PdfDocumentIndex doc in _store.Documents.OrderBy(d => d.PdfPath))
            {
                Documents.Add(doc);
            }
        }
    }
}
