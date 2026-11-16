using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Các mẫu đường cắt ngang vật thể 3D (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do).
    /// </summary>
    public enum TeddyCutPattern
    {
        Freehand,   // Cắt tự do theo nét vẽ tay
        Straight,   // Đường cắt thẳng phẳng
        Zigzag,     // Đường cắt răng cưa Zíc-zắc
        Curved,     // Đường cắt cong vòm (Cung tròn / Parabol)
        Wave,       // Đường cắt lượn sóng hình Sin
        Stepped,    // Đường cắt gấp khúc bậc thang vuông
        ChevronV    // Đường cắt mũi nhọn chữ V
    }

    /// <summary>
    /// Chế độ xử lý vật thể sau khi cắt:
    /// - RemoveOneSide: Xóa bỏ 1 phía của đường cắt, bịt kín thiết diện và tạo viền đỏ để có thể đùn tiếp (chuẩn bài báo Teddy).
    /// - SplitIntoTwoMeshes: Tách đôi vật thể thành 2 khối 3D độc lập khớp nhau theo đúng biên dạng đường cắt.
    /// </summary>
    public enum TeddyCutActionMode
    {
        RemoveOneSide,
        SplitIntoTwoMeshes
    }

    /// <summary>
    /// Bộ sinh mẫu đường cắt 2D (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do)
    /// dùng cho cả hiển thị Live Preview trên màn hình và giải thuật cắt mặt lưới 3D.
    /// </summary>
    public static class TeddyCutPatternGenerator
    {
        public static string GetPatternDisplayName(TeddyCutPattern pattern)
        {
            return pattern switch
            {
                TeddyCutPattern.Freehand => "Cắt Tự Do",
                TeddyCutPattern.Straight => "Đường Thẳng",
                TeddyCutPattern.Zigzag => "Zíc-zắc (Răng Cưa)",
                TeddyCutPattern.Curved => "Đường Cong (Cung Vòm)",
                TeddyCutPattern.Wave => "Lượn Sóng (Hình Sin)",
                TeddyCutPattern.Stepped => "Bậc Thang Vuông",
                TeddyCutPattern.ChevronV => "Gấp Khúc Chữ V",
                _ => "Cắt Tự Do"
            };
        }

        /// <summary>
        /// Biến đổi nét kéo chuột thô (rawStroke) thành đa tuyến mẫu đường cắt (Patterned Cut Polyline).
        /// </summary>
        public static List<PointF> BuildPatternedCutPolyline(
            IReadOnlyList<PointF> rawStroke,
            TeddyCutPattern pattern,
            float amplitudePx,
            int periodCount,
            bool flipRemoveSide,
            out PointF startAnchor,
            out PointF endAnchor,
            out Vector2 removeSideNormal)
        {
            startAnchor = PointF.Empty;
            endAnchor = PointF.Empty;
            removeSideNormal = new Vector2(0, -1);

            var result = new List<PointF>();
            if (rawStroke == null || rawStroke.Count == 0)
                return result;

            startAnchor = rawStroke[0];
            endAnchor = rawStroke[rawStroke.Count - 1];

            if (rawStroke.Count < 2)
            {
                result.Add(startAnchor);
                return result;
            }

            amplitudePx = Math.Clamp(amplitudePx, 4f, 90f);
            periodCount = Math.Clamp(periodCount, 1, 20);

            if (pattern == TeddyCutPattern.Freehand)
            {
                // Làm mượt nhẹ nét vẽ tay và lấy mẫu đều
                var smoothed = SmoothFreehandStroke(rawStroke);
                result = StrokeProcessor.ResampleUniform(smoothed, 5.5f, false);
                if (result.Count < 2)
                {
                    result.Clear();
                    result.Add(startAnchor);
                    result.Add(endAnchor);
                }
            }
            else
            {
                float dx = endAnchor.X - startAnchor.X;
                float dy = endAnchor.Y - startAnchor.Y;
                float chordLen = MathF.Sqrt(dx * dx + dy * dy);
                if (chordLen < 4f)
                {
                    result.Add(startAnchor);
                    result.Add(endAnchor);
                    return result;
                }

                Vector2 dir = new Vector2(dx / chordLen, dy / chordLen);
                // Vector pháp tuyến hướng về phía bên TRÁI của tia start -> end (trong hệ tọa độ màn hình Y hướng xuống)
                Vector2 leftNorm = new Vector2(dir.Y, -dir.X);

                switch (pattern)
                {
                    case TeddyCutPattern.Straight:
                    {
                        int steps = Math.Max(16, (int)(chordLen / 5f));
                        for (int i = 0; i <= steps; i++)
                        {
                            float u = i / (float)steps;
                            result.Add(new PointF(startAnchor.X + dx * u, startAnchor.Y + dy * u));
                        }
                        break;
                    }

                    case TeddyCutPattern.Zigzag:
                    {
                        // Tạo chuỗi đỉnh răng cưa nhọn rồi nội suy dày trên từng cạnh răng
                        int teeth = Math.Max(2, periodCount);
                        int totalHalfSegs = teeth * 2;
                        var keyPts = new List<PointF>(totalHalfSegs + 3);
                        float marginU = 0.06f;
                        keyPts.Add(startAnchor);
                        keyPts.Add(new PointF(startAnchor.X + dx * marginU, startAnchor.Y + dy * marginU));

                        float spanU = 1f - 2f * marginU;
                        for (int k = 0; k < totalHalfSegs; k++)
                        {
                            float u = marginU + ((k + 0.5f) / totalHalfSegs) * spanU;
                            float sign = (k % 2 == 0) ? 1f : -1f;
                            float bx = startAnchor.X + dx * u + leftNorm.X * (sign * amplitudePx);
                            float by = startAnchor.Y + dy * u + leftNorm.Y * (sign * amplitudePx);
                            keyPts.Add(new PointF(bx, by));
                        }

                        keyPts.Add(new PointF(startAnchor.X + dx * (1f - marginU), startAnchor.Y + dy * (1f - marginU)));
                        keyPts.Add(endAnchor);
                        result = SubdivideSharpPolyline(keyPts, 4.5f);
                        break;
                    }

                    case TeddyCutPattern.Curved:
                    {
                        int steps = Math.Max(32, (int)(chordLen / 4.5f));
                        float archHeight = amplitudePx * 1.45f;
                        for (int i = 0; i <= steps; i++)
                        {
                            float u = i / (float)steps;
                            // Cung vòm Parabol mượt: đạt đỉnh tại u = 0.5
                            float offset = 4f * archHeight * u * (1f - u);
                            float px = startAnchor.X + dx * u + leftNorm.X * offset;
                            float py = startAnchor.Y + dy * u + leftNorm.Y * offset;
                            result.Add(new PointF(px, py));
                        }
                        break;
                    }

                    case TeddyCutPattern.Wave:
                    {
                        int steps = Math.Max(48, (int)(chordLen / 3.8f));
                        int halfWaves = Math.Max(2, periodCount * 2);
                        float marginU = 0.05f;
                        float spanU = 1f - 2f * marginU;

                        for (int i = 0; i <= steps; i++)
                        {
                            float u = i / (float)steps;
                            float offset = 0f;
                            if (u >= marginU && u <= 1f - marginU)
                            {
                                float localU = (u - marginU) / spanU;
                                // Envelope làm mượt nhẹ 2 đầu để sóng nối êm vào 2 mút
                                float env = MathF.Sin(MathF.PI * Math.Clamp(localU, 0f, 1f));
                                env = MathF.Pow(env, 0.35f);
                                offset = amplitudePx * env * MathF.Sin(localU * halfWaves * MathF.PI);
                            }
                            float px = startAnchor.X + dx * u + leftNorm.X * offset;
                            float py = startAnchor.Y + dy * u + leftNorm.Y * offset;
                            result.Add(new PointF(px, py));
                        }
                        break;
                    }

                    case TeddyCutPattern.Stepped:
                    {
                        int stepsCount = Math.Max(2, periodCount * 2 - 1);
                        float marginU = 0.07f;
                        float spanU = 1f - 2f * marginU;
                        float stepW = spanU / stepsCount;
                        float slopeU = Math.Min(stepW * 0.14f, 0.015f);
                        float stepAmp = amplitudePx * 0.85f;

                        var keyPts = new List<PointF>();
                        keyPts.Add(startAnchor);
                        keyPts.Add(new PointF(startAnchor.X + dx * marginU, startAnchor.Y + dy * marginU));

                        for (int k = 0; k < stepsCount; k++)
                        {
                            float u0 = marginU + k * stepW;
                            float u1 = u0 + stepW;
                            float sign = (k % 2 == 0) ? 1f : -1f;
                            float off = sign * stepAmp;

                            float ua = u0 + slopeU * 0.5f;
                            float ub = u1 - slopeU * 0.5f;

                            keyPts.Add(new PointF(
                                startAnchor.X + dx * ua + leftNorm.X * off,
                                startAnchor.Y + dy * ua + leftNorm.Y * off));
                            keyPts.Add(new PointF(
                                startAnchor.X + dx * ub + leftNorm.X * off,
                                startAnchor.Y + dy * ub + leftNorm.Y * off));
                        }

                        keyPts.Add(new PointF(startAnchor.X + dx * (1f - marginU), startAnchor.Y + dy * (1f - marginU)));
                        keyPts.Add(endAnchor);
                        result = SubdivideSharpPolyline(keyPts, 4.5f);
                        break;
                    }

                    case TeddyCutPattern.ChevronV:
                    {
                        float vDepth = amplitudePx * 1.65f;
                        var keyPts = new List<PointF>
                        {
                            startAnchor,
                            new PointF(startAnchor.X + dx * 0.18f, startAnchor.Y + dy * 0.18f),
                            new PointF(startAnchor.X + dx * 0.50f - leftNorm.X * vDepth, startAnchor.Y + dy * 0.50f - leftNorm.Y * vDepth),
                            new PointF(startAnchor.X + dx * 0.82f, startAnchor.Y + dy * 0.82f),
                            endAnchor
                        };
                        result = SubdivideSharpPolyline(keyPts, 4.5f);
                        break;
                    }
                }
            }

            // Tính pháp tuyến phía bị xóa (bên trái của hướng polyline hiện tại)
            if (result.Count >= 2)
            {
                PointF pA = result[0];
                PointF pB = result[result.Count - 1];
                float vx = pB.X - pA.X;
                float vy = pB.Y - pA.Y;
                float vLen = MathF.Sqrt(vx * vx + vy * vy);
                if (vLen > 1e-3f)
                {
                    vx /= vLen;
                    vy /= vLen;
                    // Trong hệ trục màn hình (X phải, Y xuống): bên trái vector (vx, vy) là (vy, -vx)
                    removeSideNormal = new Vector2(vy, -vx);
                }
            }

            // Nếu người dùng bật "Đảo bên cắt": Đảo ngược thứ tự polyline để giữ nguyên hình dáng đường cắt trên màn hình
            // nhưng đổi phía bị cắt bỏ từ Trái sang Phải!
            if (flipRemoveSide && result.Count >= 2)
            {
                result.Reverse();
                removeSideNormal = -removeSideNormal;
            }

            return result;
        }

        /// <summary>
        /// Tạo tự động một nét kéo ngang qua chính giữa tâm bao 2D của mô hình 3D hiện tại
        /// để người dùng có thể bấm nút "Cắt Ngang Ngay" và trải nghiệm ngay mẫu đường cắt.
        /// </summary>
        public static List<PointF> CreateQuickHorizontalCutStroke(
            TeddyMesh mesh,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            if (mesh != null && !mesh.IsEmpty)
            {
                foreach (var v in mesh.Vertices)
                {
                    Vector3 sp = camera.ProjectToScreen(v, viewport);
                    if (sp.X < minX) minX = sp.X;
                    if (sp.Y < minY) minY = sp.Y;
                    if (sp.X > maxX) maxX = sp.X;
                    if (sp.Y > maxY) maxY = sp.Y;
                }
            }
            else
            {
                minX = viewport.Width * 0.3f;
                maxX = viewport.Width * 0.7f;
                minY = viewport.Height * 0.3f;
                maxY = viewport.Height * 0.7f;
            }

            float cy = (minY + maxY) * 0.5f;
            float padX = Math.Max(48f, (maxX - minX) * 0.22f);
            PointF pStart = new PointF(minX - padX, cy);
            PointF pEnd = new PointF(maxX + padX, cy);

            var raw = new List<PointF>(25);
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f;
                raw.Add(new PointF(pStart.X + (pEnd.X - pStart.X) * t, cy));
            }
            return raw;
        }

        public static List<PointF> SubdivideSharpPolyline(IReadOnlyList<PointF> keyPts, float maxSegLen = 4.5f)
        {
            var res = new List<PointF>();
            if (keyPts == null || keyPts.Count == 0) return res;
            res.Add(keyPts[0]);

            for (int i = 0; i < keyPts.Count - 1; i++)
            {
                PointF a = keyPts[i];
                PointF b = keyPts[i + 1];
                float len = StrokeProcessor.Distance(a, b);
                int sub = Math.Max(1, (int)MathF.Ceiling(len / maxSegLen));
                for (int s = 1; s <= sub; s++)
                {
                    float t = s / (float)sub;
                    res.Add(new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                }
            }
            return res;
        }

        private static List<PointF> SmoothFreehandStroke(IReadOnlyList<PointF> raw)
        {
            if (raw.Count <= 4) return new List<PointF>(raw);
            var curr = new List<PointF>(raw);
            for (int iter = 0; iter < 2; iter++)
            {
                var next = new List<PointF>(curr.Count);
                next.Add(curr[0]);
                for (int i = 1; i < curr.Count - 1; i++)
                {
                    float x = 0.25f * curr[i - 1].X + 0.5f * curr[i].X + 0.25f * curr[i + 1].X;
                    float y = 0.25f * curr[i - 1].Y + 0.5f * curr[i].Y + 0.25f * curr[i + 1].Y;
                    next.Add(new PointF(x, y));
                }
                next.Add(curr[curr.Count - 1]);
                curr = next;
            }
            return curr;
        }
    }

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
        /// Thực hiện phép Cắt ngang vật thể 3D hoặc Tách đôi vật thể thành 2 khối theo mẫu đường cắt (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do).
        /// </summary>
        public static bool TryCutOrSplitMeshWithPattern(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawCutStroke,
            TeddyCutPattern pattern,
            float amplitudePx,
            int periodCount,
            bool flipRemoveSide,
            TeddyCutActionMode actionMode,
            VirtualTrackballCamera camera,
            Size viewport,
            out TeddyMesh? secondSplitMesh,
            out string statusMessage)
        {
            secondSplitMesh = null;
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || rawCutStroke == null || rawCutStroke.Count < 2)
            {
                statusMessage = "Nét cắt không hợp lệ.";
                return false;
            }

            List<PointF> patternedPoly = TeddyCutPatternGenerator.BuildPatternedCutPolyline(
                rawCutStroke, pattern, amplitudePx, periodCount, flipRemoveSide,
                out _, out _, out Vector2 removeSideNormal);

            if (patternedPoly.Count < 2)
            {
                statusMessage = "Đường cắt quá ngắn.";
                return false;
            }

            string patternName = TeddyCutPatternGenerator.GetPatternDisplayName(pattern);

            if (!SliceSingleMeshByDirectedPolyline(mesh, patternedPoly, camera, viewport, markRedRing: true, out _))
            {
                statusMessage = $"Đường cắt [{patternName}] chưa đi xuyên qua vật thể. Hãy kéo đường cắt cắt ngang qua khối 3D!";
                return false;
            }

            statusMessage = $"Đã cắt bỏ phần thừa và tô màu vị trí bị cắt theo mẫu [{patternName}]! Bạn có thể bấm nút 'Vị trí cắt: Hiện / Ẩn' để quan sát vết cắt.";
            return true;
        }

        /// <summary>
        /// [Mục 4.5 & 5.4 - Hình 8, 9, 20] Thực hiện phép Cắt khối (Cutting) từ chuỗi điểm 2D:
        /// Chia đôi vật thể theo mặt kẻ (Ruled Surface) tạo bởi vị trí Camera và đường cắt, xóa bỏ phần bên trái nét cắt,
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
            if (mesh == null || mesh.IsEmpty || rawCutStroke == null || rawCutStroke.Count < 2)
            {
                statusMessage = "Nét cắt không hợp lệ.";
                return false;
            }

            List<PointF> cutPolyScreen = TeddyCutPatternGenerator.SubdivideSharpPolyline(rawCutStroke, 5.0f);
            if (cutPolyScreen.Count < 3)
            {
                statusMessage = "Nét cắt quá ngắn.";
                return false;
            }

            if (!SliceSingleMeshByDirectedPolyline(mesh, cutPolyScreen, camera, viewport, markRedRing: true, out statusMessage))
            {
                return false;
            }

            statusMessage = "Đã cắt mô hình (Cutting - Hình 8 & 20)! Viền mặt cắt đã chuyển sang màu đỏ: Vẽ nét tiếp để Đùn từ vết cắt (Hình 9) hoặc Click đơn để hoàn tất.";
            return true;
        }

        private static bool SliceSingleMeshByDirectedPolyline(
            TeddyMesh mesh,
            List<PointF> cutPolyScreen,
            VirtualTrackballCamera camera,
            Size viewport,
            bool markRedRing,
            out string statusMessage)
        {
            statusMessage = "";
            if (mesh == null || mesh.IsEmpty || cutPolyScreen == null || cutPolyScreen.Count < 2)
            {
                statusMessage = "Nét cắt không hợp lệ.";
                return false;
            }

            // Bước 1: Mở rộng hai đầu đường cắt ra xa ngoài vùng nhìn dọc theo hướng tổng thể
            // và dựng đa giác nửa mặt phẳng bên Phải (Right Half-Plane Jordan Polygon) để xét dấu chính xác 100% ở mọi góc nhọn Zíc-zắc / Bậc thang
            BuildExtendedPolylineAndRightHalfPolygon(
                cutPolyScreen,
                out List<PointF> extendedPoly,
                out List<Vector2> rightHalfPolygon);

            // Bước 2: Chia mịn cục bộ (Adaptive Manifold Midpoint Subdivision) các tam giác nằm gần đường cắt
            // giúp mặt cắt bám sát từng răng cưa Zíc-zắc, Bậc thang, Lượn sóng và Đường cong trên cả Khối Hộp / Khối Cầu thô
            SubdivideMeshAlongCutBand(mesh, cutPolyScreen, extendedPoly, camera, viewport);

            // Bước 3: Tính khoảng cách có dấu (Signed Distance) của từng đỉnh 3D chiếu lên màn hình
            int origVertCount = mesh.Vertices.Count;
            float[] signedDist = new float[origVertCount];
            int leftCount = 0;
            int rightCount = 0;

            for (int i = 0; i < origVertCount; i++)
            {
                Vector3 sp = camera.ProjectToScreen(mesh.Vertices[i], viewport);
                float d = ComputeSignedDistanceToExtendedPolyline(new PointF(sp.X, sp.Y), extendedPoly, rightHalfPolygon);
                // Khử suy biến khi đỉnh nằm đúng trên đường cắt (Simulation of Simplicity)
                if (MathF.Abs(d) < 1e-3f) d = 1e-3f;
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
                float t = MathF.Abs(denom) > 1e-6f ? Math.Clamp(dA / denom, 0.002f, 0.998f) : 0.5f;

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
                    continue;
                }
                else if (keptCount == 1)
                {
                    int a, b, c;
                    if (k0) { a = i0; b = i1; c = i2; }
                    else if (k1) { a = i1; b = i2; c = i0; }
                    else { a = i2; b = i0; c = i1; }

                    int ab = GetOrCreateCutVertex(a, b);
                    int ac = GetOrCreateCutVertex(a, c);

                    keptTriangles.Add(new TeddyTriangle(a, ab, ac, tri.Category, tri.CustomColor, tri.IsCutCap));
                    cutBoundarySegments.Add((ab, ac));
                }
                else if (keptCount == 2)
                {
                    int a, b, c;
                    if (!k0) { a = i0; b = i1; c = i2; }
                    else if (!k1) { a = i1; b = i2; c = i0; }
                    else { a = i2; b = i0; c = i1; }

                    int ab = GetOrCreateCutVertex(a, b);
                    int ac = GetOrCreateCutVertex(a, c);

                    keptTriangles.Add(new TeddyTriangle(ab, b, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                    keptTriangles.Add(new TeddyTriangle(ab, c, ac, tri.Category, tri.CustomColor, tri.IsCutCap));
                    cutBoundarySegments.Add((ac, ab));
                }
            }

            if (keptTriangles.Count == 0 || cutBoundarySegments.Count < 3)
            {
                statusMessage = "Không thể hoàn tất mặt cắt.";
                return false;
            }

            // Bước 4: Nối các đoạn biên tại vết cắt thành các vòng thiết diện khép kín (Section Loops)
            // Hỗ trợ cả trường hợp đường cắt đi qua nhiều nhánh / nhiều tai / nhiều răng sóng trên cùng 1 vật thể
            List<List<int>> sectionLoops = AssembleAllOrderedLoops(mesh.Vertices, cutBoundarySegments);
            List<Vector3>? largestLoopPoints = null;

            foreach (var sectionLoop in sectionLoops)
            {
                if (sectionLoop.Count < 3) continue;

                // Bịt kín mặt cắt trên không gian tham số mặt kẻ (s, z) để bảo toàn tuyệt đối biên dạng Zíc-zắc / Cong / Lượn sóng / Bậc thang
                CapSectionLoopInRuledParameterSpace(mesh, sectionLoop, extendedPoly, camera, viewport, keptTriangles);

                var loopPts = new List<Vector3>(sectionLoop.Count);
                foreach (int vIdx in sectionLoop)
                    loopPts.Add(mesh.Vertices[vIdx]);

                mesh.CutSectionRings.Add(new TeddySurfaceLine
                {
                    Points = loopPts,
                    StrokeColor = Color.FromArgb(225, 29, 72),
                    StrokeWidth = 3.2f,
                    IsClosedRing = true
                });

                if (largestLoopPoints == null || loopPts.Count > largestLoopPoints.Count)
                {
                    largestLoopPoints = loopPts;
                }
            }

            // Cắt tỉa các nét vẽ trang trí trên bề mặt (SurfaceLines) thuộc phần đã bị cắt bỏ
            ClipSurfaceLinesAfterCut(mesh, extendedPoly, rightHalfPolygon, camera, viewport);

            if (markRedRing && largestLoopPoints != null && largestLoopPoints.Count >= 3)
            {
                mesh.ActiveBaseRing = new TeddySurfaceLine
                {
                    Points = largestLoopPoints,
                    StrokeColor = Color.FromArgb(239, 68, 68),
                    StrokeWidth = 3.8f,
                    IsClosedRing = true
                };
            }
            else
            {
                mesh.ActiveBaseRing = null;
            }

            mesh.Triangles.Clear();
            mesh.Triangles.AddRange(keptTriangles);

            CompactMeshVertices(mesh);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
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
        /// Mở rộng hai đầu của đường cắt theo hướng tổng thể và dựng đa giác khép kín đại diện cho nửa mặt phẳng bên Phải (Right Half-Plane).
        /// Giúp xét dấu khoảng cách (Trái < 0, Phải >= 0) chính xác tuyệt đối không bị lật dấu tại các đỉnh nhọn Zíc-zắc / Bậc thang.
        /// </summary>
        private static void BuildExtendedPolylineAndRightHalfPolygon(
            List<PointF> cutPolyScreen,
            out List<PointF> extendedPoly,
            out List<Vector2> rightHalfPolygon)
        {
            PointF p0 = cutPolyScreen[0];
            PointF pN = cutPolyScreen[cutPolyScreen.Count - 1];

            Vector2 chord = new Vector2(pN.X - p0.X, pN.Y - p0.Y);
            if (chord.LengthSquared() < 1e-3f && cutPolyScreen.Count >= 2)
            {
                chord = new Vector2(cutPolyScreen[1].X - p0.X, cutPolyScreen[1].Y - p0.Y);
            }
            if (chord.LengthSquared() < 1e-5f)
            {
                chord = new Vector2(1f, 0f);
            }
            else
            {
                chord = Vector2.Normalize(chord);
            }

            // Pháp tuyến hướng sang phía bên PHẢI của vector chord trong tọa độ màn hình (X phải, Y xuống): (-chord.Y, chord.X)
            Vector2 rightNormal = new Vector2(-chord.Y, chord.X);
            const float EXTEND_LEN = 3200f;
            const float SIDE_DEPTH = 4800f;

            PointF extStart = new PointF(p0.X - chord.X * EXTEND_LEN, p0.Y - chord.Y * EXTEND_LEN);
            PointF extEnd = new PointF(pN.X + chord.X * EXTEND_LEN, pN.Y + chord.Y * EXTEND_LEN);

            extendedPoly = new List<PointF>(cutPolyScreen.Count + 2);
            extendedPoly.Add(extStart);
            extendedPoly.AddRange(cutPolyScreen);
            extendedPoly.Add(extEnd);

            rightHalfPolygon = new List<Vector2>(extendedPoly.Count + 2);
            for (int i = 0; i < extendedPoly.Count; i++)
            {
                rightHalfPolygon.Add(new Vector2(extendedPoly[i].X, extendedPoly[i].Y));
            }
            rightHalfPolygon.Add(new Vector2(extEnd.X + rightNormal.X * SIDE_DEPTH, extEnd.Y + rightNormal.Y * SIDE_DEPTH));
            rightHalfPolygon.Add(new Vector2(extStart.X + rightNormal.X * SIDE_DEPTH, extStart.Y + rightNormal.Y * SIDE_DEPTH));
        }

        /// <summary>
        /// Chia mịn cục bộ bảo toàn tính liên thông (Manifold Red-Green Midpoint Subdivision) cho các mặt tam giác nằm gần đường cắt.
        /// Giúp các mẫu đường cắt Zíc-zắc, Bậc thang, Lượn sóng, Chữ V, Đường cong có độ phân giải cạnh sắc nét trên mọi khối 3D.
        /// </summary>
        private static void SubdivideMeshAlongCutBand(
            TeddyMesh mesh,
            List<PointF> cutPolyScreen,
            List<PointF> extendedPoly,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            const int MAX_PASSES = 3;
            const float BAND_DIST_PX = 34f;
            const float MAX_EDGE_PX = 8.0f;

            for (int pass = 0; pass < MAX_PASSES; pass++)
            {
                if (mesh.Triangles.Count > 16000) break;

                int vCount = mesh.Vertices.Count;
                PointF[] sp = new PointF[vCount];
                float[] distToCut = new float[vCount];

                for (int i = 0; i < vCount; i++)
                {
                    Vector3 s = camera.ProjectToScreen(mesh.Vertices[i], viewport);
                    sp[i] = new PointF(s.X, s.Y);
                    distToCut[i] = ComputeUnsignedDistanceToPolyline(sp[i], cutPolyScreen);
                }

                var edgeMidpointMap = new Dictionary<(int, int), int>();

                int GetOrSplitEdge(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (edgeMidpointMap.TryGetValue(key, out int midIdx))
                        return midIdx;

                    Vector3 midPos = 0.5f * (mesh.Vertices[a] + mesh.Vertices[b]);
                    midIdx = mesh.Vertices.Count;
                    mesh.Vertices.Add(midPos);
                    edgeMidpointMap[key] = midIdx;
                    return midIdx;
                }

                // Đánh dấu các cạnh cần chia đôi thuộc các tam giác nằm trong dải lân cận đường cắt
                foreach (var tri in mesh.Triangles)
                {
                    int i0 = tri.V0, i1 = tri.V1, i2 = tri.V2;
                    float minD = MathF.Min(distToCut[i0], MathF.Min(distToCut[i1], distToCut[i2]));
                    PointF centroid2D = new PointF(
                        (sp[i0].X + sp[i1].X + sp[i2].X) / 3f,
                        (sp[i0].Y + sp[i1].Y + sp[i2].Y) / 3f);
                    float centroidD = ComputeUnsignedDistanceToPolyline(centroid2D, extendedPoly);

                    if (minD > BAND_DIST_PX && centroidD > BAND_DIST_PX)
                        continue;

                    if (StrokeProcessor.Distance(sp[i0], sp[i1]) > MAX_EDGE_PX)
                        GetOrSplitEdge(i0, i1);
                    if (StrokeProcessor.Distance(sp[i1], sp[i2]) > MAX_EDGE_PX)
                        GetOrSplitEdge(i1, i2);
                    if (StrokeProcessor.Distance(sp[i2], sp[i0]) > MAX_EDGE_PX)
                        GetOrSplitEdge(i2, i0);
                }

                if (edgeMidpointMap.Count == 0)
                    break;

                var newTris = new List<TeddyTriangle>(mesh.Triangles.Count + edgeMidpointMap.Count * 2);

                foreach (var tri in mesh.Triangles)
                {
                    int v0 = tri.V0, v1 = tri.V1, v2 = tri.V2;
                    bool s01 = edgeMidpointMap.TryGetValue(v0 < v1 ? (v0, v1) : (v1, v0), out int m01);
                    bool s12 = edgeMidpointMap.TryGetValue(v1 < v2 ? (v1, v2) : (v2, v1), out int m12);
                    bool s20 = edgeMidpointMap.TryGetValue(v2 < v0 ? (v2, v0) : (v0, v2), out int m20);

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
                        // Xoay đỉnh sao cho cạnh bị chia là (a, b) với trung điểm mAB, đỉnh đối diện là c
                        int a, b, c, mAB;
                        if (s01) { a = v0; b = v1; c = v2; mAB = m01; }
                        else if (s12) { a = v1; b = v2; c = v0; mAB = m12; }
                        else { a = v2; b = v0; c = v1; mAB = m20; }

                        newTris.Add(new TeddyTriangle(a, mAB, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                        newTris.Add(new TeddyTriangle(mAB, b, c, tri.Category, tri.CustomColor, tri.IsCutCap));
                    }
                    else if (splitCount == 2)
                    {
                        // Xoay đỉnh sao cho 2 cạnh bị chia là (a, b) có mAB và (b, c) có mBC
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
            }
        }

        private static float ComputeUnsignedDistanceToPolyline(PointF p, List<PointF> polyline)
        {
            int n = polyline.Count;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < n - 1; i++)
            {
                PointF a = polyline[i];
                PointF b = polyline[i + 1];
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float lenSq = dx * dx + dy * dy;
                float t = lenSq > 1e-5f ? Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f) : 0f;
                float px = a.X + t * dx;
                float py = a.Y + t * dy;
                float dSq = (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py);
                if (dSq < bestDistSq) bestDistSq = dSq;
            }
            return MathF.Sqrt(bestDistSq);
        }

        private static float ComputeSignedDistanceToExtendedPolyline(
            PointF p,
            List<PointF> extendedPoly,
            List<Vector2> rightHalfPolygon)
        {
            float dist = ComputeUnsignedDistanceToPolyline(p, extendedPoly);
            bool inRightHalf = IsPointInPolygon2D(new Vector2(p.X, p.Y), rightHalfPolygon);
            return inRightHalf ? dist : -dist;
        }

        /// <summary>
        /// Nối toàn bộ các đoạn biên tại vết cắt thành danh sách các vòng thiết diện khép kín (Section Loops).
        /// </summary>
        private static List<List<int>> AssembleAllOrderedLoops(List<Vector3> vertices, List<(int A, int B)> segments)
        {
            var loops = new List<List<int>>();
            if (segments.Count == 0) return loops;

            var used = new bool[segments.Count];

            for (int startIdx = 0; startIdx < segments.Count; startIdx++)
            {
                if (used[startIdx]) continue;

                var loop = new List<int>();
                used[startIdx] = true;
                loop.Add(segments[startIdx].A);
                int curr = segments[startIdx].B;

                for (int step = 0; step < segments.Count; step++)
                {
                    if (curr == loop[0]) break;
                    loop.Add(curr);

                    int nextSeg = -1;
                    float bestDist = float.MaxValue;

                    for (int i = 0; i < segments.Count; i++)
                    {
                        if (used[i]) continue;
                        if (segments[i].A == curr)
                        {
                            nextSeg = i;
                            bestDist = 0f;
                            break;
                        }
                        float d = Vector3.DistanceSquared(vertices[curr], vertices[segments[i].A]);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            nextSeg = i;
                        }
                    }

                    if (nextSeg < 0 || bestDist > 250f) break;
                    used[nextSeg] = true;
                    curr = segments[nextSeg].B;
                }

                if (loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        /// <summary>
        /// Đóng nắp kín hoàn toàn vòng thiết diện mặt cắt (sectionLoop) bằng Lưới Dải Mặt Kẻ Trục Trung Tâm (Ruled Spine Strip Triangulation):
        /// - Tạo trục xương sống 3D (Ruled Center Spine) chạy dọc theo đúng biên dạng đường cắt 2D (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Tự do)
        ///   tại độ sâu trung bình của mặt cắt.
        /// - Khâu toàn bộ các đỉnh biên của sectionLoop vào trục xương sống mặt cắt, đảm bảo lấp kín 100% phần diện tích mặt cắt không bao giờ bị hở trắng.
        /// </summary>
        private static void CapSectionLoopInRuledParameterSpace(
            TeddyMesh mesh,
            List<int> sectionLoop,
            List<PointF> extendedPoly,
            VirtualTrackballCamera camera,
            Size viewport,
            List<TeddyTriangle> keptTriangles)
        {
            int n = sectionLoop.Count;
            if (n < 3) return;

            // Tính bảng độ dài cung tích lũy của extendedPoly
            int polyCount = extendedPoly.Count;
            float[] cumArc = new float[polyCount];
            cumArc[0] = 0f;
            for (int i = 1; i < polyCount; i++)
            {
                cumArc[i] = cumArc[i - 1] + StrokeProcessor.Distance(extendedPoly[i - 1], extendedPoly[i]);
            }

            float[] loopArc = new float[n];
            float[] loopViewZ = new float[n];
            float minS = float.MaxValue;
            float maxS = float.MinValue;
            float sumZAll = 0f;

            for (int i = 0; i < n; i++)
            {
                Vector3 sp = camera.ProjectToScreen(mesh.Vertices[sectionLoop[i]], viewport);
                float s = ProjectPointToPolylineArcLength(new PointF(sp.X, sp.Y), extendedPoly, cumArc);
                loopArc[i] = s;
                loopViewZ[i] = sp.Z;
                if (s < minS) minS = s;
                if (s > maxS) maxS = s;
                sumZAll += sp.Z;
            }
            float avgZAll = sumZAll / n;

            // Tính hướng pháp tuyến hướng từ tâm phần thân còn lại ra phía mặt cắt để đồng bộ chiều tam giác nắp
            Vector3 meshCentroid = ComputeMeshCentroid(mesh);
            Vector3 loopCentroid = Vector3.Zero;
            for (int i = 0; i < n; i++) loopCentroid += mesh.Vertices[sectionLoop[i]];
            loopCentroid /= n;

            Vector3 outwardDir = loopCentroid - meshCentroid;
            if (outwardDir.LengthSquared() < 1e-4f)
            {
                outwardDir = camera.GetCameraForwardInWorld();
            }
            else
            {
                outwardDir = Vector3.Normalize(outwardDir);
            }

            void AddOrientedCapTri(int v0, int v1, int v2)
            {
                if (v0 == v1 || v1 == v2 || v2 == v0) return;
                Vector3 p0 = mesh.Vertices[v0];
                Vector3 p1 = mesh.Vertices[v1];
                Vector3 p2 = mesh.Vertices[v2];
                Vector3 nrm = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(nrm, outwardDir) < 0f)
                {
                    keptTriangles.Add(new TeddyTriangle(v0, v2, v1, TriangleCategory.Junction, customColor: null, isCutCap: true));
                }
                else
                {
                    keptTriangles.Add(new TeddyTriangle(v0, v1, v2, TriangleCategory.Junction, customColor: null, isCutCap: true));
                }
            }

            float spanS = maxS - minS;
            if (spanS < 6f)
            {
                int capCenterIdx = mesh.Vertices.Count;
                mesh.Vertices.Add(loopCentroid);
                for (int i = 0; i < n; i++)
                {
                    int vCurr = sectionLoop[i];
                    int vNext = sectionLoop[(i + 1) % n];
                    AddOrientedCapTri(vNext, vCurr, capCenterIdx);
                }
                return;
            }

            // Tạo các trạm trục giữa (Spine Stations) dọc theo đoạn [minS, maxS] của đường cắt
            int stationCount = Math.Clamp((int)(spanS / 4.0f) + 1, 8, 96);
            float[] stationArc = new float[stationCount];
            int[] stationVertIdx = new int[stationCount];

            for (int k = 0; k < stationCount; k++)
            {
                float t = stationCount > 1 ? k / (float)(stationCount - 1) : 0.5f;
                float sVal = minS + t * spanS;
                stationArc[k] = sVal;

                // Nội suy tọa độ màn hình (X, Y) nằm chính xác trên đường cắt extendedPoly tại độ dài cung sVal
                PointF screenPt = PointAtPolylineArcLength(extendedPoly, cumArc, sVal);

                // Tính độ sâu Z trung bình của mặt cắt tại vị trí sVal
                float weightedZ = 0f;
                float weightSum = 0f;
                for (int i = 0; i < n; i++)
                {
                    float ds = MathF.Abs(loopArc[i] - sVal);
                    float w = 1f / (1f + ds * ds * 0.08f);
                    weightedZ += loopViewZ[i] * w;
                    weightSum += w;
                }
                float zMid = weightSum > 1e-5f ? (weightedZ / weightSum) : avgZAll;

                Vector3 worldSpinePt = camera.ScreenToWorldPoint(screenPt, viewport, zMid);
                stationVertIdx[k] = mesh.Vertices.Count;
                mesh.Vertices.Add(worldSpinePt);
            }

            // Ánh xạ mỗi đỉnh trên sectionLoop về trạm Spine gần nhất theo độ dài cung s
            int[] nearestStation = new int[n];
            for (int i = 0; i < n; i++)
            {
                float ratio = (loopArc[i] - minS) / spanS;
                nearestStation[i] = Math.Clamp((int)MathF.Round(ratio * (stationCount - 1)), 0, stationCount - 1);
            }

            // Khâu kín toàn bộ biên sectionLoop vào chuỗi trạm Spine
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int vCurr = sectionLoop[i];
                int vNext = sectionLoop[next];
                int kA = nearestStation[i];
                int kB = nearestStation[next];

                AddOrientedCapTri(vNext, vCurr, stationVertIdx[kA]);

                if (kA != kB)
                {
                    int step = kB > kA ? 1 : -1;
                    for (int k = kA; k != kB; k += step)
                    {
                        AddOrientedCapTri(vNext, stationVertIdx[k], stationVertIdx[k + step]);
                    }
                }
            }
        }

        private static PointF PointAtPolylineArcLength(List<PointF> poly, float[] cumArc, float targetArc)
        {
            int n = poly.Count;
            if (n == 0) return PointF.Empty;
            if (targetArc <= 0f || n == 1) return poly[0];
            if (targetArc >= cumArc[n - 1]) return poly[n - 1];

            for (int i = 0; i < n - 1; i++)
            {
                if (cumArc[i + 1] >= targetArc)
                {
                    float segLen = cumArc[i + 1] - cumArc[i];
                    float t = segLen > 1e-5f ? (targetArc - cumArc[i]) / segLen : 0f;
                    return new PointF(
                        poly[i].X + (poly[i + 1].X - poly[i].X) * t,
                        poly[i].Y + (poly[i + 1].Y - poly[i].Y) * t);
                }
            }
            return poly[n - 1];
        }

        private static float ProjectPointToPolylineArcLength(PointF p, List<PointF> poly, float[] cumArc)
        {
            int n = poly.Count;
            float bestDistSq = float.MaxValue;
            float bestArc = 0f;

            for (int i = 0; i < n - 1; i++)
            {
                PointF a = poly[i];
                PointF b = poly[i + 1];
                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float lenSq = dx * dx + dy * dy;
                float segLen = cumArc[i + 1] - cumArc[i];
                float t = lenSq > 1e-5f ? Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f) : 0f;
                float px = a.X + t * dx;
                float py = a.Y + t * dy;
                float dSq = (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestArc = cumArc[i] + t * segLen;
                }
            }
            return bestArc;
        }

        private static void ClipSurfaceLinesAfterCut(
            TeddyMesh mesh,
            List<PointF> extendedPoly,
            List<Vector2> rightHalfPolygon,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            if (mesh.SurfaceLines.Count == 0) return;

            var clippedLines = new List<TeddySurfaceLine>();
            foreach (var line in mesh.SurfaceLines)
            {
                var currentSeg = new List<Vector3>();
                foreach (var pt3D in line.Points)
                {
                    Vector3 sp = camera.ProjectToScreen(pt3D, viewport);
                    bool kept = IsPointInPolygon2D(new Vector2(sp.X, sp.Y), rightHalfPolygon);
                    if (kept)
                    {
                        currentSeg.Add(pt3D);
                    }
                    else if (currentSeg.Count >= 2)
                    {
                        clippedLines.Add(new TeddySurfaceLine
                        {
                            Points = new List<Vector3>(currentSeg),
                            StrokeColor = line.StrokeColor,
                            StrokeWidth = line.StrokeWidth,
                            IsClosedRing = false
                        });
                        currentSeg.Clear();
                    }
                    else
                    {
                        currentSeg.Clear();
                    }
                }

                if (currentSeg.Count >= 2)
                {
                    clippedLines.Add(new TeddySurfaceLine
                    {
                        Points = currentSeg,
                        StrokeColor = line.StrokeColor,
                        StrokeWidth = line.StrokeWidth,
                        IsClosedRing = line.IsClosedRing && currentSeg.Count == line.Points.Count
                    });
                }
            }

            mesh.SurfaceLines.Clear();
            mesh.SurfaceLines.AddRange(clippedLines);
        }

        private static Vector3 ComputeMeshCentroid(TeddyMesh mesh)
        {
            if (mesh == null || mesh.Vertices.Count == 0) return Vector3.Zero;
            Vector3 sum = Vector3.Zero;
            foreach (var v in mesh.Vertices) sum += v;
            return sum / mesh.Vertices.Count;
        }

        private static void TranslateMesh(TeddyMesh mesh, Vector3 delta)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
                mesh.Vertices[i] += delta;
            for (int i = 0; i < mesh.SpineVertices.Count; i++)
                mesh.SpineVertices[i] += delta;
            foreach (var sl in mesh.SurfaceLines)
            {
                for (int i = 0; i < sl.Points.Count; i++)
                    sl.Points[i] += delta;
            }
            foreach (var cr in mesh.CutSectionRings)
            {
                for (int i = 0; i < cr.Points.Count; i++)
                    cr.Points[i] += delta;
            }
            if (mesh.ActiveBaseRing != null)
            {
                for (int i = 0; i < mesh.ActiveBaseRing.Points.Count; i++)
                    mesh.ActiveBaseRing.Points[i] += delta;
            }
        }

        private static void CopyMeshData(TeddyMesh source, TeddyMesh target)
        {
            target.Clear();
            target.BaseColor = source.BaseColor;
            target.IsCutOffPiece = source.IsCutOffPiece;
            target.Vertices.AddRange(source.Vertices);
            target.VertexNormals.AddRange(source.VertexNormals);
            foreach (var tri in source.Triangles)
                target.Triangles.Add(tri.Clone());
            target.Edges.AddRange(source.Edges);
            target.SpineVertices.AddRange(source.SpineVertices);
            target.SpineEdges.AddRange(source.SpineEdges);
            foreach (var sl in source.SurfaceLines)
                target.SurfaceLines.Add(sl.Clone());
            foreach (var cr in source.CutSectionRings)
                target.CutSectionRings.Add(cr.Clone());
            target.ActiveBaseRing = source.ActiveBaseRing?.Clone();
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
