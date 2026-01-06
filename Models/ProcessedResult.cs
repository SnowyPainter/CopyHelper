using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace CopyHelper.Models
{
    public sealed class ProcessedResult
    {
        public ProcessedResult(IReadOnlyList<BitmapSource> photos, string ocrText)
        {
            Photos = photos;
            OcrText = ocrText;
        }

        public IReadOnlyList<BitmapSource> Photos { get; }
        public string OcrText { get; }
    }
}
