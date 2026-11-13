using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Các chế độ hoạt động của hệ thống cử chỉ Teddy (Hình 4 trang 3 bài báo SIGGRAPH '99).
    /// </summary>
    public enum TeddyGestureMode
    {
        Creation,   // Chưa có vật thể: Vẽ 1 nét khép kín để tạo mô hình 3D mới
        Painting,   // Đã có vật thể: Vẽ trên bề mặt, khoanh vùng đùn, gạch xóa hoặc cắt
        Extrusion,  // Đã có vòng chọn màu đỏ (Base Ring): Chờ nét đùn, gạch làm mượt hoặc click hủy
        Bending     // Chế độ uốn biến dạng 2 nét (Reference stroke -> Target stroke)
    }

    /// <summary>
    /// [TV3-01] Xử lý nét vẽ 2D (Uniform Resampling), kiểm tra tự cắt (Self-Intersection),
    /// tính bao lồi nhận diện cử chỉ gạch xóa (Scribbling: sl/pl > 1.5) theo Mục 4 & 5 bài báo Teddy.
    /// </summary>
    public static class StrokeProcessor
    {
        /// <summary>
        /// [Mục 5] Lấy mẫu lại nét vẽ thô từ chuột thành đa tuyến mượt có độ dài cạnh đều nhau (Uniform Edge Length).
        /// </summary>
        public static List<PointF> ResampleUniform(IReadOnlyList<PointF> rawStroke, float targetEdgeLength = 16f, bool closeLoop = false)
        {
            var result = new List<PointF>();
            if (rawStroke == null || rawStroke.Count == 0)
                return result;

            if (rawStroke.Count == 1)
            {
                result.Add(rawStroke[0]);
                return result;
            }

            // Tạo danh sách điểm nguồn (nếu đóng vòng thì nối thêm điểm đầu vào cuối)
            var pts = new List<PointF>(rawStroke.Count + 1);
            pts.Add(rawStroke[0]);
            for (int i = 1; i < rawStroke.Count; i++)
            {
                if (Distance(pts[pts.Count - 1], rawStroke[i]) > 1.5f)
                    pts.Add(rawStroke[i]);
            }

            if (pts.Count < 2)
                return pts;

            if (closeLoop && Distance(pts[pts.Count - 1], pts[0]) > 2.0f)
            {
                pts.Add(pts[0]);
            }

            result.Add(pts[0]);
            float accumulated = 0f;

            for (int i = 1; i < pts.Count; i++)
            {
                PointF p0 = pts[i - 1];
                PointF p1 = pts[i];
                float segLen = Distance(p0, p1);

                while (accumulated + segLen >= targetEdgeLength)
                {
                    float t = (targetEdgeLength - accumulated) / segLen;
                    PointF newPt = new PointF(
                        p0.X + t * (p1.X - p0.X),
                        p0.Y + t * (p1.Y - p0.Y));

                    result.Add(newPt);
                    p0 = newPt;
                    segLen = Distance(p0, p1);
                    accumulated = 0f;
                }

                accumulated += segLen;
            }

            // Nếu là đa giác khép kín, loại bỏ điểm cuối nếu quá sát điểm đầu để tránh cạnh suy biến
            if (closeLoop && result.Count > 3)
            {
                if (Distance(result[result.Count - 1], result[0]) < targetEdgeLength * 0.45f)
                {
                    result.RemoveAt(result.Count - 1);
                }
            }
            else if (!closeLoop && pts.Count >= 2)
            {
                PointF lastRaw = pts[pts.Count - 1];
                if (Distance(result[result.Count - 1], lastRaw) > 2.0f)
                    result.Add(lastRaw);
            }

            return result;
        }

        /// <summary>
        /// [Mục 4.3 - Chú thích 1 trang 4] Kiểm tra cử chỉ gạch xóa / làm mượt (Scribbling):
        /// Một nét vẽ được nhận diện là Scribbling khi sl / pl > 1.5,
        /// trong đó sl là tổng chiều dài nét vẽ và pl là chu vi bao lồi (Convex Hull perimeter) của nó.
        /// </summary>
        public static bool IsScribbling(IReadOnlyList<PointF> stroke, out float slPlRatio)
        {
            slPlRatio = 0f;
            if (stroke == null || stroke.Count < 6)
                return false;

            float strokeLength = ComputePolylineLength(stroke, false);
            if (strokeLength < 40f)
                return false;

            List<PointF> hull = ComputeConvexHull(stroke);
            float hullPerimeter = ComputePolylineLength(hull, true);
            if (hullPerimeter < 1e-3f)
                return false;

            slPlRatio = strokeLength / hullPerimeter;
            return slPlRatio > 1.5f;
        }

        /// <summary>
        /// Kiểm tra xem người dùng vẽ nét trên bề mặt có chủ ý khép kín (tạo Base Ring để Extrude) hay không.
        /// </summary>
        public static bool IsClosedSurfaceStroke(IReadOnlyList<PointF> stroke)
        {
            if (stroke == null || stroke.Count < 8)
                return false;

            float totalLen = ComputePolylineLength(stroke, false);
            if (totalLen < 50f)
                return false;

            float endToStart = Distance(stroke[0], stroke[stroke.Count - 1]);
            return endToStart <= MathF.Max(36f, totalLen * 0.22f);
        }

        /// <summary>
        /// [Mục 4.2 & 5.1] Kiểm tra đa giác 2D có tự cắt (Self-Intersecting) hay không.
        /// </summary>
        public static bool IsSelfIntersecting(IReadOnlyList<PointF> polygon, bool isClosed = true)
        {
            if (polygon == null || polygon.Count < 4)
                return false;

            int n = polygon.Count;
            int segCount = isClosed ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                PointF a1 = polygon[i];
                PointF a2 = polygon[(i + 1) % n];

                for (int j = i + 2; j < segCount; j++)
                {
                    // Bỏ qua 2 đoạn kề nhau (chung đỉnh đầu-cuối)
                    if (isClosed && i == 0 && j == segCount - 1)
                        continue;

                    PointF b1 = polygon[j];
                    PointF b2 = polygon[(j + 1) % n];

                    if (SegmentsIntersectProper(a1, a2, b1, b2))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tính tổng chiều dài chuỗi điểm 2D.
        /// </summary>
        public static float ComputePolylineLength(IReadOnlyList<PointF> pts, bool closed)
        {
            if (pts == null || pts.Count < 2) return 0f;
            float sum = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
                sum += Distance(pts[i], pts[i + 1]);
            if (closed && pts.Count > 2)
                sum += Distance(pts[pts.Count - 1], pts[0]);
            return sum;
        }

        /// <summary>
        /// Thuật toán Monotone Chain tìm Bao lồi (Convex Hull) của tập điểm 2D trong O(N log N).
        /// </summary>
        public static List<PointF> ComputeConvexHull(IReadOnlyList<PointF> points)
        {
            var sorted = new List<PointF>(points);
            sorted.Sort((a, b) =>
            {
                int cmp = a.X.CompareTo(b.X);
                return cmp != 0 ? cmp : a.Y.CompareTo(b.Y);
            });

            if (sorted.Count <= 2)
                return sorted;

            var hull = new List<PointF>(sorted.Count * 2);

            // Nửa dưới (Lower hull)
            foreach (var pt in sorted)
            {
                while (hull.Count >= 2 && Cross2D(hull[hull.Count - 2], hull[hull.Count - 1], pt) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pt);
            }

            // Nửa trên (Upper hull)
            int lowerCount = hull.Count + 1;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                var pt = sorted[i];
                while (hull.Count >= lowerCount && Cross2D(hull[hull.Count - 2], hull[hull.Count - 1], pt) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(pt);
            }

            if (hull.Count > 1)
                hull.RemoveAt(hull.Count - 1);

            return hull;
        }

        public static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static float Cross2D(PointF o, PointF a, PointF b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private static bool SegmentsIntersectProper(PointF a, PointF b, PointF c, PointF d)
        {
            float d1 = Cross2D(c, d, a);
            float d2 = Cross2D(c, d, b);
            float d3 = Cross2D(a, b, c);
            float d4 = Cross2D(a, b, d);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }
    }
}
