using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;

namespace PaintLike.Teddy
{
    public enum TeddyRenderStyle
    {
        PenAndInk,      // Phong cách phác họa nét mực + viền đen Silhouette (Hình 1 bài báo SIGGRAPH '99)
        SmoothShaded,   // Tô bóng khối 3D Studio mượt (Phong/Lambert + Rim Light)
        WireframeSpine  // Khung dây tam giác + Hiển thị trục xương sống Chordal Axis (Spine)
    }

    /// <summary>
    /// [TV3-03] Bộ kết xuất đồ họa 3D phần mềm tốc độ cao (Mục 1 & 6 bài báo Teddy).
    /// Hỗ trợ Z-sorting (Painter's Algorithm), loại bỏ mặt khuất (Back-face culling),
    /// tự động phát hiện đường viền Silhouette cho chế độ Pen-and-Ink NPR và kết xuất ảnh PNG trong suốt.
    /// </summary>
    public static class TeddyRenderer
    {
        private struct RenderTri
        {
            public int TriIndex;
            public PointF P0;
            public PointF P1;
            public PointF P2;
            public float AvgZ;
            public Vector3 ViewNormal;
        }

        public static void RenderScene(
            Graphics g,
            Size viewport,
            TeddyMesh? mesh,
            VirtualTrackballCamera camera,
            TeddyRenderStyle style,
            IReadOnlyList<PointF>? active2DStroke = null,
            Color? activeStrokeColor = null,
            bool drawBackground = true)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (drawBackground)
            {
                DrawStudioViewportBackground(g, viewport, style);
            }

            if (mesh != null && !mesh.IsEmpty)
            {
                int vertCount = mesh.Vertices.Count;
                Vector3[] screenVerts = new Vector3[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    screenVerts[i] = camera.ProjectToScreen(mesh.Vertices[i], viewport);
                }

                int triCount = mesh.Triangles.Count;
                bool[] isFrontFacing = new bool[triCount];
                var visibleTris = new List<RenderTri>(triCount);

                for (int i = 0; i < triCount; i++)
                {
                    var tri = mesh.Triangles[i];
                    Vector3 s0 = screenVerts[tri.V0];
                    Vector3 s1 = screenVerts[tri.V1];
                    Vector3 s2 = screenVerts[tri.V2];

                    // Pháp tuyến trong không gian View
                    Vector3 vNorm = camera.NormalToView(tri.FaceNormal);

                    // Kiểm tra hướng mặt trên màn hình (Cross product 2D) kết hợp ViewNormal.Z
                    float screenCross = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
                    bool front = vNorm.Z >= -0.05f || screenCross < 0f;
                    isFrontFacing[i] = vNorm.Z >= 0f;

                    if (style != TeddyRenderStyle.WireframeSpine && !front)
                        continue;

                    float avgZ = (s0.Z + s1.Z + s2.Z) / 3f;
                    visibleTris.Add(new RenderTri
                    {
                        TriIndex = i,
                        P0 = new PointF(s0.X, s0.Y),
                        P1 = new PointF(s1.X, s1.Y),
                        P2 = new PointF(s2.X, s2.Y),
                        AvgZ = avgZ,
                        ViewNormal = vNorm
                    });
                }

                // Sắp xếp từ xa đến gần (Painter's Algorithm theo Z_view)
                visibleTris.Sort((a, b) => a.AvgZ.CompareTo(b.AvgZ));

                // Nguồn sáng chính (Key Light) và nguồn sáng phụ (Rim/Fill Light)
                Vector3 keyLight = Vector3.Normalize(new Vector3(-0.38f, 0.52f, 0.76f));
                Vector3 fillLight = Vector3.Normalize(new Vector3(0.55f, -0.30f, 0.45f));

                PointF[] polyPts = new PointF[3];

                if (style == TeddyRenderStyle.WireframeSpine)
                {
                    DrawWireframeAndSpine(g, mesh, camera, viewport, visibleTris, polyPts);
                }
                else
                {
                    // Vẽ các mặt tam giác đã tô bóng
                    foreach (var rt in visibleTris)
                    {
                        polyPts[0] = rt.P0;
                        polyPts[1] = rt.P1;
                        polyPts[2] = rt.P2;

                        Color triBaseColor = mesh.Triangles[rt.TriIndex].CustomColor ?? mesh.BaseColor;
                        Color shadedColor = ComputeShadedColor(
                            triBaseColor,
                            rt.ViewNormal,
                            keyLight,
                            fillLight,
                            style == TeddyRenderStyle.PenAndInk);

                        using (SolidBrush brush = new SolidBrush(shadedColor))
                        using (Pen seamPen = new Pen(shadedColor, 1.0f))
                        {
                            g.FillPolygon(brush, polyPts);
                            // Vẽ viền mảnh cùng màu để khử khe hở anti-alias giữa 2 tam giác kề nhau
                            g.DrawPolygon(seamPen, polyPts);
                        }
                    }

                    // Nếu là chế độ Pen-and-Ink (Hình 1 bài báo): Phát hiện và vẽ các cạnh Silhouette đậm nét mực
                    if (style == TeddyRenderStyle.PenAndInk)
                    {
                        DrawPenAndInkSilhouettes(g, mesh, screenVerts, isFrontFacing);
                    }
                }

                // Vẽ các nét trang trí trên bề mặt 3D (Surface Lines - Mục 4.3 & 5.2)
                DrawSurfaceLines(g, mesh, camera, viewport);
            }

