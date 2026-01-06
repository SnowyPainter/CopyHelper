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
            return Task.Run(() =>
            {
                using Mat mat = ImageConversion.ToMat(source);
                IReadOnlyList<SegmentedRegion> regions = _segmentationService.Segment(mat);

                List<BitmapSource> photos = new List<BitmapSource>();
                List<string> textParts = new List<string>();

                foreach (SegmentedRegion region in regions)
                {
                    WpfRect padded = PadRegion(region.Bounds, source.PixelWidth, source.PixelHeight, 8);
                    using Mat cropped = new Mat(mat, ToRect(padded));

                    if (region.Type == RegionType.Photo)
                    {
                        BitmapSource photo = ImageConversion.ToBitmapSource(cropped);
                        photo.Freeze();
                        photos.Add(photo);
                    }
                    else
                    {
                        using Mat ocrReady = PreprocessForOcr(cropped);
                        string text = _ocrService.ReadText(ocrReady);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            textParts.Add(text);
                        }
                    }
                }

                if (textParts.Count == 0)
                {
                    using Mat ocrReady = PreprocessForOcr(mat);
                    string text = _ocrService.ReadText(ocrReady);
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

            Mat binary = new Mat();
            Cv2.AdaptiveThreshold(
                gray,
                binary,
                255,
                AdaptiveThresholdTypes.GaussianC,
                ThresholdTypes.Binary,
                31,
                5);

            Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: 1);
            Cv2.Dilate(binary, binary, kernel, iterations: 1);
            return binary;
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
