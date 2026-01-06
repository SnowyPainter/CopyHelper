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
        // 처리 속도를 위해 이미지를 이 사이즈로 줄여서 연산 (원본 좌표는 비율 계산하여 복구)
        private const int ProcessingWidth = 800;

        public IReadOnlyList<SegmentedRegion> Segment(Mat source)
        {
            if (source.Empty()) return Array.Empty<SegmentedRegion>();

            // 1. 속도 최적화: 고해상도 이미지는 연산량이 기하급수적으로 늘어남.
            // 비율을 유지하며 축소(Downscale)
            double scale = 1.0;
            Mat processMat = source;
            bool needsDispose = false;

            if (source.Width > ProcessingWidth)
            {
                scale = (double)source.Width / ProcessingWidth;
                int newHeight = (int)(source.Height / scale);
                processMat = new Mat();
                Cv2.Resize(source, processMat, new OpenCvSharp.Size(ProcessingWidth, newHeight));
                needsDispose = true;
            }

            try
            {
                // 2. 전처리 파이프라인 개선
                using Mat gray = new Mat();
                if (processMat.Channels() >= 3)
                    Cv2.CvtColor(processMat, gray, ColorConversionCodes.BGR2GRAY);
                else
                    processMat.CopyTo(gray);

                // 노이즈 제거 (기존보다 커널 사이즈를 약간 키워 자잘한 텍스처 무시)
                using Mat blurred = new Mat();
                Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

                // Canny Edge Detection
                using Mat edges = new Mat();
                Cv2.Canny(blurred, edges, 50, 200); // 임계값 조정

                // 끊어진 선 연결 (Dilation) - 매우 중요
                // 사각형의 테두리가 살짝 끊겨있으면 인식이 안되므로 팽창시켜 연결함
                using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
                Cv2.Dilate(edges, edges, kernel);

                // 3. 윤곽선 검출
                Cv2.FindContours(edges, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                List<SegmentedRegion> regions = new List<SegmentedRegion>();
                
                // 최소/최대 크기 필터링 기준 (축소된 이미지 기준)
                double imgArea = processMat.Width * processMat.Height;
                double minArea = imgArea * 0.005; // 0.5% 이상 (너무 작은 노이즈 제거)
                double maxArea = imgArea * 0.95;  // 95% 이하 (전체 배경 제거)

                foreach (var contour in contours)
                {
                    double area = Cv2.ContourArea(contour);
                    if (area < minArea || area > maxArea) continue;

                    // 4. 사각형 판별 로직 개선 (핵심)
                    // 점 4개(Point check)에 집착하면 둥근 모서리나 노이즈에 취약함.
                    // 대신 "Convex Hull(볼록 껍질)"과 "Bounding Rect(외접 사각형)"의 비율을 봅니다.
                    
                    // (1) 볼록성 검사 (오목한 부분이 너무 많으면 사각형 아님)
                    if (!Cv2.IsContourConvex(contour))
                    {
                        // 볼록하지 않다면 ConvexHull을 구해서 보정 시도
                        Point[] hull = Cv2.ConvexHull(contour);
                        double hullArea = Cv2.ContourArea(hull);
                        
                        // 원래 면적과 Hull 면적의 차이가 크면(복잡한 모양) 패스
                        if (area / hullArea < 0.85) continue; 
                    }

                    // (2) 회전된 사각형(MinAreaRect)을 구해서 직사각형 유사도 검사
                    RotatedRect rotatedRect = Cv2.MinAreaRect(contour);
                    double rotatedRectArea = rotatedRect.Size.Width * rotatedRect.Size.Height;
                    
                    // "물체의 면적" / "외접 직사각형의 면적" 비율이 1에 가까울수록 사각형
                    double rectangularity = area / rotatedRectArea;
                    
                    // 0.85 이상이면 사각형으로 간주 (약간 찌그러져도 인식)
                    if (rectangularity < 0.85) continue;

                    // (3) 종횡비(Aspect Ratio) 체크 (너무 얇은 선 같은 것 제외)
                    OpenCvSharp.Rect bounding = Cv2.BoundingRect(contour);
                    double aspectRatio = (double)bounding.Width / bounding.Height;
                    if (aspectRatio < 0.1 || aspectRatio > 10.0) continue;

                    // (4) 좌표 복원 (축소된 비율만큼 다시 곱함)
                    regions.Add(new SegmentedRegion(RegionType.Photo, new WpfRect(
                        bounding.X * scale,
                        bounding.Y * scale,
                        bounding.Width * scale,
                        bounding.Height * scale
                    )));
                }

                return MergeOverlaps(regions);
            }
            finally
            {
                if (needsDispose) processMat.Dispose();
            }
        }

        // 겹치는 영역 병합 (NMS 유사 로직)
        private static IReadOnlyList<SegmentedRegion> MergeOverlaps(List<SegmentedRegion> regions)
        {
            if (regions.Count <= 1) return regions;

            // 크기가 큰 순서대로 정렬 (큰 사각형이 작은 사각형을 포함하는 경우 처리 위해)
            var sorted = regions.OrderByDescending(r => r.Bounds.Width * r.Bounds.Height).ToList();
            var merged = new List<SegmentedRegion>();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                sorted.RemoveAt(0);
                
                bool isMerged = false;

                // 이미 등록된 영역들과 비교
                for (int i = 0; i < merged.Count; i++)
                {
                    // 교집합 영역 계산
                    var intersect = WpfRect.Intersect(merged[i].Bounds, current.Bounds);
                    if (intersect.IsEmpty) continue;

                    double intersectArea = intersect.Width * intersect.Height;
                    double currentArea = current.Bounds.Width * current.Bounds.Height;
                    double existingArea = merged[i].Bounds.Width * merged[i].Bounds.Height;

                    // 겹치는 부분이 현재 영역의 70% 이상이면, 이미 있는 큰 영역에 포함된 것으로 간주하고 버림
                    if (intersectArea / currentArea > 0.7)
                    {
                        isMerged = true;
                        break;
                    }
                    
                    // 혹은 기존 영역이 현재 영역 안에 거의 다 포함되면, 기존 영역을 확장
                    if (intersectArea / existingArea > 0.9)
                    {
                        merged[i] = new SegmentedRegion(current.Type, WpfRect.Union(merged[i].Bounds, current.Bounds));
                        isMerged = true;
                        break;
                    }
                }

                if (!isMerged)
                {
                    merged.Add(current);
                }
            }

            return merged;
        }
    }
}