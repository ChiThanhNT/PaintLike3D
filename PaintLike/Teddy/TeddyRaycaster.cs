using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV2-06] Kết quả chiếu nét vẽ 2D lên bề mặt 3D theo phương pháp Bounded Ray-Plane Adjacency Splicing (Mục 5.2 - Hình 16).
    /// </summary>
    public enum SurfaceProjectionStatus
    {
        Success,
        NoSurfaceHit,
        FoldDetected
    }

    /// <summary>
    /// [TV2-01, TV2-02 & TV2-06] Hệ thống chiếu tia (Raycasting), chiếu nét vẽ bề mặt theo giao tuyến mặt phẳng tia
    /// và liên kết tam giác kề nhau (Bounded Ray-Plane Adjacency Splicing - Hình 16), phát hiện nếp gấp khuất (Fold Check),
    /// xóa nét bề mặt có kiểm tra độ sâu mặt trước (Depth-Checked Erase) và tính pháp tuyến vòng đùn (Mục 5.3).
    /// </summary>
    public static class TeddyRaycaster
    {
        /// <summary>
        /// Thuật toán Möller–Trumbore kiểm tra giao điểm tia (Origin, Dir) với tam giác (V0, V1, V2).
        /// </summary>
        public static bool RayIntersectsTriangle(
            Vector3 rayOrigin,
            Vector3 rayDir,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float t,
            out Vector3 hitPoint)
        {
            t = 0f;
            hitPoint = Vector3.Zero;

            const float EPSILON = 1e-6f;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(rayDir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -EPSILON && a < EPSILON)
                return false; // Tia song song mặt phẳng tam giác

            float f = 1.0f / a;
            Vector3 s = rayOrigin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(rayDir, q);
            if (v < 0.0f || u + v > 1.0f)
                return false;

            t = f * Vector3.Dot(edge2, q);
            if (t > EPSILON)
            {
                hitPoint = rayOrigin + rayDir * t;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Bắn một tia từ tọa độ màn hình vào TeddyMesh, trả về điểm giao trên mặt tam giác gần Camera nhất.
        /// </summary>
        public static bool RaycastMesh(
            TeddyMesh mesh,
            PointF screenPt,
            VirtualTrackballCamera camera,
            Size viewport,
            out int hitTriIndex,
            out Vector3 hitWorldPoint,
            out Vector3 hitNormal)
        {
            hitTriIndex = -1;
            hitWorldPoint = Vector3.Zero;
            hitNormal = Vector3.UnitZ;

            if (mesh == null || mesh.IsEmpty) return false;

            var (rayOrigin, rayDir) = camera.ScreenPointToRay(screenPt, viewport);
            Vector3 camForward = camera.GetCameraForwardInWorld();

            float closestT = float.MaxValue;
            bool found = false;

            for (int i = 0; i < mesh.Triangles.Count; i++)
            {
                var tri = mesh.Triangles[i];
                // Chỉ xét các mặt quay về phía Camera (Front-facing polygons - Mục 5.2)
                if (Vector3.Dot(tri.FaceNormal, camForward) < -0.05f)
                    continue;

                Vector3 p0 = mesh.Vertices[tri.V0];
                Vector3 p1 = mesh.Vertices[tri.V1];
                Vector3 p2 = mesh.Vertices[tri.V2];

                if (RayIntersectsTriangle(rayOrigin, rayDir, p0, p1, p2, out float t, out Vector3 hp))
                {
                    if (t < closestT)
                    {
                        closestT = t;
                        hitTriIndex = i;
                        hitWorldPoint = hp;
                        hitNormal = tri.FaceNormal;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// [TV2-06 & Mục 5.2 - Hình 16] Chiếu nét vẽ 2D lên bề mặt 3D bằng giao tuyến mặt phẳng tia có biên
        /// (Bounded Ray-Plane Intersection) kết hợp bước qua liên kết tam giác kề nhau (tri.Neighbors).
        /// Tự động phát hiện và hủy nét nếu nét vẽ vượt qua nếp gấp che khuất (Fold Check) hoặc nhảy giữa hai mảng mặt không kề nhau.
        /// </summary>
        public static SurfaceProjectionStatus TryProjectStrokeWithRayPlaneSplicing(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawScreenStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            bool closeLoop,
            out List<Vector3> splicedSurfacePoints,
            out int splicedEdgeCount,
            out string diagnosticMessage,
            float surfaceOffset = 1.05f)
        {
            splicedSurfacePoints = new List<Vector3>();
            splicedEdgeCount = 0;
            diagnosticMessage = "";

            if (mesh == null || mesh.IsEmpty || rawScreenStroke == null || rawScreenStroke.Count < 2)
            {
                diagnosticMessage = "Chưa đủ điểm nét vẽ 2D để chiếu lên bề mặt 3D.";
                return SurfaceProjectionStatus.NoSurfaceHit;
            }

            if (mesh.Edges.Count == 0)
            {
                mesh.BuildConnectivity();
            }

            List<PointF> samples = StrokeProcessor.ResampleUniform(rawScreenStroke, 7.5f, closeLoop);
            if (closeLoop && samples.Count >= 3 && StrokeProcessor.Distance(samples[0], samples[samples.Count - 1]) > 1.0f)
            {
                samples.Add(samples[0]);
            }

            if (samples.Count < 2)
            {
                diagnosticMessage = "Nét vẽ 2D quá ngắn.";
                return SurfaceProjectionStatus.NoSurfaceHit;
            }

            Vector3 camForward = camera.GetCameraForwardInWorld();
            var (_, sharedRayDir) = camera.ScreenPointToRay(samples[0], viewport);

            // Bước 1: Chiếu từng điểm mẫu lên mặt quay về Camera gần nhất và kiểm tra tính liên tục trên bóng vật thể
            var hits = new List<(PointF ScreenPt, int TriIdx, Vector3 HitPt, Vector3 Normal, int SampleIdx)>(samples.Count);
            bool hasStartedHit = false;
            bool leftSurfaceAfterHit = false;

            for (int i = 0; i < samples.Count; i++)
            {
                if (RaycastMesh(mesh, samples[i], camera, viewport, out int triIdx, out Vector3 hitPt, out Vector3 hitNorm))
                {
                    if (leftSurfaceAfterHit)
                    {
                        diagnosticMessage = "Phát hiện nét vẽ vượt ra ngoài biên rồi nhảy sang phần bề mặt khác (Fold / Discontinuity - Mục 5.2). Đã hủy nét vẽ!";
                        return SurfaceProjectionStatus.FoldDetected;
                    }
                    hasStartedHit = true;
                    hits.Add((samples[i], triIdx, hitPt, hitNorm, i));
                }
                else if (hasStartedHit)
                {
                    leftSurfaceAfterHit = true;
                }
            }

            if (hits.Count < 2)
            {
                diagnosticMessage = "Nét vẽ 2D chưa bám đủ trên bề mặt khối 3D! Hãy vẽ trực tiếp lên phần mặt khối nhìn thấy.";
                return SurfaceProjectionStatus.NoSurfaceHit;
            }

            // Bảng tra nhanh các tam giác kề theo đỉnh (dùng khi mặt phẳng tia đi sượt đúng qua 1 đỉnh chung)
            List<int>[]? vertexToTris = null;
            List<int>[] GetVertexToTris()
            {
                if (vertexToTris != null) return vertexToTris;
                vertexToTris = new List<int>[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; i++) vertexToTris[i] = new List<int>(6);
                for (int t = 0; t < mesh.Triangles.Count; t++)
                {
                    var tr = mesh.Triangles[t];
                    vertexToTris[tr.V0].Add(t);
                    vertexToTris[tr.V1].Add(t);
                    vertexToTris[tr.V2].Add(t);
                }
                return vertexToTris;
            }

            var outPoints = splicedSurfacePoints;
            void AppendPointUnique(Vector3 pt)
            {
                if (outPoints.Count == 0 ||
                    Vector3.DistanceSquared(outPoints[outPoints.Count - 1], pt) >= 0.36f)
                {
                    outPoints.Add(pt);
                }
            }

            // Điểm khởi đầu trên mặt tam giác đầu tiên
            AppendPointUnique(hits[0].HitPt + hits[0].Normal * surfaceOffset);

            // Bước 2: Với từng đoạn 2D liên tiếp (a -> b), dựng mặt phẳng tia có biên Π_ab và bước qua tri.Neighbors (Hình 16)
            for (int h = 0; h < hits.Count - 1; h++)
            {
                var hA = hits[h];
                var hB = hits[h + 1];

                if (hA.TriIdx == hB.TriIdx)
                {
                    AppendPointUnique(hB.HitPt + hB.Normal * surfaceOffset);
                    continue;
                }

                // Kiểm tra nhanh độ giật độ sâu cục bộ lớn bất thường (dấu hiệu nhảy tầng che khuất)
                Vector3 rayOrigA = camera.ScreenPointToRay(hA.ScreenPt, viewport).Origin;
                Vector3 rayOrigB = camera.ScreenPointToRay(hB.ScreenPt, viewport).Origin;
                Vector3 segU = rayOrigB - rayOrigA;
                float segLen = segU.Length();

                if (segLen < 1e-4f)
                {
                    AppendPointUnique(hB.HitPt + hB.Normal * surfaceOffset);
                    continue;
                }

                Vector3 segDir = segU / segLen;
                Vector3 planeNormal = Vector3.Cross(segDir, sharedRayDir);
                float pnLen = planeNormal.Length();
                if (pnLen < 1e-6f)
                {
                    AppendPointUnique(hB.HitPt + hB.Normal * surfaceOffset);
                    continue;
                }
                planeNormal /= pnLen;

                // Bước qua liên kết tam giác kề nhau (Greedy Ray-Plane Edge Walk)
                if (TryWalkRayPlaneAcrossNeighbors(
                    mesh,
                    hA.TriIdx,
                    hB.TriIdx,
                    rayOrigA,
                    segDir,
                    segLen,
                    planeNormal,
                    camForward,
                    surfaceOffset,
                    GetVertexToTris,
                    out List<Vector3> walkPts,
                    out bool hitBackFacingFold))
                {
                    foreach (var wp in walkPts)
                    {
                        AppendPointUnique(wp);
                        splicedEdgeCount++;
                    }
                    AppendPointUnique(hB.HitPt + hB.Normal * surfaceOffset);
                    continue;
                }

                // Dự phòng: BFS cục bộ chỉ trên các tam giác quay về Camera (front-facing) nằm trong dải mặt phẳng tia Π_ab
                // (Xử lý trường hợp mặt phẳng tia đi qua đỉnh/đường chéo phức tạp nhưng 2 tam giác vẫn liên thông thực sự trên mặt trước)
                if (!hitBackFacingFold && TryFindFrontFacingSlabPath(
                    mesh,
                    hA.TriIdx,
                    hB.TriIdx,
                    rayOrigA,
                    segDir,
                    segLen,
                    planeNormal,
                    camForward,
                    surfaceOffset,
                    out List<Vector3> bfsPts))
                {
                    foreach (var bp in bfsPts)
                    {
                        AppendPointUnique(bp);
                        splicedEdgeCount++;
                    }
                    AppendPointUnique(hB.HitPt + hB.Normal * surfaceOffset);
                    continue;
                }

                // Không tồn tại đường đi liên thông trên mặt hướng về Camera trong dải mặt phẳng tia -> FOLD DETECTED (Hình 16)!
                diagnosticMessage = "Phát hiện nếp gấp che khuất (Fold Check - Mục 5.2, Hình 16): Nét vẽ 2D cắt ngang qua ranh giới hai lớp bề mặt không liền kề nhau nên đã bị hủy!";
                splicedSurfacePoints.Clear();
                return SurfaceProjectionStatus.FoldDetected;
            }

            if (splicedSurfacePoints.Count < 2)
            {
                diagnosticMessage = "Không thể ghép nối đoạn nét 3D hợp lệ trên bề mặt.";
                return SurfaceProjectionStatus.NoSurfaceHit;
            }

            diagnosticMessage = $"Đã chiếu nét 3D bám sát bề mặt qua {splicedEdgeCount} cạnh tam giác ({splicedSurfacePoints.Count} điểm - Hình 16).";
            return SurfaceProjectionStatus.Success;
        }

        private static bool TryWalkRayPlaneAcrossNeighbors(
            TeddyMesh mesh,
            int startTriIdx,
            int endTriIdx,
            Vector3 rayOrigA,
            Vector3 segDir,
            float segLen,
            Vector3 planeNormal,
            Vector3 camForward,
            float surfaceOffset,
            Func<List<int>[]> getVertexToTris,
            out List<Vector3> edgeCrossingPts,
            out bool hitBackFacingFold)
        {
            edgeCrossingPts = new List<Vector3>();
            hitBackFacingFold = false;

            int currTri = startTriIdx;
            int prevTri = -1;
            float currAlpha = -0.02f;
            var visited = new HashSet<int> { startTriIdx };
            const int maxWalkSteps = 48;
            const float planeEps = 1e-4f;

            for (int step = 0; step < maxWalkSteps; step++)
            {
                if (currTri == endTriIdx)
                    return true;

                TeddyTriangle tri = mesh.Triangles[currTri];
                int[] verts = { tri.V0, tri.V1, tri.V2 };

                int bestNextTri = -1;
                float bestAlpha = float.MinValue;
                Vector3 bestCrossPt = Vector3.Zero;
                int grazedVertex = -1;

                for (int e = 0; e < 3; e++)
                {
                    int nb = tri.Neighbors[e];
                    if (nb == prevTri) continue;

                    int va = verts[e];
                    int vb = verts[(e + 1) % 3];
                    Vector3 pa = mesh.Vertices[va];
                    Vector3 pb = mesh.Vertices[vb];

                    float fa = Vector3.Dot(pa - rayOrigA, planeNormal);
                    float fb = Vector3.Dot(pb - rayOrigA, planeNormal);

                    if ((fa > planeEps && fb > planeEps) || (fa < -planeEps && fb < -planeEps))
                        continue;

                    float denom = fa - fb;
                    float te = MathF.Abs(denom) > 1e-7f ? Math.Clamp(fa / denom, 0f, 1f) : 0.5f;
                    Vector3 q = Vector3.Lerp(pa, pb, te);
                    float alphaQ = Vector3.Dot(q - rayOrigA, segDir) / segLen;

                    if (alphaQ < currAlpha - 0.04f || alphaQ > 1.08f)
                        continue;

                    if (te <= 0.02f) grazedVertex = va;
                    else if (te >= 0.98f) grazedVertex = vb;

                    if (nb >= 0 && !visited.Contains(nb) && alphaQ > bestAlpha)
                    {
                        bestAlpha = alphaQ;
                        bestNextTri = nb;
                        bestCrossPt = q;
                    }
                    else if (nb == endTriIdx)
                    {
                        bestAlpha = alphaQ;
                        bestNextTri = nb;
                        bestCrossPt = q;
                        break;
                    }
                }

                // Nếu mặt phẳng tia đi sượt đúng qua đỉnh chung grazedVertex -> duyệt 1-ring quanh đỉnh đó
                if (bestNextTri < 0 && grazedVertex >= 0)
                {
                    var incidentTris = getVertexToTris()[grazedVertex];
                    Vector3 vPos = mesh.Vertices[grazedVertex];
                    foreach (int candTri in incidentTris)
                    {
                        if (visited.Contains(candTri) && candTri != endTriIdx) continue;
                        var cTri = mesh.Triangles[candTri];
                        if (Vector3.Dot(cTri.FaceNormal, camForward) < -0.08f) continue;

                        if (candTri == endTriIdx)
                        {
                            bestNextTri = candTri;
                            bestCrossPt = vPos;
                            bestAlpha = MathF.Max(currAlpha, Vector3.Dot(vPos - rayOrigA, segDir) / segLen);
                            break;
                        }

                        // Kiểm tra tam giác ứng viên có cạnh đối diện đỉnh chung giao với mặt phẳng tia theo chiều tiến lên không
                        int[] cv = { cTri.V0, cTri.V1, cTri.V2 };
                        for (int ce = 0; ce < 3; ce++)
                        {
                            int cva = cv[ce];
                            int cvb = cv[(ce + 1) % 3];
                            if (cva == grazedVertex || cvb == grazedVertex) continue;
                            Vector3 cpa = mesh.Vertices[cva];
                            Vector3 cpb = mesh.Vertices[cvb];
                            float cfa = Vector3.Dot(cpa - rayOrigA, planeNormal);
                            float cfb = Vector3.Dot(cpb - rayOrigA, planeNormal);
                            if ((cfa >= -planeEps && cfb <= planeEps) || (cfa <= planeEps && cfb >= -planeEps))
                            {
                                float cden = cfa - cfb;
                                float cte = MathF.Abs(cden) > 1e-7f ? Math.Clamp(cfa / cden, 0f, 1f) : 0.5f;
                                Vector3 cq = Vector3.Lerp(cpa, cpb, cte);
                                float cAlpha = Vector3.Dot(cq - rayOrigA, segDir) / segLen;
                                if (cAlpha > currAlpha + 0.005f && cAlpha <= 1.08f && cAlpha > bestAlpha)
                                {
                                    bestAlpha = cAlpha;
                                    bestNextTri = candTri;
                                    bestCrossPt = vPos;
                                }
                            }
                        }
                    }
                }

                if (bestNextTri < 0)
                    return false;

                // [Fold Check] Kiểm tra tam giác kế tiếp có quay lưng ra sau Camera không (đã vượt qua nếp gấp Silhouette!)
                Vector3 nextNormal = mesh.Triangles[bestNextTri].FaceNormal;
                if (Vector3.Dot(nextNormal, camForward) < -0.08f)
                {
                    hitBackFacingFold = true;
                    return false;
                }

                Vector3 avgNorm = tri.FaceNormal + nextNormal;
                float avgNormLen = avgNorm.Length();
                Vector3 smoothNorm = avgNormLen > 1e-5f ? avgNorm / avgNormLen : tri.FaceNormal;

                edgeCrossingPts.Add(bestCrossPt + smoothNorm * surfaceOffset);

                prevTri = currTri;
                currTri = bestNextTri;
                currAlpha = MathF.Max(currAlpha, bestAlpha);
                visited.Add(currTri);
            }

            return currTri == endTriIdx;
        }

        private static bool TryFindFrontFacingSlabPath(
            TeddyMesh mesh,
            int startTriIdx,
            int endTriIdx,
            Vector3 rayOrigA,
            Vector3 segDir,
            float segLen,
            Vector3 planeNormal,
            Vector3 camForward,
            float surfaceOffset,
            out List<Vector3> pathEdgePts)
        {
            pathEdgePts = new List<Vector3>();

            // Dải giới hạn quanh mặt phẳng tia Π_ab (không cho đi vòng ra xa để che giấu lỗi Fold!)
            float slabHalfWidth = MathF.Max(18f, segLen * 0.85f);
            const int maxDepth = 18;

            var queue = new Queue<(int TriIdx, int Depth)>();
            var parentMap = new Dictionary<int, (int ParentTri, int SharedEdgeIndex)>();
            queue.Enqueue((startTriIdx, 0));
            parentMap[startTriIdx] = (-1, -1);

            bool found = false;
            while (queue.Count > 0)
            {
                var (curr, depth) = queue.Dequeue();
                if (curr == endTriIdx)
                {
                    found = true;
                    break;
                }
                if (depth >= maxDepth) continue;

                TeddyTriangle tri = mesh.Triangles[curr];
                for (int e = 0; e < 3; e++)
                {
                    int nb = tri.Neighbors[e];
                    if (nb < 0 || parentMap.ContainsKey(nb)) continue;

                    TeddyTriangle nbTri = mesh.Triangles[nb];
                    // Bắt buộc mọi tam giác trung gian đều hướng về Camera (front-facing) — chặn tuyệt đối vượt nếp gấp khuất!
                    if (Vector3.Dot(nbTri.FaceNormal, camForward) < -0.05f)
                        continue;

                    if (nb != endTriIdx)
                    {
                        Vector3 c = (mesh.Vertices[nbTri.V0] + mesh.Vertices[nbTri.V1] + mesh.Vertices[nbTri.V2]) / 3f;
                        float distPlane = MathF.Abs(Vector3.Dot(c - rayOrigA, planeNormal));
                        float alphaC = Vector3.Dot(c - rayOrigA, segDir) / segLen;
                        if (distPlane > slabHalfWidth || alphaC < -0.35f || alphaC > 1.35f)
                            continue;
                    }

                    parentMap[nb] = (curr, e);
                    queue.Enqueue((nb, depth + 1));
                }
            }

            if (!found)
                return false;

            // Truy ngược đường đi và lấy đúng giao điểm mặt phẳng tia (hoặc điểm gần nhất) trên từng cạnh chung đã đi qua
            var steps = new List<(int FromTri, int ToTri, int EdgeIdx)>();
            int trace = endTriIdx;
            while (trace != startTriIdx && parentMap.TryGetValue(trace, out var info) && info.ParentTri >= 0)
            {
                steps.Add((info.ParentTri, trace, info.SharedEdgeIndex));
                trace = info.ParentTri;
            }
            steps.Reverse();

            foreach (var st in steps)
            {
                TeddyTriangle fromTri = mesh.Triangles[st.FromTri];
                TeddyTriangle toTri = mesh.Triangles[st.ToTri];
                int[] fv = { fromTri.V0, fromTri.V1, fromTri.V2 };
                Vector3 pa = mesh.Vertices[fv[st.EdgeIdx]];
                Vector3 pb = mesh.Vertices[fv[(st.EdgeIdx + 1) % 3]];

                float fa = Vector3.Dot(pa - rayOrigA, planeNormal);
                float fb = Vector3.Dot(pb - rayOrigA, planeNormal);
                float te = 0.5f;
                if (MathF.Abs(fa - fb) > 1e-6f)
                {
                    te = Math.Clamp(fa / (fa - fb), 0f, 1f);
                }

                Vector3 q = Vector3.Lerp(pa, pb, te);
                Vector3 avgNorm = fromTri.FaceNormal + toTri.FaceNormal;
                float nLen = avgNorm.Length();
                Vector3 smoothNorm = nLen > 1e-5f ? avgNorm / nLen : fromTri.FaceNormal;
                pathEdgePts.Add(q + smoothNorm * surfaceOffset);
            }

            return true;
        }

        /// <summary>
        /// [Mục 5.2 - Hình 16] Chiếu một nét vẽ 2D trên màn hình lên bề mặt 3D của mô hình để tạo SurfaceLine.
        /// Ưu tiên sử dụng Bounded Ray-Plane Adjacency Splicing (TV2-06); trả về số điểm chiếu hợp lệ.
        /// </summary>
        public static List<Vector3> ProjectStrokeToSurface(
            TeddyMesh mesh,
            IReadOnlyList<PointF> screenStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out int hitCount)
        {
            var status = TryProjectStrokeWithRayPlaneSplicing(
                mesh,
                screenStroke,
                camera,
                viewport,
                closeLoop: false,
                out List<Vector3> surfacePoints,
                out _,
                out _);

            hitCount = status == SurfaceProjectionStatus.Success ? surfacePoints.Count : 0;
            return surfacePoints;
        }

        /// <summary>
        /// [TV2-06 & Mục 4.3 - Hình 3u-w] Xóa các nét vẽ bề mặt (SurfaceLines) bị gạch đè bởi cử chỉ Scribbling,
        /// có KIỂM TRA ĐỘ SÂU MẶT TRƯỚC (Depth-Checked Visibility) để không bao giờ xóa nhầm nét vẽ ở mặt khuất phía sau khối 3D!
        /// </summary>
        public static int EraseSurfaceLines(
            TeddyMesh mesh,
            IReadOnlyList<PointF> scribbleStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            float eraseRadiusPx = 22f,
            bool checkFrontVisibilityDepth = true)
        {
            if (mesh == null || mesh.SurfaceLines.Count == 0 || scribbleStroke == null || scribbleStroke.Count == 0)
                return 0;

            float r2 = eraseRadiusPx * eraseRadiusPx;
            int erasedLineOrSegCount = 0;
            var remainingLines = new List<TeddySurfaceLine>(mesh.SurfaceLines.Count);

            for (int i = 0; i < mesh.SurfaceLines.Count; i++)
            {
                var line = mesh.SurfaceLines[i];
                if (line.Points.Count == 0) continue;

                bool[] erasePoint = new bool[line.Points.Count];
                bool anyPointErased = false;

                for (int pIdx = 0; pIdx < line.Points.Count; pIdx++)
                {
                    Vector3 pt3D = line.Points[pIdx];
                    Vector3 screenProj = camera.ProjectToScreen(pt3D, viewport);

                    // 1. Kiểm tra khoảng cách 2D tới nét gạch xóa Scribble
                    bool nearScribble = false;
                    for (int j = 0; j < scribbleStroke.Count; j++)
                    {
                        float dx = screenProj.X - scribbleStroke[j].X;
                        float dy = screenProj.Y - scribbleStroke[j].Y;
                        if (dx * dx + dy * dy <= r2)
                        {
                            nearScribble = true;
                            break;
                        }
                    }

                    if (!nearScribble) continue;

                    // 2. [TV2-06 Depth Check] Bắn tia kiểm tra xem điểm nét vẽ có đang bị thân khối 3D che khuất phía trước không
                    if (checkFrontVisibilityDepth)
                    {
                        if (RaycastMesh(mesh, new PointF(screenProj.X, screenProj.Y), camera, viewport, out _, out Vector3 frontHit, out _))
                        {
                            float frontViewZ = camera.WorldToView(frontHit).Z;
                            float ptViewZ = screenProj.Z;
                            // Nếu mặt trước của khối 3D nằm gần Camera hơn điểm nét vẽ > 8.5 đơn vị -> điểm nét nằm ở mặt sau khuất -> BỎ QUA!
                            if (ptViewZ < frontViewZ - 8.5f)
                            {
                                continue;
                            }
                        }
                    }

                    erasePoint[pIdx] = true;
                    anyPointErased = true;
                }

                if (!anyPointErased)
                {
                    remainingLines.Add(line);
                    continue;
                }

                erasedLineOrSegCount++;

                // Tách giữ lại những đoạn nét còn nguyên (không bị gạch trúng hoặc nằm ở mặt sau không nhìn thấy)
                var currentRun = new List<Vector3>();
                for (int pIdx = 0; pIdx < line.Points.Count; pIdx++)
                {
                    if (!erasePoint[pIdx])
                    {
                        currentRun.Add(line.Points[pIdx]);
                    }
                    else
                    {
                        if (currentRun.Count >= 2)
                        {
                            remainingLines.Add(new TeddySurfaceLine
                            {
                                Points = new List<Vector3>(currentRun),
                                StrokeColor = line.StrokeColor,
                                StrokeWidth = line.StrokeWidth,
                                IsClosedRing = false
                            });
                        }
                        currentRun.Clear();
                    }
                }

                if (currentRun.Count >= 2)
                {
                    remainingLines.Add(new TeddySurfaceLine
                    {
                        Points = currentRun,
                        StrokeColor = line.StrokeColor,
                        StrokeWidth = line.StrokeWidth,
                        IsClosedRing = false
                    });
                }
            }

            if (erasedLineOrSegCount > 0)
            {
                mesh.SurfaceLines.Clear();
                mesh.SurfaceLines.AddRange(remainingLines);
            }

            return erasedLineOrSegCount;
        }

        /// <summary>
        /// [Mục 5.3 - Chú thích 2 trang 6] Tính vector pháp tuyến của vòng khép kín 3D (Base Ring)
        /// bằng công thức diện tích có hướng (Signed Area) chiếu lên 3 mặt phẳng YZ, ZX, XY:
        /// v = Normalize(Ayz, Azx, Axy).
        /// </summary>
        public static Vector3 ComputeRingNormal(IReadOnlyList<Vector3> ring)
        {
            if (ring == null || ring.Count < 3)
                return Vector3.UnitZ;

            float axy = 0f;
            float ayz = 0f;
            float azx = 0f;
            int n = ring.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = ring[i];
                Vector3 p1 = ring[(i + 1) % n];

                axy += (p0.X * p1.Y - p1.X * p0.Y);
                ayz += (p0.Y * p1.Z - p1.Y * p0.Z);
                azx += (p0.Z * p1.X - p1.Z * p0.X);
            }

            Vector3 v = new Vector3(0.5f * ayz, 0.5f * azx, 0.5f * axy);
            float len = v.Length();
            return len > 1e-6f ? v / len : Vector3.UnitZ;
        }
    }
}
