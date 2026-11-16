using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV2-03 & TV2-04] Thực hiện các phép Đùn khối (Extrusion - Mục 4.4 & 5.3),
    /// Khoét lõm (Digging a Cavity - Hình 7a-c), Cắt khối (Cutting - Mục 4.5 & 5.4)
    /// và Đùn tiếp từ mặt cắt (Extrusion after Cutting - Hình 9).
    /// </summary>
    public static class TeddyExtrudeCutOp
    {
        /// <summary>
        /// [Mục 5.3 - Hình 17 & 18] Thực hiện phép Đùn khối (Extrusion) hoặc Khoét lõm (Cavity)
        /// từ vòng chọn màu đỏ (ActiveBaseRing) dọc theo nét vẽ đùn 2D bằng thuật toán quét 2 con trỏ (Two-Pointer Sweep).
        /// Khi Đùn khối (Extrusion), hàm tạo ra một khối 3D mới độc lập (TeddyMesh riêng) không đục thủng khối cũ,
        /// cho phép chọn riêng và xóa riêng khối đùn thêm này mà khối cũ vẫn nguyên vẹn 100%.
        /// </summary>
        public static bool TryExtrudeOrDigFromBaseRing(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawExtrudeStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out TeddyMesh? extrudedMesh,
            out string statusMessage)
        {
            extrudedMesh = null;
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || mesh.ActiveBaseRing == null || mesh.ActiveBaseRing.Points.Count < 4)
            {
                statusMessage = "Chưa có vòng chọn Base Ring hợp lệ để đùn khối.";
                return false;
            }

            if (rawExtrudeStroke == null || rawExtrudeStroke.Count < 4)
            {
                statusMessage = "Nét đùn quá ngắn! Hãy vẽ một đường cong định hình phần nhô ra (hoặc lõm vào).";
                return false;
            }

            // Bước 1: Chuẩn hóa vòng cơ sở 3D (Base Ring) thành N đỉnh cách đều nhau (N = 28)
            const int RING_VERTS = 28;
            List<Vector3> baseRing = Resample3DClosedRing(mesh.ActiveBaseRing.Points, RING_VERTS);
            if (baseRing.Count < 8)
            {
                statusMessage = "Vòng chọn quá nhỏ để đùn.";
                return false;
            }

            // Tính tâm trọng lực và pháp tuyến của Base Ring (Chú thích 2 trang 6)
            Vector3 ringCenter = Vector3.Zero;
            foreach (var p in baseRing) ringCenter += p;
            ringCenter /= baseRing.Count;

            float ringRadius = 0f;
            foreach (var p in baseRing) ringRadius += Vector3.Distance(p, ringCenter);
            ringRadius /= baseRing.Count;
            if (ringRadius < 4f) ringRadius = 4f;

            Vector3 ringNormal = DetermineOutwardRingNormal(mesh, baseRing, ringCenter);

            // Bước 2: Tìm mặt phẳng chiếu (Projection Plane - Hình 17a)
            // Mặt phẳng đi qua ringCenter, song song với ringNormal và hướng về phía Camera nhiều nhất
            Vector3 viewCenter = camera.ProjectToScreen(ringCenter, viewport);
            List<PointF> uniform2D = StrokeProcessor.ResampleUniform(rawExtrudeStroke, 10f, false);
            if (uniform2D.Count < 4)
            {
                statusMessage = "Nét đùn quá ngắn.";
                return false;
            }

            var stroke3D = new List<Vector3>(uniform2D.Count);
            foreach (PointF pt in uniform2D)
            {
                stroke3D.Add(camera.ScreenToWorldPoint(pt, viewport, viewCenter.Z));
            }

            // Đảm bảo hai đầu của nét đùn bám sát mặt phẳng của Base Ring
            Vector3 strokeChord = stroke3D[stroke3D.Count - 1] - stroke3D[0];
            float baseChordWidth = strokeChord.Length();
            if (baseChordWidth < 6f) baseChordWidth = ringRadius * 2f;

            Vector3 uAxis = Vector3.Normalize(strokeChord - Vector3.Dot(strokeChord, ringNormal) * ringNormal);
            if (uAxis.LengthSquared() < 1e-4f)
            {
                uAxis = Vector3.Normalize(baseRing[0] - ringCenter);
            }
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(ringNormal, uAxis));

            // Đảm bảo baseRing có thứ tự ngược chiều kim đồng hồ quanh ringNormal để pháp tuyến mặt đùn hướng ra ngoài
            EnsureRingOrientation(baseRing, ringCenter, uAxis, vAxis);

            // Bước 3: Thuật toán quét 2 con trỏ (Two-Pointer Advancing - Mục 5.3, Hình 18a)
            // Một con trỏ chạy từ đầu trái (0) lên đỉnh, một con trỏ chạy từ đầu phải (M-1) lên đỉnh
            var sweepSlices = BuildTwoPointerSweepSlices(stroke3D, ringCenter, baseChordWidth);
            if (sweepSlices.Count == 0)
            {
                statusMessage = "Không thể tạo mặt quét từ nét vẽ này.";
                return false;
            }

            // Kiểm tra xem đây là phép Đùn lồi (Extrusion) hay Khoét lõm (Digging a Cavity - Hình 7a-c)
            Vector3 apexOffset = sweepSlices[sweepSlices.Count - 1].Center - ringCenter;
            bool isCavity = Vector3.Dot(apexOffset, ringNormal) < -ringRadius * 0.15f;

            int nRing = baseRing.Count;
            var localRingOffsets = new Vector2[nRing];
            for (int i = 0; i < nRing; i++)
            {
                Vector3 rel = baseRing[i] - ringCenter;
                localRingOffsets[i] = new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis));
            }

            if (!isCavity)
            {
                // =========================================================================
                // ĐÙN KHỐI LỒI (EXTRUSION): Tạo một TeddyMesh độc lập riêng biệt!
                // Giữ nguyên 100% khối cơ sở (không đục thủng, không hàn gộp),
                // cho phép click chọn riêng và xóa riêng khối đùn này.
                // =========================================================================
                extrudedMesh = new TeddyMesh
                {
                    BaseColor = mesh.BaseColor
                };

                var ringVertexIndices = new List<int[]>();

                // Vòng 0: Khớp sát bề mặt mô hình
                int[] ring0 = new int[nRing];
                for (int i = 0; i < nRing; i++)
                {
                    // Lùi nhẹ 0.4 đơn vị vào khối cơ sở để hai khối tiếp xúc khít, không để hở khe
                    Vector3 rootPt = baseRing[i] - ringNormal * 0.4f;
                    ring0[i] = extrudedMesh.Vertices.Count;
                    extrudedMesh.Vertices.Add(rootPt);
                }
                ringVertexIndices.Add(ring0);

                // Nắp đáy (Bottom Cap) tại Vòng 0 để khối mới là một solid 3D kín hoàn chỉnh (watertight solid)
                Vector3 baseCenter = ringCenter - ringNormal * 0.4f;
                int baseCenterIdx = extrudedMesh.Vertices.Count;
                extrudedMesh.Vertices.Add(baseCenter);
                for (int i = 0; i < nRing; i++)
                {
                    int next = (i + 1) % nRing;
                    extrudedMesh.Triangles.Add(new TeddyTriangle(ring0[next], ring0[i], baseCenterIdx));
                }

                // Sinh các vòng tiếp theo từ danh sách lát cắt 2 con trỏ
                Vector3 prevCenter = ringCenter;
                Vector3 currTangent = ringNormal;

                for (int s = 0; s < sweepSlices.Count; s++)
                {
                    var slice = sweepSlices[s];
                    Vector3 stepVec = slice.Center - prevCenter;
                    if (stepVec.LengthSquared() > 1e-3f)
                    {
                        currTangent = Vector3.Normalize(stepVec);
                    }

                    // Khung tọa độ cục bộ vuông góc với hướng đùn
                    Vector3 sliceU = slice.ChordDir;
                    Vector3 sliceV = Vector3.Normalize(Vector3.Cross(currTangent, sliceU));
                    if (Vector3.Dot(sliceV, vAxis) < 0f) sliceV = -sliceV;
                    sliceU = Vector3.Normalize(Vector3.Cross(sliceV, currTangent));

                    float scaleU = Math.Clamp(slice.Scale, 0.08f, 3.2f);
                    float scaleV = Math.Clamp(MathF.Pow(scaleU, 0.78f), 0.08f, 2.5f);

                    int[] ringK = new int[nRing];
                    for (int i = 0; i < nRing; i++)
                    {
                        Vector2 loc = localRingOffsets[i];
                        Vector3 pt3D = slice.Center + sliceU * (loc.X * scaleU) + sliceV * (loc.Y * scaleV);
                        ringK[i] = extrudedMesh.Vertices.Count;
                        extrudedMesh.Vertices.Add(pt3D);
                    }

                    ringVertexIndices.Add(ringK);
                    prevCenter = slice.Center;
                }

                // Khâu các vòng thiết diện liên tiếp nhau thành mặt lưới
                for (int r = 0; r < ringVertexIndices.Count - 1; r++)
                {
                    int[] rA = ringVertexIndices[r];
                    int[] rB = ringVertexIndices[r + 1];

                    for (int i = 0; i < nRing; i++)
                    {
                        int next = (i + 1) % nRing;
                        int a0 = rA[i];
                        int a1 = rA[next];
                        int b0 = rB[i];
                        int b1 = rB[next];

                        extrudedMesh.Triangles.Add(new TeddyTriangle(a0, a1, b1));
                        extrudedMesh.Triangles.Add(new TeddyTriangle(a0, b1, b0));
                    }
                }

                // Đóng nắp chóp (Apex cap) ở đỉnh cuối của nét đùn
                int[] lastRing = ringVertexIndices[ringVertexIndices.Count - 1];
                Vector3 apexCenter = Vector3.Zero;
                for (int i = 0; i < nRing; i++) apexCenter += extrudedMesh.Vertices[lastRing[i]];
                apexCenter /= nRing;
                apexCenter += currTangent * (ringRadius * 0.12f);

                int apexIdx = extrudedMesh.Vertices.Count;
                extrudedMesh.Vertices.Add(apexCenter);

                for (int i = 0; i < nRing; i++)
                {
                    int next = (i + 1) % nRing;
                    extrudedMesh.Triangles.Add(new TeddyTriangle(lastRing[i], lastRing[next], apexIdx));
                }

                mesh.ActiveBaseRing = null;
                extrudedMesh.BuildConnectivity();
                extrudedMesh.RecalculateNormals();

                statusMessage = $"Đã đùn thành khối 3D mới độc lập (Extrusion - Hình 6 & 18) gồm {sweepSlices.Count} lát cắt.";
                return true;
            }
            else
            {
                // =========================================================================
                // KHOÉT LÕM (CAVITY): Biến dạng trực tiếp vào lòng khối cơ sở
                // =========================================================================
                extrudedMesh = null;

                // Bước 4: Xóa các tam giác cũ nằm bên trong vòng chọn Base Ring trên bề mặt mô hình
                RemoveTrianglesInsideBaseRing(mesh, baseRing, ringCenter, ringNormal, uAxis, vAxis, ringRadius, out List<int> holeBoundaryVerts);

                // Bước 5: Tạo các vòng thiết diện 3D dọc theo đường quét và khâu lưới (Hình 18b)
                var ringVertexIndices = new List<int[]>();

                // Vòng 0: Khớp trực tiếp lên bề mặt tại vị trí Base Ring
                int[] ring0 = new int[nRing];
                for (int i = 0; i < nRing; i++)
                {
                    // Hạ nhẹ chân vòng 0 xuống 1.2 đơn vị để hàn kín vào thân mô hình
                    Vector3 rootPt = baseRing[i] - ringNormal * 1.2f;
                    ring0[i] = mesh.Vertices.Count;
                    mesh.Vertices.Add(rootPt);
                }
                ringVertexIndices.Add(ring0);

                // Nếu có biên lỗ thủng từ các tam giác vừa xóa, khâu kín biên lỗ thủng vào Vòng 0
                if (holeBoundaryVerts.Count >= 3)
                {
                    StitchHoleBoundaryToBaseRing(mesh, holeBoundaryVerts, ring0, ringCenter, uAxis, vAxis);
                }

                // Sinh các vòng tiếp theo từ danh sách lát cắt 2 con trỏ
                Vector3 prevCenter = ringCenter;
                Vector3 currTangent = -ringNormal;

                for (int s = 0; s < sweepSlices.Count; s++)
                {
                    var slice = sweepSlices[s];
                    Vector3 stepVec = slice.Center - prevCenter;
                    if (stepVec.LengthSquared() > 1e-3f)
                    {
                        currTangent = Vector3.Normalize(stepVec);
                    }

                    // Khung tọa độ cục bộ vuông góc với hướng đùn (Mục 5.3: "almost perpendicular to the direction of the extrusion")
                    Vector3 sliceU = slice.ChordDir;
                    Vector3 sliceV = Vector3.Normalize(Vector3.Cross(currTangent, sliceU));
                    if (Vector3.Dot(sliceV, vAxis) < 0f) sliceV = -sliceV;
                    sliceU = Vector3.Normalize(Vector3.Cross(sliceV, currTangent));

                    float scaleU = Math.Clamp(slice.Scale, 0.08f, 3.2f);
                    float scaleV = Math.Clamp(MathF.Pow(scaleU, 0.78f), 0.08f, 2.5f);

                    int[] ringK = new int[nRing];
                    for (int i = 0; i < nRing; i++)
                    {
                        Vector2 loc = localRingOffsets[i];
                        Vector3 pt3D = slice.Center + sliceU * (loc.X * scaleU) + sliceV * (loc.Y * scaleV);
                        ringK[i] = mesh.Vertices.Count;
                        mesh.Vertices.Add(pt3D);
                    }

                    ringVertexIndices.Add(ringK);
                    prevCenter = slice.Center;
                }

                // Khâu các vòng thiết diện liên tiếp nhau thành mặt lưới (Hình 18b)
                for (int r = 0; r < ringVertexIndices.Count - 1; r++)
                {
                    int[] rA = ringVertexIndices[r];
                    int[] rB = ringVertexIndices[r + 1];

                    for (int i = 0; i < nRing; i++)
                    {
                        int next = (i + 1) % nRing;
                        int a0 = rA[i];
                        int a1 = rA[next];
                        int b0 = rB[i];
                        int b1 = rB[next];

                        // Đảo thứ tự đỉnh để mặt trong hốc lõm hướng ra phía Camera
                        mesh.Triangles.Add(new TeddyTriangle(a0, b1, a1));
                        mesh.Triangles.Add(new TeddyTriangle(a0, b0, b1));
                    }
                }

                // Đóng nắp chóp (Apex cap) ở đáy hốc lõm
                int[] lastRing = ringVertexIndices[ringVertexIndices.Count - 1];
                Vector3 apexCenter = Vector3.Zero;
                for (int i = 0; i < nRing; i++) apexCenter += mesh.Vertices[lastRing[i]];
                apexCenter /= nRing;
                apexCenter += currTangent * (ringRadius * 0.12f);

                int apexIdx = mesh.Vertices.Count;
                mesh.Vertices.Add(apexCenter);

                for (int i = 0; i < nRing; i++)
                {
                    int next = (i + 1) % nRing;
                    mesh.Triangles.Add(new TeddyTriangle(lastRing[i], apexIdx, lastRing[next]));
                }

                mesh.ActiveBaseRing = null;
                mesh.BuildConnectivity();
                mesh.RecalculateNormals();

                statusMessage = $"Đã khoét hốc lõm 3D (Digging a Cavity - Hình 7a-c) gồm {sweepSlices.Count} lát cắt.";
                return true;
            }
        }

        /// <summary>
        /// Quá tải tương thích ngược cho phép gọi đùn khối không cần nhận tham số out TeddyMesh.
        /// </summary>
        public static bool TryExtrudeOrDigFromBaseRing(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawExtrudeStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out string statusMessage)
        {
            return TryExtrudeOrDigFromBaseRing(mesh, rawExtrudeStroke, camera, viewport, out _, out statusMessage);
        }

        /// <summary>
        /// [Mục 4.5 & 5.4 - Hình 8, 9, 20] Thực hiện phép Cắt khối (Cutting):
        /// Chia đôi vật thể theo mặt phẳng tạo bởi vị trí Camera và nét cắt, xóa bỏ phần bên trái nét cắt,
        /// bịt kín thiết diện mặt cắt và tô đỏ viền cắt để có thể đùn tiếp (Extrusion after Cutting).
        /// </summary>
        public static bool TryCutMeshWithStroke(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawCutStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || rawCutStroke == null || rawCutStroke.Count < 3)
            {
                statusMessage = "Nét cắt không hợp lệ.";
                return false;
            }

            // Lấy mẫu mượt nét cắt trên màn hình
            List<PointF> cutPolyScreen = StrokeProcessor.ResampleUniform(rawCutStroke, 10f, false);
            if (cutPolyScreen.Count < 3)
            {
                statusMessage = "Nét cắt quá ngắn.";
                return false;
            }

            // Tính khoảng cách có dấu (Signed Distance) của từng đỉnh 3D chiếu lên màn hình so với đường nét cắt có hướng
            // Theo Mục 4.5: "What is on the screen to the left of the stroke is then removed entirely (Figure 3p)"
            int origVertCount = mesh.Vertices.Count;
            float[] signedDist = new float[origVertCount];
            int leftCount = 0;
            int rightCount = 0;

            for (int i = 0; i < origVertCount; i++)
            {
                Vector3 sp = camera.ProjectToScreen(mesh.Vertices[i], viewport);
                float d = ComputeSignedDistanceToDirectedPolyline(new PointF(sp.X, sp.Y), cutPolyScreen);
                signedDist[i] = d;
                if (d < 0f) leftCount++;
                else rightCount++;
            }

            if (leftCount == 0 || rightCount == 0)
            {
                statusMessage = "Nét cắt không chia mô hình thành 2 phần. Hãy vẽ nét cắt xuyên qua mô hình.";
                return false;
            }

            // Bảng lưu đỉnh giao cắt trên từng cạnh (a, b) để tái sử dụng đỉnh chung
            var cutEdgeVertMap = new Dictionary<(int, int), int>();
            var cutBoundarySegments = new List<(int A, int B)>();

            int GetOrCreateCutVertex(int vA, int vB)
            {
                var key = vA < vB ? (vA, vB) : (vB, vA);
                if (cutEdgeVertMap.TryGetValue(key, out int cutIdx))
                    return cutIdx;

                float dA = signedDist[vA];
                float dB = signedDist[vB];
                float denom = dA - dB;
                float t = MathF.Abs(denom) > 1e-6f ? Math.Clamp(dA / denom, 0f, 1f) : 0.5f;

                Vector3 pos = Vector3.Lerp(mesh.Vertices[vA], mesh.Vertices[vB], t);
                cutIdx = mesh.Vertices.Count;
                mesh.Vertices.Add(pos);
                cutEdgeVertMap[key] = cutIdx;
                return cutIdx;
            }

            var keptTriangles = new List<TeddyTriangle>(mesh.Triangles.Count);

            foreach (var tri in mesh.Triangles)
            {
                int i0 = tri.V0;
                int i1 = tri.V1;
                int i2 = tri.V2;

                bool k0 = signedDist[i0] >= 0f; // Giữ lại phía bên phải (d >= 0), xóa phía bên trái (d < 0)
                bool k1 = signedDist[i1] >= 0f;
                bool k2 = signedDist[i2] >= 0f;

                int keptCount = (k0 ? 1 : 0) + (k1 ? 1 : 0) + (k2 ? 1 : 0);

                if (keptCount == 3)
                {
                    keptTriangles.Add(tri);
                }
                else if (keptCount == 0)
                {
                    // Nằm hoàn toàn bên trái nét cắt -> Xóa bỏ
                    continue;
                }
                else if (keptCount == 1)
                {
                    // 1 đỉnh được giữ (A), 2 đỉnh bị cắt bỏ (B, C) theo đúng thứ tự CCW
                    int a, b, c;
                    if (k0) { a = i0; b = i1; c = i2; }
                    else if (k1) { a = i1; b = i2; c = i0; }
                    else { a = i2; b = i0; c = i1; }

                    int ab = GetOrCreateCutVertex(a, b);
                    int ac = GetOrCreateCutVertex(a, c);

                    keptTriangles.Add(new TeddyTriangle(a, ab, ac));
                    cutBoundarySegments.Add((ab, ac));
                }
                else if (keptCount == 2)
                {
                    // 2 đỉnh được giữ (B, C), 1 đỉnh bị cắt bỏ (A) theo thứ tự CCW (A, B, C)
                    int a, b, c;
                    if (!k0) { a = i0; b = i1; c = i2; }
                    else if (!k1) { a = i1; b = i2; c = i0; }
                    else { a = i2; b = i0; c = i1; }

                    int ab = GetOrCreateCutVertex(a, b);
                    int ac = GetOrCreateCutVertex(a, c);

                    keptTriangles.Add(new TeddyTriangle(ab, b, c));
                    keptTriangles.Add(new TeddyTriangle(ab, c, ac));
                    cutBoundarySegments.Add((ac, ab));
                }
            }

            if (keptTriangles.Count == 0 || cutBoundarySegments.Count < 3)
            {
                statusMessage = "Không thể hoàn tất mặt cắt.";
                return false;
            }

            // Nối các đoạn biên tại vết cắt thành vòng thiết diện khép kín (Section Ring - Hình 20)
            List<int> sectionLoop = AssembleOrderedLoop(mesh.Vertices, cutBoundarySegments);

            // Bịt kín mặt cắt bằng Constrained Delaunay Triangulation (Mục 5.4: "triangulates each planar polygon [22]")
            if (sectionLoop.Count >= 3)
            {
                Vector3 capCenter = Vector3.Zero;
                var sectionRingPoints = new List<Vector3>(sectionLoop.Count);
                foreach (int vIdx in sectionLoop)
                {
                    capCenter += mesh.Vertices[vIdx];
                    sectionRingPoints.Add(mesh.Vertices[vIdx]);
                }
                capCenter /= sectionLoop.Count;

                Vector3 secNormal = TeddyRaycaster.ComputeRingNormal(sectionRingPoints);
                Vector3 uSec = Vector3.Normalize(sectionRingPoints[0] - capCenter - Vector3.Dot(sectionRingPoints[0] - capCenter, secNormal) * secNormal);
                if (uSec.LengthSquared() < 1e-4f) uSec = Vector3.UnitX;
                Vector3 vSec = Vector3.Normalize(Vector3.Cross(secNormal, uSec));

                var poly2D = new List<Vector2>(sectionLoop.Count);
                for (int i = 0; i < sectionLoop.Count; i++)
                {
                    Vector3 rel = sectionRingPoints[i] - capCenter;
                    poly2D.Add(new Vector2(Vector3.Dot(rel, uSec), Vector3.Dot(rel, vSec)));
                }

                // Lưu bản sao chỉ số đỉnh theo hướng của poly2D (vì TriangulatePolygonCDT có thể đảo chiều nếu ngược CCW)
                var orderedLoopIndices = new List<int>(sectionLoop);
                if (Delaunay2D.ComputeSignedArea(poly2D) < 0f)
                {
                    orderedLoopIndices.Reverse();
                }

                List<Tri2D> capCdt = Delaunay2D.TriangulatePolygonCDT(poly2D);
                if (capCdt.Count > 0)
                {
                    foreach (var ct in capCdt)
                    {
                        if (ct.V0 < orderedLoopIndices.Count && ct.V1 < orderedLoopIndices.Count && ct.V2 < orderedLoopIndices.Count)
                        {
                            keptTriangles.Add(new TeddyTriangle(
                                orderedLoopIndices[ct.V0],
                                orderedLoopIndices[ct.V1],
                                orderedLoopIndices[ct.V2]));
                        }
                    }
                }
                else
                {
                    int capCenterIdx = mesh.Vertices.Count;
                    mesh.Vertices.Add(capCenter);
                    for (int i = 0; i < sectionLoop.Count; i++)
                    {
                        int vCurr = sectionLoop[i];
                        int vNext = sectionLoop[(i + 1) % sectionLoop.Count];
                        keptTriangles.Add(new TeddyTriangle(vCurr, vNext, capCenterIdx));
                    }
                }

                // Tô đỏ viền mặt cắt (ActiveBaseRing) để chuyển sang Extrusion Mode (Mục 4.5 - Hình 3p, Hình 9)
                mesh.ActiveBaseRing = new TeddySurfaceLine
                {
                    Points = sectionRingPoints,
                    StrokeColor = Color.FromArgb(239, 68, 68),
                    StrokeWidth = 3.8f,
                    IsClosedRing = true
                };
            }

            mesh.Triangles.Clear();
            mesh.Triangles.AddRange(keptTriangles);

            // Dọn dẹp các đỉnh mồ côi (Compact unused vertices) để lưới gọn nhẹ
            CompactMeshVertices(mesh);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();

            statusMessage = "Đã cắt mô hình (Cutting - Hình 8 & 20)! Viền mặt cắt đã chuyển sang màu đỏ: Vẽ nét tiếp để Đùn từ vết cắt (Hình 9) hoặc Click đơn để hoàn tất.";
            return true;
        }

        // =========================================================================
        // CÁC HÀM BỔ TRỢ TOÁN HỌC CHO EXTRUSION & CUTTING
        // =========================================================================

        private struct SweepSlice
        {
            public Vector3 Center;
            public Vector3 ChordDir;
            public float Scale;
        }

        /// <summary>
        /// [Mục 5.3 - Hình 18a] Thuật toán 2 con trỏ (Left & Right Pointers) tiến từ hai chân nét đùn lên đỉnh.
        /// </summary>
        private static List<SweepSlice> BuildTwoPointerSweepSlices(
            List<Vector3> stroke,
            Vector3 ringCenter,
            float baseWidth)
        {
            var slices = new List<SweepSlice>();
            int m = stroke.Count;
            if (m < 4) return slices;

            Vector3 baseMid = 0.5f * (stroke[0] + stroke[m - 1]);
            Vector3 offsetToRing = ringCenter - baseMid;

            int left = 0;
            int right = m - 1;

            while (right - left > 1)
            {
                // Xét 3 ứng viên: tăng Left, giảm Right, hoặc tiến cả hai (Mục 5.3 trang 6)
                int bestLeft = left + 1;
                int bestRight = right - 1;

                if (right - left > 2)
                {
                    float gBoth = EvaluatePointerGoodness(stroke, left + 1, right - 1);
                    float gLeft = EvaluatePointerGoodness(stroke, left + 1, right);
                    float gRight = EvaluatePointerGoodness(stroke, left, right - 1);

                    if (gLeft >= gBoth && gLeft >= gRight)
                    {
                        bestLeft = left + 1;
                        bestRight = right;
                    }
                    else if (gRight >= gBoth && gRight >= gLeft)
                    {
                        bestLeft = left;
                        bestRight = right - 1;
                    }
                }

                left = bestLeft;
                right = bestRight;

                if (left >= right) break;

                Vector3 pL = stroke[left] + offsetToRing;
                Vector3 pR = stroke[right] + offsetToRing;
                Vector3 chord = pR - pL;
                float width = chord.Length();
                if (width < 1e-3f) break;

                slices.Add(new SweepSlice
                {
                    Center = 0.5f * (pL + pR),
                    ChordDir = chord / width,
                    Scale = width / baseWidth
                });
            }

            // Giảm mật độ lát cắt sao cho vừa đủ mượt (khoảng 6 - 14 vòng)
            if (slices.Count > 12)
            {
                var decimated = new List<SweepSlice>();
                float step = (slices.Count - 1) / 10f;
                for (int i = 0; i <= 10; i++)
                {
                    int idx = Math.Clamp((int)MathF.Round(i * step), 0, slices.Count - 1);
                    decimated.Add(slices[idx]);
                }
                return decimated;
            }

            return slices;
        }

        private static float EvaluatePointerGoodness(List<Vector3> stroke, int l, int r)
        {
            Vector3 chord = stroke[r] - stroke[l];
            float len = chord.Length();
            if (len < 1e-4f) return -10f;
            chord /= len;

            int lNext = Math.Min(stroke.Count - 1, l + 1);
            int rPrev = Math.Max(0, r - 1);

            Vector3 tanL = Vector3.Normalize(stroke[lNext] - stroke[Math.Max(0, l - 1)]);
            Vector3 tanR = Vector3.Normalize(stroke[rPrev] - stroke[Math.Min(stroke.Count - 1, r + 1)]);

            // Độ tốt cao nhất khi góc giữa dây cung (pL - pR) và hướng nét vẽ tại mỗi con trỏ gần 90 độ (Dot gần 0)
            float perpL = 1.0f - MathF.Abs(Vector3.Dot(chord, tanL));
            float perpR = 1.0f - MathF.Abs(Vector3.Dot(chord, tanR));
            return perpL + perpR;
        }

        private static Vector3 DetermineOutwardRingNormal(TeddyMesh mesh, List<Vector3> ring, Vector3 ringCenter)
        {
            Vector3 n = TeddyRaycaster.ComputeRingNormal(ring);

            // Kiểm tra pháp tuyến của các mặt tam giác gần tâm vòng chọn để đảm bảo n luôn hướng ra ngoài vật thể
            Vector3 avgSurfaceNormal = Vector3.Zero;
            float bestDist = float.MaxValue;

            foreach (var tri in mesh.Triangles)
            {
                Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                float d = Vector3.DistanceSquared(c, ringCenter);
                if (d < bestDist)
                {
                    bestDist = d;
                    avgSurfaceNormal = tri.FaceNormal;
                }
            }

            if (avgSurfaceNormal.LengthSquared() > 1e-4f && Vector3.Dot(n, avgSurfaceNormal) < 0f)
            {
                n = -n;
            }

            return n;
        }

        private static void EnsureRingOrientation(List<Vector3> ring, Vector3 center, Vector3 uAxis, Vector3 vAxis)
        {
            float signedArea = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector3 r0 = ring[i] - center;
                Vector3 r1 = ring[(i + 1) % ring.Count] - center;
                float x0 = Vector3.Dot(r0, uAxis);
                float y0 = Vector3.Dot(r0, vAxis);
                float x1 = Vector3.Dot(r1, uAxis);
                float y1 = Vector3.Dot(r1, vAxis);
                signedArea += (x0 * y1 - x1 * y0);
            }

            if (signedArea < 0f)
            {
                ring.Reverse();
            }
        }

        private static void RemoveTrianglesInsideBaseRing(
            TeddyMesh mesh,
            List<Vector3> baseRing,
            Vector3 ringCenter,
            Vector3 ringNormal,
            Vector3 uAxis,
            Vector3 vAxis,
            float ringRadius,
            out List<int> holeBoundaryVerts)
        {
            holeBoundaryVerts = new List<int>();
            var ring2D = new List<Vector2>(baseRing.Count);
            foreach (var p in baseRing)
            {
                Vector3 rel = p - ringCenter;
                ring2D.Add(new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis)));
            }

            var keptTris = new List<TeddyTriangle>(mesh.Triangles.Count);
            var removedTriEdgeCount = new Dictionary<(int, int), int>();

            foreach (var tri in mesh.Triangles)
            {
                Vector3 p0 = mesh.Vertices[tri.V0];
                Vector3 p1 = mesh.Vertices[tri.V1];
                Vector3 p2 = mesh.Vertices[tri.V2];
                Vector3 centroid = (p0 + p1 + p2) / 3f;

                Vector3 rel = centroid - ringCenter;
                float distAlongNormal = MathF.Abs(Vector3.Dot(rel, ringNormal));
                Vector2 pt2D = new Vector2(Vector3.Dot(rel, uAxis), Vector3.Dot(rel, vAxis));

                bool facingSameSide = Vector3.Dot(tri.FaceNormal, ringNormal) > -0.15f;
                if (facingSameSide && distAlongNormal < ringRadius * 1.35f && IsPointInPolygon2D(pt2D, ring2D))
                {
                    // Tam giác nằm bên trong Base Ring -> Xóa bỏ để mở miệng cho phần đùn/khoét
                    int[] v = new int[] { tri.V0, tri.V1, tri.V2 };
                    for (int e = 0; e < 3; e++)
                    {
                        int a = v[e];
                        int b = v[(e + 1) % 3];
                        var key = a < b ? (a, b) : (b, a);
                        removedTriEdgeCount[key] = removedTriEdgeCount.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                }
                else
                {
                    keptTris.Add(tri);
                }
            }

            if (keptTris.Count < mesh.Triangles.Count && keptTris.Count > 0)
            {
                mesh.Triangles.Clear();
                mesh.Triangles.AddRange(keptTris);

                var boundaryVertSet = new HashSet<int>();
                foreach (var kvp in removedTriEdgeCount)
                {
                    if (kvp.Value == 1)
                    {
                        boundaryVertSet.Add(kvp.Key.Item1);
                        boundaryVertSet.Add(kvp.Key.Item2);
                    }
                }

                holeBoundaryVerts.AddRange(boundaryVertSet);
                // Sắp xếp các đỉnh biên lỗ thủng theo góc cực quanh ringCenter
                holeBoundaryVerts.Sort((a, b) =>
                {
                    Vector3 ra = mesh.Vertices[a] - ringCenter;
                    Vector3 rb = mesh.Vertices[b] - ringCenter;
                    float angA = MathF.Atan2(Vector3.Dot(ra, vAxis), Vector3.Dot(ra, uAxis));
                    float angB = MathF.Atan2(Vector3.Dot(rb, vAxis), Vector3.Dot(rb, uAxis));
                    return angA.CompareTo(angB);
                });
            }
        }

        private static void StitchHoleBoundaryToBaseRing(
            TeddyMesh mesh,
            List<int> holeVerts,
            int[] ring0,
            Vector3 center,
            Vector3 uAxis,
            Vector3 vAxis)
        {
            int hCount = holeVerts.Count;
            int rCount = ring0.Length;
            if (hCount < 3 || rCount < 3) return;

            for (int i = 0; i < hCount; i++)
            {
                int hCurr = holeVerts[i];
                int hNext = holeVerts[(i + 1) % hCount];

                int rCurr = ring0[(i * rCount / hCount) % rCount];
                int rNext = ring0[((i + 1) * rCount / hCount) % rCount];

                mesh.Triangles.Add(new TeddyTriangle(hCurr, hNext, rNext));
                if (rCurr != rNext)
                {
                    mesh.Triangles.Add(new TeddyTriangle(hCurr, rNext, rCurr));
                }
            }
        }

        public static bool IsPointInPolygon2D(Vector2 pt, IReadOnlyList<Vector2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = poly[i];
                Vector2 pj = poly[j];
                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y + 1e-7f) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static List<Vector3> Resample3DClosedRing(IReadOnlyList<Vector3> rawRing, int targetCount)
        {
            var res = new List<Vector3>(targetCount);
            if (rawRing == null || rawRing.Count < 3) return res;

            int n = rawRing.Count;
            float totalLen = 0f;
            float[] segLens = new float[n];
            for (int i = 0; i < n; i++)
            {
                segLens[i] = Vector3.Distance(rawRing[i], rawRing[(i + 1) % n]);
                totalLen += segLens[i];
            }

            if (totalLen < 1e-4f) return res;
            float step = totalLen / targetCount;

            for (int k = 0; k < targetCount; k++)
            {
                float targetDist = k * step;
                float accum = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (accum + segLens[i] >= targetDist || i == n - 1)
                    {
                        float t = segLens[i] > 1e-5f ? (targetDist - accum) / segLens[i] : 0f;
                        res.Add(Vector3.Lerp(rawRing[i], rawRing[(i + 1) % n], Math.Clamp(t, 0f, 1f)));
                        break;
                    }
                    accum += segLens[i];
                }
            }

            return res;
        }

        /// <summary>
        /// Tính khoảng cách có dấu từ điểm p (màn hình) tới đường cắt có hướng (Cut Polyline).
        /// Giá trị âm (< 0) là nằm bên trái hướng vẽ nét cắt (vùng bị cắt bỏ theo Mục 4.5).
        /// </summary>
        private static float ComputeSignedDistanceToDirectedPolyline(PointF p, List<PointF> polyline)
        {
            int n = polyline.Count;
            float bestDistSq = float.MaxValue;
            float bestSign = 1f;

            for (int i = 0; i < n - 1; i++)
            {
                PointF a = polyline[i];
                PointF b = polyline[i + 1];

                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float lenSq = dx * dx + dy * dy;
                if (lenSq < 1e-4f) continue;

                float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
                // Cho phép tia kéo dài ở đoạn đầu tiên và đoạn cuối cùng để mặt cắt xuyên suốt toàn không gian
                float clampedT = (i == 0 && t < 0f) ? t : ((i == n - 2 && t > 1f) ? t : Math.Clamp(t, 0f, 1f));

                float projX = a.X + clampedT * dx;
                float projY = a.Y + clampedT * dy;
                float distSq = (p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY);

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    // Trong hệ tọa độ màn hình (X sang phải, Y xuống dưới):
                    // Cross = dx * (p.Y - a.Y) - dy * (p.X - a.X) < 0 tương ứng với phía bên TRÁI hướng vẽ (a -> b)
                    float cross = dx * (p.Y - a.Y) - dy * (p.X - a.X);
                    bestSign = cross < 0f ? -1f : 1f;
                }
            }

            return bestSign * MathF.Sqrt(bestDistSq);
        }

        private static List<int> AssembleOrderedLoop(List<Vector3> vertices, List<(int A, int B)> segments)
        {
            var loop = new List<int>();
            if (segments.Count == 0) return loop;

            var used = new bool[segments.Count];
            loop.Add(segments[0].A);
            int curr = segments[0].B;
            used[0] = true;

            for (int step = 0; step < segments.Count; step++)
            {
                loop.Add(curr);
                int nextSeg = -1;
                float bestDist = float.MaxValue;

                for (int i = 0; i < segments.Count; i++)
                {
                    if (used[i]) continue;
                    if (segments[i].A == curr)
                    {
                        nextSeg = i;
                        break;
                    }
                    float d = Vector3.DistanceSquared(vertices[curr], vertices[segments[i].A]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        nextSeg = i;
                    }
                }

                if (nextSeg < 0) break;
                used[nextSeg] = true;
                curr = segments[nextSeg].B;
                if (curr == loop[0]) break;
            }

            return loop;
        }

        public static void CompactMeshVertices(TeddyMesh mesh)
        {
            var remap = new Dictionary<int, int>();
            var newVerts = new List<Vector3>(mesh.Vertices.Count);

            int MapIndex(int oldIdx)
            {
                if (remap.TryGetValue(oldIdx, out int newIdx))
                    return newIdx;
                newIdx = newVerts.Count;
                newVerts.Add(mesh.Vertices[oldIdx]);
                remap[oldIdx] = newIdx;
                return newIdx;
            }

            foreach (var tri in mesh.Triangles)
            {
                tri.V0 = MapIndex(tri.V0);
                tri.V1 = MapIndex(tri.V1);
                tri.V2 = MapIndex(tri.V2);
            }

            mesh.Vertices.Clear();
            mesh.Vertices.AddRange(newVerts);
        }
    }
}
