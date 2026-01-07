namespace CopyHelper.Models
{
    public sealed class SearchResult
    {
        public SearchResult(string pdfPath, int pageNumber, double score, string snippet, IReadOnlyList<PdfHighlight> highlights)
        {
            PdfPath = pdfPath;
            PageNumber = pageNumber;
            Score = score;
            Snippet = snippet;
            Highlights = highlights;
        }

        public string PdfPath { get; }
        public int PageNumber { get; }
        public double Score { get; }
        public string Snippet { get; }
        public IReadOnlyList<PdfHighlight> Highlights { get; }
    }

    public sealed class PdfHighlight
    {
        public PdfHighlight(PdfRect bounds, string kind)
        {
            Bounds = bounds;
            Kind = kind;
        }

        public PdfRect Bounds { get; }
        public string Kind { get; }
    }
}