            // Vẽ nét bút 2D người dùng đang kéo trên màn hình
            if (active2DStroke != null && active2DStroke.Count > 1)
            {
                Color c = activeStrokeColor ?? Color.FromArgb(99, 102, 241);
                using (Pen strokePen = new Pen(c, 3.0f))
                {
                    strokePen.StartCap = LineCap.Round;
                    strokePen.EndCap = LineCap.Round;
                    strokePen.LineJoin = LineJoin.Round;

                    PointF[] arr = new PointF[active2DStroke.Count];
                    for (int i = 0; i < active2DStroke.Count; i++) arr[i] = active2DStroke[i];
                    g.DrawLines(strokePen, arr);
                }

                // Điểm đánh dấu bắt đầu nét vẽ
                PointF startPt = active2DStroke[0];
                using (SolidBrush sb = new SolidBrush(c))
                {
                    g.FillEllipse(sb, startPt.X - 4f, startPt.Y - 4f, 8f, 8f);
                }
            }
        }

        /// <summary>
        /// Kết xuất mô hình 3D hiện tại thành ảnh Bitmap có nền trong suốt (RGBA) để đưa thẳng vào một Layer 2D của PaintLike.
        /// </summary>
        public static Bitmap RenderTransparentSnapshot(
            int width,
            int height,
            TeddyMesh mesh,
            VirtualTrackballCamera camera,
            TeddyRenderStyle style)
        {
            return RenderTransparentSnapshot(width, height, new[] { mesh }, camera, style);
        }

