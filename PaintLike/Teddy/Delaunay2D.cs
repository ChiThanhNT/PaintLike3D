using System;
using System.Collections.Generic;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Tam giác phẳng 2D kết quả từ Constrained Delaunay Triangulation (CDT - Hình 13b).
    /// </summary>
    public class Tri2D
    {
        public int V0;
        public int V1;
        public int V2;
        public TriangleCategory Category;
        public int[] Neighbors = new int[] { -1, -1, -1 };

        public Tri2D(int v0, int v1, int v2)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
        }

        public bool HasEdge(int a, int b)
        {
            return (V0 == a && V1 == b) || (V1 == a && V0 == b) ||
                   (V1 == a && V2 == b) || (V2 == a && V1 == b) ||
                   (V2 == a && V0 == b) || (V0 == a && V2 == b);
        }

        public int GetOppositeVertex(int a, int b)
        {
            if (V0 != a && V0 != b) return V0;
            if (V1 != a && V1 != b) return V1;
            return V2;
        }

        public void ReplaceVertex(int oldV, int newV)
        {
            if (V0 == oldV) V0 = newV;
            else if (V1 == oldV) V1 = newV;
            else if (V2 == oldV) V2 = newV;
        }
    }

    /// <summary>
    /// [TV1-02] Thuật toán chia lưới tam giác Constrained Delaunay Triangulation (CDT - Mục 5.1, Hình 13b).
    /// Kết hợp Ear-Clipping bảo toàn biên đa giác và Lawson Edge-Flipping theo tiêu chuẩn đường tròn ngoại tiếp Delaunay,
    /// sau đó phân loại từng tam giác thành Terminal (T), Sleeve (S), và Junction (J).
    /// </summary>
    public static class Delaunay2D
    {
        public static List<Tri2D> TriangulatePolygonCDT(List<Vector2> polygon)
        {
            var triangles = new List<Tri2D>();
            int n = polygon.Count;
            if (n < 3) return triangles;

            // Đảm bảo đa giác ngược chiều kim đồng hồ (CCW) trong hệ trục 2D
            if (ComputeSignedArea(polygon) < 0f)
            {
                polygon.Reverse();
            }

            // Bước 1: Chia tam giác ban đầu bằng Ear-Clipping
            var activeIndices = new List<int>(n);
            for (int i = 0; i < n; i++) activeIndices.Add(i);

            int guard = n * n * 2;
            int idx = 0;

            while (activeIndices.Count > 3 && guard-- > 0)
            {
                int count = activeIndices.Count;
                int iPrev = activeIndices[(idx - 1 + count) % count];
                int iCurr = activeIndices[idx % count];
                int iNext = activeIndices[(idx + 1) % count];

                if (IsEar(polygon, activeIndices, iPrev, iCurr, iNext))
                {
                    triangles.Add(new Tri2D(iPrev, iCurr, iNext));
                    activeIndices.RemoveAt(idx % count);
                    idx = 0;
                }
                else
                {
                    idx++;
                    if (idx >= count)
                    {
                        // Fallback nếu có góc suy biến gần 180 độ: cắt tai lồi nhất
                        int bestEar = FindFallbackEar(polygon, activeIndices);
                        int bPrev = activeIndices[(bestEar - 1 + count) % count];
                        int bCurr = activeIndices[bestEar];
                        int bNext = activeIndices[(bestEar + 1) % count];
                        triangles.Add(new Tri2D(bPrev, bCurr, bNext));
                        activeIndices.RemoveAt(bestEar);
                        idx = 0;
                    }
                }
            }

            if (activeIndices.Count == 3)
            {
                triangles.Add(new Tri2D(activeIndices[0], activeIndices[1], activeIndices[2]));
            }

            // Bước 2: Tối ưu hóa Delaunay bằng Lawson Edge-Flipping trên toàn bộ các cạnh trong (Internal Edges)
            OptimizeDelaunayEdgeFlips(polygon, triangles, n);

            // Bước 3: Phân loại tam giác (Terminal / Sleeve / Junction) và xây dựng quan hệ kề
            ClassifyAndBuildNeighbors(triangles, n);

            return triangles;
        }

        public static bool IsBoundaryEdge(int a, int b, int boundaryVertexCount)
        {
            if (a >= boundaryVertexCount || b >= boundaryVertexCount)
                return false;
            int diff = Math.Abs(a - b);
            return diff == 1 || diff == boundaryVertexCount - 1;
        }

        public static void ClassifyAndBuildNeighbors(List<Tri2D> triangles, int boundaryVertexCount)
        {
            var edgeMap = new Dictionary<(int, int), (int TriIdx, int EdgeSlot)>();

            for (int tIdx = 0; tIdx < triangles.Count; tIdx++)
            {
                var tri = triangles[tIdx];
                tri.Neighbors[0] = -1;
                tri.Neighbors[1] = -1;
                tri.Neighbors[2] = -1;

                int extCount = 0;
                int[] v = new int[] { tri.V0, tri.V1, tri.V2 };

                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    if (IsBoundaryEdge(a, b, boundaryVertexCount))
                    {
                        extCount++;
                    }
                    else
                    {
                        var key = a < b ? (a, b) : (b, a);
                        if (edgeMap.TryGetValue(key, out var other))
                        {
                            tri.Neighbors[e] = other.TriIdx;
                            triangles[other.TriIdx].Neighbors[other.EdgeSlot] = tIdx;
                        }
                        else
                        {
                            edgeMap[key] = (tIdx, e);
                        }
                    }
                }

                tri.Category = extCount switch
                {
                    2 => TriangleCategory.Terminal,
                    1 => TriangleCategory.Sleeve,
                    _ => TriangleCategory.Junction
                };
            }
        }

        private static void OptimizeDelaunayEdgeFlips(List<Vector2> poly, List<Tri2D> triangles, int boundaryCount)
        {
            int maxPasses = triangles.Count * 3;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool flippedAny = false;

                for (int i = 0; i < triangles.Count; i++)
                {
                    for (int j = i + 1; j < triangles.Count; j++)
                    {
                        if (TryGetSharedInternalEdge(triangles[i], triangles[j], boundaryCount, out int a, out int b, out int c, out int d))
                        {
                            // Đảm bảo tứ giác (a, c, b, d) lồi và c, d nằm hai phía của cạnh mới
                            Vector2 pa = poly[a];
                            Vector2 pb = poly[b];
                            Vector2 pc = poly[c];
                            Vector2 pd = poly[d];

                            if (IsConvexQuad(pa, pb, pc, pd) && IsPointInCircumcircle(pa, pb, pc, pd))
                            {
                                // Lật cạnh chung từ (a, b) sang (c, d)
                                SetCcwTriangle(triangles[i], poly, c, d, a);
                                SetCcwTriangle(triangles[j], poly, c, b, d);
                                flippedAny = true;
                            }
                        }
                    }
                }

                if (!flippedAny) break;
            }
        }

        private static void SetCcwTriangle(Tri2D tri, List<Vector2> pts, int i0, int i1, int i2)
        {
            if (Cross2D(pts[i0], pts[i1], pts[i2]) < 0f)
            {
                int tmp = i1;
                i1 = i2;
                i2 = tmp;
            }
            tri.V0 = i0;
            tri.V1 = i1;
            tri.V2 = i2;
        }

        private static bool TryGetSharedInternalEdge(Tri2D t1, Tri2D t2, int boundaryCount, out int a, out int b, out int c, out int d)
        {
            a = b = c = d = -1;
            int[] v1 = new int[] { t1.V0, t1.V1, t1.V2 };
            int[] v2 = new int[] { t2.V0, t2.V1, t2.V2 };

            int sharedCount = 0;
            int s0 = -1, s1 = -1;

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (v1[i] == v2[j])
                    {
                        if (sharedCount == 0) s0 = v1[i];
                        else if (sharedCount == 1) s1 = v1[i];
                        sharedCount++;
                    }
                }
            }

            if (sharedCount != 2) return false;
            if (IsBoundaryEdge(s0, s1, boundaryCount)) return false;

            a = s0;
            b = s1;
            c = t1.GetOppositeVertex(a, b);
            d = t2.GetOppositeVertex(a, b);
            return true;
        }

        private static bool IsConvexQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Đoạn thẳng (c, d) phải cắt đoạn thẳng (a, b) ở phần trong
            float c1 = Cross2D(a, b, c);
            float c2 = Cross2D(a, b, d);
            float c3 = Cross2D(c, d, a);
            float c4 = Cross2D(c, d, b);
            return (c1 * c2 < -1e-5f) && (c3 * c4 < -1e-5f);
        }

        /// <summary>
        /// Kiểm tra điểm D có nằm bên trong đường tròn ngoại tiếp tam giác (A, B, C) hay không.
        /// </summary>
        private static bool IsPointInCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Đảm bảo (A, B, C) ngược chiều kim đồng hồ
            if (Cross2D(a, b, c) < 0f)
            {
                Vector2 tmp = b;
                b = c;
                c = tmp;
            }

            double ax = a.X - d.X;
            double ay = a.Y - d.Y;
            double bx = b.X - d.X;
            double by = b.Y - d.Y;
            double cx = c.X - d.X;
            double cy = c.Y - d.Y;

            double det =
                (ax * ax + ay * ay) * (bx * cy - cx * by) -
                (bx * bx + by * by) * (ax * cy - cx * ay) +
                (cx * cx + cy * cy) * (ax * by - bx * ay);

            return det > 1e-4;
        }

        private static bool IsEar(List<Vector2> poly, List<int> active, int iPrev, int iCurr, int iNext)
        {
            Vector2 a = poly[iPrev];
            Vector2 b = poly[iCurr];
            Vector2 c = poly[iNext];

            // Góc phải lồi (CCW > 0)
            if (Cross2D(a, b, c) <= 1e-5f)
                return false;

            // Không có đỉnh nào khác nằm trong tam giác (a, b, c)
            for (int i = 0; i < active.Count; i++)
            {
                int vIdx = active[i];
                if (vIdx == iPrev || vIdx == iCurr || vIdx == iNext)
                    continue;

                if (PointInTriangle(poly[vIdx], a, b, c))
                    return false;
            }

            return true;
        }

        private static int FindFallbackEar(List<Vector2> poly, List<int> active)
        {
            int bestIdx = 0;
            float bestCross = float.MinValue;
            int count = active.Count;

            for (int i = 0; i < count; i++)
            {
                Vector2 a = poly[active[(i - 1 + count) % count]];
                Vector2 b = poly[active[i]];
                Vector2 c = poly[active[(i + 1) % count]];
                float cross = Cross2D(a, b, c);
                if (cross > bestCross)
                {
                    bestCross = cross;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2D(a, b, p);
            float d2 = Cross2D(b, c, p);
            float d3 = Cross2D(c, a, p);

            bool hasNeg = (d1 < -1e-4f) || (d2 < -1e-4f) || (d3 < -1e-4f);
            bool hasPos = (d1 > 1e-4f) || (d2 > 1e-4f) || (d3 > 1e-4f);
            return !(hasNeg && hasPos);
        }

        public static float ComputeSignedArea(IReadOnlyList<Vector2> poly)
        {
            float sum = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = poly[i];
                Vector2 p1 = poly[(i + 1) % n];
                sum += (p0.X * p1.Y - p1.X * p0.Y);
            }
            return 0.5f * sum;
        }

        public static float SignedArea2(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>
        /// [TV1-06] Chọn đường chéo tối ưu Delaunay cho tứ giác lồi (a, b, c, d) theo thứ tự CCW:
        /// Trả về true nếu đường chéo (a, c) cho góc nhỏ nhất tốt hơn đường chéo (b, d).
        /// </summary>
        public static bool PreferDiagonalAC(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Kiểm tra tính hợp lệ CCW cho cả hai cách chia
            bool acValid = SignedArea2(a, b, c) > 1e-5f && SignedArea2(a, c, d) > 1e-5f;
            bool bdValid = SignedArea2(b, c, d) > 1e-5f && SignedArea2(b, d, a) > 1e-5f;

            if (acValid && !bdValid) return true;
            if (bdValid && !acValid) return false;
            if (!acValid && !bdValid) return true;

            float minCosAC = MathF.Max(MaxCosAngle(a, b, c), MaxCosAngle(a, c, d));
            float minCosBD = MathF.Max(MaxCosAngle(b, c, d), MaxCosAngle(b, d, a));
            // Cos càng nhỏ -> góc lớn nhất càng nhỏ -> góc nhỏ nhất càng lớn (tối ưu Delaunay)
            return minCosAC <= minCosBD;
        }

        private static float MaxCosAngle(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            float c0 = AngleCos(p0, p1, p2);
            float c1 = AngleCos(p1, p2, p0);
            float c2 = AngleCos(p2, p0, p1);
            return MathF.Max(c0, MathF.Max(c1, c2));
        }

        private static float AngleCos(Vector2 v, Vector2 a, Vector2 b)
        {
            Vector2 da = a - v;
            Vector2 db = b - v;
            float la = da.Length();
            float lb = db.Length();
            if (la < 1e-6f || lb < 1e-6f) return 1f;
            return Math.Clamp(Vector2.Dot(da, db) / (la * lb), -1f, 1f);
        }

        private static float Cross2D(Vector2 o, Vector2 a, Vector2 b)
        {
            return SignedArea2(o, a, b);
        }
    }
}
