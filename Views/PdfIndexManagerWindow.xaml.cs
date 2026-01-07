using System.Windows;
using CopyHelper.ViewModels;

namespace CopyHelper.Views
{
    public partial class PdfIndexManagerWindow : Window
    {
        public PdfIndexManagerWindow(PdfIndexManagerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
