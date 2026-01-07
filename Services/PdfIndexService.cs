using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public PdfIndexService(string baseDirectory)
        {
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

        public List<SearchResult> Search(PdfIndexStore store, string queryText, IReadOnlyList<string> imageHashes, int topN = 8)
        {
            List<string> queryTokens = Tokenize(queryText).ToList();
            HashSet<string> querySet = new HashSet<string>(queryTokens);

            List<SearchResult> results = new List<SearchResult>();
            foreach (PdfDocumentIndex doc in store.Documents)
            {
                foreach (PdfPageIndex page in doc.Pages)
                {
                    double textScore = 0;
                    if (querySet.Count > 0 && page.Tokens.Length > 0)
                    {
                        int overlap = page.Tokens.Count(querySet.Contains);
                        double denom = Math.Sqrt(querySet.Count * page.Tokens.Length);
                        textScore = denom > 0 ? overlap / denom : 0;
                    }

                    double imageScore = 0;
                    if (imageHashes.Count > 0 && page.ImageHashes.Count > 0)
                    {
                        foreach (string pageHash in page.ImageHashes)
                        {
                            foreach (string queryHash in imageHashes)
                            {
                                double sim = ImageHash.Similarity(pageHash, queryHash);
                                if (sim > imageScore)
                                {
                                    imageScore = sim;
                                }
                            }
                        }
                    }

                    double score = CombineScores(textScore, imageScore, querySet.Count > 0, imageHashes.Count > 0);
                    if (score <= 0)
                    {
                        continue;
                    }

                    string snippet = page.Text;
                    if (snippet.Length > 180)
                    {
                        snippet = snippet.Substring(0, 180) + "...";
                    }

                    results.Add(new SearchResult(doc.PdfPath, page.PageNumber, score, snippet));
                }
            }

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

        private static PdfDocumentIndex BuildIndex(string path, DateTime lastWrite)
        {
            PdfDocumentIndex index = new PdfDocumentIndex
            {
                PdfPath = path,
                LastWriteUtc = lastWrite
            };

            using PdfDocument document = PdfDocument.Open(path);
            foreach (Page page in document.GetPages())
            {
                PdfPageIndex pageIndex = new PdfPageIndex
                {
                    PageNumber = page.Number,
                    Text = page.Text ?? string.Empty,
                    Tokens = Tokenize(page.Text ?? string.Empty).ToArray()
                };

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

                    string hash = ImageHash.ComputeDHash(bitmap);
                    pageIndex.ImageHashes.Add(hash);
                }

                index.Pages.Add(pageIndex);
            }

            return index;
        }

        private static IEnumerable<string> Tokenize(string? text)
        {
            List<string> tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    if (builder.Length > 1)
                    {
                        tokens.Add(builder.ToString());
                    }
                    builder.Clear();
                }
            }

            if (builder.Length > 1)
            {
                tokens.Add(builder.ToString());
            }

            return tokens;
        }
    }
}
