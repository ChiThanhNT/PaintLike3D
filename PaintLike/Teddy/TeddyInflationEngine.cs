using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV1-03, TV1-04 & TV1-05] Engine Tạo Khối 3D từ Đường Viền 2D (Inflation Algorithm - Mục 5.1 bài báo Teddy).
    /// Thực hiện đầy đủ quy trình:
    /// 1. Chuẩn hóa đa giác 2D & kiểm tra tự cắt.
    /// 2. Chia tam giác Constrained Delaunay (CDT) & phân loại Terminal (T), Sleeve (S), Junction (J) (Hình 13b).
    /// 3. Cắt tỉa nhánh phụ bằng kiểm tra nửa đường tròn (Pruning - Hình 14) & rút trích trục xương sống (Spine - Hình 13e).
    /// 4. Chia lưới 2D dọc theo Spine (Hình 13f), nâng cao độ Z tỉ lệ thuận với bề rộng vùng (Hình 15b).
    /// 5. Khâu mặt lưới thiết diện hình Oval đối xứng (+Z và -Z) (Hình 15c-d) và làm mịn lưới (Mesh Refinement).
    /// </summary>
    public static class TeddyInflationEngine
    {
        private struct BaseTri2D
        {
            public int V0;
            public int V1;
            public int V2;

            public BaseTri2D(int v0, int v1, int v2)
            {
                V0 = v0;
                V1 = v1;
                V2 = v2;
            }
        }

        /// <summary>
        /// Tạo một TeddyMesh 3D hoàn chỉnh từ nét vẽ 2D khép kín của người dùng trên màn hình.
        /// </summary>
        public static bool TryCreateMeshFromStroke(
            IReadOnlyList<PointF> rawScreenStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            Color modelColor,
            out TeddyMesh? resultMesh,
            out string statusMessage)
        {
            resultMesh = null;
            statusMessage = "";

            if (rawScreenStroke == null || rawScreenStroke.Count < 6)
            {
                statusMessage = "Nét vẽ quá ngắn! Hãy vẽ một đường khép kín lớn hơn để tạo khối 3D.";
                return false;
            }

            // Bước 1: Lấy mẫu lại nét vẽ với độ dài cạnh đồng đều (Uniform Edge Length - Mục 5.1, Hình 13a)
            float totalRawLen = StrokeProcessor.ComputePolylineLength(rawScreenStroke, true);
            if (totalRawLen < 90f)
            {
                statusMessage = "Vùng vẽ quá nhỏ! Hãy vẽ hình có chu vi lớn hơn.";
                return false;
            }

            // Chọn độ dài cạnh chuẩn để giữ chi tiết tai/cuống/uốn lượn mà vẫn giữ lưới mượt
            float targetEdgeLen = Math.Clamp(totalRawLen / 72f, 8f, 18f);
            List<PointF> uniformScreenPoly = StrokeProcessor.ResampleUniform(rawScreenStroke, targetEdgeLen, true);

            if (uniformScreenPoly.Count < 5)
            {
                statusMessage = "Không đủ đỉnh biên để tạo đa giác khép kín.";
                return false;
            }

            // Kiểm tra tự cắt (Self-Intersection check - Mục 4.2 & 5.1)
            if (StrokeProcessor.IsSelfIntersecting(uniformScreenPoly, true))
            {
                statusMessage = "Nét vẽ tự cắt nhau! Vui lòng vẽ một đường viền khép kín không giao cắt (Mục 4.2).";
                return false;
            }

            // Chuyển tọa độ màn hình sang hệ tọa độ 2D trong không gian View (Z = 0)
            int boundaryCount = uniformScreenPoly.Count;
            var vertices2D = new List<Vector2>(boundaryCount * 3);
            for (int i = 0; i < boundaryCount; i++)
            {
                Vector3 wp = camera.ScreenToWorldPoint(uniformScreenPoly[i], viewport, 0f);
                // Chiếu lên mặt phẳng View hiện tại để xây dựng hình học chuẩn
                Vector3 vp = camera.WorldToView(wp);
                vertices2D.Add(new Vector2(vp.X, vp.Y));
            }

            // Đảm bảo đa giác ngược chiều kim đồng hồ (CCW)
            if (Delaunay2D.ComputeSignedArea(vertices2D) < 0f)
            {
                vertices2D.Reverse();
            }

            // Bước 2: Chia tam giác Constrained Delaunay Triangulation (CDT - Hình 13b)
            List<Tri2D> cdtTriangles = Delaunay2D.TriangulatePolygonCDT(vertices2D);
            if (cdtTriangles.Count == 0)
            {
                statusMessage = "Không thể chia lưới tam giác cho nét vẽ này.";
                return false;
            }

            // Khởi tạo đối tượng lưu dữ liệu trực quan hóa từng bước thuật toán (Hình 13, 14, 15)
            var debugInfo = new TeddyAlgorithmDebugInfo
            {
                CreationViewRotation = camera.Rotation
            };
            Quaternion invCreationRot = Quaternion.Conjugate(camera.Rotation);
            for (int i = 0; i < boundaryCount; i++)
            {
                debugInfo.BoundaryPolygon2D.Add(vertices2D[i]);
                debugInfo.BoundaryPolygon3D.Add(Vector3.Transform(new Vector3(vertices2D[i].X, vertices2D[i].Y, 0f), invCreationRot));
            }
            foreach (var ct in cdtTriangles)
            {
                var copyTri = new Tri2D(ct.V0, ct.V1, ct.V2) { Category = ct.Category };
                Array.Copy(ct.Neighbors, copyTri.Neighbors, 3);
                debugInfo.CdtTriangles.Add(copyTri);
            }
            BuildRawChordalAxisForDebug(vertices2D, boundaryCount, cdtTriangles, debugInfo.RawChordalAxisEdges);

            // Bước 3: Cắt tỉa nhánh phụ (Semicircle Pruning - Hình 14) & Tạo trục xương sống (Spine - Hình 13e)
            BuildPrunedSpineAndBaseMesh(
                vertices2D,
                boundaryCount,
                cdtTriangles,
                debugInfo,
                out List<bool> isSpineVertex,
                out List<float> spineHeights,
                out List<(int A, int B)> spineEdges,
                out List<BaseTri2D> baseTriangles);

            // Bước 4: Nâng cao độ Z theo thiết diện hình Oval (Quarter-Oval Elevation) & Khâu lưới đối xứng (+Z / -Z)
            TeddyMesh mesh = ConstructSymmetricOvalMesh(
                vertices2D,
                isSpineVertex,
                spineHeights,
                spineEdges,
                baseTriangles,
                camera.Rotation,
                modelColor,
                debugInfo);

            // Bước 5: Tối ưu lưới 3D (Mesh Refinement - Hoppe 1993 [12], Mục 5.1 cuối trang 6)
            TeddyMeshOptimizer.RefineMesh(mesh, 2.2f);
            mesh.DebugInfo = debugInfo;

            // Bù độ lệch tâm xoay động PivotWorld / PivotView để khối 3D & minh họa 5 bước khớp 1:1 với nét 2D
            Vector3 pivotOffset = camera.PivotWorld - Vector3.Transform(camera.PivotView, Quaternion.Conjugate(camera.Rotation));
            if (pivotOffset.LengthSquared() > 1e-6f)
            {
                mesh.Translate(pivotOffset);
            }

            resultMesh = mesh;
            statusMessage = $"Đã tạo mô hình 3D: {mesh.Vertices.Count} đỉnh, {mesh.Triangles.Count} mặt (CDT: {cdtTriangles.Count} tam giác, Spine: {spineEdges.Count} đoạn).";
            return true;
        }

        private static void BuildRawChordalAxisForDebug(
            List<Vector2> poly,
            int boundaryCount,
            List<Tri2D> cdtTris,
            List<(Vector2 A, Vector2 B)> outRawAxes)
        {
            foreach (var tri in cdtTris)
            {
                int[] v = new int[] { tri.V0, tri.V1, tri.V2 };
                var internalMids = new List<Vector2>(3);
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    if (!Delaunay2D.IsBoundaryEdge(a, b, boundaryCount))
                    {
                        internalMids.Add(0.5f * (poly[a] + poly[b]));
                    }
                }

                if (internalMids.Count == 2)
                {
                    outRawAxes.Add((internalMids[0], internalMids[1]));
                }
                else if (internalMids.Count == 3)
                {
                    Vector2 c = (poly[tri.V0] + poly[tri.V1] + poly[tri.V2]) / 3f;
                    outRawAxes.Add((c, internalMids[0]));
                    outRawAxes.Add((c, internalMids[1]));
                    outRawAxes.Add((c, internalMids[2]));
                }
            }
        }

        /// <summary>
        /// [Mục 5.1 - Hình 13 & 14] Cắt tỉa các tam giác Terminal (T) bằng kiểm tra nửa đường tròn,
        /// rút trích trục xương sống (Chordal Axis / Spine) và chia nhỏ các tam giác Sleeve (S) / Junction (J).
        /// </summary>
        private static void BuildPrunedSpineAndBaseMesh(
            List<Vector2> vertices2D,
            int boundaryCount,
            List<Tri2D> cdtTris,
            TeddyAlgorithmDebugInfo debugInfo,
            out List<bool> isSpineVertex,
            out List<float> spineHeights,
            out List<(int A, int B)> spineEdges,
            out List<BaseTri2D> baseTriangles)
        {
            var spineFlags = new List<bool>(new bool[boundaryCount]); // Các đỉnh biên (0..boundaryCount-1) có isSpine = false
            isSpineVertex = spineFlags;
            spineEdges = new List<(int A, int B)>();
            baseTriangles = new List<BaseTri2D>();

            int triCount = cdtTris.Count;
            bool[] pruned = new bool[triCount];

            // Bảng ánh xạ cạnh trong (u, v) -> chỉ số đỉnh trung điểm (Spine Vertex)
            var edgeMidpointMap = new Dictionary<(int, int), int>();

            int GetOrCreateEdgeMidpoint(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                if (edgeMidpointMap.TryGetValue(key, out int midIdx))
                    return midIdx;

                Vector2 midPt = 0.5f * (vertices2D[u] + vertices2D[v]);
                midIdx = vertices2D.Count;
                vertices2D.Add(midPt);
                spineFlags.Add(true);
                edgeMidpointMap[key] = midIdx;
                return midIdx;
            }

            var junctionCenterMap = new Dictionary<int, int>();
            int GetOrCreateJunctionCenter(int tIdx)
            {
                if (junctionCenterMap.TryGetValue(tIdx, out int cIdx))
                    return cIdx;

                Tri2D t = cdtTris[tIdx];
                Vector2 center = (vertices2D[t.V0] + vertices2D[t.V1] + vertices2D[t.V2]) / 3f;
                cIdx = vertices2D.Count;
                vertices2D.Add(center);
                spineFlags.Add(true);
                junctionCenterMap[tIdx] = cIdx;
                return cIdx;
            }

            // Lưu các quạt tam giác (Fan regions) sinh ra sau khi cắt tỉa từ từng Terminal triangle (Hình 14)
            var fanConnections = new List<(int SpineVertex, int BoundA, int BoundB)>();
            var fanDedup = new HashSet<(int, int, int)>();
            var encounteredJunctions = new HashSet<int>();
            var junctionConsumedEdges = new HashSet<(int, int)>();

            // [TV1-06] Kiểm tra Nửa Đường Tròn Có Hướng (Oriented Semicircle Criterion - Prasad [21], Hình 14):
            // Mọi đỉnh trong vùng X phải đồng thời nằm trong bán kính R VÀ nằm cùng nửa mặt phẳng với vùng X đối với cạnh trong (edgeU, edgeV).
            bool AreAllVerticesInOrientedSemicircle(int edgeU, int edgeV, int insideRefVert, List<int> verts, int extraVert)
            {
                Vector2 pu = vertices2D[edgeU];
                Vector2 pv = vertices2D[edgeV];
                Vector2 mid = 0.5f * (pu + pv);
                float radius = 0.5f * Vector2.Distance(pu, pv);
                if (radius < 1e-5f) return false;
                float maxDist = radius * 1.02f;

                float refSide = insideRefVert >= 0 ? Delaunay2D.SignedArea2(pu, pv, vertices2D[insideRefVert]) : 0f;
                float sideEps = 1e-3f * radius * radius;

                bool CheckVertex(int bv)
                {
                    if (bv < 0 || bv == edgeU || bv == edgeV) return true;
                    Vector2 p = vertices2D[bv];
                    if (Vector2.Distance(p, mid) > maxDist)
                        return false;

                    if (MathF.Abs(refSide) > 1e-5f)
                    {
                        float s = Delaunay2D.SignedArea2(pu, pv, p);
                        if (s * refSide < -sideEps)
                            return false;
                    }
                    return true;
                }

                for (int k = 0; k < verts.Count; k++)
                {
                    if (!CheckVertex(verts[k]))
                        return false;
                }

                return CheckVertex(extraVert);
            }

            for (int tIdx = 0; tIdx < triCount; tIdx++)
            {
                if (pruned[tIdx] || cdtTris[tIdx].Category != TriangleCategory.Terminal)
                    continue;

                // Tìm cạnh trong duy nhất của Terminal triangle
                Tri2D startTri = cdtTris[tIdx];
                if (!TryGetTerminalInternalEdge(startTri, boundaryCount, out int u, out int v, out int tipVert, out int nextTriIdx))
                    continue;

                pruned[tIdx] = true;
                var regionBoundaryVerts = new List<int> { u, v, tipVert };
                var regionBoundaryEdges = new List<(int A, int B)>();
                AddBoundaryEdgesOfTri(startTri, boundaryCount, regionBoundaryEdges);

                int currTriIdx = tIdx;
                int currU = u;
                int currV = v;
                int currInsideRef = tipVert;
                int candidateNextTri = nextTriIdx;
                int stopJunctionTri = -1;

                if (!AreAllVerticesInOrientedSemicircle(currU, currV, currInsideRef, regionBoundaryVerts, -1))
                {
                    Vector2 mid0 = 0.5f * (vertices2D[currU] + vertices2D[currV]);
                    float radius0 = 0.5f * Vector2.Distance(vertices2D[currU], vertices2D[currV]);
                    debugInfo.PruningCircles.Add((mid0, radius0, vertices2D[currU], vertices2D[currV]));
                }
                else
                {
                    while (candidateNextTri >= 0)
                    {
                        Tri2D nextTri = cdtTris[candidateNextTri];
                        if (nextTri.Category == TriangleCategory.Junction)
                        {
                            // Chạm tam giác ngã ba Junction -> Hình 14e-f (phối hợp nhiều nhánh Terminal hội tụ cùng Junction)
                            stopJunctionTri = candidateNextTri;
                            pruned[candidateNextTri] = true;
                            encounteredJunctions.Add(candidateNextTri);
                            junctionConsumedEdges.Add((Math.Min(currU, currV), Math.Max(currU, currV)));
                            break;
                        }

                        if (pruned[candidateNextTri])
                            break;

                        if (nextTri.Category == TriangleCategory.Terminal)
                        {
                            // Hai tam giác Terminal chạm nhau qua cạnh trong chung (đa giác hình viên nang / oval ngắn)
                            pruned[candidateNextTri] = true;
                            AddBoundaryEdgesOfTri(nextTri, boundaryCount, regionBoundaryEdges);
                            break;
                        }

                        // Sleeve triangle: kiểm tra nửa đường tròn có hướng trên cạnh trong kế tiếp TRƯỚC khi gộp (Hình 14b-d)
                        int oppVert = nextTri.GetOppositeVertex(currU, currV);
                        int candU, candV, nextInsideRef;
                        if (Delaunay2D.IsBoundaryEdge(currU, oppVert, boundaryCount))
                        {
                            // (currU, oppVert) là cạnh biên => cạnh trong mới là (oppVert, currV), đỉnh currU nằm trong vùng X
                            candU = oppVert;
                            candV = currV;
                            nextInsideRef = currU;
                        }
                        else
                        {
                            // (currV, oppVert) là cạnh biên => cạnh trong mới là (currU, oppVert), đỉnh currV nằm trong vùng X
                            candU = currU;
                            candV = oppVert;
                            nextInsideRef = currV;
                        }

                        if (!AreAllVerticesInOrientedSemicircle(candU, candV, nextInsideRef, regionBoundaryVerts, oppVert))
                        {
                            Vector2 stopMid = 0.5f * (vertices2D[currU] + vertices2D[currV]);
                            float stopRadius = 0.5f * Vector2.Distance(vertices2D[currU], vertices2D[currV]);
                            debugInfo.PruningCircles.Add((stopMid, stopRadius, vertices2D[currU], vertices2D[currV]));
                            break;
                        }

                        // Nửa đường tròn có hướng đạt chuẩn -> gộp chính thức Sleeve này vào vùng X
                        pruned[candidateNextTri] = true;
                        if (!regionBoundaryVerts.Contains(oppVert))
                            regionBoundaryVerts.Add(oppVert);
                        AddBoundaryEdgesOfTri(nextTri, boundaryCount, regionBoundaryEdges);

                        currU = candU;
                        currV = candV;
                        currInsideRef = nextInsideRef;
                        currTriIdx = candidateNextTri;
                        candidateNextTri = FindNeighborAcrossEdge(cdtTris, currTriIdx, currU, currV);
                    }
                }

                // Xác định tâm quạt (Fan center): Tâm Junction (Hình 14f) hoặc Trung điểm cạnh trong dừng (Hình 14d)
                int fanCenterVertex = stopJunctionTri >= 0
                    ? GetOrCreateJunctionCenter(stopJunctionTri)
                    : GetOrCreateEdgeMidpoint(currU, currV);

                // Tạo các tam giác hình quạt (Fan triangles) khử trùng lặp nối từ fanCenterVertex tới mọi cạnh biên vùng X
                foreach (var bEdge in regionBoundaryEdges)
                {
                    var key = (fanCenterVertex, Math.Min(bEdge.A, bEdge.B), Math.Max(bEdge.A, bEdge.B));
                    if (fanDedup.Add(key))
                    {
                        fanConnections.Add((fanCenterVertex, bEdge.A, bEdge.B));
                    }
                }
            }

            // Sau khi cắt tỉa xong tất cả các nhánh Terminal, nối tâm Junction với các cạnh chưa bị quạt nhánh nào phủ
            foreach (int jIdx in encounteredJunctions)
            {
                int fanCenterVertex = GetOrCreateJunctionCenter(jIdx);
                Tri2D jTri = cdtTris[jIdx];
                int[] jv = new int[] { jTri.V0, jTri.V1, jTri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int ja = jv[e];
                    int jb = jv[(e + 1) % 3];
                    var edgeKey = (Math.Min(ja, jb), Math.Max(ja, jb));
                    if (junctionConsumedEdges.Contains(edgeKey))
                        continue;

                    int midJEdge = GetOrCreateEdgeMidpoint(ja, jb);
                    spineEdges.Add((fanCenterVertex, midJEdge));
                    baseTriangles.Add(new BaseTri2D(fanCenterVertex, ja, midJEdge));
                    baseTriangles.Add(new BaseTri2D(fanCenterVertex, midJEdge, jb));
                }
            }

            // Đưa toàn bộ Fan triangles vào danh sách lưới 2D cơ sở
            foreach (var fan in fanConnections)
            {
                baseTriangles.Add(new BaseTri2D(fan.SpineVertex, fan.BoundA, fan.BoundB));
            }

            // Bước 3.3: Chia nhỏ các tam giác Sleeve (S) và Junction (J) chưa bị cắt tỉa dọc theo trục xương sống (Hình 13e, 13f)
            for (int tIdx = 0; tIdx < triCount; tIdx++)
            {
                if (pruned[tIdx]) continue;
                Tri2D tri = cdtTris[tIdx];

                if (tri.Category == TriangleCategory.Sleeve)
                {
                    // Tìm cạnh biên duy nhất (b1, b2) và đỉnh đối diện vOpp
                    GetSleeveEdges(tri, boundaryCount, out int b1, out int b2, out int vOpp);
                    int m1 = GetOrCreateEdgeMidpoint(b1, vOpp);
                    int m2 = GetOrCreateEdgeMidpoint(b2, vOpp);

                    spineEdges.Add((m1, m2));

                    // [TV1-06] Chia Sleeve triangle thành 3 tam giác với đường chéo tứ giác (m1, b1, b2, m2) tối ưu Delaunay
                    baseTriangles.Add(new BaseTri2D(vOpp, m1, m2));
                    if (Delaunay2D.PreferDiagonalAC(vertices2D[m1], vertices2D[b1], vertices2D[b2], vertices2D[m2]))
                    {
                        baseTriangles.Add(new BaseTri2D(m1, b1, b2));
                        baseTriangles.Add(new BaseTri2D(m1, b2, m2));
                    }
                    else
                    {
                        baseTriangles.Add(new BaseTri2D(m1, b1, m2));
                        baseTriangles.Add(new BaseTri2D(b1, b2, m2));
                    }
                }
                else if (tri.Category == TriangleCategory.Junction)
                {
                    int cJ = GetOrCreateJunctionCenter(tIdx);
                    int m01 = GetOrCreateEdgeMidpoint(tri.V0, tri.V1);
                    int m12 = GetOrCreateEdgeMidpoint(tri.V1, tri.V2);
                    int m20 = GetOrCreateEdgeMidpoint(tri.V2, tri.V0);

                    spineEdges.Add((cJ, m01));
                    spineEdges.Add((cJ, m12));
                    spineEdges.Add((cJ, m20));

                    baseTriangles.Add(new BaseTri2D(cJ, tri.V0, m01));
                    baseTriangles.Add(new BaseTri2D(cJ, m01, tri.V1));
                    baseTriangles.Add(new BaseTri2D(cJ, tri.V1, m12));
                    baseTriangles.Add(new BaseTri2D(cJ, m12, tri.V2));
                    baseTriangles.Add(new BaseTri2D(cJ, tri.V2, m20));
                    baseTriangles.Add(new BaseTri2D(cJ, m20, tri.V0));
                }
            }

            // Trường hợp đặc biệt: Đa giác tròn / oval / giọt nước khiến toàn bộ tam giác cắt tỉa về 1 tâm duy nhất (không có cạnh Spine).
            // Đưa đỉnh tâm về đúng trọng tâm hình học của biên 2D thay vì tâm lệch của tam giác Junction / dây cung CDT ngẫu nhiên!
            if (vertices2D.Count == boundaryCount || spineEdges.Count == 0)
            {
                Vector2 centroid = ComputePolygonCentroid(vertices2D, boundaryCount);
                if (IsPointInsidePolygon(centroid, vertices2D, boundaryCount))
                {
                    vertices2D.RemoveRange(boundaryCount, vertices2D.Count - boundaryCount);
                    isSpineVertex.RemoveRange(boundaryCount, isSpineVertex.Count - boundaryCount);

                    int cIdx = vertices2D.Count;
                    vertices2D.Add(centroid);
                    isSpineVertex.Add(true);

                    spineEdges.Clear();
                    baseTriangles.Clear();
                    for (int i = 0; i < boundaryCount; i++)
                    {
                        baseTriangles.Add(new BaseTri2D(cIdx, i, (i + 1) % boundaryCount));
                    }
                }
            }

            // Bước 3.4: Tính chiều cao nâng Z (Elevation) của từng đỉnh Spine
            // "Mỗi đỉnh của Spine được nâng cao tỉ lệ thuận với khoảng cách trung bình giữa nó và các đỉnh biên nối trực tiếp với nó" (Mục 5.1)
            spineHeights = new List<float>(new float[vertices2D.Count]);
            var connectedBoundaryVerts = new HashSet<int>[vertices2D.Count];
            for (int i = boundaryCount; i < vertices2D.Count; i++)
                connectedBoundaryVerts[i] = new HashSet<int>();

            foreach (var bt in baseTriangles)
            {
                int[] v = new int[] { bt.V0, bt.V1, bt.V2 };
                for (int i = 0; i < 3; i++)
                {
                    if (isSpineVertex[v[i]])
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            if (!isSpineVertex[v[j]])
                                connectedBoundaryVerts[v[i]].Add(v[j]);
                        }
                    }
                }
            }

            bool isSingleCenterBlob = spineEdges.Count == 0 && (vertices2D.Count - boundaryCount) == 1;
            for (int i = boundaryCount; i < vertices2D.Count; i++)
            {
                var bSet = connectedBoundaryVerts[i];
                if (bSet.Count > 0)
                {
                    float sumDist = 0f;
                    float minDist = float.MaxValue;
                    float maxDist = 0f;
                    foreach (int bIdx in bSet)
                    {
                        float d = Vector2.Distance(vertices2D[i], vertices2D[bIdx]);
                        sumDist += d;
                        if (d < minDist) minDist = d;
                        if (d > maxDist) maxDist = d;
                    }
                    float avgDist = sumDist / bSet.Count;
                    float heightFactor = 0.94f;
                    if (isSingleCenterBlob && maxDist > 1e-4f)
                    {
                        // Hình tròn / gần tròn: giữ tỉ lệ 1:1:1 để phồng ra khối cầu tròn đều theo cả 3 trục X, Y, Z
                        float roundness = Math.Clamp((minDist / maxDist - 0.65f) / 0.25f, 0f, 1f);
                        heightFactor = 0.94f + 0.06f * roundness;
                    }
                    spineHeights[i] = avgDist * heightFactor;
                }
                else
                {
                    spineHeights[i] = 28f;
                }
            }

            // Làm mượt chiều cao dọc theo trục xương sống (Spine height smoothing) để tránh gãy khúc
            for (int pass = 0; pass < 3; pass++)
            {
                float[] nextH = spineHeights.ToArray();
                foreach (var se in spineEdges)
                {
                    float avg = 0.5f * (spineHeights[se.A] + spineHeights[se.B]);
                    nextH[se.A] = 0.65f * nextH[se.A] + 0.35f * avg;
                    nextH[se.B] = 0.65f * nextH[se.B] + 0.35f * avg;
                }
                for (int i = boundaryCount; i < vertices2D.Count; i++)
                    spineHeights[i] = nextH[i];
            }

            // Lưu dữ liệu Hình 13e & 13f vào debugInfo
            for (int i = boundaryCount; i < vertices2D.Count; i++)
            {
                if (isSpineVertex[i])
                    debugInfo.PrunedSpineNodes2D.Add(vertices2D[i]);
            }
            foreach (var se in spineEdges)
            {
                debugInfo.PrunedSpineEdges2D.Add((vertices2D[se.A], vertices2D[se.B]));
            }
            foreach (var bt in baseTriangles)
            {
                debugInfo.SubdividedMesh2D.Add((vertices2D[bt.V0], vertices2D[bt.V1], vertices2D[bt.V2]));
            }
        }

        /// <summary>
        /// [Mục 5.1 - Hình 15c, 15d] Chuyển đổi từng tam giác cơ sở 2D thành mặt cong Quarter-Oval 3D
        /// và khâu đối xứng cả hai mặt trước (+Z) và sau (-Z) thành một lưới kín hoàn chỉnh.
        /// </summary>
        private static TeddyMesh ConstructSymmetricOvalMesh(
            List<Vector2> vertices2D,
            List<bool> isSpineVertex,
            List<float> spineHeights,
            List<(int A, int B)> spineEdges2D,
            List<BaseTri2D> baseTriangles,
            Quaternion viewRotation,
            Color modelColor,
            TeddyAlgorithmDebugInfo debugInfo)
        {
            TeddyMesh mesh = new TeddyMesh
            {
                BaseColor = modelColor
            };

            Quaternion invRot = Quaternion.Conjugate(viewRotation);

            // Lưu thông tin trục xương sống 3D vào TeddyMesh để hiển thị ở chế độ Wireframe + Spine
            var spineIndexRemap = new Dictionary<int, int>();
            for (int i = 0; i < vertices2D.Count; i++)
            {
                if (isSpineVertex[i])
                {
                    spineIndexRemap[i] = mesh.SpineVertices.Count;
                    Vector3 spinePosView = new Vector3(vertices2D[i].X, vertices2D[i].Y, 0f);
                    mesh.SpineVertices.Add(Vector3.Transform(spinePosView, invRot));
                }
            }
            foreach (var se in spineEdges2D)
            {
                if (spineIndexRemap.TryGetValue(se.A, out int sa) && spineIndexRemap.TryGetValue(se.B, out int sb))
                {
                    mesh.SpineEdges.Add((sa, sb));
                }

                // Lưu đường Spine đã nâng Z (+Z và -Z) cho Hình 15b
                Vector3 topA = Vector3.Transform(new Vector3(vertices2D[se.A].X, vertices2D[se.A].Y, spineHeights[se.A]), invRot);
                Vector3 topB = Vector3.Transform(new Vector3(vertices2D[se.B].X, vertices2D[se.B].Y, spineHeights[se.B]), invRot);
                debugInfo.ElevatedSpineTop3D.Add((topA, topB));

                Vector3 botA = Vector3.Transform(new Vector3(vertices2D[se.A].X, vertices2D[se.A].Y, -spineHeights[se.A]), invRot);
                Vector3 botB = Vector3.Transform(new Vector3(vertices2D[se.B].X, vertices2D[se.B].Y, -spineHeights[se.B]), invRot);
                debugInfo.ElevatedSpineBottom3D.Add((botA, botB));
            }

            // Lưu các cung 1/4 elip (Quarter-Oval Ribs - Hình 15c) nối từ Spine ra Biên
            var addedRibPairs = new HashSet<(int, int)>();
            foreach (var bt in baseTriangles)
            {
                int[] v = new int[] { bt.V0, bt.V1, bt.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    if (isSpineVertex[a] != isSpineVertex[b])
                    {
                        int sIdx = isSpineVertex[a] ? a : b;
                        int bIdx = isSpineVertex[a] ? b : a;
                        if (addedRibPairs.Add((sIdx, bIdx)))
                        {
                            var ribFront = new Vector3[6];
                            var ribBack = new Vector3[6];
                            for (int step = 0; step <= 5; step++)
                            {
                                float theta = (MathF.PI * 0.5f) * (step / 5f);
                                Vector2 xy = Vector2.Lerp(vertices2D[sIdx], vertices2D[bIdx], MathF.Sin(theta));
                                float z = spineHeights[sIdx] * MathF.Cos(theta);
                                ribFront[step] = Vector3.Transform(new Vector3(xy.X, xy.Y, z), invRot);
                                ribBack[step] = Vector3.Transform(new Vector3(xy.X, xy.Y, -z), invRot);
                            }
                            debugInfo.QuarterOvalRibs3D.Add(ribFront);
                            debugInfo.QuarterOvalRibs3D.Add(ribBack);
                        }
                    }
                }
            }

            // Số bậc chia cung 1/4 elip theo góc đều từ trục Spine ra biên (K = 5 tạo mặt cắt 10 đoạn tròn mượt, không bị gờ sắc ở đường xích đạo)
            const int K = 5;

            // [TV1-06] Khâu đỉnh bằng Khóa Topo Chính Xác (Topological Vertex Sewing) thay cho lượng tử hóa tọa độ (Round(xy*10)):
            // Bảo toàn tuyệt đối các cổ/cuống hẹp (Cherry, Snake, tai Teddy) không bị hàn nhầm và không bao giờ nứt cạnh chia sẻ.
            var topoMapFront = new Dictionary<(byte Kind, int A, int B, int C, int W0, int W1), int>();
            var topoMapBack = new Dictionary<(byte Kind, int A, int B, int C, int W0, int W1), int>();

            (byte Kind, int A, int B, int C, int W0, int W1) MakeTopoKey(int triIdx, int iA, int iB, int iC, int r, int c, int d)
            {
                if (r == K) return (0, iA, -1, -1, 0, 0);
                if (c == K) return (0, iB, -1, -1, 0, 0);
                if (d == K) return (0, iC, -1, -1, 0, 0);

                if (d == 0)
                {
                    return iA < iB ? ((byte)1, iA, iB, -1, r, c) : ((byte)1, iB, iA, -1, c, r);
                }
                if (r == 0)
                {
                    return iB < iC ? ((byte)1, iB, iC, -1, c, d) : ((byte)1, iC, iB, -1, d, c);
                }
                if (c == 0)
                {
                    return iA < iC ? ((byte)1, iA, iC, -1, r, d) : ((byte)1, iC, iA, -1, d, r);
                }

                return (2, triIdx, r, c, 0, 0);
            }

            int GetOrAddTopo3DVertex((byte Kind, int A, int B, int C, int W0, int W1) topoKey, Vector2 xy, float zAbs, bool isOnSilhouetteBoundary, bool isBackSide)
            {
                // Nếu nằm trên biên hình bóng (z == 0, tất cả đỉnh tham gia đều là đỉnh biên), mặt trước (+Z) và mặt sau (-Z) dùng chung đỉnh Topo!
                if (isOnSilhouetteBoundary || zAbs <= 1e-4f)
                {
                    if (topoMapFront.TryGetValue(topoKey, out int sharedIdx))
                        return sharedIdx;

                    Vector3 worldPt = Vector3.Transform(new Vector3(xy.X, xy.Y, 0f), invRot);
                    int newIdx = mesh.Vertices.Count;
                    mesh.Vertices.Add(worldPt);
                    topoMapFront[topoKey] = newIdx;
                    topoMapBack[topoKey] = newIdx;
                    return newIdx;
                }

                var targetMap = isBackSide ? topoMapBack : topoMapFront;
                if (targetMap.TryGetValue(topoKey, out int existingIdx))
                    return existingIdx;

                float signedZ = isBackSide ? -zAbs : zAbs;
                Vector3 pos3D = Vector3.Transform(new Vector3(xy.X, xy.Y, signedZ), invRot);
                int idx = mesh.Vertices.Count;
                mesh.Vertices.Add(pos3D);
                targetMap[topoKey] = idx;
                return idx;
            }

            for (int btIdx = 0; btIdx < baseTriangles.Count; btIdx++)
            {
                var bt = baseTriangles[btIdx];
                int iA = bt.V0;
                int iB = bt.V1;
                int iC = bt.V2;

                Vector2 pA = vertices2D[iA];
                Vector2 pB = vertices2D[iB];
                Vector2 pC = vertices2D[iC];

                // Đảm bảo tam giác cơ sở ngược chiều kim đồng hồ (CCW)
                if ((pB.X - pA.X) * (pC.Y - pA.Y) - (pB.Y - pA.Y) * (pC.X - pA.X) < 0f)
                {
                    int tmpIdx = iB; iB = iC; iC = tmpIdx;
                    Vector2 tmpPt = pB; pB = pC; pC = tmpPt;
                }

                // Tạo lưới con barycentric K x K cho mặt trước (+Z) và mặt sau (-Z)
                int[,] gridFront = new int[K + 1, K + 1];
                int[,] gridBack = new int[K + 1, K + 1];

                for (int r = 0; r <= K; r++)
                {
                    for (int c = 0; c <= K - r; c++)
                    {
                        int d = K - r - c;
                        float wA = r / (float)K;
                        float wB = c / (float)K;
                        float wC = d / (float)K;

                        // Tham số hóa cung 1/4 elip chuẩn theo góc theta = (pi/2) * wBound:
                        // xy = Lerp(spinePt, boundPt, sin(theta)), z = baseH * cos(theta)
                        // Đảm bảo x^2 + y^2 + z^2 = R^2 chính xác cho hình tròn và mượt liên tục trên mọi cạnh chung!
                        float wSpine = 0f;
                        float weightedSpineH = 0f;
                        Vector2 spineWeightedPos = Vector2.Zero;
                        Vector2 boundWeightedPos = Vector2.Zero;

                        if (isSpineVertex[iA]) { wSpine += wA; weightedSpineH += wA * spineHeights[iA]; spineWeightedPos += wA * pA; }
                        else { boundWeightedPos += wA * pA; }
                        if (isSpineVertex[iB]) { wSpine += wB; weightedSpineH += wB * spineHeights[iB]; spineWeightedPos += wB * pB; }
                        else { boundWeightedPos += wB * pB; }
                        if (isSpineVertex[iC]) { wSpine += wC; weightedSpineH += wC * spineHeights[iC]; spineWeightedPos += wC * pC; }
                        else { boundWeightedPos += wC * pC; }

                        float wBound = Math.Clamp(1.0f - wSpine, 0f, 1f);
                        bool isOnBoundary = wSpine <= 1e-6f;
                        Vector2 xy;
                        float zElevation = 0f;

                        if (isOnBoundary)
                        {
                            xy = boundWeightedPos;
                        }
                        else if (wBound <= 1e-6f)
                        {
                            xy = spineWeightedPos / wSpine;
                            zElevation = weightedSpineH / wSpine;
                        }
                        else
                        {
                            Vector2 spinePt = spineWeightedPos / wSpine;
                            Vector2 boundPt = boundWeightedPos / wBound;
                            float baseH = weightedSpineH / wSpine;
                            float theta = (MathF.PI * 0.5f) * wBound;
                            xy = Vector2.Lerp(spinePt, boundPt, MathF.Sin(theta));
                            zElevation = baseH * MathF.Cos(theta);
                        }

                        var topoKey = MakeTopoKey(btIdx, iA, iB, iC, r, c, d);
                        gridFront[r, c] = GetOrAddTopo3DVertex(topoKey, xy, zElevation, isOnBoundary, false);
                        gridBack[r, c] = GetOrAddTopo3DVertex(topoKey, xy, zElevation, isOnBoundary, true);
                    }
                }

                // Khâu các tam giác con trên lưới K x K
                for (int r = 0; r < K; r++)
                {
                    for (int c = 0; c < K - r; c++)
                    {
                        // Tam giác xuôi
                        int f0 = gridFront[r, c];
                        int f1 = gridFront[r + 1, c];
                        int f2 = gridFront[r, c + 1];
                        if (f0 != f1 && f1 != f2 && f2 != f0)
                            mesh.Triangles.Add(new TeddyTriangle(f0, f1, f2));

                        int b0 = gridBack[r, c];
                        int b1 = gridBack[r + 1, c];
                        int b2 = gridBack[r, c + 1];
                        if (b0 != b1 && b1 != b2 && b2 != b0)
                            mesh.Triangles.Add(new TeddyTriangle(b0, b2, b1)); // Đảo chiều mặt sau để pháp tuyến hướng ra ngoài

                        // Tam giác ngược (nếu chưa chạm cạnh huyền)
                        if (r + c + 1 < K)
                        {
                            int fn0 = gridFront[r + 1, c];
                            int fn1 = gridFront[r + 1, c + 1];
                            int fn2 = gridFront[r, c + 1];
                            if (fn0 != fn1 && fn1 != fn2 && fn2 != fn0)
                                mesh.Triangles.Add(new TeddyTriangle(fn0, fn1, fn2));

                            int bn0 = gridBack[r + 1, c];
                            int bn1 = gridBack[r + 1, c + 1];
                            int bn2 = gridBack[r, c + 1];
                            if (bn0 != bn1 && bn1 != bn2 && bn2 != bn0)
                                mesh.Triangles.Add(new TeddyTriangle(bn0, bn2, bn1));
                        }
                    }
                }
            }

            mesh.BuildConnectivity();
            // Làm mịn lưới Taubin 2 bước để khử vết gợn giữa các tam giác quạt (Mục 5.1 cuối trang 6)
            mesh.SmoothMesh(2, 0.38f, -0.40f);
            mesh.RecalculateNormals();

            return mesh;
        }

        private static bool TryGetTerminalInternalEdge(
            Tri2D tri,
            int boundaryCount,
            out int u,
            out int v,
            out int tipVert,
            out int neighborTri)
        {
            u = v = tipVert = neighborTri = -1;
            int[] verts = new int[] { tri.V0, tri.V1, tri.V2 };
            for (int e = 0; e < 3; e++)
            {
                int a = verts[e];
                int b = verts[(e + 1) % 3];
                if (!Delaunay2D.IsBoundaryEdge(a, b, boundaryCount))
                {
                    u = a;
                    v = b;
                    tipVert = verts[(e + 2) % 3];
                    neighborTri = tri.Neighbors[e];
                    return true;
                }
            }
            return false;
        }

        private static void AddBoundaryEdgesOfTri(Tri2D tri, int boundaryCount, List<(int A, int B)> list)
        {
            int[] verts = new int[] { tri.V0, tri.V1, tri.V2 };
            for (int e = 0; e < 3; e++)
            {
                int a = verts[e];
                int b = verts[(e + 1) % 3];
                if (Delaunay2D.IsBoundaryEdge(a, b, boundaryCount))
                {
                    list.Add((a, b));
                }
            }
        }

        private static int FindNeighborAcrossEdge(List<Tri2D> tris, int currentTriIdx, int u, int v)
        {
            Tri2D t = tris[currentTriIdx];
            int[] verts = new int[] { t.V0, t.V1, t.V2 };
            for (int e = 0; e < 3; e++)
            {
                int a = verts[e];
                int b = verts[(e + 1) % 3];
                if ((a == u && b == v) || (a == v && b == u))
                    return t.Neighbors[e];
            }
            return -1;
        }

        private static void GetSleeveEdges(Tri2D tri, int boundaryCount, out int b1, out int b2, out int vOpp)
        {
            int[] verts = new int[] { tri.V0, tri.V1, tri.V2 };
            for (int e = 0; e < 3; e++)
            {
                int a = verts[e];
                int b = verts[(e + 1) % 3];
                if (Delaunay2D.IsBoundaryEdge(a, b, boundaryCount))
                {
                    b1 = a;
                    b2 = b;
                    vOpp = verts[(e + 2) % 3];
                    return;
                }
            }
            b1 = tri.V0;
            b2 = tri.V1;
            vOpp = tri.V2;
        }

        private static Vector2 ComputePolygonCentroid(List<Vector2> poly, int count)
        {
            double area2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;
            for (int i = 0; i < count; i++)
            {
                Vector2 p0 = poly[i];
                Vector2 p1 = poly[(i + 1) % count];
                double cross = (double)p0.X * p1.Y - (double)p1.X * p0.Y;
                area2 += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }

            if (Math.Abs(area2) > 1e-4)
                return new Vector2((float)(cx / (3.0 * area2)), (float)(cy / (3.0 * area2)));

            Vector2 mean = Vector2.Zero;
            for (int i = 0; i < count; i++) mean += poly[i];
            return mean / Math.Max(1, count);
        }

        private static bool IsPointInsidePolygon(Vector2 pt, List<Vector2> poly, int count)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 pi = poly[i];
                Vector2 pj = poly[j];
                bool intersect = ((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y + 1e-12f) + pi.X);
                if (intersect) inside = !inside;
            }
            return inside;
        }
    }
}
