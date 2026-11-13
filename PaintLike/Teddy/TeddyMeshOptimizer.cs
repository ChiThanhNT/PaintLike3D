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
        /// để minh họa thuật toán Inflation ("making wide areas fat, and narrow areas thin").
        /// </summary>
        public static List<PointF> CreatePaperSampleStroke(string presetKey, Size viewport)
        {
            float cx = viewport.Width * 0.5f;
            float cy = viewport.Height * 0.5f;
            float scale = MathF.Min(viewport.Width, viewport.Height) * 0.34f;
            var pts = new List<PointF>();

            switch (presetKey)
            {
                case "Cherry": // Hình 5c: Quả Cherry có cuống mảnh và thân quả tròn phồng
                {
                    // Phần quả tròn bên dưới
                    int fruitSteps = 28;
                    float fruitCx = cx - scale * 0.12f;
                    float fruitCy = cy + scale * 0.35f;
                    float fruitR = scale * 0.44f;

                    for (int i = 0; i <= fruitSteps; i++)
                    {
                        float t = i / (float)fruitSteps;
                        float ang = -MathF.PI * 0.32f + t * (MathF.PI * 1.72f);
                        float rx = fruitR * (1.0f + 0.08f * MathF.Cos(ang * 2f));
                        float ry = fruitR * 0.92f;
                        pts.Add(new PointF(fruitCx + rx * MathF.Cos(ang), fruitCy + ry * MathF.Sin(ang)));
                    }

                    // Cuống mảnh vươn lên phải rồi uốn cong ở đỉnh (bề ngang hẹp -> tự động phồng mỏng!)
                    pts.Add(new PointF(cx - scale * 0.05f, cy - scale * 0.08f));
                    pts.Add(new PointF(cx + scale * 0.14f, cy - scale * 0.45f));
                    pts.Add(new PointF(cx + scale * 0.36f, cy - scale * 0.76f));
                    pts.Add(new PointF(cx + scale * 0.46f, cy - scale * 0.72f));
                    pts.Add(new PointF(cx + scale * 0.24f, cy - scale * 0.40f));
                    pts.Add(new PointF(cx + scale * 0.06f, cy - scale * 0.04f));
                    break;
                }

                case "Snake": // Hình 5a: Con rắn uốn chữ C/S đầu to đuôi nhọn
                {
                    int N = 36;
                    var spine = new Vector2[N];
                    var halfW = new float[N];
                    for (int i = 0; i < N; i++)
                    {
                        float t = i / (float)(N - 1);
                        float ang = -MathF.PI * 0.75f + t * MathF.PI * 1.65f;
                        float r = scale * (0.68f - 0.18f * t);
                        spine[i] = new Vector2(cx + r * MathF.Cos(ang), cy + r * 0.78f * MathF.Sin(ang));
                        // Đầu rắn phình nhẹ, thân đều, đuôi thu nhỏ dần
                        float headBulge = MathF.Exp(-MathF.Pow((t - 0.12f) / 0.12f, 2f)) * 0.08f;
                        halfW[i] = scale * (0.17f * (1f - 0.75f * t * t) + headBulge);
                    }

                    BuildRibbonStrokeFromSpine(spine, halfW, pts);
                    break;
                }

                case "TeddySilhouette": // Hình 1 & Hình 2: Đường viền Gấu Teddy nguyên khối (Đầu + 2 Tai + Thân + Tay Chân)
                default:
                {
                    int samples = 96;
                    for (int i = 0; i < samples; i++)
                    {
                        float theta = 2f * MathF.PI * i / samples;
                        // Hàm cực tổng hợp tạo hình dáng gấu bông tròn trịa có tai, tay, chân
                        float baseR = 0.52f
                            + 0.12f * MathF.Sin(theta)
                            + 0.16f * MathF.Exp(-MathF.Pow(WrapAngle(theta - 1.05f) / 0.26f, 2f))  // Tai phải
                            + 0.16f * MathF.Exp(-MathF.Pow(WrapAngle(theta - 2.09f) / 0.26f, 2f))  // Tai trái
                            + 0.20f * MathF.Exp(-MathF.Pow(WrapAngle(theta - 0.15f) / 0.34f, 2f))  // Tay phải
                            + 0.20f * MathF.Exp(-MathF.Pow(WrapAngle(theta - 2.99f) / 0.34f, 2f))  // Tay trái
                            + 0.24f * MathF.Exp(-MathF.Pow(WrapAngle(theta + 0.95f) / 0.36f, 2f))  // Chân phải
                            + 0.24f * MathF.Exp(-MathF.Pow(WrapAngle(theta + 2.19f) / 0.36f, 2f)); // Chân trái

                        float x = cx + scale * baseR * MathF.Cos(theta);
                        float y = cy - scale * baseR * MathF.Sin(theta);
                        pts.Add(new PointF(x, y));
                    }
                    break;
                }
            }

            return pts;
        }

        private static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= 2f * MathF.PI;
            while (a < -MathF.PI) a += 2f * MathF.PI;
            return a;
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
        /// [Hình 2 trang 1 bài báo] Tự động tô màu đa vùng (Multi-region Face Painting) và nét mặt
        /// cho các mẫu bài báo (Gấu Teddy, Quả Cherry, Con Rắn) để mô hình sống động như Hình 2.
        /// </summary>
        public static void ApplyPaperPresetPainting(TeddyMesh mesh, string presetKey)
        {
            if (mesh == null || mesh.IsEmpty) return;

            switch (presetKey)
            {
                case "Cherry":
                {
                    // Phần quả ở dưới (Y < -10) màu Đỏ tươi, phần cuống ở trên (Y >= -10) màu Xanh lá cây
                    Color berryRed = Color.FromArgb(225, 29, 72);
                    Color stemGreen = Color.FromArgb(34, 197, 94);
                    mesh.BaseColor = berryRed;

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        tri.CustomColor = c.Y > -5f ? stemGreen : berryRed;
                    }
                    break;
                }

                case "Snake":
                {
                    // Thân rắn xanh lục, bụng vàng cốm, đốm lưng xanh đậm
                    Color bodyGreen = Color.FromArgb(34, 197, 94);
                    Color bellyYellow = Color.FromArgb(250, 204, 21);
                    Color stripeDark = Color.FromArgb(21, 128, 61);
                    mesh.BaseColor = bodyGreen;

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        if (c.Z < -12f)
                            tri.CustomColor = bellyYellow;
                        else if (((int)MathF.Floor((c.X + c.Y) / 28f)) % 2 == 0 && c.Z > 15f)
                            tri.CustomColor = stripeDark;
                        else
                            tri.CustomColor = bodyGreen;
                    }
                    break;
                }

                case "TeddySilhouette":
                {
                    // Gấu Teddy Hình 2: Thân nâu cam ấm, bụng & mõm màu kem sáng, đầu tai & bàn chân nâu sẫm
                    Color furWarm = Color.FromArgb(234, 138, 68);
                    Color creamBelly = Color.FromArgb(254, 243, 199);
                    Color darkPawEar = Color.FromArgb(154, 52, 18);
                    mesh.BaseColor = furWarm;

                    foreach (var tri in mesh.Triangles)
                    {
                        Vector3 c = (mesh.Vertices[tri.V0] + mesh.Vertices[tri.V1] + mesh.Vertices[tri.V2]) / 3f;
                        float rXY = MathF.Sqrt(c.X * c.X + c.Y * c.Y);

                        if (c.Z > 18f && MathF.Sqrt(c.X * c.X + (c.Y + 12f) * (c.Y + 12f)) < 58f)
                        {
                            tri.CustomColor = creamBelly; // Bụng kem tròn phía trước
                        }
                        else if (c.Z > 12f && MathF.Sqrt(c.X * c.X + (c.Y - 48f) * (c.Y - 48f)) < 26f)
                        {
                            tri.CustomColor = creamBelly; // Mõm kem
                        }
                        else if (rXY > 128f && c.Y > 75f)
                        {
                            tri.CustomColor = darkPawEar; // Hai chóp tai nâu đậm
                        }
                        else
                        {
                            tri.CustomColor = furWarm;
                        }
                    }
                    break;
                }
            }
        }
    }
}
