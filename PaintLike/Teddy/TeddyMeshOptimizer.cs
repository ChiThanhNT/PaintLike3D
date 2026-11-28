using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV1-05] Tối ưu hóa & Tinh chỉnh chất lượng lưới 3D (Mesh Refinement - Hoppe et al. 1993 [12], Mục 5.1)
    /// kèm bộ sinh nét vẽ mẫu chuẩn theo Hình 1 & Hình 5 của bài báo Teddy (SIGGRAPH '99).
    /// </summary>
    public static class TeddyMeshOptimizer
    {
        /// <summary>
        /// [TV1-05 & TV1-09 - Hoppe 1993 / Botsch-Kobbelt] Tối ưu hóa & Tinh chỉnh chất lượng lưới 3D:
        /// Kết hợp gộp cạnh ngắn AN TOÀN ĐA TẠP (kiểm tra Link Condition: |Lk(u) ∩ Lk(v)| == 2)
        /// để đảm bảo mọi lưới khép kín sau tối ưu luôn bảo toàn đặc trưng Euler V - E + F = 2.
        /// </summary>
        public static int RefineMesh(TeddyMesh mesh, float minEdgeLength = 2.8f, HashSet<int>? protectedVertices = null)
        {
            if (mesh == null || mesh.IsEmpty) return 0;

            int collapsedCount = 0;
            float minLenSq = minEdgeLength * minEdgeLength;

            // Tính độ dài cạnh trung bình để xác định ngưỡng gộp cạnh ngắn
            float totalLen = 0f;
            int sampleEdges = 0;
            foreach (var tri in mesh.Triangles)
            {
                totalLen += Vector3.Distance(mesh.Vertices[tri.V0], mesh.Vertices[tri.V1]);
                sampleEdges++;
            }
            if (sampleEdges > 0)
            {
                float avgLen = totalLen / sampleEdges;
                float adaptiveMin = avgLen * 0.08f;
                minLenSq = MathF.Max(minLenSq, adaptiveMin * adaptiveMin);
            }

            // [TV1-09] Gộp cạnh ngắn có kiểm tra Link Condition để tuyệt đối không tạo cạnh suy biến hay non-manifold
            collapsedCount = CollapseShortEdgesSafely(mesh, minLenSq, protectedVertices);

            TeddyExtrudeCutOp.CompactMeshVertices(mesh);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
            return collapsedCount;
        }

        /// <summary>
        /// [TV1-09] Bộ Tái Chia Lưới Đẳng Hướng Bảo Toàn Đa Tạp 4 Pha (Isotropic Manifold Remesher):
        /// 1. Long-Edge Split (cạnh > 4/3 L_target)
        /// 2. Link-Condition Safe Short-Edge Collapse (cạnh < 4/5 L_target, giữ Link(u) ∩ Link(v) == {w1, w2})
        /// 3. Valence-6 Edge Flip (cân bằng bậc đỉnh tiến về 6)
        /// 4. Tangential Taubin Fairing (làm đều lưới trên mặt tiếp tuyến không hao hụt thể tích)
        /// </summary>
        public static int IsotropicManifoldRemesh(
            TeddyMesh mesh,
            float targetEdgeLength = 0f,
            int iterations = 1,
            HashSet<int>? protectedVertices = null)
        {
            if (mesh == null || mesh.IsEmpty || mesh.Triangles.Count < 4)
                return 0;

            float targetL = targetEdgeLength;
            if (targetL <= 1e-3f)
            {
                double sumLen = 0.0;
                int cnt = 0;
                foreach (var tri in mesh.Triangles)
                {
                    sumLen += Vector3.Distance(mesh.Vertices[tri.V0], mesh.Vertices[tri.V1]);
                    sumLen += Vector3.Distance(mesh.Vertices[tri.V1], mesh.Vertices[tri.V2]);
                    sumLen += Vector3.Distance(mesh.Vertices[tri.V2], mesh.Vertices[tri.V0]);
                    cnt += 3;
                }
                targetL = cnt > 0 ? Math.Clamp((float)(sumLen / cnt), 6f, 60f) : 16f;
            }

            float maxLen = (4f / 3f) * targetL;
            float minLen = (4f / 5f) * targetL;
            int totalOps = 0;

            for (int iter = 0; iter < Math.Max(1, iterations); iter++)
            {
                // Pha 1: Chia đôi các cạnh quá dài (> 4/3 L) bảo toàn đa tạp kín (1-to-2 hoặc 4-subdivision)
                totalOps += SplitLongEdgesManifold(mesh, maxLen);

                // Pha 2: Gộp các cạnh quá ngắn (< 4/5 L) chỉ khi thỏa mãn Link Condition
                totalOps += CollapseShortEdgesSafely(mesh, minLen * minLen, protectedVertices);

                // Pha 3: Lật cạnh cân bằng bậc đỉnh Valence -> 6 và cải thiện góc nhỏ nhất
                totalOps += FlipEdgesToValence6(mesh, protectedVertices);

                // Pha 4: Tangential Fairing (dịch chuyển đỉnh theo mặt phẳng tiếp tuyến để giữ nguyên thể tích)
                ApplyTangentialTaubinFairing(mesh, 2, 0.35f, -0.36f, protectedVertices);
            }

            TeddyExtrudeCutOp.CompactMeshVertices(mesh);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
            return totalOps;
        }

        private static int SplitLongEdgesManifold(TeddyMesh mesh, float maxEdgeLen)
        {
            if (mesh.Triangles.Count > 14000) return 0;

            float maxLenSq = maxEdgeLen * maxEdgeLen;
            var edgeSplitMid = new Dictionary<(int, int), int>();

            foreach (var tri in mesh.Triangles)
            {
                int[] v = { tri.V0, tri.V1, tri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    if (!edgeSplitMid.ContainsKey(key) && Vector3.DistanceSquared(mesh.Vertices[a], mesh.Vertices[b]) > maxLenSq)
                    {
                        Vector3 mid = 0.5f * (mesh.Vertices[a] + mesh.Vertices[b]);
                        int mIdx = mesh.Vertices.Count;
                        mesh.Vertices.Add(mid);
                        edgeSplitMid[key] = mIdx;
                    }
                }
            }

            if (edgeSplitMid.Count == 0) return 0;

            var newTris = new List<TeddyTriangle>(mesh.Triangles.Count + edgeSplitMid.Count * 2);
            foreach (var tri in mesh.Triangles)
            {
                int v0 = tri.V0, v1 = tri.V1, v2 = tri.V2;
                bool s01 = edgeSplitMid.TryGetValue(v0 < v1 ? (v0, v1) : (v1, v0), out int m01);
                bool s12 = edgeSplitMid.TryGetValue(v1 < v2 ? (v1, v2) : (v2, v1), out int m12);
                bool s20 = edgeSplitMid.TryGetValue(v2 < v0 ? (v2, v0) : (v0, v2), out int m20);
                int splitCount = (s01 ? 1 : 0) + (s12 ? 1 : 0) + (s20 ? 1 : 0);

                if (splitCount == 0)
                {
                    newTris.Add(tri);
                }
                else if (splitCount == 3)
                {
                    newTris.Add(new TeddyTriangle(v0, m01, m20, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(m01, v1, m12, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(m20, m12, v2, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(m01, m12, m20, tri.Category, tri.CustomColor, tri.IsCutCap));
                }
                else if (splitCount == 1)
                {
                    int a, b, c, mAB;
                    if (s01) { a = v0; b = v1; c = v2; mAB = m01; }
                    else if (s12) { a = v1; b = v2; c = v0; mAB = m12; }
                    else { a = v2; b = v0; c = v1; mAB = m20; }

                    newTris.Add(new TeddyTriangle(a, mAB, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(mAB, b, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                }
                else
                {
                    int a, b, c, mAB, mBC;
                    if (!s20) { a = v0; b = v1; c = v2; mAB = m01; mBC = m12; }
                    else if (!s01) { a = v1; b = v2; c = v0; mAB = m12; mBC = m20; }
                    else { a = v2; b = v0; c = v1; mAB = m20; mBC = m01; }

                    newTris.Add(new TeddyTriangle(mAB, b, mBC, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(a, mAB, mBC, tri.Category, tri.CustomColor, tri.IsCutCap));
                    newTris.Add(new TeddyTriangle(a, mBC, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                }
            }

            mesh.Triangles.Clear();
            mesh.Triangles.AddRange(newTris);
            return edgeSplitMid.Count;
        }

        private static int CollapseShortEdgesSafely(TeddyMesh mesh, float minLenSq, HashSet<int>? protectedVertices)
        {
            int nVerts = mesh.Vertices.Count;
            if (nVerts == 0 || mesh.Triangles.Count < 4) return 0;

            var vertexNeighbors = new HashSet<int>[nVerts];
            var vertexTris = new List<int>[nVerts];
            var edgeTriCount = new Dictionary<(int, int), List<int>>(mesh.Triangles.Count * 2);

            for (int i = 0; i < nVerts; i++)
            {
                vertexNeighbors[i] = new HashSet<int>();
                vertexTris[i] = new List<int>(6);
            }

            for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
            {
                var tri = mesh.Triangles[tIdx];
                if (tri.V0 == tri.V1 || tri.V1 == tri.V2 || tri.V2 == tri.V0) continue;

                vertexTris[tri.V0].Add(tIdx);
                vertexTris[tri.V1].Add(tIdx);
                vertexTris[tri.V2].Add(tIdx);

                vertexNeighbors[tri.V0].Add(tri.V1); vertexNeighbors[tri.V0].Add(tri.V2);
                vertexNeighbors[tri.V1].Add(tri.V0); vertexNeighbors[tri.V1].Add(tri.V2);
                vertexNeighbors[tri.V2].Add(tri.V0); vertexNeighbors[tri.V2].Add(tri.V1);

                int[] v = { tri.V0, tri.V1, tri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e], b = v[(e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    if (!edgeTriCount.TryGetValue(key, out var list))
                    {
                        list = new List<int>(2);
                        edgeTriCount[key] = list;
                    }
                    list.Add(tIdx);
                }
            }

            bool[] locked = new bool[nVerts];
            bool[] deadTri = new bool[mesh.Triangles.Count];
            int[] remap = new int[nVerts];
            for (int i = 0; i < nVerts; i++) remap[i] = i;

            int collapsedCount = 0;

            foreach (var kv in edgeTriCount)
            {
                int a = kv.Key.Item1;
                int b = kv.Key.Item2;
                var sharedTris = kv.Value;

                if (locked[a] || locked[b]) continue;
                if (protectedVertices != null && (protectedVertices.Contains(a) || protectedVertices.Contains(b))) continue;
                if (sharedTris.Count != 2) continue; // Chỉ gộp cạnh đa tạp nằm giữa đúng 2 tam giác

                int t1Idx = sharedTris[0];
                int t2Idx = sharedTris[1];
                if (deadTri[t1Idx] || deadTri[t2Idx]) continue;

                float distSq = Vector3.DistanceSquared(mesh.Vertices[a], mesh.Vertices[b]);
                if (distSq >= minLenSq) continue;

                // Kiểm tra Link Condition: Lk(a) ∩ Lk(b) phải có đúng 2 đỉnh đối diện của t1 và t2!
                int opp1 = GetOppositeVert(mesh.Triangles[t1Idx], a, b);
                int opp2 = GetOppositeVert(mesh.Triangles[t2Idx], a, b);
                if (opp1 < 0 || opp2 < 0 || opp1 == opp2) continue;

                int commonCount = 0;
                bool linkOk = true;
                foreach (int nb in vertexNeighbors[a])
                {
                    if (vertexNeighbors[b].Contains(nb))
                    {
                        commonCount++;
                        if (nb != opp1 && nb != opp2)
                        {
                            linkOk = false;
                            break;
                        }
                    }
                }
                if (!linkOk || commonCount != 2) continue;

                // Bảo toàn hai mảnh cắt / nắp cắt không gộp lẫn giữa IsCutCap và thân ngoài
                if (mesh.Triangles[t1Idx].IsCutCap != mesh.Triangles[t2Idx].IsCutCap) continue;

                Vector3 mid = 0.5f * (mesh.Vertices[a] + mesh.Vertices[b]);

                // Kiểm tra chống lật pháp tuyến tam giác xung quanh (u, v) sau khi gộp về mid
                bool normalSafe = true;
                foreach (int tIdx in vertexTris[a])
                {
                    if (deadTri[tIdx] || tIdx == t1Idx || tIdx == t2Idx) continue;
                    if (!WouldRemainValidAfterMove(mesh, mesh.Triangles[tIdx], a, b, mid)) { normalSafe = false; break; }
                }
                if (normalSafe)
                {
                    foreach (int tIdx in vertexTris[b])
                    {
                        if (deadTri[tIdx] || tIdx == t1Idx || tIdx == t2Idx) continue;
                        if (!WouldRemainValidAfterMove(mesh, mesh.Triangles[tIdx], a, b, mid)) { normalSafe = false; break; }
                    }
                }
                if (!normalSafe) continue;

                mesh.Vertices[a] = mid;
                remap[b] = a;
                deadTri[t1Idx] = true;
                deadTri[t2Idx] = true;
                locked[a] = true;
                locked[b] = true;
                // Khóa toàn bộ láng giềng 1-ring quanh (a, b) trong lượt này để tuyệt đối bảo toàn Link Condition (không sinh non-manifold)
                foreach (int nb in vertexNeighbors[a]) locked[nb] = true;
                foreach (int nb in vertexNeighbors[b]) locked[nb] = true;
                collapsedCount++;
            }

            if (collapsedCount == 0)
            {
                // Vẫn lọc bỏ tam giác suy biến diện tích ~0 nếu có
                var cleanTris = new List<TeddyTriangle>(mesh.Triangles.Count);
                foreach (var tri in mesh.Triangles)
                {
                    if (tri.V0 == tri.V1 || tri.V1 == tri.V2 || tri.V2 == tri.V0) continue;
                    if (Vector3.Cross(mesh.Vertices[tri.V1] - mesh.Vertices[tri.V0], mesh.Vertices[tri.V2] - mesh.Vertices[tri.V0]).LengthSquared() <= 1e-8f)
                        continue;
                    cleanTris.Add(tri);
                }
                mesh.Triangles.Clear();
                mesh.Triangles.AddRange(cleanTris);
                return 0;
            }

            var validTriangles = new List<TeddyTriangle>(mesh.Triangles.Count);
            for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
            {
                if (deadTri[tIdx]) continue;
                var tri = mesh.Triangles[tIdx];
                int i0 = remap[tri.V0];
                int i1 = remap[tri.V1];
                int i2 = remap[tri.V2];
                if (i0 == i1 || i1 == i2 || i2 == i0) continue;

                if (Vector3.Cross(mesh.Vertices[i1] - mesh.Vertices[i0], mesh.Vertices[i2] - mesh.Vertices[i0]).LengthSquared() <= 1e-8f)
                    continue;

                tri.V0 = i0; tri.V1 = i1; tri.V2 = i2;
                validTriangles.Add(tri);
            }

            mesh.Triangles.Clear();
            mesh.Triangles.AddRange(validTriangles);
            return collapsedCount;
        }

        private static bool WouldRemainValidAfterMove(TeddyMesh mesh, TeddyTriangle tri, int keepV, int dropV, Vector3 newPos)
        {
            Vector3 o0 = mesh.Vertices[tri.V0];
            Vector3 o1 = mesh.Vertices[tri.V1];
            Vector3 o2 = mesh.Vertices[tri.V2];
            Vector3 oldN = Vector3.Cross(o1 - o0, o2 - o0);

            Vector3 n0 = (tri.V0 == keepV || tri.V0 == dropV) ? newPos : o0;
            Vector3 n1 = (tri.V1 == keepV || tri.V1 == dropV) ? newPos : o1;
            Vector3 n2 = (tri.V2 == keepV || tri.V2 == dropV) ? newPos : o2;
            Vector3 newN = Vector3.Cross(n1 - n0, n2 - n0);

            float oldLen = oldN.Length();
            float newLen = newN.Length();
            if (newLen <= 1e-4f) return false;
            if (oldLen <= 1e-5f) return true;
            return Vector3.Dot(oldN / oldLen, newN / newLen) > 0.25f;
        }

        private static int GetOppositeVert(TeddyTriangle tri, int a, int b)
        {
            if (tri.V0 != a && tri.V0 != b) return tri.V0;
            if (tri.V1 != a && tri.V1 != b) return tri.V1;
            if (tri.V2 != a && tri.V2 != b) return tri.V2;
            return -1;
        }

        private static int FlipEdgesToValence6(TeddyMesh mesh, HashSet<int>? protectedVertices)
        {
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();

            int nVerts = mesh.Vertices.Count;
            if (nVerts == 0 || mesh.Edges.Count == 0) return 0;

            int[] valence = new int[nVerts];
            var edgeSet = new HashSet<(int, int)>(mesh.Edges.Count);
            foreach (var edge in mesh.Edges)
            {
                valence[edge.A]++;
                valence[edge.B]++;
                edgeSet.Add(edge.A < edge.B ? (edge.A, edge.B) : (edge.B, edge.A));
            }

            bool[] touchedTri = new bool[mesh.Triangles.Count];
            int flipped = 0;

            foreach (var edge in mesh.Edges)
            {
                if (edge.TriLeft < 0 || edge.TriRight < 0) continue;
                if (touchedTri[edge.TriLeft] || touchedTri[edge.TriRight]) continue;

                int a = edge.A, b = edge.B;
                if (protectedVertices != null && (protectedVertices.Contains(a) || protectedVertices.Contains(b)))
                    continue;

                var t1 = mesh.Triangles[edge.TriLeft];
                var t2 = mesh.Triangles[edge.TriRight];
                if (t1.IsCutCap != t2.IsCutCap) continue;
                if (Vector3.Dot(t1.FaceNormal, t2.FaceNormal) < 0.78f) continue;

                int c = GetOppositeVert(t1, a, b);
                int d = GetOppositeVert(t2, a, b);
                if (c < 0 || d < 0 || c == d) continue;

                // Kiểm tra (c, d) đã tồn tại trong edgeSet (bao gồm các cạnh mới flip ở lượt này) chưa
                var newKey = c < d ? (c, d) : (d, c);
                if (edgeSet.Contains(newKey)) continue;

                int beforeErr = (valence[a] - 6) * (valence[a] - 6) + (valence[b] - 6) * (valence[b] - 6)
                              + (valence[c] - 6) * (valence[c] - 6) + (valence[d] - 6) * (valence[d] - 6);
                int afterErr = (valence[a] - 7) * (valence[a] - 7) + (valence[b] - 7) * (valence[b] - 7)
                             + (valence[c] - 5) * (valence[c] - 5) + (valence[d] - 5) * (valence[d] - 5);
                if (afterErr >= beforeErr) continue;

                // Bảo toàn hướng pháp tuyến trước/sau flip bất kể thứ tự (a, b) trong t1
                Vector3 avgNorm = Vector3.Normalize(t1.FaceNormal + t2.FaceNormal);
                Vector3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c], pd = mesh.Vertices[d];

                Vector3 n1Raw = Vector3.Cross(pd - pc, pa - pc);
                bool flipOrder = Vector3.Dot(n1Raw, avgNorm) < 0f;
                Vector3 n1 = flipOrder ? -n1Raw : n1Raw;
                Vector3 n2 = flipOrder ? Vector3.Cross(pd - pc, pb - pc) : Vector3.Cross(pb - pc, pd - pc);

                float l1 = n1.Length();
                float l2 = n2.Length();
                if (l1 <= 1e-4f || l2 <= 1e-4f) continue;
                if (Vector3.Dot(n1 / l1, avgNorm) < 0.35f || Vector3.Dot(n2 / l2, avgNorm) < 0.35f) continue;

                if (!flipOrder)
                {
                    t1.V0 = c; t1.V1 = d; t1.V2 = a;
                    t2.V0 = c; t2.V1 = b; t2.V2 = d;
                }
                else
                {
                    t1.V0 = c; t1.V1 = a; t1.V2 = d;
                    t2.V0 = c; t2.V1 = d; t2.V2 = b;
                }

                edgeSet.Remove(a < b ? (a, b) : (b, a));
                edgeSet.Add(newKey);
                valence[a]--; valence[b]--;
                valence[c]++; valence[d]++;
                touchedTri[edge.TriLeft] = true;
                touchedTri[edge.TriRight] = true;
                flipped++;
            }

            return flipped;
        }

        private static void ApplyTangentialTaubinFairing(
            TeddyMesh mesh,
            int iterations,
            float lambda,
            float mu,
            HashSet<int>? protectedVertices)
        {
            mesh.RecalculateNormals();
            int nVerts = mesh.Vertices.Count;
            var neighbors = new List<int>[nVerts];
            for (int i = 0; i < nVerts; i++) neighbors[i] = new List<int>(6);

            foreach (var tri in mesh.Triangles)
            {
                if (!neighbors[tri.V0].Contains(tri.V1)) neighbors[tri.V0].Add(tri.V1);
                if (!neighbors[tri.V0].Contains(tri.V2)) neighbors[tri.V0].Add(tri.V2);
                if (!neighbors[tri.V1].Contains(tri.V0)) neighbors[tri.V1].Add(tri.V0);
                if (!neighbors[tri.V1].Contains(tri.V2)) neighbors[tri.V1].Add(tri.V2);
                if (!neighbors[tri.V2].Contains(tri.V0)) neighbors[tri.V2].Add(tri.V0);
                if (!neighbors[tri.V2].Contains(tri.V1)) neighbors[tri.V2].Add(tri.V1);
            }

            Vector3[] temp = new Vector3[nVerts];
            void Step(float factor)
            {
                for (int i = 0; i < nVerts; i++)
                {
                    if ((protectedVertices != null && protectedVertices.Contains(i)) || neighbors[i].Count == 0)
                    {
                        temp[i] = mesh.Vertices[i];
                        continue;
                    }

                    Vector3 avg = Vector3.Zero;
                    foreach (int nb in neighbors[i]) avg += mesh.Vertices[nb];
                    avg /= neighbors[i].Count;

                    Vector3 lap = avg - mesh.Vertices[i];
                    Vector3 nrm = i < mesh.VertexNormals.Count ? mesh.VertexNormals[i] : Vector3.UnitZ;
                    // Chỉ dịch chuyển theo mặt phẳng tiếp tuyến (Tangential relaxation) -> bảo toàn thể tích!
                    Vector3 tanLap = lap - Vector3.Dot(lap, nrm) * nrm;
                    temp[i] = mesh.Vertices[i] + factor * tanLap;
                }

                for (int i = 0; i < nVerts; i++)
                    mesh.Vertices[i] = temp[i];
            }

            for (int it = 0; it < iterations; it++)
            {
                Step(lambda);
                Step(mu);
            }
        }

        /// <summary>
        /// Tạo các đường nét vẽ mẫu 2D chuẩn từ Hình 1 & Hình 5 của bài báo Teddy (SIGGRAPH '99)
        /// với biên mượt, tỷ lệ cân đối và không tự cắt để phồng 3D đẹp mắt.
        /// </summary>
        public static List<PointF> CreatePaperSampleStroke(string presetKey, Size viewport)
        {
            float cx = viewport.Width * 0.5f;
            float cy = viewport.Height * 0.5f;
            float scale = MathF.Min(viewport.Width, viewport.Height) * 0.36f;

            switch (presetKey)
            {
                case "Sphere":
                    return CreateSphereSampleStroke(cx, cy, scale);

                case "Cube":
                    return CreateCubeSampleStroke(cx, cy, scale);

                case "Cherry":
                    return CreateCherrySampleStroke(cx, cy, scale);

                case "Snake":
                    return CreateSnakeSampleStroke(cx, cy, scale);

                case "TeddySilhouette":
                default:
                    return CreateTeddySampleStroke(cx, cy, scale);
            }
        }

        private static List<PointF> CreateSphereSampleStroke(float cx, float cy, float scale)
        {
            float r = scale * 0.62f;
            const int segments = 48;
            var pts = new List<PointF>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i % segments) * (MathF.PI * 2f / segments);
                pts.Add(new PointF(cx + MathF.Cos(angle) * r, cy + MathF.Sin(angle) * r));
            }
            return pts;
        }

        private static List<PointF> CreateCubeSampleStroke(float cx, float cy, float scale)
        {
            float h = scale * 0.54f;
            var corners = new PointF[]
            {
                new(cx - h, cy - h),
                new(cx + h, cy - h),
                new(cx + h, cy + h),
                new(cx - h, cy + h)
            };

            const int stepsPerSide = 12;
            var pts = new List<PointF>(stepsPerSide * 4 + 1);
            for (int side = 0; side < 4; side++)
            {
                PointF a = corners[side];
                PointF b = corners[(side + 1) % 4];
                for (int s = 0; s < stepsPerSide; s++)
                {
                    float t = s / (float)stepsPerSide;
                    pts.Add(new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                }
            }
            pts.Add(corners[0]);
            return pts;
        }

        private static List<PointF> CreateTeddySampleStroke(float cx, float cy, float scale)
        {
            // Tọa độ chuẩn hóa Y-up: (-1..1, -1.03..1.07) -> chuyển sang tọa độ màn hình (cx + x*s, cy - y*s)
            float s = scale * 0.90f;
            var rightHalf = new Vector2[]
            {
                new(0.00f,  0.93f), // Đỉnh đầu giữa
                new(0.11f,  0.92f),
                new(0.20f,  0.89f), // Khe tai phải
                new(0.25f,  1.02f), // Tai phải lên
                new(0.39f,  1.07f), // Chóp tai phải
                new(0.52f,  1.00f),
                new(0.55f,  0.86f),
                new(0.47f,  0.76f), // Chân tai phải
                new(0.49f,  0.65f), // Má phải
                new(0.46f,  0.52f),
                new(0.37f,  0.41f),
                new(0.28f,  0.35f), // Cổ thắt nhẹ
                new(0.45f,  0.31f), // Vai phải
                new(0.66f,  0.25f), // Tay trên phải
                new(0.81f,  0.15f),
                new(0.84f,  0.04f), // Bàn tay phải
                new(0.76f, -0.06f),
                new(0.58f, -0.07f), // Tay dưới phải
                new(0.47f, -0.06f), // Nách phải
                new(0.50f, -0.20f), // Thân phải
                new(0.54f, -0.40f),
                new(0.57f, -0.60f), // Hông phải
                new(0.58f, -0.80f), // Chân ngoài phải
                new(0.52f, -0.96f),
                new(0.41f, -1.03f), // Đế chân phải
                new(0.28f, -1.02f),
                new(0.19f, -0.94f),
                new(0.16f, -0.78f), // Chân trong phải
                new(0.15f, -0.66f),
                new(0.08f, -0.62f), // Háng
                new(0.00f, -0.61f)
            };

            var controlPoly = new List<Vector2>(rightHalf.Length * 2 - 2);
            for (int i = 0; i < rightHalf.Length; i++)
                controlPoly.Add(rightHalf[i]);

            // Đối xứng sang nửa trái theo thứ tự khép kín (bỏ điểm đầu/cuối trùng trục X=0)
            for (int i = rightHalf.Length - 2; i >= 1; i--)
                controlPoly.Add(new Vector2(-rightHalf[i].X, rightHalf[i].Y));

            List<Vector2> smoothed = ApplyChaikinClosed(controlPoly, 2);
            var pts = new List<PointF>(smoothed.Count);
            foreach (var p in smoothed)
                pts.Add(new PointF(cx + p.X * s, cy - p.Y * s));

            return pts;
        }

        private static List<PointF> CreateCherrySampleStroke(float cx, float cy, float scale)
        {
            float s = scale * 0.96f;
            var poly = new List<Vector2>();

            // Thân quả Cherry tròn đầy hơi dẹt phần đỉnh bên dưới (-0.78 .. +0.06)
            float fruitCx = -0.10f;
            float fruitCy = -0.36f;
            float rx = 0.46f;
            float ry = 0.42f;
            int fruitSteps = 36;
            float angStart = 1.64f;
            float angEnd = 2f * MathF.PI + 1.34f;

            for (int i = 0; i <= fruitSteps; i++)
            {
                float t = i / (float)fruitSteps;
                float ang = angStart + t * (angEnd - angStart);
                float lobe = 1.0f + 0.05f * MathF.Cos(2f * (ang - MathF.PI * 0.5f));
                poly.Add(new Vector2(
                    fruitCx + rx * lobe * MathF.Cos(ang),
                    fruitCy + ry * lobe * MathF.Sin(ang)));
            }

            // Bờ phải cuống đi lên -> Lá bên phải -> Đỉnh cuống -> Bờ trái cuống quay về đỉnh quả
            var stemAndLeaf = new Vector2[]
            {
                new(0.02f, 0.16f),
                new(0.07f, 0.34f),
                new(0.14f, 0.51f),
                // Nhánh lá xanh bên phải (đủ dày để thổi phồng 3D mượt)
                new(0.26f, 0.48f),
                new(0.47f, 0.44f),
                new(0.68f, 0.47f),
                new(0.82f, 0.57f), // Chóp lá
                new(0.67f, 0.68f),
                new(0.44f, 0.71f),
                new(0.23f, 0.65f),
                // Ngọn cuống uốn cong nhẹ sang phải
                new(0.28f, 0.78f),
                new(0.32f, 0.90f),
                new(0.26f, 0.95f),
                new(0.17f, 0.94f),
                // Bờ trái cuống quay xuống đỉnh quả
                new(0.14f, 0.79f),
                new(0.07f, 0.59f),
                new(-0.02f, 0.39f),
                new(-0.09f, 0.20f)
            };

            poly.AddRange(stemAndLeaf);
            List<Vector2> smoothed = ApplyChaikinClosed(poly, 1);

            var pts = new List<PointF>(smoothed.Count);
            foreach (var p in smoothed)
                pts.Add(new PointF(cx + p.X * s, cy - p.Y * s));

            return pts;
        }

        private static List<PointF> CreateSnakeSampleStroke(float cx, float cy, float scale)
        {
            // Rắn uốn lượn hình chữ S: đầu phình cân đối bên trái, cổ thắt nhẹ, thân đều, đuôi thuôn nhọn bên phải
            float s = scale * 0.96f;
            int n = 48;
            var spine = new Vector2[n];
            var halfW = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float nx = -0.58f + 1.26f * t;
                float ny = 0.24f * MathF.Sin(t * MathF.PI * 1.85f + 0.45f);
                spine[i] = new Vector2(cx + nx * s, cy - ny * s);

                float headBulge = MathF.Exp(-MathF.Pow((t - 0.065f) / 0.055f, 2f)) * 0.070f;
                float neckPinch = MathF.Exp(-MathF.Pow((t - 0.165f) / 0.045f, 2f)) * 0.022f;
                float baseBody = 0.105f * (1f - 0.68f * MathF.Pow(t, 1.7f));
                halfW[i] = MathF.Max(0.032f, baseBody + headBulge - neckPinch) * s;
            }

            var pts = new List<PointF>();
            BuildRibbonStrokeFromSpine(spine, halfW, pts);
            return pts;
        }

        private static List<Vector2> ApplyChaikinClosed(List<Vector2> pts, int iterations)
        {
            List<Vector2> curr = pts;
            for (int it = 0; it < iterations; it++)
            {
                int n = curr.Count;
                var next = new List<Vector2>(n * 2);
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = curr[i];
                    Vector2 b = curr[(i + 1) % n];
                    next.Add(a * 0.75f + b * 0.25f);
                    next.Add(a * 0.25f + b * 0.75f);
                }
                curr = next;
            }
            return curr;
        }

        private static void BuildRibbonStrokeFromSpine(Vector2[] spine, float[] halfW, List<PointF> outPts)
        {
            int n = spine.Length;
            var leftSide = new List<PointF>(n);
            var rightSide = new List<PointF>(n);

            for (int i = 0; i < n; i++)
            {
                int i0 = Math.Max(0, i - 1);
                int i1 = Math.Min(n - 1, i + 1);
                Vector2 tan = Vector2.Normalize(spine[i1] - spine[i0]);
                Vector2 norm = new Vector2(-tan.Y, tan.X);

                Vector2 pL = spine[i] + norm * halfW[i];
                Vector2 pR = spine[i] - norm * halfW[i];
                leftSide.Add(new PointF(pL.X, pL.Y));
                rightSide.Add(new PointF(pR.X, pR.Y));
            }

            // Vòm tròn ở đầu
            Vector2 startDir = Vector2.Normalize(spine[0] - spine[1]);
            PointF headTip = new PointF(spine[0].X + startDir.X * halfW[0], spine[0].Y + startDir.Y * halfW[0]);

            // Vòm nhọn ở đuôi
            Vector2 endDir = Vector2.Normalize(spine[n - 1] - spine[n - 2]);
            PointF tailTip = new PointF(spine[n - 1].X + endDir.X * halfW[n - 1], spine[n - 1].Y + endDir.Y * halfW[n - 1]);

            outPts.Add(headTip);
            outPts.AddRange(leftSide);
            outPts.Add(tailTip);
            rightSide.Reverse();
            outPts.AddRange(rightSide);
        }

        /// <summary>
        /// [Hình 2 trang 1 bài báo] Tự động tô màu đa vùng (Multi-region Face Painting) và nét vẽ 3D
        /// cho 3 mẫu chuẩn (Gấu Teddy, Quả Cherry, Con Rắn).
        /// </summary>
        public static void ApplyPaperPresetPainting(TeddyMesh mesh, string presetKey)
        {
            if (mesh == null || mesh.IsEmpty) return;

            mesh.SurfaceLines.Clear();
            ComputeBoundingBox(mesh, out float minX, out float maxX, out float minY, out float maxY, out float minZ, out float maxZ);

            switch (presetKey)
            {
                case "Cherry":
                {
                    Color berryRed = Color.FromArgb(220, 24, 60);
                    Color berryDeep = Color.FromArgb(165, 16, 48);
                    Color stemGreen = Color.FromArgb(101, 163, 13);
                    Color leafGreen = Color.FromArgb(34, 197, 94);
                    mesh.BaseColor = berryRed;

                    float h = Math.Max(1f, maxY - minY);
                    float unit = h / 1.73f;
                    float zeroY = minY + 0.78f * unit;
                    float zeroX = minX + 0.56f * unit;

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        float nx = (c.X - zeroX) / unit;
                        float ny = (c.Y - zeroY) / unit;

                        if (ny <= 0.08f)
                            tri.CustomColor = ny < -0.60f ? berryDeep : berryRed;
                        else if (ny > 0.36f && nx > 0.21f)
                            tri.CustomColor = leafGreen;
                        else
                            tri.CustomColor = stemGreen;
                    }

                    // Vệt bóng phản chiếu trên mặt trước quả Cherry
                    AddProjectedSurfacePolyline(mesh, new[]
                    {
                        new Vector2(zeroX - 0.34f * unit, zeroY - 0.18f * unit),
                        new Vector2(zeroX - 0.38f * unit, zeroY - 0.29f * unit),
                        new Vector2(zeroX - 0.35f * unit, zeroY - 0.41f * unit)
                    }, Color.White, 4.5f, false, 3.0f);

                    // Gân lá xanh đậm
                    AddProjectedSurfacePolyline(mesh, new[]
                    {
                        new Vector2(zeroX + 0.20f * unit, zeroY + 0.56f * unit),
                        new Vector2(zeroX + 0.48f * unit, zeroY + 0.57f * unit),
                        new Vector2(zeroX + 0.75f * unit, zeroY + 0.57f * unit)
                    }, Color.FromArgb(21, 128, 61), 2.4f, false, 2.5f);
                    break;
                }

                case "Snake":
                {
                    Color emeraldGreen = Color.FromArgb(34, 197, 94);
                    Color headGreen = Color.FromArgb(22, 163, 74);
                    Color saddleDark = Color.FromArgb(20, 83, 45);
                    Color diamondLime = Color.FromArgb(132, 204, 22);
                    Color bellyYellow = Color.FromArgb(254, 240, 138);
                    Color tailAccent = Color.FromArgb(163, 230, 53);
                    mesh.BaseColor = emeraldGreen;

                    float spanX = Math.Max(1f, maxX - minX);
                    float spanY = Math.Max(1f, maxY - minY);

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        float u = (c.X - minX) / spanX;

                        if (c.Z < -Math.Max(6f, maxZ * 0.22f))
                        {
                            tri.CustomColor = bellyYellow;
                        }
                        else if (u < 0.16f)
                        {
                            tri.CustomColor = headGreen;
                        }
                        else if (u > 0.89f)
                        {
                            tri.CustomColor = tailAccent;
                        }
                        else
                        {
                            float bandWave = MathF.Sin((u - 0.16f) * MathF.PI * 11f);
                            if (bandWave > 0.35f)
                                tri.CustomColor = saddleDark;
                            else if (bandWave < -0.55f)
                                tri.CustomColor = diamondLime;
                            else
                                tri.CustomColor = emeraldGreen;
                        }
                    }

                    // Tìm mũi đầu rắn (minX) và tâm đầu rắn để đặt cặp mắt + lưỡi chẻ đôi
                    Vector3 snout = mesh.Vertices[0];
                    for (int i = 1; i < mesh.Vertices.Count; i++)
                    {
                        if (mesh.Vertices[i].X < snout.X)
                            snout = mesh.Vertices[i];
                    }

                    float headMinX = minX + spanX * 0.045f;
                    float headMaxX = minX + spanX * 0.115f;
                    Vector2 headSum = Vector2.Zero;
                    int headCount = 0;
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        Vector3 v = mesh.Vertices[i];
                        if (v.X >= headMinX && v.X <= headMaxX)
                        {
                            headSum += new Vector2(v.X, v.Y);
                            headCount++;
                        }
                    }

                    Vector2 headCenter = headCount > 0
                        ? headSum / headCount
                        : new Vector2(minX + spanX * 0.08f, snout.Y);

                    Vector2 forward = Vector2.Normalize(new Vector2(snout.X, snout.Y) - headCenter);
                    if (float.IsNaN(forward.X)) forward = new Vector2(-1f, -0.3f);
                    Vector2 sideNormal = new Vector2(-forward.Y, forward.X);

                    float eyeSpread = Math.Max(7f, spanY * 0.07f);
                    float eyeR = Math.Max(2.6f, spanY * 0.025f);
                    Vector2 leftEye = headCenter + sideNormal * eyeSpread;
                    Vector2 rightEye = headCenter - sideNormal * eyeSpread;

                    AddProjectedCircleLine(mesh, leftEye.X, leftEye.Y, eyeR, Color.FromArgb(250, 204, 21), 2.8f, 3.0f);
                    AddProjectedCircleLine(mesh, rightEye.X, rightEye.Y, eyeR, Color.FromArgb(250, 204, 21), 2.8f, 3.0f);
                    AddProjectedCircleLine(mesh, leftEye.X, leftEye.Y, eyeR * 0.45f, Color.FromArgb(20, 20, 20), 2.6f, 3.3f);
                    AddProjectedCircleLine(mesh, rightEye.X, rightEye.Y, eyeR * 0.45f, Color.FromArgb(20, 20, 20), 2.6f, 3.3f);

                    // Lưỡi chẻ đôi màu đỏ từ chóp mõm
                    float tongueLen = Math.Max(14f, spanX * 0.085f);
                    Vector3 tongueBase = new Vector3(snout.X, snout.Y, 2.5f);
                    Vector3 tongueMid = new Vector3(snout.X + forward.X * tongueLen * 0.58f, snout.Y + forward.Y * tongueLen * 0.58f, 2.5f);
                    Vector3 forkA = tongueMid + new Vector3((forward.X * 0.42f + sideNormal.X * 0.28f) * tongueLen, (forward.Y * 0.42f + sideNormal.Y * 0.28f) * tongueLen, 0f);
                    Vector3 forkB = tongueMid + new Vector3((forward.X * 0.42f - sideNormal.X * 0.28f) * tongueLen, (forward.Y * 0.42f - sideNormal.Y * 0.28f) * tongueLen, 0f);

                    mesh.SurfaceLines.Add(new TeddySurfaceLine
                    {
                        StrokeColor = Color.FromArgb(239, 68, 68),
                        StrokeWidth = 2.8f,
                        IsClosedRing = false,
                        Points = { tongueBase, tongueMid, forkA }
                    });
                    mesh.SurfaceLines.Add(new TeddySurfaceLine
                    {
                        StrokeColor = Color.FromArgb(239, 68, 68),
                        StrokeWidth = 2.8f,
                        IsClosedRing = false,
                        Points = { tongueMid, forkB }
                    });
                    break;
                }

                case "TeddySilhouette":
                default:
                {
                    Color furWarm = Color.FromArgb(214, 128, 62);
                    Color creamPatch = Color.FromArgb(254, 243, 199);
                    Color innerEar = Color.FromArgb(245, 208, 178);
                    Color darkEarPaw = Color.FromArgb(138, 62, 24);
                    mesh.BaseColor = furWarm;

                    float cxMesh = 0.5f * (minX + maxX);
                    float h = Math.Max(1f, maxY - minY);
                    float unit = h / 2.10f;
                    float zeroY = minY + 1.03f * unit;

                    // Đẩy nhẹ vùng mõm ra phía trước (+Z) và sau (-Z) để mặt gấu có khối mũi 3D tự nhiên
                    float zThreshold = Math.Max(4f, maxZ * 0.15f);
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        Vector3 v = mesh.Vertices[i];
                        if (MathF.Abs(v.Z) < zThreshold) continue;

                        float nx = (v.X - cxMesh) / unit;
                        float ny = (v.Y - zeroY) / unit;
                        float snoutDist = MathF.Sqrt((nx / 0.20f) * (nx / 0.20f) + ((ny - 0.57f) / 0.15f) * ((ny - 0.57f) / 0.15f));
                        if (snoutDist < 1.0f)
                        {
                            float bump = (1f - snoutDist) * (1f - snoutDist) * (unit * 0.15f);
                            v.Z += MathF.Sign(v.Z) * bump;
                            mesh.Vertices[i] = v;
                        }
                    }

                    mesh.RecalculateNormals();
                    ComputeBoundingBox(mesh, out minX, out maxX, out minY, out maxY, out minZ, out maxZ);

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        float nx = (c.X - cxMesh) / unit;
                        float absNx = MathF.Abs(nx);
                        float ny = (c.Y - zeroY) / unit;

                        // Bụng kem phía trước
                        if (c.Z > maxZ * 0.22f && MathF.Pow(nx / 0.28f, 2f) + MathF.Pow((ny + 0.18f) / 0.30f, 2f) < 1.0f)
                        {
                            tri.CustomColor = creamPatch;
                        }
                        // Mõm kem phía trước
                        else if (c.Z > maxZ * 0.24f && MathF.Pow(nx / 0.19f, 2f) + MathF.Pow((ny - 0.57f) / 0.14f, 2f) < 1.0f)
                        {
                            tri.CustomColor = creamPatch;
                        }
                        // Lòng tai hồng kem & vành tai nâu sẫm
                        else if (ny > 0.78f && absNx > 0.23f)
                        {
                            float earDist = MathF.Sqrt(MathF.Pow(absNx - 0.39f, 2f) + MathF.Pow(ny - 0.90f, 2f));
                            tri.CustomColor = (c.Z > maxZ * 0.05f && earDist < 0.095f) ? innerEar : darkEarPaw;
                        }
                        // Đệm chân & lòng bàn tay
                        else if (c.Z > maxZ * 0.10f && MathF.Sqrt(MathF.Pow(absNx - 0.73f, 2f) + MathF.Pow(ny - 0.05f, 2f)) < 0.085f)
                        {
                            tri.CustomColor = creamPatch;
                        }
                        else if (ny < -0.76f && absNx > 0.18f)
                        {
                            float soleDist = MathF.Sqrt(MathF.Pow(absNx - 0.37f, 2f) + MathF.Pow(ny + 0.89f, 2f));
                            tri.CustomColor = (c.Z > maxZ * 0.10f && soleDist < 0.115f) ? creamPatch : darkEarPaw;
                        }
                        else
                        {
                            tri.CustomColor = furWarm;
                        }
                    }

                    // Vẽ 2 mắt tròn, mũi bầu dục & nụ cười trên mặt trước (+Z)
                    Color featureDark = Color.FromArgb(30, 24, 20);
                    AddProjectedCircleLine(mesh, cxMesh - 0.145f * unit, zeroY + 0.72f * unit, 0.042f * unit, featureDark, 3.6f, 2.8f);
                    AddProjectedCircleLine(mesh, cxMesh + 0.145f * unit, zeroY + 0.72f * unit, 0.042f * unit, featureDark, 3.6f, 2.8f);

                    // Mũi bầu dục
                    AddProjectedEllipseLine(mesh, cxMesh, zeroY + 0.61f * unit, 0.062f * unit, 0.040f * unit, featureDark, 4.2f, 3.2f);

                    // Nụ cười neo chữ W
                    AddProjectedSurfacePolyline(mesh, new[]
                    {
                        new Vector2(cxMesh, zeroY + 0.57f * unit),
                        new Vector2(cxMesh, zeroY + 0.515f * unit)
                    }, featureDark, 2.5f, false, 3.0f);

                    AddProjectedSurfacePolyline(mesh, new[]
                    {
                        new Vector2(cxMesh - 0.095f * unit, zeroY + 0.535f * unit),
                        new Vector2(cxMesh - 0.048f * unit, zeroY + 0.505f * unit),
                        new Vector2(cxMesh, zeroY + 0.515f * unit),
                        new Vector2(cxMesh + 0.048f * unit, zeroY + 0.505f * unit),
                        new Vector2(cxMesh + 0.095f * unit, zeroY + 0.535f * unit)
                    }, featureDark, 2.5f, false, 3.0f);
                    break;
                }
            }
        }

        private static void ComputeBoundingBox(
            TeddyMesh mesh,
            out float minX, out float maxX,
            out float minY, out float maxY,
            out float minZ, out float maxZ)
        {
            minX = minY = minZ = float.MaxValue;
            maxX = maxY = maxZ = float.MinValue;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3 v = mesh.Vertices[i];
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }
        }

        private static Vector3 ProjectPointOntoFrontSurface(TeddyMesh mesh, float x, float y, float zBias)
        {
            float sumW = 0f;
            float sumZ = 0f;
            float bestDistSq = float.MaxValue;
            float fallbackZ = 6f;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3 v = mesh.Vertices[i];
                if (v.Z < -0.5f) continue;

                float dx = v.X - x;
                float dy = v.Y - y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    fallbackZ = v.Z;
                }

                if (d2 < 900f)
                {
                    float w = 1f / (d2 + 12f);
                    sumW += w;
                    sumZ += w * v.Z;
                }
            }

            float z = sumW > 0f ? (sumZ / sumW) : fallbackZ;
            return new Vector3(x, y, z + zBias);
        }

        private static void AddProjectedSurfacePolyline(
            TeddyMesh mesh,
            IEnumerable<Vector2> pts2D,
            Color color,
            float width,
            bool closed,
            float zBias)
        {
            var line = new TeddySurfaceLine
            {
                StrokeColor = color,
                StrokeWidth = width,
                IsClosedRing = closed
            };

            foreach (var p in pts2D)
                line.Points.Add(ProjectPointOntoFrontSurface(mesh, p.X, p.Y, zBias));

            mesh.SurfaceLines.Add(line);
        }

        private static void AddProjectedCircleLine(TeddyMesh mesh, float cx, float cy, float radius, Color color, float width, float zBias)
        {
            AddProjectedEllipseLine(mesh, cx, cy, radius, radius, color, width, zBias);
        }

        private static void AddProjectedEllipseLine(TeddyMesh mesh, float cx, float cy, float rx, float ry, Color color, float width, float zBias)
        {
            const int segments = 14;
            var pts = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * MathF.PI * i / segments;
                pts[i] = new Vector2(cx + rx * MathF.Cos(a), cy + ry * MathF.Sin(a));
            }
            AddProjectedSurfacePolyline(mesh, pts, color, width, true, zBias);
        }
    }
}
