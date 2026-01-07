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
        public float PageWidth { get; set; }
        public float PageHeight { get; set; }
        public string Text { get; set; } = string.Empty;
        public float[]? TextEmbedding { get; set; }
        public List<TextChunk> TextChunks { get; set; } = new();
        public List<ImageChunk> ImageChunks { get; set; } = new();
    }

    public sealed class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public PdfRect Bounds { get; set; } = new PdfRect();
        public float[]? Embedding { get; set; }
    }

    public sealed class ImageChunk
    {
        public PdfRect Bounds { get; set; } = new PdfRect();
        public float[]? Embedding { get; set; }
    }

    public sealed class PdfRect
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }
}
