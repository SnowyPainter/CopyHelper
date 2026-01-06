using System;
using System.Collections.Generic;
using System.Linq;
using CopyHelper.Models;
using OpenCvSharp;
using WpfRect = System.Windows.Rect;

namespace CopyHelper.Services
{
    public sealed class ImageSegmentationService
    {
        public IReadOnlyList<SegmentedRegion> Segment(Mat source)
        {
            if (source.Empty())
            {
                return Array.Empty<SegmentedRegion>();
            }

            using Mat gray = new Mat();
            if (source.Channels() == 4)
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
            }
            else
            {
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            }

            using Mat binary = new Mat();
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            using Mat textMask = binary.Clone();
            using Mat textKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 5));
            Cv2.Dilate(textMask, textMask, textKernel, iterations: 2);

            List<SegmentedRegion> regions = new List<SegmentedRegion>();
            WpfRect imageRect = new WpfRect(0, 0, source.Width, source.Height);

            Cv2.FindContours(textMask, out Point[][] textContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (Point[] contour in textContours)
            {
                OpenCvSharp.Rect rect = Cv2.BoundingRect(contour);
                if (rect.Width * rect.Height < 500)
                {
                    continue;
                }

                WpfRect wpfRect = new WpfRect(rect.X, rect.Y, rect.Width, rect.Height);
                if (imageRect.Contains(wpfRect.TopLeft) && imageRect.Contains(wpfRect.BottomRight))
                {
                    regions.Add(new SegmentedRegion(RegionType.Text, wpfRect));
                }
            }

            using Mat edges = new Mat();
            Cv2.Canny(gray, edges, 60, 180);
            using Mat edgeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, edgeKernel, iterations: 2);

            Cv2.FindContours(edges, out Point[][] photoContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            double minPhotoArea = source.Width * source.Height * 0.08;

            foreach (Point[] contour in photoContours)
            {
                OpenCvSharp.Rect rect = Cv2.BoundingRect(contour);
                double area = rect.Width * rect.Height;
                if (area < minPhotoArea)
                {
                    continue;
                }

                double aspect = rect.Width / (double)rect.Height;
                if (aspect < 0.3 || aspect > 3.5)
                {
                    continue;
                }

                WpfRect wpfRect = new WpfRect(rect.X, rect.Y, rect.Width, rect.Height);
                if (IsMostlyCoveredByText(wpfRect, regions))
                {
                    continue;
                }

                regions.Add(new SegmentedRegion(RegionType.Photo, wpfRect));
            }

            return MergeOverlaps(regions);
        }

        private static bool IsMostlyCoveredByText(WpfRect candidate, IReadOnlyList<SegmentedRegion> regions)
        {
            double candidateArea = candidate.Width * candidate.Height;
            if (candidateArea <= 0)
            {
                return true;
            }

            double overlapArea = 0;
            foreach (SegmentedRegion region in regions.Where(r => r.Type == RegionType.Text))
            {
                WpfRect intersection = WpfRect.Intersect(candidate, region.Bounds);
                if (!intersection.IsEmpty)
                {
                    overlapArea += intersection.Width * intersection.Height;
                }
            }

            return overlapArea / candidateArea > 0.6;
        }

        private static IReadOnlyList<SegmentedRegion> MergeOverlaps(IReadOnlyList<SegmentedRegion> regions)
        {
            List<SegmentedRegion> merged = new List<SegmentedRegion>();

            foreach (SegmentedRegion region in regions.OrderBy(r => r.Type))
            {
                bool mergedExisting = false;
                for (int i = 0; i < merged.Count; i++)
                {
                    if (merged[i].Type != region.Type)
                    {
                        continue;
                    }

                    WpfRect intersection = WpfRect.Intersect(merged[i].Bounds, region.Bounds);
                    if (intersection.IsEmpty)
                    {
                        continue;
                    }

                    WpfRect union = WpfRect.Union(merged[i].Bounds, region.Bounds);
                    merged[i] = new SegmentedRegion(region.Type, union);
                    mergedExisting = true;
                    break;
                }

                if (!mergedExisting)
                {
                    merged.Add(region);
                }
            }

            return merged;
        }
    }
}