        public static Bitmap RenderTransparentSnapshot(
            int width,
            int height,
            IReadOnlyList<TeddyMesh> meshes,
            VirtualTrackballCamera camera,
            TeddyRenderStyle style)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, width), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (meshes != null)
                {
                    var sorted = meshes.Where(m => m != null && !m.IsEmpty).OrderBy(m =>
                    {
                        float sumZ = 0f;
                        foreach (var v in m.Vertices) sumZ += camera.WorldToView(v).Z;
                        return m.Vertices.Count > 0 ? sumZ / m.Vertices.Count : 0f;
                    }).ToList();

                    foreach (var mesh in sorted)
                    {
                        RenderScene(g, new Size(width, height), mesh, camera, style, null, null, drawBackground: false);
                    }
                }
            }
            return bmp;
        }

        private static Color ComputeShadedColor(
            Color baseColor,
            Vector3 normal,
            Vector3 keyLight,
            Vector3 fillLight,
            bool toonQuantize)
        {
            float ndotl = MathF.Max(0f, Vector3.Dot(normal, keyLight));
            float fill = MathF.Max(0f, Vector3.Dot(normal, fillLight)) * 0.25f;
            float rim = MathF.Pow(1.0f - Math.Clamp(MathF.Abs(normal.Z), 0f, 1f), 2.4f) * 0.18f;

            if (toonQuantize)
            {
                // Phân bậc sáng Cel/Toon cho chế độ Pen-and-Ink (Hình 1 bài báo)
                float level;
                if (ndotl > 0.72f) level = 1.05f;
                else if (ndotl > 0.36f) level = 0.88f;
                else if (ndotl > 0.12f) level = 0.72f;
                else level = 0.56f;

                int r = Math.Clamp((int)(baseColor.R * level), 0, 255);
                int g = Math.Clamp((int)(baseColor.G * level), 0, 255);
                int b = Math.Clamp((int)(baseColor.B * level), 0, 255);
                return Color.FromArgb(r, g, b);
            }
            else
            {
                // Specular Blinn-Phong cho chế độ Smooth Shaded
                Vector3 halfVec = Vector3.Normalize(keyLight + Vector3.UnitZ);
                float spec = MathF.Pow(MathF.Max(0f, Vector3.Dot(normal, halfVec)), 28f) * 0.28f;
                float intensity = 0.28f + ndotl * 0.68f + fill + rim;

                int r = Math.Clamp((int)(baseColor.R * intensity + 255f * spec), 0, 255);
                int g = Math.Clamp((int)(baseColor.G * intensity + 255f * spec), 0, 255);
                int b = Math.Clamp((int)(baseColor.B * intensity + 255f * spec), 0, 255);
                return Color.FromArgb(r, g, b);
            }
        }

        /// <summary>
        /// [Mục 1 - Real-time Pen-and-Ink Rendering] Phát hiện các cạnh biên nơi mặt trước giao với mặt sau
        /// và vẽ đường nét mực đậm tạo hiệu ứng phác họa 3D trực quan.
        /// </summary>
        private static void DrawPenAndInkSilhouettes(
            Graphics g,
            TeddyMesh mesh,
            Vector3[] screenVerts,
            bool[] isFrontFacing)
        {
            using (Pen silhouettePen = new Pen(Color.FromArgb(30, 58, 95), 2.8f))
            using (Pen creasePen = new Pen(Color.FromArgb(95, 51, 65, 85), 1.1f))
            {
                silhouettePen.StartCap = LineCap.Round;
                silhouettePen.EndCap = LineCap.Round;

                foreach (var edge in mesh.Edges)
                {
                    bool leftFront = edge.TriLeft >= 0 && isFrontFacing[edge.TriLeft];
                    bool rightFront = edge.TriRight >= 0 && isFrontFacing[edge.TriRight];

                    // Cạnh Silhouette: 1 mặt hướng về Camera, mặt kia quay lưng lại (hoặc cạnh biên hở)
                    if (leftFront != rightFront || (edge.TriRight == -1 && leftFront))
                    {
                        Vector3 pA = screenVerts[edge.A];
                        Vector3 pB = screenVerts[edge.B];
                        g.DrawLine(silhouettePen, pA.X, pA.Y, pB.X, pB.Y);
                    }
                    else if (leftFront && rightFront && edge.TriLeft >= 0 && edge.TriRight >= 0)
                    {
                        // Cạnh gờ sắc nét (Crease edge) khi góc giữa 2 mặt lớn
                        float dot = Vector3.Dot(mesh.Triangles[edge.TriLeft].FaceNormal, mesh.Triangles[edge.TriRight].FaceNormal);
                        if (dot < 0.45f)
                        {
                            Vector3 pA = screenVerts[edge.A];
                            Vector3 pB = screenVerts[edge.B];
                            g.DrawLine(creasePen, pA.X, pA.Y, pB.X, pB.Y);
                        }
                    }
                }
            }
        }

        private static void DrawWireframeAndSpine(
            Graphics g,
            TeddyMesh mesh,
            VirtualTrackballCamera camera,
            Size viewport,
            List<RenderTri> tris,
            PointF[] polyPts)
        {
            using (SolidBrush faceBrush = new SolidBrush(Color.FromArgb(38, 79, 70, 229)))
            using (Pen wirePen = new Pen(Color.FromArgb(145, 37, 99, 235), 1.0f))
            {
                foreach (var rt in tris)
                {
                    if (rt.ViewNormal.Z < 0f) continue;
                    polyPts[0] = rt.P0;
                    polyPts[1] = rt.P1;
                    polyPts[2] = rt.P2;
                    g.FillPolygon(faceBrush, polyPts);
                    g.DrawPolygon(wirePen, polyPts);
                }
            }

            // Vẽ trục xương sống Chordal Axis (Spine - Hình 13e) nổi bật bằng màu Vàng Cam / Hồng Đỏ
            if (mesh.SpineVertices.Count > 0)
            {
                using (Pen spinePen = new Pen(Color.FromArgb(245, 158, 11), 3.0f))
                using (SolidBrush nodeBrush = new SolidBrush(Color.FromArgb(239, 68, 68)))
                {
                    foreach (var se in mesh.SpineEdges)
                    {
                        if (se.A >= 0 && se.A < mesh.SpineVertices.Count &&
                            se.B >= 0 && se.B < mesh.SpineVertices.Count)
                        {
                            Vector3 pA = camera.ProjectToScreen(mesh.SpineVertices[se.A], viewport);
                            Vector3 pB = camera.ProjectToScreen(mesh.SpineVertices[se.B], viewport);
                            g.DrawLine(spinePen, pA.X, pA.Y, pB.X, pB.Y);
                        }
                    }

                    foreach (var sv in mesh.SpineVertices)
                    {
                        Vector3 p = camera.ProjectToScreen(sv, viewport);
                        g.FillEllipse(nodeBrush, p.X - 3.5f, p.Y - 3.5f, 7f, 7f);
                    }
                }
            }
        }

        private static void DrawSurfaceLines(
            Graphics g,
            TeddyMesh mesh,
            VirtualTrackballCamera camera,
            Size viewport)
        {
            foreach (var sl in mesh.SurfaceLines)
            {
                DrawSingle3DPolyline(g, sl, camera, viewport, false);
            }

            // Vẽ vòng chọn màu đỏ khi đang ở Extrusion Mode (Mục 4.4 - Hình 3i)
            if (mesh.ActiveBaseRing != null)
            {
                DrawSingle3DPolyline(g, mesh.ActiveBaseRing, camera, viewport, true);
            }
        }

        private static void DrawSingle3DPolyline(
            Graphics g,
            TeddySurfaceLine line,
            VirtualTrackballCamera camera,
            Size viewport,
            bool isHighlightRing)
        {
            if (line.Points.Count < 2) return;

            Color c = isHighlightRing ? Color.FromArgb(239, 68, 68) : line.StrokeColor;
            float w = isHighlightRing ? 3.6f : line.StrokeWidth;

            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;

                var screenPts = new List<PointF>(line.Points.Count + 1);
                foreach (var pt3D in line.Points)
                {
                    Vector3 sp = camera.ProjectToScreen(pt3D, viewport);
                    screenPts.Add(new PointF(sp.X, sp.Y));
                }

                if (line.IsClosedRing && screenPts.Count > 2)
                {
                    screenPts.Add(screenPts[0]);
                }

                g.DrawLines(p, screenPts.ToArray());
            }
        }

        private static void DrawStudioViewportBackground(Graphics g, Size viewport, TeddyRenderStyle style)
        {
            if (style == TeddyRenderStyle.PenAndInk)
            {
                // Nền giấy vẽ phác thảo sáng dịu giống giao diện bài báo Teddy (Hình 1 & Hình 3)
                g.Clear(Color.FromArgb(245, 247, 250));
                using (Pen gridPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
                {
                    int step = 36;
                    for (int x = 0; x < viewport.Width; x += step)
                        g.DrawLine(gridPen, x, 0, x, viewport.Height);
                    for (int y = 0; y < viewport.Height; y += step)
                        g.DrawLine(gridPen, 0, y, viewport.Width, y);
                }
            }
            else
            {
                // Nền Bright Studio Light hiện đại đồng bộ với MASTER.md
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, viewport.Width), Math.Max(1, viewport.Height)),
                    Color.FromArgb(248, 250, 252),
                    Color.FromArgb(226, 232, 240),
                    LinearGradientMode.Vertical))
                {
                    g.FillRectangle(bg, 0, 0, viewport.Width, viewport.Height);
                }

                using (Pen gridPen = new Pen(Color.FromArgb(215, 224, 234), 1f))
                {
                    int step = 40;
                    for (int x = 0; x < viewport.Width; x += step)
                        g.DrawLine(gridPen, x, 0, x, viewport.Height);
                    for (int y = 0; y < viewport.Height; y += step)
                        g.DrawLine(gridPen, 0, y, viewport.Width, y);
                }
            }
        }

        // =========================================================================
        // TRỰC QUAN HÓA TỪNG BƯỚC THUẬT TOÁN BÀI BÁO (HÌNH 13, 14, 15 - MỤC 5.1)
        // =========================================================================

        public static void RenderAlgorithmStep(
            Graphics g,
            Size viewport,
            TeddyMesh mesh,
            VirtualTrackballCamera camera,
            TeddyAlgorithmStepView stepView)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawStudioViewportBackground(g, viewport, TeddyRenderStyle.PenAndInk);

            var dbg = mesh.DebugInfo;
            if (dbg == null || dbg.BoundaryPolygon2D.Count < 3)
                return;

            Quaternion invCreationRot = Quaternion.Conjugate(dbg.CreationViewRotation);
            PointF Map2DToScreen(Vector2 pt2D)
            {
                Vector3 worldPt = Vector3.Transform(new Vector3(pt2D.X, pt2D.Y, 0f), invCreationRot);
                Vector3 sp = camera.ProjectToScreen(worldPt, viewport);
                return new PointF(sp.X, sp.Y);
            }

            int nBound = dbg.BoundaryPolygon2D.Count;
            PointF[] boundScreen = new PointF[nBound];
            for (int i = 0; i < nBound; i++)
                boundScreen[i] = Map2DToScreen(dbg.BoundaryPolygon2D[i]);

            switch (stepView)
            {
                case TeddyAlgorithmStepView.Step1_InitialPolygon2D:
                {
                    // Hình 13a: Đa giác phẳng 2D với các cạnh ngoài (External Edges) độ dài đều nhau
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(45, 99, 102, 241)))
                    using (Pen extPen = new Pen(Color.FromArgb(15, 23, 42), 3.0f))
                    using (SolidBrush vertBrush = new SolidBrush(Color.FromArgb(220, 38, 38)))
                    {
                        g.FillPolygon(fill, boundScreen);
                        g.DrawPolygon(extPen, boundScreen);
                        foreach (var pt in boundScreen)
                            g.FillEllipse(vertBrush, pt.X - 3.5f, pt.Y - 3.5f, 7f, 7f);
                    }
                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 1 (Hình 13a): ĐA GIÁC PHẲNG 2D CHUẨN HÓA (UNIFORM EXTERNAL EDGES)",
                        $"Nối điểm đầu-cuối và lấy mẫu đều thành {nBound} cạnh biên (External Edges) có độ dài bằng nhau.");
                    break;
                }

                case TeddyAlgorithmStepView.Step2_CDT_T_S_J:
                {
                    // Hình 13b: Lưới Constrained Delaunay Triangulation & Phân loại tam giác T / S / J
                    using (SolidBrush brushT = new SolidBrush(Color.FromArgb(175, 250, 204, 21)))  // Terminal (T) - Vàng giống Hình 13b
                    using (SolidBrush brushS = new SolidBrush(Color.FromArgb(175, 196, 181, 253))) // Sleeve (S) - Tím nhạt giống Hình 13b
                    using (SolidBrush brushJ = new SolidBrush(Color.FromArgb(185, 248, 113, 113))) // Junction (J) - Cam/Đỏ giống Hình 13b
                    using (Pen intPen = new Pen(Color.FromArgb(51, 65, 85), 1.3f))
                    using (Pen extPen = new Pen(Color.FromArgb(15, 23, 42), 2.8f))
                    {
                        int countT = 0, countS = 0, countJ = 0;
                        foreach (var tri in dbg.CdtTriangles)
                        {
                            if (tri.V0 >= nBound || tri.V1 >= nBound || tri.V2 >= nBound) continue;
                            PointF p0 = boundScreen[tri.V0];
                            PointF p1 = boundScreen[tri.V1];
                            PointF p2 = boundScreen[tri.V2];
                            PointF[] pts = new PointF[] { p0, p1, p2 };

                            Brush b = tri.Category switch
                            {
                                TriangleCategory.Terminal => brushT,
                                TriangleCategory.Sleeve => brushS,
                                _ => brushJ
                            };
                            if (tri.Category == TriangleCategory.Terminal) countT++;
                            else if (tri.Category == TriangleCategory.Sleeve) countS++;
                            else countJ++;

                            g.FillPolygon(b, pts);
                            g.DrawPolygon(intPen, pts);

                            // Vẽ nhãn T / S / J tại trọng tâm tam giác
                            PointF c = new PointF((p0.X + p1.X + p2.X) / 3f, (p0.Y + p1.Y + p2.Y) / 3f);
                            string label = tri.Category == TriangleCategory.Terminal ? "T" :
                                (tri.Category == TriangleCategory.Sleeve ? "S" : "J");
                            TextRenderer.DrawText(g, label, UITheme.FontSmallBold,
                                new Rectangle((int)c.X - 10, (int)c.Y - 8, 20, 16),
                                Color.FromArgb(15, 23, 42),
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }

                        g.DrawPolygon(extPen, boundScreen);
                        DrawStepLegendBanner(g, viewport,
                            "BƯỚC 2 (Hình 13b): CHIA TAM GIÁC CONSTRAINED DELAUNAY (CDT) & PHÂN LOẠI T / S / J",
                            $"Terminal [T - Vàng]: {countT} (2 cạnh biên)  •  Sleeve [S - Tím]: {countS} (1 cạnh biên)  •  Junction [J - Đỏ]: {countJ} (0 cạnh biên)");
                    }
                    break;
                }

                case TeddyAlgorithmStepView.Step3_RawChordalAxis:
                {
                    // Hình 13c: Trục Chordal Axis thô trước khi cắt tỉa
                    using (Pen intPen = new Pen(Color.FromArgb(148, 163, 184), 1.1f))
                    using (Pen extPen = new Pen(Color.FromArgb(15, 23, 42), 2.5f))
                    using (Pen axisPen = new Pen(Color.FromArgb(220, 38, 38), 2.6f))
                    {
                        foreach (var tri in dbg.CdtTriangles)
                        {
                            if (tri.V0 >= nBound || tri.V1 >= nBound || tri.V2 >= nBound) continue;
                            g.DrawPolygon(intPen, new PointF[] { boundScreen[tri.V0], boundScreen[tri.V1], boundScreen[tri.V2] });
                        }
                        g.DrawPolygon(extPen, boundScreen);

                        foreach (var seg in dbg.RawChordalAxisEdges)
                        {
                            PointF a = Map2DToScreen(seg.A);
                            PointF b = Map2DToScreen(seg.B);
                            g.DrawLine(axisPen, a, b);
                        }
                    }
                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 3 (Hình 13c): TRỤC CHORDAL AXIS THÔ (TRƯỚC KHI CẮT TỈA NHÁNH PHỤ)",
                        "Nối trung điểm tất cả các cạnh trong của lưới CDT tạo thành cây xương sống thô có nhiều nhánh nhỏ ở biên.");
                    break;
                }

                case TeddyAlgorithmStepView.Step4_PruningAndSpine:
                {
                    // Hình 13d-f & Hình 14: Cắt tỉa nửa đường tròn, tạo Fan Triangles và Trục Xương Sống (Pruned Spine)
                    using (SolidBrush fanFill = new SolidBrush(Color.FromArgb(65, 74, 222, 128)))
                    using (Pen subPen = new Pen(Color.FromArgb(100, 116, 139), 1.2f))
                    using (Pen circlePen = new Pen(Color.FromArgb(180, 217, 70, 239), 1.6f) { DashStyle = DashStyle.Dash })
                    using (Pen extPen = new Pen(Color.FromArgb(15, 23, 42), 2.6f))
                    using (Pen spinePen = new Pen(Color.FromArgb(234, 88, 12), 3.6f))
                    using (SolidBrush nodeBrush = new SolidBrush(Color.FromArgb(220, 38, 38)))
                    {
                        foreach (var subTri in dbg.SubdividedMesh2D)
                        {
                            PointF a = Map2DToScreen(subTri.A);
                            PointF b = Map2DToScreen(subTri.B);
                            PointF c = Map2DToScreen(subTri.C);
                            g.FillPolygon(fanFill, new PointF[] { a, b, c });
                            g.DrawPolygon(subPen, new PointF[] { a, b, c });
                        }

                        // Vẽ các vòng tròn kiểm tra cắt tỉa (Pruning Semicircles - Hình 14c)
                        foreach (var pc in dbg.PruningCircles)
                        {
                            PointF c = Map2DToScreen(pc.Center);
                            float rPx = pc.Radius * camera.Zoom;
                            if (rPx > 4f)
                                g.DrawEllipse(circlePen, c.X - rPx, c.Y - rPx, rPx * 2f, rPx * 2f);
                        }

                        g.DrawPolygon(extPen, boundScreen);

                        foreach (var se in dbg.PrunedSpineEdges2D)
                        {
                            PointF a = Map2DToScreen(se.A);
                            PointF b = Map2DToScreen(se.B);
                            g.DrawLine(spinePen, a, b);
                        }

                        foreach (var sn in dbg.PrunedSpineNodes2D)
                        {
                            PointF p = Map2DToScreen(sn);
                            g.FillEllipse(nodeBrush, p.X - 4f, p.Y - 4f, 8f, 8f);
                        }
                    }
                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 4 (Hình 13e-f & Hình 14): CẮT TỈA NỬA ĐƯỜNG TRÒN (PRUNING) & TRỤC XƯƠNG SỐNG (SPINE)",
                        "Gộp các tam giác Terminal nằm trong nửa đường tròn (vòng tím đứt nét), chia quạt Fan (xanh lá) và rút trích trục Spine chính (cam đậm).");
                    break;
                }

                case TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D:
                {
                    // Hình 15a-c: Nâng cao độ Z cho Spine và uốn các cạnh sườn thành cung 1/4 hình Elip (Quarter-Oval 3D)
                    using (Pen boundPen = new Pen(Color.FromArgb(15, 23, 42), 2.5f))
                    using (Pen ribPen = new Pen(Color.FromArgb(14, 116, 144), 1.6f))
                    using (Pen spineTopPen = new Pen(Color.FromArgb(220, 38, 38), 3.4f))
                    using (Pen spineBotPen = new Pen(Color.FromArgb(37, 99, 235), 2.8f))
                    {
                        g.DrawPolygon(boundPen, boundScreen);

                        foreach (var rib in dbg.QuarterOvalRibs3D)
                        {
                            PointF[] ribPts = new PointF[rib.Length];
                            for (int i = 0; i < rib.Length; i++)
                            {
                                Vector3 sp = camera.ProjectToScreen(rib[i], viewport);
                                ribPts[i] = new PointF(sp.X, sp.Y);
                            }
                            g.DrawLines(ribPen, ribPts);
                        }

                        foreach (var seg in dbg.ElevatedSpineTop3D)
                        {
                            Vector3 a = camera.ProjectToScreen(seg.A, viewport);
                            Vector3 b = camera.ProjectToScreen(seg.B, viewport);
                            g.DrawLine(spineTopPen, a.X, a.Y, b.X, b.Y);
                        }

                        foreach (var seg in dbg.ElevatedSpineBottom3D)
                        {
                            Vector3 a = camera.ProjectToScreen(seg.A, viewport);
                            Vector3 b = camera.ProjectToScreen(seg.B, viewport);
                            g.DrawLine(spineBotPen, a.X, a.Y, b.X, b.Y);
                        }
                    }
                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 5 (Hình 15b-c): NÂNG ĐỘ CAO Z CHO SPINE & KHUNG SƯỜN QUARTER-OVAL 3D (XOAY ĐƯỢC CHUỘT PHẢI!)",
                        "Hãy giữ Chuột Phải xoay nghiêng để thấy trục Spine nâng cao (+Z màu đỏ, -Z màu xanh) và các cung 1/4 elip nối ra biên!");
                    break;
                }
            }
        }

        private static void DrawStepLegendBanner(Graphics g, Size viewport, string title, string subtitle)
        {
            int w = Math.Min(820, viewport.Width - 28);
            Rectangle rect = new Rectangle((viewport.Width - w) / 2, 12, w, 54);

            using (GraphicsPath path = UITheme.CreateRoundedRect(rect, 8))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            using (Pen border = new Pen(Color.FromArgb(99, 102, 241), 1.5f))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            TextRenderer.DrawText(g, title, UITheme.FontSmallBold,
                new Rectangle(rect.X + 12, rect.Y + 6, rect.Width - 24, 20),
                UITheme.AccentPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, subtitle, UITheme.FontSmall,
                new Rectangle(rect.X + 12, rect.Y + 26, rect.Width - 24, 22),
                UITheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
