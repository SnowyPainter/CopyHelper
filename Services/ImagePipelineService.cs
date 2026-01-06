using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CopyHelper.Models;
using CopyHelper.Utilities;
using OpenCvSharp;
using WpfRect = System.Windows.Rect;

namespace CopyHelper.Services
{
    public sealed class ImagePipelineService
    {
        private readonly ImageSegmentationService _segmentationService;
        private readonly OcrService _ocrService;

        public ImagePipelineService(ImageSegmentationService segmentationService, OcrService ocrService)
        {
            _segmentationService = segmentationService;
            _ocrService = ocrService;
        }

        public Task<ProcessedResult> ProcessAsync(BitmapSource source)
        {
            return Task.Run(async () =>
            {
                using Mat mat = ImageConversion.ToMat(source);
                IReadOnlyList<SegmentedRegion> regions = _segmentationService.Segment(mat);

                List<BitmapSource> photos = new List<BitmapSource>();
                List<string> textParts = new List<string>();

                List<SegmentedRegion> orderedTextRegions = regions
                    .Where(r => r.Type == RegionType.Text)
                    .OrderBy(r => r.Bounds.Y)
                    .ThenBy(r => r.Bounds.X)
                    .ToList();

                foreach (SegmentedRegion region in regions.Where(r => r.Type == RegionType.Photo))
                {
                    WpfRect padded = PadRegion(region.Bounds, source.PixelWidth, source.PixelHeight, 8);
                    using Mat cropped = new Mat(mat, ToRect(padded));

                    if (region.Type == RegionType.Photo)
                    {
                        BitmapSource photo = ImageConversion.ToBitmapSource(cropped);
                        photo.Freeze();
                        photos.Add(photo);
                    }
                }

                foreach (SegmentedRegion region in orderedTextRegions)
                {
                    WpfRect padded = PadRegion(region.Bounds, source.PixelWidth, source.PixelHeight, 8);
                    using Mat cropped = new Mat(mat, ToRect(padded));
                    using Mat ocrReady = PreprocessForOcr(cropped);
                    string text = await _ocrService.ReadTextAsync(ocrReady).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textParts.Add(text);
                    }
                }

                if (textParts.Count == 0)
                {
                    using Mat ocrReady = PreprocessForOcr(mat);
                    string text = await _ocrService.ReadTextAsync(ocrReady).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        textParts.Add(text);
                    }
                }

                string combined = string.Join(Environment.NewLine + Environment.NewLine, textParts).Trim();
                return new ProcessedResult(photos, combined);
            });
        }

        private static Mat PreprocessForOcr(Mat source)
        {
            Mat gray = new Mat();
            if (source.Channels() == 4)
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
            }
            else
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            }

            Mat resized = EnsureMinHeight(gray, 60);
            if (!ReferenceEquals(resized, gray))
            {
                gray.Dispose();
                gray = resized;
            }

            Mat normalized = new Mat();
            Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);

            Mat blurred = new Mat();
            Cv2.GaussianBlur(normalized, blurred, new Size(0, 0), 1.0);

            Mat sharpened = new Mat();
            Cv2.AddWeighted(normalized, 1.5, blurred, -0.5, 0, sharpened);

            normalized.Dispose();
            blurred.Dispose();
            return sharpened;
        }

        private static Mat EnsureMinHeight(Mat source, int minHeight)
        {
            if (source.Height >= minHeight)
            {
                return source;
            }

            double scale = minHeight / (double)source.Height;
            Mat resized = new Mat();
            Cv2.Resize(source, resized, new Size(), scale, scale, InterpolationFlags.Cubic);
            return resized;
        }

        private static WpfRect PadRegion(WpfRect region, int maxWidth, int maxHeight, int padding)
        {
            double x = Math.Max(0, region.X - padding);
            double y = Math.Max(0, region.Y - padding);
            double width = Math.Min(maxWidth - x, region.Width + padding * 2);
            double height = Math.Min(maxHeight - y, region.Height + padding * 2);
            return new WpfRect(x, y, width, height);
        }

        private static OpenCvSharp.Rect ToRect(WpfRect rect)
        {
            return new OpenCvSharp.Rect(
                (int)rect.X,
                (int)rect.Y,
                Math.Max(1, (int)rect.Width),
                Math.Max(1, (int)rect.Height));
        }
    }
}
