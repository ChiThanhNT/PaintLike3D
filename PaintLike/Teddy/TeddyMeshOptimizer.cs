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
        /// [Mục 5.1 cuối trang 6] Loại bỏ các cạnh quá ngắn (Edge Collapse) và tam giác suy biến diện tích nhỏ,
        /// kết hợp cân bằng chất lượng lưới tam giác.
        /// </summary>
        public static int RefineMesh(TeddyMesh mesh, float minEdgeLength = 2.8f)
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
                float adaptiveMin = avgLen * 0.22f;
                minLenSq = MathF.Max(minLenSq, adaptiveMin * adaptiveMin);
            }

            // Gộp các cạnh quá ngắn về trung điểm của chúng
            int[] parent = new int[mesh.Vertices.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int FindRoot(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var tri in mesh.Triangles)
            {
                int[] v = new int[] { FindRoot(tri.V0), FindRoot(tri.V1), FindRoot(tri.V2) };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    if (a != b && Vector3.DistanceSquared(mesh.Vertices[a], mesh.Vertices[b]) < minLenSq)
                    {
                        Vector3 mid = 0.5f * (mesh.Vertices[a] + mesh.Vertices[b]);
                        int keep = Math.Min(a, b);
                        int drop = Math.Max(a, b);
                        mesh.Vertices[keep] = mid;
                        parent[drop] = keep;
                        collapsedCount++;
                    }
                }
            }

            // Cập nhật lại chỉ số đỉnh của các tam giác và loại bỏ tam giác suy biến
            var validTriangles = new List<TeddyTriangle>(mesh.Triangles.Count);
            foreach (var tri in mesh.Triangles)
            {
                int i0 = FindRoot(tri.V0);
                int i1 = FindRoot(tri.V1);
                int i2 = FindRoot(tri.V2);

                if (i0 == i1 || i1 == i2 || i2 == i0)
                    continue;

                Vector3 p0 = mesh.Vertices[i0];
                Vector3 p1 = mesh.Vertices[i1];
                Vector3 p2 = mesh.Vertices[i2];
                float doubleArea = Vector3.Cross(p1 - p0, p2 - p0).Length();
                if (doubleArea <= 1e-4f)
                    continue;

                tri.V0 = i0;
                tri.V1 = i1;
                tri.V2 = i2;
                validTriangles.Add(tri);
            }

            mesh.Triangles.Clear();
            mesh.Triangles.AddRange(validTriangles);

            TeddyExtrudeCutOp.CompactMeshVertices(mesh);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
            return collapsedCount;
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
                case "Cherry":
                    return CreateCherrySampleStroke(cx, cy, scale);

                case "Snake":
                    return CreateSnakeSampleStroke(cx, cy, scale);

                case "TeddySilhouette":
                default:
                    return CreateTeddySampleStroke(cx, cy, scale);
            }
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
