using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV2-05] Thực hiện phép Làm mượt vùng chọn (Smoothing - Mục 4.6 & 5.5, Hình 10, 21)
    /// và phép Uốn biến dạng mô hình bằng 2 nét vẽ (Bending / Transformation - Mục 4.7, Hình 11).
    /// </summary>
    public static class TeddySmoothBendOp
    {
        /// <summary>
        /// [Mục 4.6 & 5.5 - Hình 10, 21] Làm mượt vùng bề mặt được bao quanh bởi vòng chọn màu đỏ (ActiveBaseRing)
        /// khi người dùng thực hiện cử chỉ gạch xóa (Scribbling) trong chế độ Extrusion Mode.
        /// Kết hợp nội suy đường cong Bezier qua tâm vòng (Hình 21c) và làm trơn bề mặt cục bộ (Surface Fairing - Hình 21d).
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
            Vector3 uAxis = Vector3.Normalize(ringPts[0] - ringCenter - Vector3.Dot(ringPts[0] - ringCenter, ringNormal) * ringNormal);
            if (uAxis.LengthSquared() < 1e-4f) uAxis = Vector3.UnitX;
            Vector3 vAxis = Vector3.Normalize(Vector3.Cross(ringNormal, uAxis));

            // Chiếu vòng chọn lên mặt phẳng XY cục bộ và lưu độ cao Z + độ dốc biên của từng đỉnh vòng
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

            // Tìm tất cả các đỉnh nằm bên trong vùng vòng chọn để nội suy Bezier qua từng cặp cạnh đối diện (Hình 21c)
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
                    // [Mục 5.5 - Hình 21c] Duyệt qua các đường kính nối trung điểm cạnh biên i và điểm đối diện (i + N/2),
                    // nội suy đường cong Bezier bậc 3 trên mặt phẳng song song trục Z và lấy trung bình có trọng số
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

                        // Khoảng cách từ đỉnh đang xét tới mặt phẳng thẳng đứng đi qua (pA, pB)
                        Vector2 perp = (xy - pA) - proj * dir;
                        float distToPlane = perp.Length();
                        float weight = 1.0f / (4.0f + distToPlane * distToPlane);

                        // Đường cong Bezier bậc 3 nội suy hai đầu (zA, zB) với tiếp tuyến vòm nhẹ ở giữa (Hình 21c)
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

            // Áp dụng Surface Fairing (Mục 5.5 cuối trang 7: "apply a surface-fairing algorithm [24] to the newly created polygons")
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
