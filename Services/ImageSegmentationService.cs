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
        private const int ProcessingWidth = 800;

        public IReadOnlyList<SegmentedRegion> Segment(Mat source)
        {
            if (source.Empty())
                return Array.Empty<SegmentedRegion>();

            // === 1. Downscale (속도 최적화) ===
            double scale = 1.0;
            Mat work = source;
            bool dispose = false;

            if (source.Width > ProcessingWidth)
            {
                scale = (double)source.Width / ProcessingWidth;
                int newHeight = (int)(source.Height / scale);
                work = new Mat();
                Cv2.Resize(source, work, new Size(ProcessingWidth, newHeight));
                dispose = true;
            }

            try
            {
                // === 2. Grayscale + Blur ===
                using var gray = new Mat();
                if (work.Channels() >= 3)
                    Cv2.CvtColor(work, gray, ColorConversionCodes.BGR2GRAY);
                else
                    work.CopyTo(gray);

                using var blurred = new Mat();
                Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

                // === 3. Edge Density Map ===
                using var edges = new Mat();
                Cv2.Canny(blurred, edges, 60, 180);

                using var edgeDilated = new Mat();
                var bigKernel = Cv2.GetStructuringElement(
                    MorphShapes.Rect, new Size(9, 9));
                Cv2.Dilate(edges, edgeDilated, bigKernel, iterations: 2);

                // === 4. Blob화 (텍스처 덩어리 만들기) ===
                using var blob = new Mat();
                Cv2.MorphologyEx(
                    edgeDilated,
                    blob,
                    MorphTypes.Close,
                    bigKernel,
                    iterations: 2);

                Cv2.Threshold(blob, blob, 1, 255, ThresholdTypes.Binary);

                // === 5. Contour 검출 ===
                Cv2.FindContours(
                    blob,
                    out Point[][] contours,
                    out _,
                    RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple);

                var regions = new List<SegmentedRegion>();

                double imgArea = work.Width * work.Height;
                double minArea = imgArea * 0.01;   // 너무 작은 노이즈 제거
                double maxArea = imgArea * 0.90;   // 전체 배경 제거

                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area < minArea || area > maxArea)
                        continue;

                    var rect = Cv2.BoundingRect(contour);

                    // 종횡비 필터 (너무 얇은 라인 제거)
                    double aspect =
                        (double)rect.Width / Math.Max(1, rect.Height);
                    if (aspect < 0.15 || aspect > 8.0)
                        continue;

                    // Rectangularity (완화 기준)
                    var rrect = Cv2.MinAreaRect(contour);
                    double rArea =
                        rrect.Size.Width * rrect.Size.Height;
                    double rectangularity = area / rArea;

                    if (rectangularity < 0.55)
                        continue;

                    regions.Add(new SegmentedRegion(
                        RegionType.Photo,
                        new WpfRect(
                            rect.X * scale,
                            rect.Y * scale,
                            rect.Width * scale,
                            rect.Height * scale
                        )));
                }

                return MergeOverlaps(regions);
            }
            finally
            {
                if (dispose)
                    work.Dispose();
            }
        }

        // === 겹치는 영역 병합 (NMS 유사) ===
        private static IReadOnlyList<SegmentedRegion>
            MergeOverlaps(List<SegmentedRegion> regions)
        {
            if (regions.Count <= 1)
                return regions;

            var sorted = regions
                .OrderByDescending(r =>
                    r.Bounds.Width * r.Bounds.Height)
                .ToList();

            var merged = new List<SegmentedRegion>();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                sorted.RemoveAt(0);

                bool absorbed = false;

                for (int i = 0; i < merged.Count; i++)
                {
                    var intersect = WpfRect.Intersect(
                        merged[i].Bounds, current.Bounds);

                    if (intersect.IsEmpty)
                        continue;

                    double ia =
                        intersect.Width * intersect.Height;
                    double ca =
                        current.Bounds.Width *
                        current.Bounds.Height;
                    double ea =
                        merged[i].Bounds.Width *
                        merged[i].Bounds.Height;

                    // 현재 영역이 기존에 거의 포함되면 버림
                    if (ia / ca > 0.7)
                    {
                        absorbed = true;
                        break;
                    }

                    // 기존 영역이 현재에 거의 포함되면 확장
                    if (ia / ea > 0.85)
                    {
                        merged[i] = new SegmentedRegion(
                            current.Type,
                            WpfRect.Union(
                                merged[i].Bounds,
                                current.Bounds));
                        absorbed = true;
                        break;
                    }
                }

                if (!absorbed)
                    merged.Add(current);
            }

            return merged;
        }
    }
}
