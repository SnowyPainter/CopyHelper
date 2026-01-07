namespace CopyHelper.Models
{
    public sealed class SearchResult
    {
        public SearchResult(string pdfPath, int pageNumber, double score, string snippet)
        {
            PdfPath = pdfPath;
            PageNumber = pageNumber;
            Score = score;
            Snippet = snippet;
        }

        public string PdfPath { get; }
        public int PageNumber { get; }
        public double Score { get; }
        public string Snippet { get; }
    }
}
