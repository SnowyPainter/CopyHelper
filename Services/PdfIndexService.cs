using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using CopyHelper.Models;
using CopyHelper.Utilities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CopyHelper.Services
{
    public sealed class PdfIndexService
    {
        private readonly string _indexPath;
        private readonly ClipEmbeddingService _embeddingService;

        public PdfIndexService(string baseDirectory, ClipEmbeddingService embeddingService)
        {
            _embeddingService = embeddingService;
            string dataDir = Path.Combine(baseDirectory, "data");
            Directory.CreateDirectory(dataDir);
            _indexPath = Path.Combine(dataDir, "pdf_index.json");
        }

        public PdfIndexStore Load()
        {
            if (!File.Exists(_indexPath))
            {
                return new PdfIndexStore();
            }

            string json = File.ReadAllText(_indexPath);
            return JsonSerializer.Deserialize<PdfIndexStore>(json) ?? new PdfIndexStore();
        }

        public void Save(PdfIndexStore store)
        {
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_indexPath, json);
        }

        public async Task<PdfIndexStore> IngestAsync(IEnumerable<string> pdfPaths, PdfIndexStore existing)
        {
            PdfIndexStore store = existing ?? new PdfIndexStore();
            foreach (string path in pdfPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                PdfDocumentIndex? existingDoc = store.Documents.FirstOrDefault(d => string.Equals(d.PdfPath, path, StringComparison.OrdinalIgnoreCase));
                if (existingDoc != null && existingDoc.LastWriteUtc == lastWrite)
                {
                    continue;
                }

                PdfDocumentIndex docIndex = await Task.Run(() => BuildIndex(path, lastWrite)).ConfigureAwait(false);
                if (existingDoc != null)
                {
                    store.Documents.Remove(existingDoc);
                }

                store.Documents.Add(docIndex);
            }

            Save(store);
            return store;
        }

        public List<SearchResult> Search(PdfIndexStore store, float[] queryTextEmbedding, IReadOnlyList<float[]> queryImageEmbeddings, int topN = 8)
        {
            int totalPages = store.Documents.Sum(d => d.Pages.Count);
            Debug.WriteLine($"[PDFSearch] docs={store.Documents.Count}, pages={totalPages}, textEmb={queryTextEmbedding.Length}, imageEmb={queryImageEmbeddings.Count}");

            List<SearchResult> results = new List<SearchResult>();
            int scoredPages = 0;
            double maxScore = double.NegativeInfinity;
            foreach (PdfDocumentIndex doc in store.Documents)
            {
                foreach (PdfPageIndex page in doc.Pages)
                {
                    if (page.TextChunks.Count == 0 && page.ImageChunks.Count == 0)
                    {
                        continue;
                    }

                    List<PdfHighlight> highlights = new List<PdfHighlight>();
                    double textScore = 0;
                    string snippet = page.Text;

                    if (queryTextEmbedding.Length > 0 && page.TextChunks.Count > 0)
                    {
                        List<(TextChunk chunk, double score)> scored = new List<(TextChunk, double)>();
                        foreach (TextChunk chunk in page.TextChunks)
                        {
                            if (chunk.Embedding == null || chunk.Embedding.Length == 0)
                            {
                                continue;
                            }

                            double sim = Cosine(queryTextEmbedding, chunk.Embedding);
                            scored.Add((chunk, sim));
                        }

                        if (scored.Count > 0)
                        {
                            textScore = scored.Max(s => s.score);
                        }

                        foreach (var match in scored.OrderByDescending(s => s.score).Take(3))
                        {
                            if (textScore < match.score)
                            {
                                textScore = match.score;
                                snippet = match.chunk.Text;
                            }

                            if (match.score >= 0.2)
                            {
                                highlights.Add(new PdfHighlight(match.chunk.Bounds, "text"));
                            }
                        }
                    }

                    double imageScore = 0;
                    if (queryImageEmbeddings.Count > 0 && page.ImageChunks.Count > 0)
                    {
                        List<(ImageChunk chunk, double score)> scored = new List<(ImageChunk, double)>();
                        foreach (ImageChunk chunk in page.ImageChunks)
                        {
                            if (chunk.Embedding == null || chunk.Embedding.Length == 0)
                            {
                                continue;
                            }

                            foreach (float[] queryEmbedding in queryImageEmbeddings)
                            {
                                double sim = Cosine(queryEmbedding, chunk.Embedding);
                                scored.Add((chunk, sim));
                            }
                        }

                        if (scored.Count > 0)
                        {
                            imageScore = scored.Max(s => s.score);
                        }

                        foreach (var match in scored.OrderByDescending(s => s.score).Take(2))
                        {
                            if (imageScore < match.score)
                            {
                                imageScore = match.score;
                            }

                            if (match.score >= 0.2)
                            {
                                highlights.Add(new PdfHighlight(match.chunk.Bounds, "image"));
                            }
                        }
                    }

                    double score = CombineScores(textScore, imageScore, queryTextEmbedding.Length > 0, queryImageEmbeddings.Count > 0);
                    scoredPages++;
                    if (score > maxScore)
                    {
                        maxScore = score;
                    }

                    if (snippet.Length > 180)
                    {
                        snippet = snippet.Substring(0, 180) + "...";
                    }

                    results.Add(new SearchResult(doc.PdfPath, page.PageNumber, score, snippet, highlights));
                }
            }

            Debug.WriteLine($"[PDFSearch] scoredPages={scoredPages}, maxScore={maxScore:0.0000}, results={results.Count}");
            return results
                .OrderByDescending(r => r.Score)
                .Take(topN)
                .ToList();
        }

        private static double CombineScores(double textScore, double imageScore, bool hasText, bool hasImage)
        {
            if (hasText && hasImage)
            {
                return textScore * 0.7 + imageScore * 0.3;
            }

            if (hasText)
            {
                return textScore;
            }

            if (hasImage)
            {
                return imageScore;
            }

            return 0;
        }

        private PdfDocumentIndex BuildIndex(string path, DateTime lastWrite)
        {
            PdfDocumentIndex index = new PdfDocumentIndex
            {
                PdfPath = path,
                LastWriteUtc = lastWrite
            };

            using PdfDocument document = PdfDocument.Open(path);
            foreach (Page page in document.GetPages())
            {
                double pageWidth = page.Width;
                double pageHeight = page.Height;
                PdfPageIndex pageIndex = new PdfPageIndex
                {
                    PageNumber = page.Number,
                    Text = page.Text ?? string.Empty,
                    PageWidth = (float)pageWidth,
                    PageHeight = (float)pageHeight
                };

                foreach (TextChunk chunk in ExtractTextChunks(page, pageWidth, pageHeight))
                {
                    if (!string.IsNullOrWhiteSpace(chunk.Text))
                    {
                        chunk.Embedding = _embeddingService.EncodeText(chunk.Text);
                    }

                    pageIndex.TextChunks.Add(chunk);
                }

                foreach (IPdfImage image in page.GetImages())
                {
                    if (!image.TryGetPng(out byte[] data) || data.Length == 0)
                    {
                        continue;
                    }

                    var bitmap = ImageConversion.FromEncodedBytes(data);
                    if (bitmap.PixelWidth < 50 || bitmap.PixelHeight < 50)
                    {
                        continue;
                    }

                    float[] embedding = _embeddingService.EncodeImage(bitmap);
                    if (embedding.Length == 0)
                    {
                        continue;
                    }

                    PdfRect bounds = GetNormalizedRect(image.Bounds, pageWidth, pageHeight);
                    pageIndex.ImageChunks.Add(new ImageChunk
                    {
                        Bounds = bounds,
                        Embedding = embedding
                    });
                }

                index.Pages.Add(pageIndex);
            }

            return index;
        }

        private static IEnumerable<TextChunk> ExtractTextChunks(Page page, double pageWidth, double pageHeight)
        {
            List<Word> words = page.GetWords()
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            const double lineTolerance = 2.5;
            List<Word> line = new List<Word>();
            double currentY = double.NaN;

            foreach (Word word in words)
            {
                double y = word.BoundingBox.Bottom;
                if (line.Count == 0)
                {
                    line.Add(word);
                    currentY = y;
                    continue;
                }

                if (Math.Abs(y - currentY) <= lineTolerance)
                {
                    line.Add(word);
                }
                else
                {
                    yield return BuildChunk(line, pageWidth, pageHeight);
                    line.Clear();
                    line.Add(word);
                    currentY = y;
                }
            }

            if (line.Count > 0)
            {
                yield return BuildChunk(line, pageWidth, pageHeight);
            }
        }

        private static TextChunk BuildChunk(List<Word> words, double pageWidth, double pageHeight)
        {
            string text = string.Join(" ", words.Select(w => w.Text));
            double left = words.Min(w => w.BoundingBox.Left);
            double right = words.Max(w => w.BoundingBox.Right);
            double bottom = words.Min(w => w.BoundingBox.Bottom);
            double top = words.Max(w => w.BoundingBox.Top);

            PdfRect bounds = GetNormalizedRect(new { Left = left, Bottom = bottom, Width = right - left, Height = top - bottom }, pageWidth, pageHeight);
            return new TextChunk
            {
                Text = text,
                Bounds = bounds
            };
        }

        private static PdfRect GetNormalizedRect(object rectObject, double pageWidth, double pageHeight)
        {
            dynamic rect = rectObject;
            double left = rect.Left;
            double bottom = rect.Bottom;
            double width = rect.Width;
            double height = rect.Height;

            double x = left / pageWidth;
            double yTop = pageHeight - (bottom + height);
            double y = yTop / pageHeight;
            double w = width / pageWidth;
            double h = height / pageHeight;

            return new PdfRect
            {
                X = (float)x,
                Y = (float)y,
                Width = (float)w,
                Height = (float)h
            };
        }

        private static double Cosine(float[] a, float[] b)
        {
            if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            {
                return 0;
            }

            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }

            return sum;
        }
    }
}
