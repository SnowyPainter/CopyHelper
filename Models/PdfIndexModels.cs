using System;
using System.Collections.Generic;

namespace CopyHelper.Models
{
    public sealed class PdfIndexStore
    {
        public List<PdfDocumentIndex> Documents { get; set; } = new();
    }

    public sealed class PdfDocumentIndex
    {
        public string PdfPath { get; set; } = string.Empty;
        public DateTime LastWriteUtc { get; set; }
        public List<PdfPageIndex> Pages { get; set; } = new();
    }

    public sealed class PdfPageIndex
    {
        public int PageNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        public string[] Tokens { get; set; } = Array.Empty<string>();
        public List<string> ImageHashes { get; set; } = new();
    }
}
