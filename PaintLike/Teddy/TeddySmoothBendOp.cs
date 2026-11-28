using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV2-05, TV2-09 & TV2-10] Thực hiện phép Làm mượt vùng chọn bằng Xóa Lỗ + Steiner-CDT + Cubic Bezier đa mặt phẳng
    /// (Mục 4.6 & 5.5, Hình 10, 21), phép Uốn biến dạng 1 nét Auto-Spine hoặc 2 nét (Mục 4.7, Hình 11),
    /// tạo gờ sắc (Crease Stroke) và xoắn khối (Twisting - Mục 8).
    /// </summary>
    public static class TeddySmoothBendOp
    {
        /// <summary>
        /// [TV2-09 & Mục 4.6, 5.5 - Hình 10, 21] Làm mượt vùng bề mặt được bao quanh bởi Vòng Đế Đỏ (ActiveBaseRing):
        /// 1. Định khung cục bộ Z || ringNormal, tách vòng biên lỗ đơn và lưu pháp tuyến mặt ngoài.
        /// 2. Xóa toàn bộ tam giác cũ nhăn nhúm bên trong vòng (True Hole-Deleting).
        /// 3. Chia lưới lỗ mới bằng Delaunay2D.TriangulateQualitySteinerCDT (TV1-08).
        /// 4. Nội suy độ cao Z từng đỉnh Steiner qua các cặp mặt phẳng đối diện bằng Cubic Bezier khớp tiếp tuyến mặt ngoài (Hình 21c).
        /// 5. Áp dụng Taubin Surface Fairing cục bộ trên các đỉnh mới (Hình 21d) và lưu dữ liệu minh họa Bước 7.
        /// </summary>
        public static bool TrySmoothRegionInBaseRing(
            TeddyMesh mesh,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || mesh.ActiveBaseRing == null || mesh.ActiveBaseRing.Points.Count < 4)
            {
                statusMessage = "Chưa có vòng chọn Base Ring để làm mượt.";
                return false;
            }

            var ringPts = mesh.ActiveBaseRing.Points;
            Vector3 ringCenter = Vector3.Zero;
            foreach (var p in ringPts) ringCenter += p;
            ringCenter /= ringPts.Count;

            float ringRadius = 0f;
            foreach (var p in ringPts) ringRadius += Vector3.Distance(p, ringCenter);
            ringRadius /= ringPts.Count;
            if (ringRadius < 4f) ringRadius = 4f;

            Vector3 ringNormal = TeddyRaycaster.ComputeRingNormal(ringPts);

            // Kiểm tra hướng pháp tuyến ra ngoài bề mặt
            Vector3 nearestFaceNormal = Vector3.Zero;
            float bestDist = float.MaxValue;
            foreach (var tri in mesh.Triangles)
            {
                Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                float d = Vector3.DistanceSquared(c, ringCenter);
                if (d < bestDist)
                {
                    bestDist = d;
                    nearestFaceNormal = tri.FaceNormal;
                }
            }
            if (nearestFaceNormal.LengthSquared() > 1e-4f && Vector3.Dot(ringNormal, nearestFaceNormal) < 0f)
                ringNormal = -ringNormal;

            // Hệ trục tọa độ cục bộ của vòng chọn (Z-axis song song với ringNormal - Mục 5.5)
            Vector3 uAxis = ringPts[0] - ringCenter - Vector3.Dot(ringPts[0] - ringCenter, ringNormal) * ringNormal;
            uAxis = uAxis.LengthSquared() > 1e-4f ? Vector3.Normalize(uAxis) : Vector3.UnitX;
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(ringNormal, uAxis));

            var ring2D = new List<Vector2>(ringPts.Count);
            var ringZ = new float[ringPts.Count];
            float avgRingZ = 0f;
            for (int i = 0; i < ringPts.Count; i++)
            {
                Vector3 rel = ringPts[i] - ringCenter;
                ring2D.Add(new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis)));
                float rz = Vector3.Dot(rel, ringNormal);
                ringZ[i] = rz;
                avgRingZ += rz;
            }
            avgRingZ /= ringPts.Count;

            // =========================================================================
            // [TV2-09] BƯỚC 1: TÌM CÁC TAM GIÁC BÊN TRONG VÒNG VÀ TÁCH BIÊN LỖ THỦNG ĐƠN
            // =========================================================================
            var insideTriIndices = new HashSet<int>();
            var exteriorNormalSum = new Vector3[mesh.Vertices.Count];

            for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
            {
                var tri = mesh.Triangles[tIdx];
                Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                Vector3 rel = c - ringCenter;
                float zVal = Vector3.Dot(rel, ringNormal);
                Vector2 xy = new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis));

                bool facingSameSide = Vector3.Dot(tri.FaceNormal, ringNormal) > -0.25f;
                bool insidePoly = TeddyExtrudeCutOp.IsPointInPolygon2D(xy, ring2D);
                if (facingSameSide && insidePoly && MathF.Abs(zVal) <= ringRadius * 1.65f)
                {
                    insideTriIndices.Add(tIdx);
                }
                else
                {
                    exteriorNormalSum[tri.V0] += tri.FaceNormal;
                    exteriorNormalSum[tri.V1] += tri.FaceNormal;
                    exteriorNormalSum[tri.V2] += tri.FaceNormal;
                }
            }

            if (insideTriIndices.Count >= 2 && insideTriIndices.Count < mesh.Triangles.Count)
            {
                // Giữ lại vùng liên thông lớn nhất chứa tâm ringCenter để tránh lỗ nhiều mảnh hoặc đỉnh chạm góc nhọn (pinch)
                if (mesh.Edges.Count == 0) mesh.BuildConnectivity();
                var mainComponent = ExtractLargestConnectedInsideComponent(mesh, insideTriIndices, ringCenter);
                if (mainComponent.Count >= 2 && mainComponent.Count < insideTriIndices.Count)
                {
                    insideTriIndices = mainComponent;
                    Array.Clear(exteriorNormalSum, 0, exteriorNormalSum.Length);
                    for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
                    {
                        if (!insideTriIndices.Contains(tIdx))
                        {
                            var tr = mesh.Triangles[tIdx];
                            exteriorNormalSum[tr.V0] += tr.FaceNormal;
                            exteriorNormalSum[tr.V1] += tr.FaceNormal;
                            exteriorNormalSum[tr.V2] += tr.FaceNormal;
                        }
                    }
                }

                // Tìm tất cả cạnh biên có hướng (a -> b) chỉ thuộc insideTriIndices mà cạnh ngược (b -> a) không thuộc insideTriIndices
                var insideHalfEdges = new HashSet<(int A, int B)>();
                var insideDirectedList = new List<(int A, int B)>();
                foreach (int tIdx in insideTriIndices)
                {
                    var tri = mesh.Triangles[tIdx];
                    insideHalfEdges.Add((tri.V0, tri.V1)); insideDirectedList.Add((tri.V0, tri.V1));
                    insideHalfEdges.Add((tri.V1, tri.V2)); insideDirectedList.Add((tri.V1, tri.V2));
                    insideHalfEdges.Add((tri.V2, tri.V0)); insideDirectedList.Add((tri.V2, tri.V0));
                }

                var boundaryAdj = new Dictionary<int, List<int>>();
                foreach (var e in insideDirectedList)
                {
                    if (!insideHalfEdges.Contains((e.B, e.A)))
                    {
                        if (!boundaryAdj.TryGetValue(e.A, out var list))
                        {
                            list = new List<int>(2);
                            boundaryAdj[e.A] = list;
                        }
                        list.Add(e.B);
                    }
                }

                // Kiểm tra biên lỗ có đúng 1 chu trình đơn (mọi đỉnh bậc ra = 1) hay không
                List<int>? holeLoop = TryExtractLargestSimpleLoop(boundaryAdj);
                if (holeLoop != null && holeLoop.Count >= 5 && boundaryAdj.Values.All(l => l.Count == 1))
                {
                    var holePoly2D = new List<Vector2>(holeLoop.Count);
                    var holeZ = new float[holeLoop.Count];
                    var holeExtNormals = new Vector3[holeLoop.Count];

                    for (int i = 0; i < holeLoop.Count; i++)
                    {
                        int vIdx = holeLoop[i];
                        Vector3 rel = mesh.Vertices[vIdx] - ringCenter;
                        holePoly2D.Add(new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis)));
                        holeZ[i] = Vector3.Dot(rel, ringNormal);

                        Vector3 nExt = exteriorNormalSum[vIdx];
                        holeExtNormals[i] = nExt.LengthSquared() > 1e-5f ? Vector3.Normalize(nExt) : ringNormal;
                    }

                    // Nếu vòng lỗ không tự cắt trong mặt phẳng chiếu cục bộ (Sec. 5.5) -> thực thi True Hole-Deleting Steiner-CDT + Tangent Cubic Bezier!
                    if (!HasSelfIntersection2D(holePoly2D))
                    {
                        SteinerCdtResult cdt = Delaunay2D.TriangulateQualitySteinerCDT(
                            holePoly2D,
                            minAngleDegrees: 20.5f,
                            maxEdgeLength: MathF.Max(11f, ringRadius * 0.34f),
                            allowBoundarySplit: false,
                            maxSteinerPoints: 128);

                        if (cdt.Triangles.Count > 0 && cdt.BoundaryVertexCount == holeLoop.Count)
                        {
                            // BƯỚC 2: Xóa sạch tam giác cũ trong vùng khoanh
                            var keptTris = new List<TeddyTriangle>(mesh.Triangles.Count);
                            for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
                            {
                                if (!insideTriIndices.Contains(tIdx))
                                    keptTris.Add(mesh.Triangles[tIdx]);
                            }

                            int nHole = holeLoop.Count;
                            int halfHole = Math.Max(1, nHole / 2);
                            var cdtToMeshVert = new int[cdt.Vertices.Count];
                            for (int i = 0; i < nHole; i++)
                                cdtToMeshVert[i] = holeLoop[i];

                            // BƯỚC 3: Nâng độ cao Z cho từng điểm Steiner bằng Multi-Plane Tangent-Matched Cubic Bezier (Hình 21c)
                            for (int k = nHole; k < cdt.Vertices.Count; k++)
                            {
                                Vector2 xy = cdt.Vertices[k];
                                float sumZ = 0f;
                                float sumW = 0f;

                                for (int e = 0; e < nHole; e++)
                                {
                                    int eOpp = (e + halfHole) % nHole;
                                    Vector2 pA = holePoly2D[e];
                                    Vector2 pB = holePoly2D[eOpp];
                                    Vector2 chord = pB - pA;
                                    float chordLen = chord.Length();
                                    if (chordLen < 1e-3f) continue;

                                    Vector2 dir2D = chord / chordLen;
                                    float proj = Vector2.Dot(xy - pA, dir2D);
                                    float t = Math.Clamp(proj / chordLen, 0f, 1f);

                                    Vector2 perp = (xy - pA) - proj * dir2D;
                                    float distPlane = perp.Length();
                                    float weight = 1.0f / (2.5f + distPlane * distPlane);

                                    Vector3 dir3D = Vector3.Normalize(uAxis * dir2D.X + vAxis * dir2D.Y);
                                    Vector3 nA = holeExtNormals[e];
                                    Vector3 nB = holeExtNormals[eOpp];
                                    float denomA = MathF.Max(0.28f, Vector3.Dot(nA, ringNormal));
                                    float denomB = MathF.Max(0.28f, Vector3.Dot(nB, ringNormal));

                                    // Độ dốc dz/ds = -(n · dir) / (n · ringNormal) để mặt mới khớp tiếp tuyến C1 với bề mặt ngoài
                                    float slopeA = Math.Clamp(-Vector3.Dot(nA, dir3D) / denomA, -1.25f, 1.25f);
                                    float slopeB = Math.Clamp(-Vector3.Dot(nB, dir3D) / denomB, -1.25f, 1.25f);

                                    float z0 = holeZ[e];
                                    float z3 = holeZ[eOpp];
                                    float z1 = z0 + (chordLen / 3f) * slopeA;
                                    float z2 = z3 - (chordLen / 3f) * slopeB;

                                    float omt = 1f - t;
                                    float zBezier = omt * omt * omt * z0
                                                  + 3f * omt * omt * t * z1
                                                  + 3f * omt * t * t * z2
                                                  + t * t * t * z3;

                                    sumZ += weight * zBezier;
                                    sumW += weight;
                                }

                                float finalZ = sumW > 1e-5f ? (sumZ / sumW) : avgRingZ;
                                Vector3 worldPt = ringCenter + uAxis * xy.X + vAxis * xy.Y + ringNormal * finalZ;
                                cdtToMeshVert[k] = mesh.Vertices.Count;
                                mesh.Vertices.Add(worldPt);
                            }

                            // Đưa các đỉnh mới vào danh sách được phép làm mịn, khóa toàn bộ phần vỏ ngoài (kể cả biên holeLoop)
                            var newSteinerSet = new HashSet<int>();
                            for (int k = nHole; k < cdt.Vertices.Count; k++)
                                newSteinerSet.Add(cdtToMeshVert[k]);

                            var lockedAllExterior = new HashSet<int>();
                            for (int vIdx = 0; vIdx < mesh.Vertices.Count; vIdx++)
                            {
                                if (!newSteinerSet.Contains(vIdx))
                                    lockedAllExterior.Add(vIdx);
                            }

                            // BƯỚC 4: Khâu tam giác Steiner-CDT mới vào lưới với pháp tuyến hướng ra ngoài +ringNormal
                            var smoothTriDebug = new List<(Vector3 A, Vector3 B, Vector3 C)>(cdt.Triangles.Count);
                            foreach (var st in cdt.Triangles)
                            {
                                int i0 = cdtToMeshVert[st.V0];
                                int i1 = cdtToMeshVert[st.V1];
                                int i2 = cdtToMeshVert[st.V2];
                                if (i0 == i1 || i1 == i2 || i2 == i0) continue;

                                Vector3 p0 = mesh.Vertices[i0];
                                Vector3 p1 = mesh.Vertices[i1];
                                Vector3 p2 = mesh.Vertices[i2];
                                Vector3 triN = Vector3.Cross(p1 - p0, p2 - p0);
                                if (Vector3.Dot(triN, ringNormal) < 0f)
                                {
                                    keptTris.Add(new TeddyTriangle(i0, i2, i1));
                                }
                                else
                                {
                                    keptTris.Add(new TeddyTriangle(i0, i1, i2));
                                }
                                smoothTriDebug.Add((p0, p1, p2));
                            }

                            mesh.Triangles.Clear();
                            mesh.Triangles.AddRange(keptTris);

                            // Taubin Surface Fairing chỉ trên các đỉnh Steiner vừa chèn (Hình 21d)
                            if (newSteinerSet.Count > 0)
                            {
                                mesh.SmoothMesh(4, 0.45f, -0.47f, lockedAllExterior);
                            }

                            // Tạo tối đa 4 đường cong vòm Cubic Bezier minh họa cho Bước 7 (TV3-08)
                            var bezierArchDebug = BuildDebugBezierArches(holePoly2D, holeZ, holeExtNormals, ringCenter, ringNormal, uAxis, vAxis);

                            if (mesh.DebugInfo == null) mesh.DebugInfo = new TeddyAlgorithmDebugInfo();
                            mesh.DebugInfo.SmoothHoleBoundary3D = holeLoop.Select(idx => mesh.Vertices[idx]).ToList();
                            mesh.DebugInfo.SmoothBezierArches3D = bezierArchDebug;
                            mesh.DebugInfo.SmoothSteinerTriangles3D = smoothTriDebug;

                            TeddyExtrudeCutOp.CompactMeshVertices(mesh);
                            mesh.ActiveBaseRing = null;
                            mesh.BuildConnectivity();
                            mesh.RecalculateNormals();

                            statusMessage = $"Đã Xóa lỗ & Đắp lưới Steiner-CDT + Cubic Bezier (TV2-09 Hình 10 & 21): {cdt.Triangles.Count} tam giác ({cdt.InsertedSteinerCount} điểm Steiner, góc min {cdt.MinAngleDegrees:F1}°)!";
                            return true;
                        }
                    }
                }
            }

            // Fallback an toàn: Nếu vòng chọn bao phủ vùng không tách được lỗ đơn (quá hẹp / mép cắt), nâng Bezier tại chỗ + Taubin Fairing
            return ApplyInPlaceBezierSmoothingFallback(mesh, ring2D, ringZ, avgRingZ, ringCenter, ringNormal, uAxis, vAxis, ringRadius, out statusMessage);
        }

        private static HashSet<int> ExtractLargestConnectedInsideComponent(TeddyMesh mesh, HashSet<int> insideSet, Vector3 ringCenter)
        {
            var visited = new HashSet<int>();
            HashSet<int>? bestComp = null;
            float bestCenterDist = float.MaxValue;

            foreach (int seed in insideSet)
            {
                if (visited.Contains(seed)) continue;
                var comp = new HashSet<int>();
                var q = new Queue<int>();
                q.Enqueue(seed);
                visited.Add(seed);
                float minD = float.MaxValue;

                while (q.Count > 0)
                {
                    int curr = q.Dequeue();
                    comp.Add(curr);
                    var tr = mesh.Triangles[curr];
                    Vector3 c = (mesh.Vertices[tr.V0] + mesh.Vertices[tr.V1] + mesh.Vertices[tr.V2]) / 3f;
                    float d = Vector3.DistanceSquared(c, ringCenter);
                    if (d < minD) minD = d;

                    for (int e = 0; e < 3; e++)
                    {
                        int nb = tr.Neighbors[e];
                        if (nb >= 0 && insideSet.Contains(nb) && visited.Add(nb))
                            q.Enqueue(nb);
                    }
                }

                if (bestComp == null || comp.Count > bestComp.Count || (comp.Count == bestComp.Count && minD < bestCenterDist))
                {
                    bestComp = comp;
                    bestCenterDist = minD;
                }
            }

            return bestComp ?? insideSet;
        }

        private static List<int>? TryExtractLargestSimpleLoop(Dictionary<int, List<int>> adj)
        {
            if (adj.Count < 3) return null;
            var visited = new HashSet<int>();
            List<int>? bestLoop = null;

            foreach (int start in adj.Keys)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<int>();
                int curr = start;
                int guard = adj.Count + 4;
                bool closed = false;

                while (guard-- > 0)
                {
                    if (!visited.Add(curr))
                    {
                        closed = (curr == start && loop.Count >= 3);
                        break;
                    }
                    loop.Add(curr);
                    if (!adj.TryGetValue(curr, out var nexts) || nexts.Count == 0)
                        break;
                    curr = nexts[0];
                }

                if (closed && (bestLoop == null || loop.Count > bestLoop.Count))
                    bestLoop = loop;
            }
            return bestLoop;
        }

        private static List<Vector3[]> BuildDebugBezierArches(
            List<Vector2> holePoly2D,
            float[] holeZ,
            Vector3[] holeExtNormals,
            Vector3 ringCenter,
            Vector3 ringNormal,
            Vector3 uAxis,
            Vector3 vAxis)
        {
            var arches = new List<Vector3[]>();
            int n = holePoly2D.Count;
            if (n < 4) return arches;

            int half = n / 2;
            int step = Math.Max(1, half / 3);
            for (int e = 0; e < half && arches.Count < 4; e += step)
            {
                int eOpp = (e + half) % n;
                Vector2 pA = holePoly2D[e];
                Vector2 pB = holePoly2D[eOpp];
                Vector2 chord = pB - pA;
                float len = chord.Length();
                if (len < 1e-3f) continue;

                Vector2 dir2D = chord / len;
                Vector3 dir3D = Vector3.Normalize(uAxis * dir2D.X + vAxis * dir2D.Y);
                float slopeA = Math.Clamp(-Vector3.Dot(holeExtNormals[e], dir3D) / MathF.Max(0.28f, Vector3.Dot(holeExtNormals[e], ringNormal)), -1.25f, 1.25f);
                float slopeB = Math.Clamp(-Vector3.Dot(holeExtNormals[eOpp], dir3D) / MathF.Max(0.28f, Vector3.Dot(holeExtNormals[eOpp], ringNormal)), -1.25f, 1.25f);

                float z0 = holeZ[e], z3 = holeZ[eOpp];
                float z1 = z0 + (len / 3f) * slopeA;
                float z2 = z3 - (len / 3f) * slopeB;

                const int segs = 12;
                var pts = new Vector3[segs + 1];
                for (int i = 0; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    float omt = 1f - t;
                    Vector2 xy = Vector2.Lerp(pA, pB, t);
                    float z = omt * omt * omt * z0 + 3f * omt * omt * t * z1 + 3f * omt * t * t * z2 + t * t * t * z3;
                    pts[i] = ringCenter + uAxis * xy.X + vAxis * xy.Y + ringNormal * z;
                }
                arches.Add(pts);
            }
            return arches;
        }

        private static bool HasSelfIntersection2D(List<Vector2> poly)
        {
            int n = poly.Count;
            if (n < 4) return false;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 c = poly[j];
                    Vector2 d = poly[(j + 1) % n];
                    float c1 = Delaunay2D.SignedArea2(a, b, c);
                    float c2 = Delaunay2D.SignedArea2(a, b, d);
                    float c3 = Delaunay2D.SignedArea2(c, d, a);
                    float c4 = Delaunay2D.SignedArea2(c, d, b);
                    if ((c1 * c2 < -1e-4f) && (c3 * c4 < -1e-4f)) return true;
                }
            }
            return false;
        }

        private static bool ApplyInPlaceBezierSmoothingFallback(
            TeddyMesh mesh,
            List<Vector2> ring2D,
            float[] ringZ,
            float avgRingZ,
            Vector3 ringCenter,
            Vector3 ringNormal,
            Vector3 uAxis,
            Vector3 vAxis,
            float ringRadius,
            out string statusMessage)
        {
            var lockedVertices = new HashSet<int>();
            int smoothedVertexCount = 0;
            int nRing = ring2D.Count;
            int halfRing = Math.Max(1, nRing / 2);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3 rel = mesh.Vertices[i] - ringCenter;
                float zVal = Vector3.Dot(rel, ringNormal);
                Vector2 xy = new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis));
                float rDist = xy.Length();

                bool insidePoly = TeddyExtrudeCutOp.IsPointInPolygon2D(xy, ring2D) || rDist <= ringRadius * 1.08f;
                if (insidePoly && MathF.Abs(zVal) <= ringRadius * 1.65f)
                {
                    float sumBezierZ = 0f;
                    float sumWeight = 0f;

                    for (int e = 0; e < halfRing; e++)
                    {
                        Vector2 pA = ring2D[e];
                        Vector2 pB = ring2D[(e + halfRing) % nRing];
                        float zA = ringZ[e];
                        float zB = ringZ[(e + halfRing) % nRing];

                        Vector2 chord = pB - pA;
                        float chordLen = chord.Length();
                        if (chordLen < 1e-3f) continue;

                        Vector2 dir = chord / chordLen;
                        float proj = Vector2.Dot(xy - pA, dir);
                        float t = Math.Clamp(proj / chordLen, 0f, 1f);
                        Vector2 perp = (xy - pA) - proj * dir;
                        float distToPlane = perp.Length();
                        float weight = 1.0f / (4.0f + distToPlane * distToPlane);

                        float archApex = 0.5f * (zA + zB) + 0.12f * (zVal - avgRingZ);
                        float oneMinusT = 1.0f - t;
                        float zBezier = oneMinusT * oneMinusT * zA + 2f * oneMinusT * t * archApex + t * t * zB;

                        sumBezierZ += weight * zBezier;
                        sumWeight += weight;
                    }

                    float targetBezierZ = sumWeight > 1e-5f ? (sumBezierZ / sumWeight) : avgRingZ;
                    float blendedZ = 0.25f * zVal + 0.75f * targetBezierZ;

                    mesh.Vertices[i] = ringCenter + uAxis * xy.X + vAxis * xy.Y + ringNormal * blendedZ;
                    smoothedVertexCount++;
                }
                else
                {
                    lockedVertices.Add(i);
                }
            }

            if (smoothedVertexCount == 0)
            {
                statusMessage = "Không tìm thấy đỉnh lưới nào trong vùng chọn để làm mượt.";
                return false;
            }

            mesh.SmoothMesh(6, 0.50f, -0.51f, lockedVertices);
            mesh.ActiveBaseRing = null;
            mesh.RecalculateNormals();

            statusMessage = $"Đã làm mượt bề mặt vùng chọn bằng nội suy Bezier đa mặt phẳng (Hình 21c-d) trên {smoothedVertexCount} đỉnh.";
            return true;
        }

        /// <summary>
        /// [Hình 2 bài báo] Tô màu chất liệu (Texture/Face Color) cho tất cả các mặt tam giác nằm bên trong vòng chọn ActiveBaseRing.
        /// Giúp tạo các mô hình thú bông đa sắc (mõm kem, tai nâu, má hồng, cuống lá xanh...) giống Hình 2 trang 1.
        /// </summary>
        public static bool TryFillColorInBaseRing(
            TeddyMesh mesh,
            Color fillColor,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || mesh.ActiveBaseRing == null || mesh.ActiveBaseRing.Points.Count < 4)
            {
                statusMessage = "Hãy khoanh một vòng kín (Vòng đỏ) trên bề mặt trước khi tô màu vùng!";
                return false;
            }

            var ringPts = mesh.ActiveBaseRing.Points;
            Vector3 ringCenter = Vector3.Zero;
            foreach (var p in ringPts) ringCenter += p;
            ringCenter /= ringPts.Count;

            float ringRadius = 0f;
            foreach (var p in ringPts) ringRadius += Vector3.Distance(p, ringCenter);
            ringRadius /= ringPts.Count;

            Vector3 ringNormal = TeddyRaycaster.ComputeRingNormal(ringPts);
            Vector3 nearestNormal = Vector3.Zero;
            float bestD = float.MaxValue;
            foreach (var tri in mesh.Triangles)
            {
                Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                float d = Vector3.DistanceSquared(c, ringCenter);
                if (d < bestD) { bestD = d; nearestNormal = tri.FaceNormal; }
            }
            if (nearestNormal.LengthSquared() > 1e-4f && Vector3.Dot(ringNormal, nearestNormal) < 0f)
                ringNormal = -ringNormal;

            Vector3 uAxis = Vector3.Normalize(ringPts[0] - ringCenter - Vector3.Dot(ringPts[0] - ringCenter, ringNormal) * ringNormal);
            if (uAxis.LengthSquared() < 1e-4f) uAxis = Vector3.UnitX;
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(ringNormal, uAxis));

            var ring2D = new List<Vector2>(ringPts.Count);
            foreach (var p in ringPts)
            {
                Vector3 rel = p - ringCenter;
                ring2D.Add(new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis)));
            }

            int paintedFaces = 0;
            foreach (var tri in mesh.Triangles)
            {
                Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                Vector3 rel = c - ringCenter;
                if (Vector3.Dot(tri.FaceNormal, ringNormal) < -0.15f)
                    continue;
                if (MathF.Abs(Vector3.Dot(rel, ringNormal)) > ringRadius * 1.45f)
                    continue;

                Vector2 pt2D = new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis));
                if (TeddyExtrudeCutOp.IsPointInPolygon2D(pt2D, ring2D))
                {
                    tri.CustomColor = fillColor;
                    paintedFaces++;
                }
            }

            mesh.ActiveBaseRing = null;
            statusMessage = $"Đã tô màu vùng chọn cho {paintedFaces} mặt tam giác (Hình 2 bài báo)!";
            return paintedFaces > 0;
        }

        /// <summary>
        /// [Mục 4.7 - Hình 11] Thực hiện phép Uốn / Biến dạng mô hình (Bending / Freeform Warp)
        /// bằng 2 nét vẽ: Reference Stroke (đường chuẩn gốc) và Target Stroke (đường đích).
        /// Dịch chuyển các đỉnh song song với màn hình sao cho quan hệ không gian với Target Stroke
        /// khớp với quan hệ ban đầu đối với Reference Stroke.
        /// </summary>
        public static bool TryBendMeshWithStrokes(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawRefStroke,
            IReadOnlyList<PointF> rawTargetStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty)
            {
                statusMessage = "Chưa có mô hình 3D để uốn.";
                return false;
            }

            if (rawRefStroke == null || rawRefStroke.Count < 3 || rawTargetStroke == null || rawTargetStroke.Count < 3)
            {
                statusMessage = "Nét chuẩn (Reference) và nét đích (Target) phải có độ dài hợp lệ.";
                return false;
            }

            const int SAMPLES = 24;
            List<Vector2> refCurve = ResampleStrokeInViewSpace(rawRefStroke, SAMPLES, camera, viewport);
            List<Vector2> dstCurve = ResampleStrokeInViewSpace(rawTargetStroke, SAMPLES, camera, viewport);

            if (refCurve.Count < 4 || dstCurve.Count < 4)
            {
                statusMessage = "Nét uốn quá ngắn.";
                return false;
            }

            // Tính độ dài tổng của đường Reference và đường Target để hỗ trợ kéo giãn (Elongation - Hình 11)
            float refLen = ComputeCurveLength(refCurve);
            float dstLen = ComputeCurveLength(dstCurve);
            if (refLen < 5f || dstLen < 5f)
            {
                statusMessage = "Độ dài nét uốn quá nhỏ.";
                return false;
            }

            // Tính tiếp tuyến đơn vị và pháp tuyến đơn vị tại mỗi điểm mẫu trên 2 đường cong
            int n = refCurve.Count;
            Vector2[] refTan = new Vector2[n];
            Vector2[] refNorm = new Vector2[n];
            Vector2[] dstTan = new Vector2[n];
            Vector2[] dstNorm = new Vector2[n];

            ComputeCurveFrames(refCurve, refTan, refNorm);
            ComputeCurveFrames(dstCurve, dstTan, dstNorm);

            // Bán kính ảnh hưởng mềm dựa trên độ dài đường chuẩn
            float influenceRadius = MathF.Max(65f, refLen * 0.95f);
            float smoothingA = influenceRadius * 0.18f;

            for (int vIdx = 0; vIdx < mesh.Vertices.Count; vIdx++)
            {
                // Chuyển đỉnh sang không gian View (Z_view giữ nguyên vì biến dạng song song màn hình - Mục 4.7)
                Vector3 viewPos = camera.WorldToView(mesh.Vertices[vIdx]);
                Vector2 p = new Vector2(viewPos.X, viewPos.Y);

                Vector2 weightedTargetSum = Vector2.Zero;
                float weightSum = 0f;
                float minDistToRef = float.MaxValue;

                for (int i = 0; i < n; i++)
                {
                    Vector2 rel = p - refCurve[i];
                    float dist = rel.Length();
                    if (dist < minDistToRef) minDistToRef = dist;

                    // Tọa độ cục bộ (u, v) đối với điểm mẫu thứ i của Reference Stroke
                    float u = Vector2.Dot(rel, refTan[i]);
                    float v = Vector2.Dot(rel, refNorm[i]);

                    // Vị trí tương ứng đối với điểm mẫu thứ i của Target Stroke
                    Vector2 mappedPt = dstCurve[i] + u * dstTan[i] + v * dstNorm[i];

                    // Trọng số nghịch đảo bình phương khoảng cách (Field Warp - Correa 1998 [5])
                    float w = 1.0f / MathF.Pow(smoothingA + dist, 2.3f);
                    weightedTargetSum += w * mappedPt;
                    weightSum += w;
                }

                if (weightSum > 1e-7f)
                {
                    Vector2 warpedXY = weightedTargetSum / weightSum;

                    // Hệ số suy giảm mượt cho các phần nằm rất xa trục uốn
                    float falloff = MathF.Exp(-MathF.Pow(MathF.Max(0f, minDistToRef - influenceRadius * 0.5f) / influenceRadius, 2f));
                    Vector2 finalXY = Vector2.Lerp(p, warpedXY, falloff);

                    Vector3 newViewPos = new Vector3(finalXY.X, finalXY.Y, viewPos.Z);
                    mesh.Vertices[vIdx] = camera.ViewToWorld(newViewPos);
                }
            }

            // Cũng biến dạng theo cho các nét vẽ trên bề mặt (SurfaceLines) để nét vẽ không bị lệch khỏi lưới
            foreach (var sl in mesh.SurfaceLines)
            {
                for (int k = 0; k < sl.Points.Count; k++)
                {
                    Vector3 vp = camera.WorldToView(sl.Points[k]);
                    Vector2 p = new Vector2(vp.X, vp.Y);
                    Vector2 wSum = Vector2.Zero;
                    float wTot = 0f;
                    float minD = float.MaxValue;

                    for (int i = 0; i < n; i++)
                    {
                        Vector2 rel = p - refCurve[i];
                        float d = rel.Length();
                        if (d < minD) minD = d;
                        float u = Vector2.Dot(rel, refTan[i]);
                        float v = Vector2.Dot(rel, refNorm[i]);
                        Vector2 mapped = dstCurve[i] + u * dstTan[i] + v * dstNorm[i];
                        float w = 1.0f / MathF.Pow(smoothingA + d, 2.3f);
                        wSum += w * mapped;
                        wTot += w;
                    }

                    if (wTot > 1e-7f)
                    {
                        float falloff = MathF.Exp(-MathF.Pow(MathF.Max(0f, minD - influenceRadius * 0.5f) / influenceRadius, 2f));
                        Vector2 finalXY = Vector2.Lerp(p, wSum / wTot, falloff);
                        sl.Points[k] = camera.ViewToWorld(new Vector3(finalXY.X, finalXY.Y, vp.Z));
                    }
                }
            }

            mesh.RecalculateNormals();
            statusMessage = "Đã hoàn tất phép Uốn biến dạng mô hình (Bending / Transformation - Hình 11)!";
            return true;
        }

        /// <summary>
        /// [TV2-10 - Mục 4.7] Hỗ trợ Uốn khối 3D bằng 1 nét (tự động bắt trục Spine / trục chính khối làm Reference)
        /// hoặc 2 nét phác thảo 2D (Nét #1 Reference → Nét #2 Target).
        /// </summary>
        public static bool TryAutoSpineOrTwoStrokeBend(
            TeddyMesh mesh,
            IReadOnlyList<SketchStroke2D> freehandStrokes,
            VirtualTrackballCamera camera,
            Size viewport,
            out List<SketchStroke2D> consumedStrokes,
            out string statusMessage)
        {
            consumedStrokes = new List<SketchStroke2D>();
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || freehandStrokes == null || freehandStrokes.Count == 0)
            {
                statusMessage = "Cần ít nhất 1 nét 2D (Auto-Spine Bend) hoặc 2 nét 2D (Reference → Target) để uốn khối!";
                return false;
            }

            if (freehandStrokes.Count >= 2)
            {
                var refSk = freehandStrokes[freehandStrokes.Count - 2];
                var dstSk = freehandStrokes[freehandStrokes.Count - 1];
                if (TryBendMeshWithStrokes(mesh, refSk.Points, dstSk.Points, camera, viewport, out statusMessage))
                {
                    consumedStrokes.Add(refSk);
                    consumedStrokes.Add(dstSk);
                    return true;
                }
                return false;
            }

            // Chế độ 1 nét (Auto-Spine Bending): Tự động trích xuất trục chuẩn (Reference) từ SpineVertices hoặc trục PCA của khối 3D
            var targetSk = freehandStrokes[freehandStrokes.Count - 1];
            if (targetSk.Points.Count < 3)
            {
                statusMessage = "Nét uốn 2D quá ngắn!";
                return false;
            }

            List<PointF> autoRefScreen = BuildAutoReferenceAxisOnScreen(mesh, targetSk.Points, camera, viewport);
            if (autoRefScreen.Count < 2)
            {
                statusMessage = "Không thể trích xuất trục chuẩn tự động cho khối này.";
                return false;
            }

            if (TryBendMeshWithStrokes(mesh, autoRefScreen, targetSk.Points, camera, viewport, out string bendMsg))
            {
                consumedStrokes.Add(targetSk);
                statusMessage = $"[Auto-Spine Bend (1 nét) - TV2-10] {bendMsg}";
                return true;
            }

            statusMessage = bendMsg;
            return false;
        }

        private static List<PointF> BuildAutoReferenceAxisOnScreen(
            TeddyMesh mesh,
            IReadOnlyList<PointF> targetStroke,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            PointF tStart = targetStroke[0];
            PointF tEnd = targetStroke[targetStroke.Count - 1];
            Vector2 tDir = new Vector2(tEnd.X - tStart.X, tEnd.Y - tStart.Y);
            if (tDir.LengthSquared() < 1e-3f) tDir = Vector2.UnitX;
            else tDir = Vector2.Normalize(tDir);

            // Ưu tiên 1: Nếu khối có trục Spine (tạo từ TeddyInflationEngine), chiếu chuỗi dài nhất của Spine lên màn hình
            var ptsScreen = new List<PointF>();
            if (mesh.SpineVertices.Count >= 3)
            {
                foreach (var sv in mesh.SpineVertices)
                {
                    Vector3 sp = camera.ProjectToScreen(sv, viewport);
                    ptsScreen.Add(new PointF(sp.X, sp.Y));
                }
            }
            else
            {
                foreach (var v in mesh.Vertices)
                {
                    Vector3 sp = camera.ProjectToScreen(v, viewport);
                    ptsScreen.Add(new PointF(sp.X, sp.Y));
                }
            }

            // Tìm trục chính theo hướng phương sai lớn nhất (hoặc gióng gần nhất với hướng nét vẽ target) trên tập điểm 2D
            Vector2 mean = Vector2.Zero;
            foreach (var p in ptsScreen) mean += new Vector2(p.X, p.Y);
            mean /= Math.Max(1, ptsScreen.Count);

            float sxx = 0f, syy = 0f, sxy = 0f;
            foreach (var p in ptsScreen)
            {
                float dx = p.X - mean.X;
                float dy = p.Y - mean.Y;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            float trace = sxx + syy;
            float det = sxx * syy - sxy * sxy;
            float disc = MathF.Sqrt(MathF.Max(0f, trace * trace * 0.25f - det));
            float lambda1 = trace * 0.5f + disc;
            Vector2 principalDir = MathF.Abs(sxy) > 1e-4f
                ? Vector2.Normalize(new Vector2(lambda1 - syy, sxy))
                : (sxx >= syy ? Vector2.UnitX : Vector2.UnitY);

            if (Vector2.Dot(principalDir, tDir) < 0f)
                principalDir = -principalDir;

            float minU = float.MaxValue, maxU = float.MinValue;
            foreach (var p in ptsScreen)
            {
                float u = Vector2.Dot(new Vector2(p.X, p.Y) - mean, principalDir);
                if (u < minU) minU = u;
                if (u > maxU) maxU = u;
            }

            // Nội suy nhẹ chiều dài giữ tự nhiên khớp giữa nét trục khối và nét Target
            float span = MathF.Max(30f, maxU - minU);
            var line = new List<PointF>(12);
            for (int i = 0; i <= 11; i++)
            {
                float u = minU + span * (i / 11f);
                Vector2 pt = mean + principalDir * u;
                line.Add(new PointF(pt.X, pt.Y));
            }
            return line;
        }

        /// <summary>
        /// [TV2-10 - Mục 8 Extensions] Tạo gờ sắc / đường gân nổi 3D (Crease Stroke) dọc theo nét vẽ 2D trên bề mặt khối.
        /// </summary>
        public static bool TryApplyCreaseStroke(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            Color creaseColor,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || rawStroke == null || rawStroke.Count < 3)
            {
                statusMessage = "Nét tạo gờ sắc (Crease) không hợp lệ.";
                return false;
            }

            var status = TeddyRaycaster.TryProjectStrokeWithRayPlaneSplicing(
                mesh, rawStroke, camera, viewport, closeLoop: false,
                out List<Vector3> surfacePts, out _, out string diag, surfaceOffset: 0.4f);

            if (status != SurfaceProjectionStatus.Success || surfacePts.Count < 2)
            {
                statusMessage = string.IsNullOrEmpty(diag) ? "Nét vẽ tạo gờ sắc phải nằm trên bề mặt khối 3D." : diag;
                return false;
            }

            float avgEdge = 16f;
            if (mesh.Triangles.Count > 0)
            {
                float sumE = 0f;
                int sampleCount = Math.Min(60, mesh.Triangles.Count);
                for (int i = 0; i < sampleCount; i++)
                {
                    var tr = mesh.Triangles[i];
                    sumE += Vector3.Distance(mesh.Vertices[tr.V0], mesh.Vertices[tr.V1]);
                }
                avgEdge = Math.Clamp(sumE / sampleCount, 8f, 36f);
            }

            float bandRadius = avgEdge * 1.45f;
            float ridgeAmplitude = avgEdge * 0.58f;
            int displacedCount = 0;

            mesh.RecalculateNormals();
            for (int vIdx = 0; vIdx < mesh.Vertices.Count; vIdx++)
            {
                Vector3 vPos = mesh.Vertices[vIdx];
                float minDistSq = float.MaxValue;
                for (int s = 0; s < surfacePts.Count - 1; s++)
                {
                    Vector3 a = surfacePts[s];
                    Vector3 b = surfacePts[s + 1];
                    Vector3 ab = b - a;
                    float lenSq = ab.LengthSquared();
                    float t = lenSq > 1e-6f ? Math.Clamp(Vector3.Dot(vPos - a, ab) / lenSq, 0f, 1f) : 0f;
                    float d2 = Vector3.DistanceSquared(vPos, a + ab * t);
                    if (d2 < minDistSq) minDistSq = d2;
                }

                float d = MathF.Sqrt(minDistSq);
                if (d < bandRadius)
                {
                    float profile = MathF.Pow(1f - d / bandRadius, 1.35f);
                    mesh.Vertices[vIdx] += mesh.VertexNormals[vIdx] * (ridgeAmplitude * profile);
                    displacedCount++;
                }
            }

            // Vẽ kèm nét mực nhấn gờ sắc dọc theo đỉnh sống nổi
            var elevatedLine = new List<Vector3>(surfacePts.Count);
            foreach (var p in surfacePts)
                elevatedLine.Add(p + camera.GetCameraForwardInWorld() * (ridgeAmplitude * 0.92f));
            TeddySurfacePaintOp.AddSurfaceLine(mesh, elevatedLine, creaseColor, 2.6f);

            mesh.RecalculateNormals();
            statusMessage = $"Đã tạo gờ sắc 3D (Crease Stroke - TV2-10 Mục 8) dọc {displacedCount} đỉnh lưới!";
            return displacedCount > 0;
        }

        /// <summary>
        /// [TV2-10 - Mục 8 Extensions] Xoắn khối 3D (Twisting) quanh trục nét phác thảo (hoặc trục Spine chính).
        /// </summary>
        public static bool TryTwistMeshAlongStroke(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawAxisStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            float totalTwistRadians,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || rawAxisStroke == null || rawAxisStroke.Count < 2)
            {
                statusMessage = "Cần vẽ 1 nét định hướng trục xoắn trên khối 3D!";
                return false;
            }

            Vector3 p0 = camera.ScreenToWorldPoint(rawAxisStroke[0], viewport, 0f);
            Vector3 p1 = camera.ScreenToWorldPoint(rawAxisStroke[rawAxisStroke.Count - 1], viewport, 0f);
            Vector3 axis = p1 - p0;
            if (axis.LengthSquared() < 1e-4f)
            {
                statusMessage = "Độ dài nét định trục xoắn quá ngắn.";
                return false;
            }
            axis = Vector3.Normalize(axis);

            Vector3 center = Vector3.Zero;
            foreach (var v in mesh.Vertices) center += v;
            center /= Math.Max(1, mesh.Vertices.Count);

            float minS = float.MaxValue, maxS = float.MinValue;
            foreach (var v in mesh.Vertices)
            {
                float s = Vector3.Dot(v - center, axis);
                if (s < minS) minS = s;
                if (s > maxS) maxS = s;
            }
            float span = MathF.Max(1e-3f, maxS - minS);

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vector3 rel = mesh.Vertices[i] - center;
                float s = Vector3.Dot(rel, axis);
                float t = ((s - minS) / span) - 0.5f; // [-0.5 .. +0.5]
                Quaternion twistQ = Quaternion.CreateFromAxisAngle(axis, t * totalTwistRadians);
                mesh.Vertices[i] = center + Vector3.Transform(rel, twistQ);
            }

            foreach (var sl in mesh.SurfaceLines)
            {
                for (int k = 0; k < sl.Points.Count; k++)
                {
                    Vector3 rel = sl.Points[k] - center;
                    float s = Vector3.Dot(rel, axis);
                    float t = ((s - minS) / span) - 0.5f;
                    Quaternion twistQ = Quaternion.CreateFromAxisAngle(axis, t * totalTwistRadians);
                    sl.Points[k] = center + Vector3.Transform(rel, twistQ);
                }
            }

            mesh.RecalculateNormals();
            float deg = totalTwistRadians * (180f / MathF.PI);
            statusMessage = $"Đã xoắn khối 3D (Twisting - TV2-10 Mục 8) góc tổng {deg:F0}° quanh trục đã vẽ!";
            return true;
        }

        private static List<Vector2> ResampleStrokeInViewSpace(
            IReadOnlyList<PointF> rawScreen,
            int sampleCount,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            var res = new List<Vector2>(sampleCount);
            if (rawScreen == null || rawScreen.Count < 2) return res;

            float totalLen = StrokeProcessor.ComputePolylineLength(rawScreen, false);
            if (totalLen < 1e-3f) return res;

            float step = totalLen / (sampleCount - 1);
            List<PointF> uniformScreen = StrokeProcessor.ResampleUniform(rawScreen, MathF.Max(2f, step), false);

            // Chuyển sang tọa độ View Space 2D
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                float idxF = t * (uniformScreen.Count - 1);
                int idx0 = Math.Clamp((int)MathF.Floor(idxF), 0, uniformScreen.Count - 1);
                int idx1 = Math.Clamp(idx0 + 1, 0, uniformScreen.Count - 1);
                float frac = idxF - idx0;

                PointF sp = new PointF(
                    uniformScreen[idx0].X + frac * (uniformScreen[idx1].X - uniformScreen[idx0].X),
                    uniformScreen[idx0].Y + frac * (uniformScreen[idx1].Y - uniformScreen[idx0].Y));

                Vector3 worldPt = camera.ScreenToWorldPoint(sp, viewport, 0f);
                Vector3 viewPt = camera.WorldToView(worldPt);
                res.Add(new Vector2(viewPt.X, viewPt.Y));
            }

            return res;
        }

        private static float ComputeCurveLength(List<Vector2> curve)
        {
            float sum = 0f;
            for (int i = 0; i < curve.Count - 1; i++)
                sum += Vector2.Distance(curve[i], curve[i + 1]);
            return sum;
        }

        private static void ComputeCurveFrames(List<Vector2> curve, Vector2[] tangents, Vector2[] normals)
        {
            int n = curve.Count;
            for (int i = 0; i < n; i++)
            {
                int iPrev = Math.Max(0, i - 1);
                int iNext = Math.Min(n - 1, i + 1);
                Vector2 diff = curve[iNext] - curve[iPrev];
                float len = diff.Length();
                Vector2 t = len > 1e-5f ? diff / len : new Vector2(1f, 0f);
                tangents[i] = t;
                normals[i] = new Vector2(-t.Y, t.X);
            }
        }
    }
}
