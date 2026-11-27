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
    /// [TV1-08] Kết quả chia lưới 2D chất lượng cao Steiner-CDT (Shewchuk [22] / Ruppert Delaunay Refinement).
    /// Bảo toàn nguyên vẹn chỉ số đỉnh biên gốc 0..(BoundaryVertexCount - 1) khi allowBoundarySplit = false,
    /// đồng thời chèn thêm điểm Steiner tâm ngoại tiếp để nâng góc nhỏ nhất lên > 18°.
    /// </summary>
    public class SteinerCdtResult
    {
        public List<Vector2> Vertices { get; set; } = new List<Vector2>();
        public List<Tri2D> Triangles { get; set; } = new List<Tri2D>();
        public int BoundaryVertexCount { get; set; }
        public HashSet<(int A, int B)> ConstrainedEdges { get; set; } = new HashSet<(int A, int B)>();
        public int InsertedSteinerCount => Math.Max(0, Vertices.Count - BoundaryVertexCount);
        public float MinAngleDegrees { get; set; }
        public float AverageMinAngleDegrees { get; set; }
        public float MaxEdgeLength { get; set; }
    }

    /// <summary>
    /// [TV1-02, TV1-06 & TV1-08] Thuật toán chia lưới tam giác Constrained Delaunay Triangulation (CDT - Mục 5.1, Hình 13b)
    /// và Bộ chia lưới 2D chất lượng cao Steiner-CDT (Shewchuk [22] - Mục 5.4 & 5.5).
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

        // =========================================================================
        // [TV1-08] BỘ CHIA LƯỚI 2D STEINER-CDT CHẤT LƯỢNG CAO (SHEWCHUK [22])
        // =========================================================================

        /// <summary>
        /// [TV1-08] Chia lưới tam giác 2D chất lượng cao cho đa giác lõm bất kỳ theo thuật toán Shewchuk [22] / Ruppert:
        /// - Khởi tạo CDT (Ear-Clipping + Constrained Lawson Flips) bảo toàn 100% các cạnh biên bị ràng buộc.
        /// - Chèn điểm Steiner tại tâm ngoại tiếp (Circumcenter) / Off-center của tam giác xấu nhất (góc < minAngleDegrees hoặc cạnh > maxEdgeLength).
        /// - Khôi phục tính chất Delaunay cục bộ bằng Lawson Edge-Flipping (có bảo vệ mọi cạnh biên ConstrainedEdges).
        /// - Tùy chọn allowBoundarySplit: false để giữ cố định 1:1 đỉnh biên (dùng cho nắp mặt cắt TV2-08 & đắp lỗ Bezier TV2-09),
        ///   hoặc true để chia đôi cạnh biên bị xâm lấn đường tròn đường kính (Ruppert thuần túy).
        /// - Giữ nguyên thứ tự chỉ số đỉnh biên 0..(N-1) khớp chính xác với danh sách đầu vào kể cả khi đa giác đầu vào cùng chiều kim đồng hồ.
        /// </summary>
        public static SteinerCdtResult TriangulateQualitySteinerCDT(
            IReadOnlyList<Vector2> boundaryPolygon,
            float minAngleDegrees = 21.0f,
            float maxEdgeLength = 0f,
            bool allowBoundarySplit = false,
            int maxSteinerPoints = 320)
        {
            var result = new SteinerCdtResult();
            if (boundaryPolygon == null || boundaryPolygon.Count < 3)
                return result;

            int n = boundaryPolygon.Count;
            var vertices = new List<Vector2>(n + Math.Min(maxSteinerPoints, 256));
            for (int i = 0; i < n; i++)
                vertices.Add(boundaryPolygon[i]);

            bool inputIsCw = ComputeSignedArea(vertices) < 0f;

            // Giữ nguyên chỉ số gốc 0..(n-1) của người gọi; chỉ đảo thứ tự duyệt vòng Ear-Clipping nếu đầu vào CW
            var activeIndices = new List<int>(n);
            if (!inputIsCw)
            {
                for (int i = 0; i < n; i++) activeIndices.Add(i);
            }
            else
            {
                for (int i = n - 1; i >= 0; i--) activeIndices.Add(i);
            }

            var constrainedEdges = new HashSet<(int A, int B)>();
            float sumBoundaryLen = 0f;
            float minBoundaryLen = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                constrainedEdges.Add(MakeEdgeKey(i, next));
                float edgeLen = Vector2.Distance(vertices[i], vertices[next]);
                sumBoundaryLen += edgeLen;
                if (edgeLen > 1e-5f && edgeLen < minBoundaryLen) minBoundaryLen = edgeLen;
            }
            if (minBoundaryLen == float.MaxValue) minBoundaryLen = 1f;
            float avgBoundaryLen = sumBoundaryLen / n;

            // Bước 1: Chia lưới ban đầu bằng Ear-Clipping bảo toàn đa giác lõm
            var triangles = new List<Tri2D>(Math.Max(n, 4));
            int guard = n * n * 2;
            int idx = 0;
            while (activeIndices.Count > 3 && guard-- > 0)
            {
                int count = activeIndices.Count;
                int iPrev = activeIndices[(idx - 1 + count) % count];
                int iCurr = activeIndices[idx % count];
                int iNext = activeIndices[(idx + 1) % count];

                if (IsEar(vertices, activeIndices, iPrev, iCurr, iNext))
                {
                    triangles.Add(CreateCcwTri(vertices, iPrev, iCurr, iNext));
                    activeIndices.RemoveAt(idx % count);
                    idx = 0;
                }
                else
                {
                    idx++;
                    if (idx >= count)
                    {
                        int bestEar = FindFallbackEar(vertices, activeIndices);
                        int bPrev = activeIndices[(bestEar - 1 + count) % count];
                        int bCurr = activeIndices[bestEar];
                        int bNext = activeIndices[(bestEar + 1) % count];
                        triangles.Add(CreateCcwTri(vertices, bPrev, bCurr, bNext));
                        activeIndices.RemoveAt(bestEar);
                        idx = 0;
                    }
                }
            }
            if (activeIndices.Count == 3)
            {
                triangles.Add(CreateCcwTri(vertices, activeIndices[0], activeIndices[1], activeIndices[2]));
            }

            // Bước 2: Xây quan hệ kề & tối ưu CDT ban đầu bằng Constrained Lawson Edge-Flipping
            RebuildNeighborsConstrained(triangles, constrainedEdges);
            RestoreDelaunayAll(vertices, triangles, constrainedEdges);

            // Bước 3: Tinh chỉnh lưới chất lượng cao bằng chèn điểm Steiner tâm ngoại tiếp (Shewchuk [22])
            float clampedMinAngle = Math.Clamp(minAngleDegrees, 5f, 30f);
            float targetMaxEdge = maxEdgeLength > 1e-3f ? maxEdgeLength : MathF.Max(avgBoundaryLen * 1.75f, minBoundaryLen * 2.2f);
            float minSteinerSpacing = MathF.Max(1e-3f, MathF.Min(minBoundaryLen * 0.42f, targetMaxEdge * 0.18f));
            float minBoundaryClearance = allowBoundarySplit ? minSteinerSpacing * 0.45f : minSteinerSpacing * 0.48f;
            float targetRatioB = 1.0f / (2.0f * MathF.Sin(clampedMinAngle * MathF.PI / 180f));

            // Ghi nhận các góc nhọn sẵn có trên biên đa giác đầu vào (để tránh lặp vô hạn khi bản thân góc đầu vào < minAngle)
            var acuteInputBoundaryVertices = FindAcuteBoundaryVertices(vertices, n, clampedMinAngle);

            var skippedBadTris = new HashSet<(int, int, int)>();
            int inserted = 0;
            int safetyIterations = Math.Max(maxSteinerPoints * 4, 64);

            while (inserted < maxSteinerPoints && safetyIterations-- > 0)
            {
                // Ưu tiên 1 (khi allowBoundarySplit = true): Chia đôi cạnh biên bị xâm lấn đường tròn đường kính (Ruppert Encroachment)
                if (allowBoundarySplit && TryFindEncroachedConstrainedEdge(
                    vertices, triangles, constrainedEdges, minSteinerSpacing * 2.0f,
                    out int encTriIdx, out int encEdgeSlot, out int encA, out int encB))
                {
                    if (SplitConstrainedSegment(vertices, triangles, constrainedEdges, encTriIdx, encEdgeSlot, encA, encB))
                    {
                        inserted++;
                        skippedBadTris.Clear();
                        continue;
                    }
                }

                // Ưu tiên 2: Tìm tam giác xấu nhất cần tinh chỉnh (góc < clampedMinAngle hoặc cạnh dài > targetMaxEdge)
                int worstIdx = -1;
                float worstScore = 0f;
                Vector2 worstCircumcenter = Vector2.Zero;
                float worstMinEdge = 0f;
                int worstShortA = -1, worstShortB = -1;

                for (int t = 0; t < triangles.Count; t++)
                {
                    var tri = triangles[t];
                    var triSig = MakeTriSignature(tri);
                    if (skippedBadTris.Contains(triSig)) continue;

                    EvaluateTriangleQuality(
                        vertices[tri.V0], vertices[tri.V1], vertices[tri.V2],
                        out float minAngDeg, out float minEdgeLen, out float maxEdgeLen,
                        out Vector2 cc, out float circumRadius, out int shortSlot);

                    if (maxEdgeLen <= minSteinerSpacing * 1.45f)
                        continue;

                    // Nếu tam giác kẹp ngay đỉnh góc nhọn sẵn của biên đầu vào và đã đủ nhỏ -> không ép chia vô ích
                    if (IsProtectedAcuteCornerTri(tri, constrainedEdges, acuteInputBoundaryVertices, maxEdgeLen, targetMaxEdge))
                        continue;

                    bool failsAngle = minAngDeg < clampedMinAngle;
                    bool failsSize = maxEdgeLen > targetMaxEdge;
                    if (!failsAngle && !failsSize)
                        continue;

                    // Điểm ưu tiên: tam giác có tỉ lệ R / L_min lớn nhất (góc nhọn nhất) hoặc kích thước lớn nhất
                    float angleSeverity = failsAngle ? (clampedMinAngle - minAngDeg) * 2.5f + (circumRadius / MathF.Max(minEdgeLen, 1e-5f)) : 0f;
                    float sizeSeverity = failsSize ? (maxEdgeLen / targetMaxEdge) * 1.8f : 0f;
                    float score = angleSeverity + sizeSeverity;

                    if (score > worstScore)
                    {
                        worstScore = score;
                        worstIdx = t;
                        worstCircumcenter = cc;
                        worstMinEdge = minEdgeLen;
                        int[] tv = { tri.V0, tri.V1, tri.V2 };
                        worstShortA = tv[shortSlot];
                        worstShortB = tv[(shortSlot + 1) % 3];
                    }
                }

                if (worstIdx < 0)
                    break;

                Tri2D badTri = triangles[worstIdx];
                Vector2 candPt = worstCircumcenter;

                // Kiểm tra xem tâm ngoại tiếp có xâm lấn cạnh biên bị ràng buộc không (Diametral circle test)
                if (TryFindEdgeEncroachedByPoint(vertices, constrainedEdges, candPt, out int encroachedA, out int encroachedB))
                {
                    float encLen = Vector2.Distance(vertices[encroachedA], vertices[encroachedB]);
                    if (allowBoundarySplit && encLen > minSteinerSpacing * 2.0f &&
                        TryLocateConstrainedEdgeTriangle(triangles, encroachedA, encroachedB, out int splitT, out int splitSlot))
                    {
                        if (SplitConstrainedSegment(vertices, triangles, constrainedEdges, splitT, splitSlot, encroachedA, encroachedB))
                        {
                            inserted++;
                            skippedBadTris.Clear();
                            continue;
                        }
                    }

                    // Không chia biên (hoặc cạnh biên đã nhỏ): thử điểm Off-center (Üngör/Shewchuk) rồi fallback về trọng tâm tam giác
                    candPt = ComputeSafeInteriorSteinerCandidate(
                        vertices, badTri, worstShortA, worstShortB, worstMinEdge, targetRatioB,
                        triangles, constrainedEdges, minSteinerSpacing, minBoundaryClearance, out bool foundSafe);
                    if (!foundSafe)
                    {
                        skippedBadTris.Add(MakeTriSignature(badTri));
                        continue;
                    }
                }
                else
                {
                    // Kiểm tra tâm ngoại tiếp có nằm trong đa giác lõm và đủ khoảng cách an toàn không
                    int locTri = FindContainingTriangle(vertices, triangles, candPt);
                    if (locTri < 0 || !IsValidSteinerPlacement(vertices, constrainedEdges, candPt, minSteinerSpacing, minBoundaryClearance))
                    {
                        candPt = ComputeSafeInteriorSteinerCandidate(
                            vertices, badTri, worstShortA, worstShortB, worstMinEdge, targetRatioB,
                            triangles, constrainedEdges, minSteinerSpacing, minBoundaryClearance, out bool foundSafe);
                        if (!foundSafe)
                        {
                            skippedBadTris.Add(MakeTriSignature(badTri));
                            continue;
                        }
                    }
                }

                // Chèn điểm Steiner vào tam giác chứa nó (hoặc cạnh trong nếu nằm đúng trên cạnh chia sẻ) + Lawson flips cục bộ
                if (InsertInteriorSteinerPoint(vertices, triangles, constrainedEdges, candPt))
                {
                    inserted++;
                    skippedBadTris.Clear();
                }
                else
                {
                    skippedBadTris.Add(MakeTriSignature(badTri));
                }
            }

            // Bước 4: Làm mượt nhẹ các đỉnh Steiner bên trong (chỉ chấp nhận dịch chuyển cải thiện góc nhỏ nhất và không đảo chiều tam giác)
            RelaxInteriorSteinerVertices(vertices, triangles, n, constrainedEdges, iterations: 3);
            RestoreDelaunayAll(vertices, triangles, constrainedEdges);

            // Bước 5: Tính thống kê chất lượng lưới đầu ra
            ComputeMeshQualityStats(vertices, triangles, out float minAng, out float avgMinAng, out float maxEdge);

            result.Vertices = vertices;
            result.Triangles = triangles;
            result.BoundaryVertexCount = n;
            result.ConstrainedEdges = constrainedEdges;
            result.MinAngleDegrees = minAng;
            result.AverageMinAngleDegrees = avgMinAng;
            result.MaxEdgeLength = maxEdge;
            return result;
        }

        /// <summary>
        /// Tính thống kê chất lượng tam giác 2D (góc nhỏ nhất toàn lưới, trung bình góc nhỏ nhất từng tam giác, cạnh dài nhất).
        /// </summary>
        public static void ComputeMeshQualityStats(
            IReadOnlyList<Vector2> vertices,
            IReadOnlyList<Tri2D> triangles,
            out float minAngleDegrees,
            out float avgMinAngleDegrees,
            out float maxEdgeLength)
        {
            minAngleDegrees = 180f;
            avgMinAngleDegrees = 0f;
            maxEdgeLength = 0f;
            if (triangles == null || triangles.Count == 0)
            {
                minAngleDegrees = 0f;
                return;
            }

            float sumMin = 0f;
            foreach (var tri in triangles)
            {
                EvaluateTriangleQuality(
                    vertices[tri.V0], vertices[tri.V1], vertices[tri.V2],
                    out float triMinAng, out _, out float triMaxEdge, out _, out _, out _);
                if (triMinAng < minAngleDegrees) minAngleDegrees = triMinAng;
                if (triMaxEdge > maxEdgeLength) maxEdgeLength = triMaxEdge;
                sumMin += triMinAng;
            }
            avgMinAngleDegrees = sumMin / triangles.Count;
        }

        private static (int A, int B) MakeEdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        private static (int, int, int) MakeTriSignature(Tri2D tri)
        {
            int a = tri.V0, b = tri.V1, c = tri.V2;
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        private static Tri2D CreateCcwTri(List<Vector2> pts, int i0, int i1, int i2)
        {
            var t = new Tri2D(i0, i1, i2);
            SetCcwTriangle(t, pts, i0, i1, i2);
            return t;
        }

        private static void RebuildNeighborsConstrained(List<Tri2D> triangles, HashSet<(int A, int B)> constrainedEdges)
        {
            var edgeMap = new Dictionary<(int, int), (int TriIdx, int EdgeSlot)>(triangles.Count * 2);
            for (int tIdx = 0; tIdx < triangles.Count; tIdx++)
            {
                var tri = triangles[tIdx];
                tri.Neighbors[0] = -1;
                tri.Neighbors[1] = -1;
                tri.Neighbors[2] = -1;

                int extCount = 0;
                int[] v = { tri.V0, tri.V1, tri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    var key = MakeEdgeKey(v[e], v[(e + 1) % 3]);
                    if (constrainedEdges.Contains(key))
                    {
                        extCount++;
                    }
                    else if (edgeMap.TryGetValue(key, out var other))
                    {
                        tri.Neighbors[e] = other.TriIdx;
                        triangles[other.TriIdx].Neighbors[other.EdgeSlot] = tIdx;
                    }
                    else
                    {
                        edgeMap[key] = (tIdx, e);
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

        private static void RestoreDelaunayAll(List<Vector2> pts, List<Tri2D> triangles, HashSet<(int A, int B)> constrainedEdges)
        {
            var queue = new Queue<(int TriIdx, int EdgeSlot)>(triangles.Count * 2);
            for (int t = 0; t < triangles.Count; t++)
            {
                for (int e = 0; e < 3; e++)
                {
                    if (triangles[t].Neighbors[e] > t)
                        queue.Enqueue((t, e));
                }
            }
            LegalizeEdgeQueue(pts, triangles, constrainedEdges, queue);
        }

        private static void LegalizeEdgeQueue(
            List<Vector2> pts,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            Queue<(int TriIdx, int EdgeSlot)> queue)
        {
            int maxFlips = Math.Max(triangles.Count * 8, 256);
            while (queue.Count > 0 && maxFlips-- > 0)
            {
                var (t1Idx, e1) = queue.Dequeue();
                if (t1Idx < 0 || t1Idx >= triangles.Count) continue;

                Tri2D t1 = triangles[t1Idx];
                int t2Idx = t1.Neighbors[e1];
                if (t2Idx < 0 || t2Idx >= triangles.Count) continue;

                int[] v1 = { t1.V0, t1.V1, t1.V2 };
                int a = v1[e1];
                int b = v1[(e1 + 1) % 3];
                if (constrainedEdges.Contains(MakeEdgeKey(a, b))) continue;

                Tri2D t2 = triangles[t2Idx];
                int e2 = FindEdgeSlot(t2, a, b);
                if (e2 < 0) continue;

                int c = v1[(e1 + 2) % 3];
                int[] v2 = { t2.V0, t2.V1, t2.V2 };
                int d = v2[(e2 + 2) % 3];
                if (c == d) continue;

                Vector2 pa = pts[a], pb = pts[b], pc = pts[c], pd = pts[d];
                if (!IsConvexQuad(pa, pb, pc, pd)) continue;
                if (!IsPointInCircumcircle(pa, pb, pc, pd)) continue;

                // Lưu 4 láng giềng ngoài của tứ giác (a, c, b, d) trước khi lật cạnh (a, b) -> (c, d)
                // t1 CCW: (a, b, c) => edge0=(a,b)[e1], edge1=(b,c)[(e1+1)%3], edge2=(c,a)[(e1+2)%3]
                // t2 CCW: (b, a, d) => edge0=(b,a)[e2], edge1=(a,d)[(e2+1)%3], edge2=(d,b)[(e2+2)%3]
                int nBC = t1.Neighbors[(e1 + 1) % 3];
                int nCA = t1.Neighbors[(e1 + 2) % 3];
                int nAD = t2.Neighbors[(e2 + 1) % 3];
                int nDB = t2.Neighbors[(e2 + 2) % 3];

                // Đảm bảo t1 = (c, a, d) và t2 = (d, b, c) đều CCW
                if (SignedArea2(pc, pa, pd) <= 1e-6f || SignedArea2(pd, pb, pc) <= 1e-6f)
                    continue;

                t1.V0 = c; t1.V1 = a; t1.V2 = d;
                t1.Neighbors[0] = nCA;
                t1.Neighbors[1] = nAD;
                t1.Neighbors[2] = t2Idx;

                t2.V0 = d; t2.V1 = b; t2.V2 = c;
                t2.Neighbors[0] = nDB;
                t2.Neighbors[1] = nBC;
                t2.Neighbors[2] = t1Idx;

                ReplaceNeighborRef(triangles, nCA, t1Idx, t1Idx);
                ReplaceNeighborRef(triangles, nAD, t2Idx, t1Idx);
                ReplaceNeighborRef(triangles, nDB, t2Idx, t2Idx);
                ReplaceNeighborRef(triangles, nBC, t1Idx, t2Idx);

                // Đưa 4 cạnh ngoài của tứ giác vừa lật vào hàng đợi kiểm tra tiếp
                if (nCA >= 0) queue.Enqueue((t1Idx, 0));
                if (nAD >= 0) queue.Enqueue((t1Idx, 1));
                if (nDB >= 0) queue.Enqueue((t2Idx, 0));
                if (nBC >= 0) queue.Enqueue((t2Idx, 1));
            }
        }

        private static int FindEdgeSlot(Tri2D tri, int a, int b)
        {
            if ((tri.V0 == a && tri.V1 == b) || (tri.V0 == b && tri.V1 == a)) return 0;
            if ((tri.V1 == a && tri.V2 == b) || (tri.V1 == b && tri.V2 == a)) return 1;
            if ((tri.V2 == a && tri.V0 == b) || (tri.V2 == b && tri.V0 == a)) return 2;
            return -1;
        }

        private static void ReplaceNeighborRef(List<Tri2D> triangles, int nbTriIdx, int oldRef, int newRef)
        {
            if (nbTriIdx < 0 || nbTriIdx >= triangles.Count || oldRef == newRef) return;
            var nb = triangles[nbTriIdx];
            for (int e = 0; e < 3; e++)
            {
                if (nb.Neighbors[e] == oldRef)
                {
                    nb.Neighbors[e] = newRef;
                    return;
                }
            }
        }

        private static void EvaluateTriangleQuality(
            Vector2 a, Vector2 b, Vector2 c,
            out float minAngleDeg,
            out float minEdgeLen,
            out float maxEdgeLen,
            out Vector2 circumcenter,
            out float circumRadius,
            out int shortestEdgeSlot)
        {
            float l0 = Vector2.Distance(a, b);
            float l1 = Vector2.Distance(b, c);
            float l2 = Vector2.Distance(c, a);

            minEdgeLen = l0;
            shortestEdgeSlot = 0;
            if (l1 < minEdgeLen) { minEdgeLen = l1; shortestEdgeSlot = 1; }
            if (l2 < minEdgeLen) { minEdgeLen = l2; shortestEdgeSlot = 2; }
            maxEdgeLen = MathF.Max(l0, MathF.Max(l1, l2));

            float angA = MathF.Acos(AngleCos(a, b, c)) * (180f / MathF.PI);
            float angB = MathF.Acos(AngleCos(b, c, a)) * (180f / MathF.PI);
            float angC = MathF.Acos(AngleCos(c, a, b)) * (180f / MathF.PI);
            minAngleDeg = MathF.Min(angA, MathF.Min(angB, angC));

            // Tính tâm và bán kính đường tròn ngoại tiếp bằng double precision
            double ax = a.X, ay = a.Y;
            double bx = b.X, by = b.Y;
            double cx = c.X, cy = c.Y;
            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-9)
            {
                circumcenter = (a + b + c) / 3f;
                circumRadius = maxEdgeLen * 100f;
                return;
            }

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;

            circumcenter = new Vector2((float)ux, (float)uy);
            circumRadius = Vector2.Distance(circumcenter, a);
        }

        private static HashSet<int> FindAcuteBoundaryVertices(List<Vector2> vertices, int boundaryCount, float minAngleDeg)
        {
            var acute = new HashSet<int>();
            for (int i = 0; i < boundaryCount; i++)
            {
                Vector2 prev = vertices[(i - 1 + boundaryCount) % boundaryCount];
                Vector2 curr = vertices[i];
                Vector2 next = vertices[(i + 1) % boundaryCount];
                float ang = MathF.Acos(AngleCos(curr, prev, next)) * (180f / MathF.PI);
                if (ang < minAngleDeg + 2.0f)
                {
                    acute.Add(i);
                }
            }
            return acute;
        }

        private static bool IsProtectedAcuteCornerTri(
            Tri2D tri,
            HashSet<(int A, int B)> constrainedEdges,
            HashSet<int> acuteInputVerts,
            float maxEdgeLen,
            float targetMaxEdge)
        {
            if (maxEdgeLen > targetMaxEdge * 1.15f) return false;
            int[] v = { tri.V0, tri.V1, tri.V2 };
            for (int i = 0; i < 3; i++)
            {
                if (!acuteInputVerts.Contains(v[i])) continue;
                int vPrev = v[(i + 2) % 3];
                int vNext = v[(i + 1) % 3];
                if (constrainedEdges.Contains(MakeEdgeKey(v[i], vPrev)) &&
                    constrainedEdges.Contains(MakeEdgeKey(v[i], vNext)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindEncroachedConstrainedEdge(
            List<Vector2> vertices,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            float minSplitLen,
            out int triIdx,
            out int edgeSlot,
            out int edgeA,
            out int edgeB)
        {
            triIdx = edgeSlot = edgeA = edgeB = -1;
            float bestLen = minSplitLen;

            for (int t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                int[] v = { tri.V0, tri.V1, tri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    if (!constrainedEdges.Contains(MakeEdgeKey(a, b))) continue;

                    Vector2 pa = vertices[a];
                    Vector2 pb = vertices[b];
                    float len = Vector2.Distance(pa, pb);
                    if (len <= bestLen) continue;

                    // Đỉnh đối diện nằm trong đường tròn đường kính (a, b) <=> góc tại đỉnh đối diện > 90° <=> Dot(pa-pc, pb-pc) < 0
                    Vector2 pc = vertices[v[(e + 2) % 3]];
                    if (Vector2.Dot(pa - pc, pb - pc) < -1e-3f * len * len)
                    {
                        bestLen = len;
                        triIdx = t;
                        edgeSlot = e;
                        edgeA = a;
                        edgeB = b;
                    }
                }
            }
            return triIdx >= 0;
        }

        private static bool TryFindEdgeEncroachedByPoint(
            List<Vector2> vertices,
            HashSet<(int A, int B)> constrainedEdges,
            Vector2 pt,
            out int edgeA,
            out int edgeB)
        {
            edgeA = edgeB = -1;
            float worstMargin = 0f;
            foreach (var edge in constrainedEdges)
            {
                Vector2 pa = vertices[edge.A];
                Vector2 pb = vertices[edge.B];
                float dot = Vector2.Dot(pa - pt, pb - pt);
                if (dot < -1e-4f)
                {
                    float margin = -dot;
                    if (margin > worstMargin)
                    {
                        worstMargin = margin;
                        edgeA = edge.A;
                        edgeB = edge.B;
                    }
                }
            }
            return edgeA >= 0;
        }

        private static bool TryLocateConstrainedEdgeTriangle(List<Tri2D> triangles, int a, int b, out int triIdx, out int edgeSlot)
        {
            for (int t = 0; t < triangles.Count; t++)
            {
                int slot = FindEdgeSlot(triangles[t], a, b);
                if (slot >= 0)
                {
                    triIdx = t;
                    edgeSlot = slot;
                    return true;
                }
            }
            triIdx = edgeSlot = -1;
            return false;
        }

        private static bool SplitConstrainedSegment(
            List<Vector2> vertices,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            int triIdx,
            int edgeSlot,
            int a,
            int b)
        {
            Tri2D t1 = triangles[triIdx];
            int[] v = { t1.V0, t1.V1, t1.V2 };
            int va = v[edgeSlot];
            int vb = v[(edgeSlot + 1) % 3];
            int vc = v[(edgeSlot + 2) % 3];
            int nBC = t1.Neighbors[(edgeSlot + 1) % 3];
            int nCA = t1.Neighbors[(edgeSlot + 2) % 3];

            Vector2 mid = 0.5f * (vertices[va] + vertices[vb]);
            int midIdx = vertices.Count;
            vertices.Add(mid);

            constrainedEdges.Remove(MakeEdgeKey(a, b));
            constrainedEdges.Add(MakeEdgeKey(va, midIdx));
            constrainedEdges.Add(MakeEdgeKey(midIdx, vb));

            // Tách t1 = (va, vb, vc) thành t1 = (va, midIdx, vc) và tNew = (midIdx, vb, vc)
            int newTriIdx = triangles.Count;
            t1.V0 = va; t1.V1 = midIdx; t1.V2 = vc;
            t1.Neighbors[0] = -1;
            t1.Neighbors[1] = newTriIdx;
            t1.Neighbors[2] = nCA;

            var tNew = new Tri2D(midIdx, vb, vc);
            tNew.Neighbors[0] = -1;
            tNew.Neighbors[1] = nBC;
            tNew.Neighbors[2] = triIdx;
            triangles.Add(tNew);

            ReplaceNeighborRef(triangles, nBC, triIdx, newTriIdx);

            var queue = new Queue<(int, int)>();
            if (nCA >= 0) queue.Enqueue((triIdx, 2));
            if (nBC >= 0) queue.Enqueue((newTriIdx, 1));
            LegalizeEdgeQueue(vertices, triangles, constrainedEdges, queue);
            return true;
        }

        private static Vector2 ComputeSafeInteriorSteinerCandidate(
            List<Vector2> vertices,
            Tri2D badTri,
            int shortA,
            int shortB,
            float shortLen,
            float targetRatioB,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            float minSpacing,
            float minBoundaryClearance,
            out bool foundSafe)
        {
            // Ứng viên 1: Điểm Off-center (Üngör / Shewchuk) nằm trên trung trực cạnh ngắn nhất, dịch vào trong tam giác
            Vector2 pA = vertices[shortA];
            Vector2 pB = vertices[shortB];
            int oppV = badTri.GetOppositeVertex(shortA, shortB);
            Vector2 pOpp = vertices[oppV];
            Vector2 midShort = 0.5f * (pA + pB);
            Vector2 edgeDir = Vector2.Normalize(pB - pA);
            Vector2 perp = new Vector2(-edgeDir.Y, edgeDir.X);
            if (Vector2.Dot(perp, pOpp - midShort) < 0f) perp = -perp;

            float beta = MathF.Sqrt(MathF.Max(0.25f, targetRatioB * targetRatioB - 0.25f));
            Vector2 offCenter = midShort + perp * (beta * shortLen);
            Vector2 centroid = (vertices[badTri.V0] + vertices[badTri.V1] + vertices[badTri.V2]) / 3f;
            Vector2 midOpp = 0.5f * (midShort + pOpp);

            var candidates = new Vector2[]
            {
                offCenter,
                Vector2.Lerp(centroid, offCenter, 0.5f),
                centroid,
                midOpp
            };

            foreach (var c in candidates)
            {
                if (FindContainingTriangle(vertices, triangles, c) >= 0 &&
                    IsValidSteinerPlacement(vertices, constrainedEdges, c, minSpacing, minBoundaryClearance))
                {
                    foundSafe = true;
                    return c;
                }
            }

            // Fallback cuối cùng: trọng tâm tam giác với ngưỡng lỏng hơn để vẫn chia được tam giác rất lớn
            if (FindContainingTriangle(vertices, triangles, centroid) >= 0 &&
                IsValidSteinerPlacement(vertices, constrainedEdges, centroid, minSpacing * 0.65f, minBoundaryClearance * 0.55f))
            {
                foundSafe = true;
                return centroid;
            }

            foundSafe = false;
            return centroid;
        }

        private static bool IsValidSteinerPlacement(
            List<Vector2> vertices,
            HashSet<(int A, int B)> constrainedEdges,
            Vector2 pt,
            float minVertexDist,
            float minBoundaryEdgeDist)
        {
            float minV2 = minVertexDist * minVertexDist;
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Vector2.DistanceSquared(vertices[i], pt) < minV2)
                    return false;
            }

            float minE2 = minBoundaryEdgeDist * minBoundaryEdgeDist;
            foreach (var edge in constrainedEdges)
            {
                if (DistanceSqPointToSegment(pt, vertices[edge.A], vertices[edge.B]) < minE2)
                    return false;
            }

            return true;
        }

        private static int FindContainingTriangle(List<Vector2> vertices, List<Tri2D> triangles, Vector2 pt)
        {
            for (int t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                if (PointInTriangle(pt, vertices[tri.V0], vertices[tri.V1], vertices[tri.V2]))
                    return t;
            }
            return -1;
        }

        private static bool InsertInteriorSteinerPoint(
            List<Vector2> vertices,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            Vector2 pt)
        {
            int locIdx = FindContainingTriangle(vertices, triangles, pt);
            if (locIdx < 0) return false;

            Tri2D t0 = triangles[locIdx];
            int[] v = { t0.V0, t0.V1, t0.V2 };
            Vector2 p0 = vertices[v[0]], p1 = vertices[v[1]], p2 = vertices[v[2]];

            // Kiểm tra điểm có nằm sát ngay trên một cạnh trong chia sẻ giữa 2 tam giác không
            float triArea = MathF.Abs(SignedArea2(p0, p1, p2));
            if (triArea < 1e-6f) return false;

            int onEdgeSlot = -1;
            float bestEdgeDistSq = float.MaxValue;
            for (int e = 0; e < 3; e++)
            {
                Vector2 ea = vertices[v[e]];
                Vector2 eb = vertices[v[(e + 1) % 3]];
                float d2 = DistanceSqPointToSegment(pt, ea, eb);
                float edgeLenSq = Vector2.DistanceSquared(ea, eb);
                if (d2 < edgeLenSq * 1e-4f && d2 < bestEdgeDistSq)
                {
                    bestEdgeDistSq = d2;
                    onEdgeSlot = e;
                }
            }

            if (onEdgeSlot >= 0)
            {
                int ea = v[onEdgeSlot];
                int eb = v[(onEdgeSlot + 1) % 3];
                if (constrainedEdges.Contains(MakeEdgeKey(ea, eb)))
                    return false;

                int nbIdx = t0.Neighbors[onEdgeSlot];
                if (nbIdx >= 0)
                {
                    return SplitSharedInternalEdge(vertices, triangles, constrainedEdges, locIdx, onEdgeSlot, nbIdx, pt);
                }
                return false;
            }

            // Chèn nghiêm ngặt bên trong tam giác t0 -> tách thành 3 tam giác con (v0,v1,p), (v1,v2,p), (v2,v0,p)
            if (SignedArea2(p0, p1, pt) <= 1e-5f ||
                SignedArea2(p1, p2, pt) <= 1e-5f ||
                SignedArea2(p2, p0, pt) <= 1e-5f)
            {
                return false;
            }

            int pIdx = vertices.Count;
            vertices.Add(pt);

            int n01 = t0.Neighbors[0];
            int n12 = t0.Neighbors[1];
            int n20 = t0.Neighbors[2];

            int t1Idx = triangles.Count;
            int t2Idx = triangles.Count + 1;

            t0.V0 = v[0]; t0.V1 = v[1]; t0.V2 = pIdx;
            t0.Neighbors[0] = n01;
            t0.Neighbors[1] = t1Idx;
            t0.Neighbors[2] = t2Idx;

            var tri1 = new Tri2D(v[1], v[2], pIdx);
            tri1.Neighbors[0] = n12;
            tri1.Neighbors[1] = t2Idx;
            tri1.Neighbors[2] = locIdx;

            var tri2 = new Tri2D(v[2], v[0], pIdx);
            tri2.Neighbors[0] = n20;
            tri2.Neighbors[1] = locIdx;
            tri2.Neighbors[2] = t1Idx;

            triangles.Add(tri1);
            triangles.Add(tri2);

            ReplaceNeighborRef(triangles, n12, locIdx, t1Idx);
            ReplaceNeighborRef(triangles, n20, locIdx, t2Idx);

            var queue = new Queue<(int, int)>();
            if (n01 >= 0) queue.Enqueue((locIdx, 0));
            if (n12 >= 0) queue.Enqueue((t1Idx, 0));
            if (n20 >= 0) queue.Enqueue((t2Idx, 0));
            LegalizeEdgeQueue(vertices, triangles, constrainedEdges, queue);
            return true;
        }

        private static bool SplitSharedInternalEdge(
            List<Vector2> vertices,
            List<Tri2D> triangles,
            HashSet<(int A, int B)> constrainedEdges,
            int t1Idx,
            int e1,
            int t2Idx,
            Vector2 pt)
        {
            Tri2D t1 = triangles[t1Idx];
            Tri2D t2 = triangles[t2Idx];
            int[] v1 = { t1.V0, t1.V1, t1.V2 };
            int a = v1[e1];
            int b = v1[(e1 + 1) % 3];
            int c = v1[(e1 + 2) % 3];

            int e2 = FindEdgeSlot(t2, a, b);
            if (e2 < 0) return false;
            int[] v2 = { t2.V0, t2.V1, t2.V2 };
            int d = v2[(e2 + 2) % 3];

            // Kiểm tra 4 tam giác sau khi tách đều CCW
            if (SignedArea2(vertices[c], vertices[a], pt) <= 1e-5f ||
                SignedArea2(vertices[b], vertices[c], pt) <= 1e-5f ||
                SignedArea2(vertices[a], vertices[d], pt) <= 1e-5f ||
                SignedArea2(vertices[d], vertices[b], pt) <= 1e-5f)
            {
                return false;
            }

            int nBC = t1.Neighbors[(e1 + 1) % 3];
            int nCA = t1.Neighbors[(e1 + 2) % 3];
            int nAD = t2.Neighbors[(e2 + 1) % 3];
            int nDB = t2.Neighbors[(e2 + 2) % 3];

            int pIdx = vertices.Count;
            vertices.Add(pt);

            int t3Idx = triangles.Count;
            int t4Idx = triangles.Count + 1;

            // t1 = (c, a, p), t3 = (b, c, p), t2 = (a, d, p), t4 = (d, b, p)
            t1.V0 = c; t1.V1 = a; t1.V2 = pIdx;
            t1.Neighbors[0] = nCA;
            t1.Neighbors[1] = t2Idx;
            t1.Neighbors[2] = t3Idx;

            var t3 = new Tri2D(b, c, pIdx);
            t3.Neighbors[0] = nBC;
            t3.Neighbors[1] = t1Idx;
            t3.Neighbors[2] = t4Idx;

            t2.V0 = a; t2.V1 = d; t2.V2 = pIdx;
            t2.Neighbors[0] = nAD;
            t2.Neighbors[1] = t4Idx;
            t2.Neighbors[2] = t1Idx;

            var t4 = new Tri2D(d, b, pIdx);
            t4.Neighbors[0] = nDB;
            t4.Neighbors[1] = t3Idx;
            t4.Neighbors[2] = t2Idx;

            triangles.Add(t3);
            triangles.Add(t4);

            ReplaceNeighborRef(triangles, nBC, t1Idx, t3Idx);
            ReplaceNeighborRef(triangles, nDB, t2Idx, t4Idx);

            var queue = new Queue<(int, int)>();
            if (nCA >= 0) queue.Enqueue((t1Idx, 0));
            if (nBC >= 0) queue.Enqueue((t3Idx, 0));
            if (nAD >= 0) queue.Enqueue((t2Idx, 0));
            if (nDB >= 0) queue.Enqueue((t4Idx, 0));
            LegalizeEdgeQueue(vertices, triangles, constrainedEdges, queue);
            return true;
        }

        private static void RelaxInteriorSteinerVertices(
            List<Vector2> vertices,
            List<Tri2D> triangles,
            int boundaryCount,
            HashSet<(int A, int B)> constrainedEdges,
            int iterations)
        {
            if (vertices.Count <= boundaryCount) return;

            // Các đỉnh nằm trên cạnh bị ràng buộc (khi allowBoundarySplit = true) cũng bị khóa
            var lockedVerts = new HashSet<int>();
            for (int i = 0; i < boundaryCount; i++) lockedVerts.Add(i);
            foreach (var edge in constrainedEdges)
            {
                lockedVerts.Add(edge.A);
                lockedVerts.Add(edge.B);
            }

            var incidentTris = new List<int>[vertices.Count];
            var neighborVerts = new HashSet<int>[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                incidentTris[i] = new List<int>(6);
                neighborVerts[i] = new HashSet<int>();
            }

            for (int t = 0; t < triangles.Count; t++)
            {
                var tri = triangles[t];
                incidentTris[tri.V0].Add(t);
                incidentTris[tri.V1].Add(t);
                incidentTris[tri.V2].Add(t);

                neighborVerts[tri.V0].Add(tri.V1); neighborVerts[tri.V0].Add(tri.V2);
                neighborVerts[tri.V1].Add(tri.V0); neighborVerts[tri.V1].Add(tri.V2);
                neighborVerts[tri.V2].Add(tri.V0); neighborVerts[tri.V2].Add(tri.V1);
            }

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int v = boundaryCount; v < vertices.Count; v++)
                {
                    if (lockedVerts.Contains(v) || neighborVerts[v].Count == 0) continue;

                    Vector2 avg = Vector2.Zero;
                    foreach (int nb in neighborVerts[v]) avg += vertices[nb];
                    avg /= neighborVerts[v].Count;

                    Vector2 oldPos = vertices[v];
                    Vector2 newPos = Vector2.Lerp(oldPos, avg, 0.65f);

                    float oldMinAng = 180f;
                    foreach (int tIdx in incidentTris[v])
                    {
                        var tr = triangles[tIdx];
                        EvaluateTriangleQuality(vertices[tr.V0], vertices[tr.V1], vertices[tr.V2], out float ang, out _, out _, out _, out _, out _);
                        if (ang < oldMinAng) oldMinAng = ang;
                    }

                    vertices[v] = newPos;
                    bool valid = true;
                    float newMinAng = 180f;
                    foreach (int tIdx in incidentTris[v])
                    {
                        var tr = triangles[tIdx];
                        if (SignedArea2(vertices[tr.V0], vertices[tr.V1], vertices[tr.V2]) <= 1e-5f)
                        {
                            valid = false;
                            break;
                        }
                        EvaluateTriangleQuality(vertices[tr.V0], vertices[tr.V1], vertices[tr.V2], out float ang, out _, out _, out _, out _, out _);
                        if (ang < newMinAng) newMinAng = ang;
                    }

                    if (!valid || newMinAng < oldMinAng - 0.1f)
                    {
                        vertices[v] = oldPos;
                    }
                }
            }
        }

        private static float DistanceSqPointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-9f) return Vector2.DistanceSquared(p, a);
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            return Vector2.DistanceSquared(p, a + ab * t);
        }
    }
}
