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
    /// Chế độ hiển thị Phần Bị Cắt Rời (Cut-Off Piece) để người dùng quan sát vị trí bị cắt.
    /// </summary>
    public enum TeddyCutPieceVisibility
    {
        Visible,            // Hiển thị rõ phần bị cắt (tách hé một khoảng khỏi thân chính)
        GhostTransparent,   // Hiển thị mờ xuyên thấu (Ghost / X-Ray) để nhìn xuyên vào vị trí mặt cắt
        Hidden              // Ẩn hoàn toàn phần bị cắt để nhìn trực diện vào vị trí bị cắt
    }

    /// <summary>
    /// Chế độ hiển thị Thiết Diện Mặt Cắt (Cut Cross-Section Surface) tại vị trí bị cắt.
    /// </summary>
    public enum TeddyCutSurfaceViewMode
    {
        HighlightedCrossSection, // Tô nổi bật thiết diện mặt cắt + viền đỏ cam tại vị trí bị cắt
        OpenHollowInterior,      // Ẩn nắp mặt cắt (Mở rỗng lòng khối) để nhìn sâu vào bên trong vật thể 3D
        SameAsBodyColor          // Mặt cắt đồng màu với vỏ ngoài vật thể
    }

    /// <summary>
    /// Góc nhìn nhanh theo trục tọa độ 3D khi bấm vào La bàn Trục 3D.
    /// </summary>
    public enum TeddyAxisSnapView
    {
        None,
        PosX,
        NegX,
        PosY,
        NegY,
        PosZ,
        NegZ,
        Isometric
    }

    /// <summary>
    /// Phần tay nắm của Hệ Trục Tọa Độ 3D Oxyz để kéo di chuyển khối 3D trên Viewport.
    /// </summary>
    public enum TeddyAxisDragHandle
    {
        None,
        FreeCenterO,
        AxisX,
        AxisY,
        AxisZ
    }

    /// <summary>
    /// [TV3-03 & TV3-07] Bộ kết xuất đồ họa 3D phần mềm tốc độ cao (Mục 1 & 6 bài báo Teddy).
    /// Tích hợp Software Z-Buffer khử nét Silhouette & nét vẽ bị che khuất (Hidden-Line Removal),
    /// tô bóng Pen-and-Ink NPR / Smooth Shaded và kết xuất ảnh PNG trong suốt.
    /// </summary>
    public static class TeddyRenderer
    {
        /// <summary>
        /// [TV3-07] Bộ đệm độ sâu phần mềm (Software Z-Buffer) dạng lưới thô 2x2 px dùng để khử nét khuất
        /// trong chế độ Pen-and-Ink NPR và vẽ nét bề mặt 3D (Mục 1 & 6).
        /// </summary>
        public sealed class SoftwareDepthBuffer
        {
            private readonly int _cellSize;
            private readonly int _cols;
            private readonly int _rows;
            private readonly float[] _maxViewZ;

            public SoftwareDepthBuffer(Size viewport, int cellSize = 2)
            {
                _cellSize = Math.Max(1, cellSize);
                _cols = Math.Max(1, (viewport.Width + _cellSize - 1) / _cellSize + 1);
                _rows = Math.Max(1, (viewport.Height + _cellSize - 1) / _cellSize + 1);
                _maxViewZ = new float[_cols * _rows];
                Array.Fill(_maxViewZ, float.NegativeInfinity);
            }

            public void RasterizeTriangle(Vector3 s0, Vector3 s1, Vector3 s2)
            {
                float minX = MathF.Min(s0.X, MathF.Min(s1.X, s2.X));
                float maxX = MathF.Max(s0.X, MathF.Max(s1.X, s2.X));
                float minY = MathF.Min(s0.Y, MathF.Min(s1.Y, s2.Y));
                float maxY = MathF.Max(s0.Y, MathF.Max(s1.Y, s2.Y));

                int c0 = Math.Clamp((int)MathF.Floor(minX / _cellSize), 0, _cols - 1);
                int c1 = Math.Clamp((int)MathF.Ceiling(maxX / _cellSize), 0, _cols - 1);
                int r0 = Math.Clamp((int)MathF.Floor(minY / _cellSize), 0, _rows - 1);
                int r1 = Math.Clamp((int)MathF.Ceiling(maxY / _cellSize), 0, _rows - 1);

                float area = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
                if (MathF.Abs(area) < 1e-4f) return;
                float invArea = 1f / area;

                for (int r = r0; r <= r1; r++)
                {
                    float py = r * _cellSize + _cellSize * 0.5f;
                    int rowOffset = r * _cols;
                    for (int c = c0; c <= c1; c++)
                    {
                        float px = c * _cellSize + _cellSize * 0.5f;
                        float w0 = ((s1.X - px) * (s2.Y - py) - (s1.Y - py) * (s2.X - px)) * invArea;
                        float w1 = ((s2.X - px) * (s0.Y - py) - (s2.Y - py) * (s0.X - px)) * invArea;
                        float w2 = 1f - w0 - w1;

                        if (w0 >= -0.04f && w1 >= -0.04f && w2 >= -0.04f)
                        {
                            float z = w0 * s0.Z + w1 * s1.Z + w2 * s2.Z;
                            int idx = rowOffset + c;
                            if (z > _maxViewZ[idx])
                                _maxViewZ[idx] = z;
                        }
                    }
                }
            }

            public bool IsPointVisible(float screenX, float screenY, float viewZ, float depthBias = 5.5f)
            {
                int cc = (int)MathF.Round(screenX / _cellSize);
                int rr = (int)MathF.Round(screenY / _cellSize);
                if (cc < 0 || cc >= _cols || rr < 0 || rr >= _rows) return true;

                // Lấy giá trị độ sâu thấp nhất (gần về phía sau nhất) trong lân cận 3x3 ô để tránh cắt nhầm nét viền Silhouette
                float minNeighborMaxZ = float.PositiveInfinity;
                for (int dr = -1; dr <= 1; dr++)
                {
                    int r = rr + dr;
                    if (r < 0 || r >= _rows) continue;
                    int rowOffset = r * _cols;
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int c = cc + dc;
                        if (c < 0 || c >= _cols) continue;
                        float zVal = _maxViewZ[rowOffset + c];
                        if (float.IsNegativeInfinity(zVal))
                            return true; // Chạm vùng nền trống ngay sát biên Silhouette -> hiển thị nét!
                        if (zVal < minNeighborMaxZ)
                            minNeighborMaxZ = zVal;
                    }
                }

                if (float.IsPositiveInfinity(minNeighborMaxZ)) return true;
                return viewZ >= minNeighborMaxZ - depthBias;
            }
        }

        private struct RenderTri
        {
            public int TriIndex;
            public PointF P0;
            public PointF P1;
            public PointF P2;
            public float AvgZ;
            public Vector3 ViewNormal;
            public bool IsInteriorBackFace;
        }

        /// <summary>
        /// [TV3-07] Dựng Software Z-Buffer dùng chung cho toàn bộ các khối 3D hiển thị trong cảnh.
        /// </summary>
        public static SoftwareDepthBuffer BuildSceneDepthBuffer(
            Size viewport,
            IEnumerable<TeddyMesh> meshes,
            VirtualTrackballCamera camera,
            TeddyCutPieceVisibility cutPieceVisibility,
            TeddyCutSurfaceViewMode cutSurfaceViewMode)
        {
            var zBuf = new SoftwareDepthBuffer(viewport);
            if (meshes == null) return zBuf;

            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.IsEmpty) continue;
                if (mesh.IsCutOffPiece && (cutPieceVisibility == TeddyCutPieceVisibility.Hidden || cutPieceVisibility == TeddyCutPieceVisibility.GhostTransparent))
                    continue;

                bool isOpenHollow = cutSurfaceViewMode == TeddyCutSurfaceViewMode.OpenHollowInterior && mesh.CutSectionRings.Count > 0;
                Vector3[] screenVerts = new Vector3[mesh.Vertices.Count];
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    screenVerts[i] = camera.ProjectToScreen(mesh.Vertices[i], viewport);

                foreach (var tri in mesh.Triangles)
                {
                    if (tri.IsCutCap && isOpenHollow) continue;
                    Vector3 vNorm = camera.NormalToView(tri.FaceNormal);
                    bool front = vNorm.Z >= -0.05f;
                    bool hasCut = mesh.CutSectionRings.Count > 0 || tri.IsCutCap;
                    if (!front && !isOpenHollow && !hasCut) continue;

                    zBuf.RasterizeTriangle(screenVerts[tri.V0], screenVerts[tri.V1], screenVerts[tri.V2]);
                }
            }
            return zBuf;
        }

        public static void RenderScene(
            Graphics g,
            Size viewport,
            TeddyMesh? mesh,
            VirtualTrackballCamera camera,
            TeddyRenderStyle style,
            IReadOnlyList<PointF>? active2DStroke = null,
            Color? activeStrokeColor = null,
            bool drawBackground = true,
            TeddyCutPieceVisibility cutPieceVisibility = TeddyCutPieceVisibility.Visible,
            TeddyCutSurfaceViewMode cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection,
            Color? cutFillColor = null,
            SoftwareDepthBuffer? sharedDepthBuffer = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (drawBackground)
            {
                DrawStudioViewportBackground(g, viewport, style);
            }

            if (mesh != null && !mesh.IsEmpty && !(mesh.IsCutOffPiece && cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
            {
                bool isGhostPiece = mesh.IsCutOffPiece && cutPieceVisibility == TeddyCutPieceVisibility.GhostTransparent;
                bool isOpenHollow = cutSurfaceViewMode == TeddyCutSurfaceViewMode.OpenHollowInterior && mesh.CutSectionRings.Count > 0;

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

                    // Nếu người dùng chọn "Ẩn vị trí cắt" -> Ẩn nắp mặt cắt để nhìn thẳng vào bên trong vị trí bị cắt
                    if (tri.IsCutCap && isOpenHollow)
                    {
                        isFrontFacing[i] = false;
                        continue;
                    }

                    Vector3 s0 = screenVerts[tri.V0];
                    Vector3 s1 = screenVerts[tri.V1];
                    Vector3 s2 = screenVerts[tri.V2];

                    // Pháp tuyến trong không gian View
                    Vector3 vNorm = camera.NormalToView(tri.FaceNormal);

                    // Kiểm tra hướng mặt trên màn hình (Cross product 2D) kết hợp ViewNormal.Z
                    float screenCross = (s1.X - s0.X) * (s2.Y - s0.Y) - (s1.Y - s0.Y) * (s2.X - s0.X);
                    bool front = vNorm.Z >= -0.05f || screenCross < 0f;
                    isFrontFacing[i] = vNorm.Z >= 0f || tri.IsCutCap;

                    bool hasCut = mesh.CutSectionRings.Count > 0 || tri.IsCutCap;
                    if (style != TeddyRenderStyle.WireframeSpine && !front && !isOpenHollow && !hasCut)
                        continue;

                    float avgZ = (s0.Z + s1.Z + s2.Z) / 3f;
                    visibleTris.Add(new RenderTri
                    {
                        TriIndex = i,
                        P0 = new PointF(s0.X, s0.Y),
                        P1 = new PointF(s1.X, s1.Y),
                        P2 = new PointF(s2.X, s2.Y),
                        AvgZ = avgZ,
                        ViewNormal = vNorm.Z >= 0f ? vNorm : -vNorm,
                        IsInteriorBackFace = !front && !tri.IsCutCap
                    });
                }

                // Sắp xếp từ xa đến gần (Painter's Algorithm theo Z_view)
                visibleTris.Sort((a, b) => a.AvgZ.CompareTo(b.AvgZ));

                // [TV3-07] Nạp SoftwareDepthBuffer (nếu chưa truyền từ cảnh) để khử nét khuất ở chế độ Pen-and-Ink và SurfaceLines
                SoftwareDepthBuffer? zBuf = sharedDepthBuffer;
                if (zBuf == null && !isGhostPiece)
                {
                    zBuf = new SoftwareDepthBuffer(viewport);
                    foreach (var rt in visibleTris)
                    {
                        var t = mesh.Triangles[rt.TriIndex];
                        zBuf.RasterizeTriangle(screenVerts[t.V0], screenVerts[t.V1], screenVerts[t.V2]);
                    }
                }

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
                    Color highlightedCapColor = cutFillColor ?? ComputeCrossSectionHighlightColor(mesh.BaseColor);

                    // Vẽ các mặt tam giác đã tô bóng
                    foreach (var rt in visibleTris)
                    {
                        polyPts[0] = rt.P0;
                        polyPts[1] = rt.P1;
                        polyPts[2] = rt.P2;

                        var tri = mesh.Triangles[rt.TriIndex];
                        bool isInnerOrCap = tri.IsCutCap || rt.IsInteriorBackFace;
                        Color triBaseColor;
                        if (tri.CustomColor.HasValue)
                        {
                            triBaseColor = tri.CustomColor.Value;
                        }
                        else if (tri.IsCutCap)
                        {
                            // Phía trong mặt cắt được tô sẫm màu hơn bên ngoài (hoặc màu vết cắt nếu người dùng chọn riêng)
                            triBaseColor = cutFillColor ?? highlightedCapColor;
                        }
                        else if (rt.IsInteriorBackFace)
                        {
                            // Mặt phía trong lòng hình khối được tô sẫm màu hơn bên ngoài
                            triBaseColor = Color.FromArgb(
                                (int)(mesh.BaseColor.R * 0.50f),
                                (int)(mesh.BaseColor.G * 0.46f),
                                (int)(mesh.BaseColor.B * 0.50f));
                        }
                        else
                        {
                            triBaseColor = mesh.BaseColor;
                        }

                        Color shadedColor = ComputeShadedColor(
                            triBaseColor,
                            rt.ViewNormal,
                            keyLight,
                            fillLight,
                            style == TeddyRenderStyle.PenAndInk,
                            isInnerOrCap);

                        if (isGhostPiece)
                        {
                            shadedColor = Color.FromArgb(72, shadedColor.R, shadedColor.G, shadedColor.B);
                            using (SolidBrush brush = new SolidBrush(shadedColor))
                            {
                                g.FillPolygon(brush, polyPts);
                            }
                        }
                        else
                        {
                            using (SolidBrush brush = new SolidBrush(shadedColor))
                            using (Pen seamPen = new Pen(shadedColor, 1.0f))
                            {
                                g.FillPolygon(brush, polyPts);
                                // Vẽ viền mảnh cùng màu để khử khe hở anti-alias giữa 2 tam giác kề nhau
                                g.DrawPolygon(seamPen, polyPts);
                            }
                        }
                    }

                    // Nếu là chế độ Pen-and-Ink (Hình 1 bài báo): Phát hiện và vẽ các cạnh Silhouette đậm nét mực có khử nét khuất Z-Buffer
                    if (style == TeddyRenderStyle.PenAndInk)
                    {
                        DrawPenAndInkSilhouettes(g, mesh, screenVerts, isFrontFacing, isGhostPiece, isOpenHollow, isGhostPiece ? null : zBuf);
                    }
                }

                // Vẽ các nét trang trí trên bề mặt 3D và đường viền đánh dấu vị trí mặt cắt (có khử nét khuất Z-Buffer)
                DrawSurfaceLines(g, mesh, camera, viewport, isGhostPiece, cutSurfaceViewMode, isGhostPiece ? null : zBuf);
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
            TeddyRenderStyle style,
            TeddyCutPieceVisibility cutPieceVisibility = TeddyCutPieceVisibility.Visible,
            TeddyCutSurfaceViewMode cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection,
            Color? cutFillColor = null)
        {
            Bitmap bmp = new Bitmap(Math.Max(1, width), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (meshes != null)
                {
                    var sorted = meshes.Where(m => m != null && !m.IsEmpty && !(m.IsCutOffPiece && cutPieceVisibility == TeddyCutPieceVisibility.Hidden)).OrderBy(m =>
                    {
                        float sumZ = 0f;
                        foreach (var v in m.Vertices) sumZ += camera.WorldToView(v).Z;
                        return m.Vertices.Count > 0 ? sumZ / m.Vertices.Count : 0f;
                    }).ToList();

                    foreach (var mesh in sorted)
                    {
                        RenderScene(g, new Size(width, height), mesh, camera, style, null, null,
                            drawBackground: false,
                            cutPieceVisibility: cutPieceVisibility,
                            cutSurfaceViewMode: cutSurfaceViewMode,
                            cutFillColor: cutFillColor);
                    }
                }
            }
            return bmp;
        }

        /// <summary>
        /// Tạo tông màu phía trong hình khối / thiết diện mặt cắt SẪM MÀU HƠN bên ngoài.
        /// </summary>
        private static Color ComputeCrossSectionHighlightColor(Color bodyColor)
        {
            int r = Math.Clamp((int)(bodyColor.R * 0.56f), 0, 255);
            int g = Math.Clamp((int)(bodyColor.G * 0.52f), 0, 255);
            int b = Math.Clamp((int)(bodyColor.B * 0.54f), 0, 255);
            return Color.FromArgb(r, g, b);
        }

        private static Color ComputeShadedColor(
            Color baseColor,
            Vector3 normal,
            Vector3 keyLight,
            Vector3 fillLight,
            bool toonQuantize,
            bool isInteriorDark = false)
        {
            float ndotl = MathF.Max(0f, Vector3.Dot(normal, keyLight));
            float fill = MathF.Max(0f, Vector3.Dot(normal, fillLight)) * 0.25f;
            float rim = MathF.Pow(1.0f - Math.Clamp(MathF.Abs(normal.Z), 0f, 1f), 2.4f) * 0.18f;

            if (isInteriorDark)
            {
                // Phía trong hình khối giữ độ sẫm màu ổn định hơn bên ngoài, không bị lóa sáng specular
                float innerLevel = 0.68f + ndotl * 0.26f;
                int r = Math.Clamp((int)(baseColor.R * innerLevel), 0, 255);
                int g = Math.Clamp((int)(baseColor.G * innerLevel), 0, 255);
                int b = Math.Clamp((int)(baseColor.B * innerLevel), 0, 255);
                return Color.FromArgb(r, g, b);
            }

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
            bool[] isFrontFacing,
            bool isGhostPiece = false,
            bool isOpenHollow = false,
            SoftwareDepthBuffer? zBuf = null)
        {
            Color silColor = isGhostPiece ? Color.FromArgb(95, 71, 85, 105) : Color.FromArgb(30, 58, 95);
            Color crsColor = isGhostPiece ? Color.FromArgb(55, 71, 85, 105) : Color.FromArgb(95, 51, 65, 85);

            bool IsEdgeVisible(Vector3 a, Vector3 b, float bias)
            {
                if (zBuf == null) return true;
                Vector3 m = (a + b) * 0.5f;
                if (zBuf.IsPointVisible(m.X, m.Y, m.Z, bias)) return true;
                Vector3 q1 = Vector3.Lerp(a, b, 0.25f);
                Vector3 q3 = Vector3.Lerp(a, b, 0.75f);
                return zBuf.IsPointVisible(q1.X, q1.Y, q1.Z, bias) || zBuf.IsPointVisible(q3.X, q3.Y, q3.Z, bias);
            }

            using (Pen silhouettePen = new Pen(silColor, isGhostPiece ? 1.8f : 2.8f))
            using (Pen creasePen = new Pen(crsColor, 1.1f))
            {
                silhouettePen.StartCap = LineCap.Round;
                silhouettePen.EndCap = LineCap.Round;
                if (isGhostPiece)
                {
                    silhouettePen.DashPattern = new float[] { 3f, 3f };
                }

                foreach (var edge in mesh.Edges)
                {
                    bool leftCap = edge.TriLeft >= 0 && mesh.Triangles[edge.TriLeft].IsCutCap;
                    bool rightCap = edge.TriRight >= 0 && mesh.Triangles[edge.TriRight].IsCutCap;

                    if (isOpenHollow && (leftCap || rightCap))
                    {
                        continue;
                    }

                    if (leftCap && rightCap)
                    {
                        // Cạnh bên trong bề mặt vết cắt: chỉ vẽ nét gờ mảnh tại các đỉnh gấp khúc Zíc-zắc / Bậc thang
                        float capDot = Vector3.Dot(mesh.Triangles[edge.TriLeft].FaceNormal, mesh.Triangles[edge.TriRight].FaceNormal);
                        if (capDot < 0.75f)
                        {
                            Vector3 pA = screenVerts[edge.A];
                            Vector3 pB = screenVerts[edge.B];
                            if (IsEdgeVisible(pA, pB, 4.5f))
                                g.DrawLine(creasePen, pA.X, pA.Y, pB.X, pB.Y);
                        }
                        continue;
                    }

                    bool leftFront = edge.TriLeft >= 0 && isFrontFacing[edge.TriLeft];
                    bool rightFront = edge.TriRight >= 0 && isFrontFacing[edge.TriRight];

                    // Cạnh Silhouette: 1 mặt hướng về Camera, mặt kia quay lưng lại (hoặc cạnh biên hở)
                    if (leftFront != rightFront || (edge.TriRight == -1 && leftFront))
                    {
                        Vector3 pA = screenVerts[edge.A];
                        Vector3 pB = screenVerts[edge.B];
                        if (IsEdgeVisible(pA, pB, 8.5f))
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
                            if (IsEdgeVisible(pA, pB, 5.0f))
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
            Size viewport,
            bool isGhostPiece = false,
            TeddyCutSurfaceViewMode cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection,
            SoftwareDepthBuffer? zBuf = null)
        {
            foreach (var sl in mesh.SurfaceLines)
            {
                DrawSingle3DPolyline(g, sl, camera, viewport, false, isGhostPiece, zBuf: zBuf);
            }

            // Vẽ đường viền đánh dấu vị trí thiết diện mặt cắt bằng NÉT ĐỨT
            if (mesh.CutSectionRings.Count > 0)
            {
                foreach (var cr in mesh.CutSectionRings)
                {
                    DrawSingle3DPolyline(g, cr, camera, viewport, false, isGhostPiece,
                        overrideColor: isGhostPiece ? Color.FromArgb(120, 225, 29, 72) : Color.FromArgb(235, 225, 29, 72),
                        overrideWidth: isGhostPiece ? 2.2f : 3.0f,
                        forceDashed: true,
                        zBuf: null); // Đường thiết diện nét đứt luôn hiển thị rõ vị trí vết cắt
                }
            }

            // Vẽ vòng chọn màu đỏ khi đang ở Extrusion Mode (Mục 4.4 - Hình 3i)
            if (mesh.ActiveBaseRing != null)
            {
                DrawSingle3DPolyline(g, mesh.ActiveBaseRing, camera, viewport, true, isGhostPiece,
                    forceDashed: mesh.ActiveBaseRing.IsDashed,
                    zBuf: null);
            }
        }

        private static void DrawSingle3DPolyline(
            Graphics g,
            TeddySurfaceLine line,
            VirtualTrackballCamera camera,
            Size viewport,
            bool isHighlightRing,
            bool isGhostPiece = false,
            Color? overrideColor = null,
            float? overrideWidth = null,
            bool forceDashed = false,
            SoftwareDepthBuffer? zBuf = null)
        {
            if (line.Points.Count < 2) return;

            Color c = overrideColor ?? (isHighlightRing ? Color.FromArgb(239, 68, 68) : line.StrokeColor);
            if (isGhostPiece && !overrideColor.HasValue)
            {
                c = Color.FromArgb(90, c.R, c.G, c.B);
            }
            float w = overrideWidth ?? (isHighlightRing ? 3.6f : line.StrokeWidth);

            using (Pen p = new Pen(c, w))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                if (forceDashed || line.IsDashed)
                {
                    p.DashPattern = new float[] { 3.5f, 2.5f };
                }

                var projected = new List<Vector3>(line.Points.Count + 1);
                foreach (var pt3D in line.Points)
                    projected.Add(camera.ProjectToScreen(pt3D, viewport));
                if (line.IsClosedRing && projected.Count > 2)
                    projected.Add(projected[0]);

                if (zBuf == null)
                {
                    var screenPts = new PointF[projected.Count];
                    for (int i = 0; i < projected.Count; i++)
                        screenPts[i] = new PointF(projected[i].X, projected[i].Y);
                    g.DrawLines(p, screenPts);
                }
                else
                {
                    // [TV3-07] Tách các đoạn nét vẽ bề mặt theo Z-Buffer: chỉ vẽ những đoạn ở mặt nhìn thấy phía trước
                    var visibleRun = new List<PointF>();
                    for (int i = 0; i < projected.Count; i++)
                    {
                        Vector3 sp = projected[i];
                        bool vis = zBuf.IsPointVisible(sp.X, sp.Y, sp.Z, depthBias: 7.0f);
                        if (vis)
                        {
                            visibleRun.Add(new PointF(sp.X, sp.Y));
                        }
                        else if (visibleRun.Count >= 2)
                        {
                            g.DrawLines(p, visibleRun.ToArray());
                            visibleRun.Clear();
                        }
                        else
                        {
                            visibleRun.Clear();
                        }
                    }
                    if (visibleRun.Count >= 2)
                        g.DrawLines(p, visibleRun.ToArray());
                }
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
            if (dbg == null)
                return;

            bool isExtrudeOrSmoothStep =
                stepView == TeddyAlgorithmStepView.Step6_ExtrusionTwoPointer3D ||
                stepView == TeddyAlgorithmStepView.Step7_BezierHoleSmoothing3D;

            if (!isExtrudeOrSmoothStep && dbg.BoundaryPolygon2D.Count < 3)
                return;

            Quaternion invCreationRot = Quaternion.Conjugate(dbg.CreationViewRotation);
            PointF Map2DToScreen(Vector2 pt2D)
            {
                Vector3 worldPt = Vector3.Transform(new Vector3(pt2D.X, pt2D.Y, 0f), invCreationRot) + dbg.TranslationOffset3D;
                Vector3 sp = camera.ProjectToScreen(worldPt, viewport);
                return new PointF(sp.X, sp.Y);
            }

            int nBound = dbg.BoundaryPolygon2D.Count;
            PointF[] boundScreen = new PointF[nBound];
            if (stepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D && dbg.BoundaryPolygon3D.Count == nBound)
            {
                for (int i = 0; i < nBound; i++)
                {
                    Vector3 sp = camera.ProjectToScreen(dbg.BoundaryPolygon3D[i], viewport);
                    boundScreen[i] = new PointF(sp.X, sp.Y);
                }
            }
            else if (nBound > 0)
            {
                for (int i = 0; i < nBound; i++)
                    boundScreen[i] = Map2DToScreen(dbg.BoundaryPolygon2D[i]);
            }

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
                        "BƯỚC 5 (TV1-07, Hình 15b-c): NÂNG SPINE & CUNG 1/4 ELIP THEO GÓC θ (sin θ, cos θ)",
                        $"Giữ Chuột Phải xoay 90° để thấy trục Spine (+Z đỏ, -Z xanh) & cung 1/4 elip K={TeddyInflationEngine.QuarterOvalSteps} tròn mượt tại biên Z=0!");
                    break;
                }

                case TeddyAlgorithmStepView.Step6_ExtrusionTwoPointer3D:
                {
                    // Vẽ lưới khối 3D mờ phía dưới để định vị rõ vị trí vòng đế & phần đùn
                    RenderScene(g, viewport, mesh, camera, TeddyRenderStyle.SmoothShaded, null, null, drawBackground: false,
                        cutPieceVisibility: TeddyCutPieceVisibility.GhostTransparent);

                    if (dbg.ExtrudeBaseRing3D.Count >= 3)
                    {
                        var ringScreen = new PointF[dbg.ExtrudeBaseRing3D.Count + 1];
                        for (int i = 0; i < dbg.ExtrudeBaseRing3D.Count; i++)
                        {
                            Vector3 sp = camera.ProjectToScreen(dbg.ExtrudeBaseRing3D[i], viewport);
                            ringScreen[i] = new PointF(sp.X, sp.Y);
                        }
                        ringScreen[dbg.ExtrudeBaseRing3D.Count] = ringScreen[0];
                        using Pen basePen = new Pen(Color.FromArgb(239, 68, 68), 3.2f);
                        g.DrawLines(basePen, ringScreen);
                    }

                    // Vẽ các vòng lát cắt RMF màu Cyan
                    using (Pen slicePen = new Pen(Color.FromArgb(185, 6, 182, 212), 1.6f))
                    {
                        foreach (var ring in dbg.ExtrudeSweepRings3D)
                        {
                            if (ring.Length < 3) continue;
                            var pts = new PointF[ring.Length + 1];
                            for (int i = 0; i < ring.Length; i++)
                            {
                                Vector3 sp = camera.ProjectToScreen(ring[i], viewport);
                                pts[i] = new PointF(sp.X, sp.Y);
                            }
                            pts[ring.Length] = pts[0];
                            g.DrawLines(slicePen, pts);
                        }
                    }

                    // Vẽ các thanh nối 2 con trỏ (Two-Pointer Rungs L <-> R - Hình 18a) màu Cam
                    using (Pen rungPen = new Pen(Color.FromArgb(245, 158, 11), 2.1f))
                    using (SolidBrush ptrDot = new SolidBrush(Color.FromArgb(234, 88, 12)))
                    {
                        foreach (var rung in dbg.ExtrudePointerRungs3D)
                        {
                            Vector3 a = camera.ProjectToScreen(rung.LeftPt, viewport);
                            Vector3 b = camera.ProjectToScreen(rung.RightPt, viewport);
                            g.DrawLine(rungPen, a.X, a.Y, b.X, b.Y);
                            g.FillEllipse(ptrDot, a.X - 3f, a.Y - 3f, 6f, 6f);
                            g.FillEllipse(ptrDot, b.X - 3f, b.Y - 3f, 6f, 6f);
                        }
                    }

                    // Vẽ nét đùn 3D chiếu lên mặt phẳng Ring-Normal
                    if (dbg.ExtrudeStroke3D.Count >= 2)
                    {
                        var stPts = new PointF[dbg.ExtrudeStroke3D.Count];
                        for (int i = 0; i < dbg.ExtrudeStroke3D.Count; i++)
                        {
                            Vector3 sp = camera.ProjectToScreen(dbg.ExtrudeStroke3D[i], viewport);
                            stPts[i] = new PointF(sp.X, sp.Y);
                        }
                        using Pen strokePen = new Pen(Color.FromArgb(37, 99, 235), 3.0f);
                        g.DrawLines(strokePen, stPts);
                    }

                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 6 (TV2-07 & TV3-08, Hình 17–19): CHIẾU RING-NORMAL, QUÉT 2 CON TRỎ & KHUNG RMF",
                        dbg.ExtrudeSweepRings3D.Count > 0
                            ? $"Hiển thị Vòng Đế (đỏ), nét đùn Π_ext (xanh dương), các bậc 2 con trỏ L↔R (cam: {dbg.ExtrudePointerRungs3D.Count}) và lát cắt RMF (cyan)."
                            : "Chưa có dữ liệu Đùn khối! Hãy [Đặt Vòng Đế] → vẽ nét vòm → bấm [Đùn / Khoét 3D] rồi quay lại Bước 6.");
                    break;
                }

                case TeddyAlgorithmStepView.Step7_BezierHoleSmoothing3D:
                {
                    RenderScene(g, viewport, mesh, camera, TeddyRenderStyle.SmoothShaded, null, null, drawBackground: false,
                        cutPieceVisibility: TeddyCutPieceVisibility.GhostTransparent);

                    // Lưới tam giác Steiner-CDT vừa đắp vào lỗ thủng (xanh lục)
                    using (SolidBrush triFill = new SolidBrush(Color.FromArgb(85, 34, 197, 94)))
                    using (Pen triWire = new Pen(Color.FromArgb(180, 22, 163, 74), 1.3f))
                    {
                        foreach (var tri in dbg.SmoothSteinerTriangles3D)
                        {
                            Vector3 p0 = camera.ProjectToScreen(tri.A, viewport);
                            Vector3 p1 = camera.ProjectToScreen(tri.B, viewport);
                            Vector3 p2 = camera.ProjectToScreen(tri.C, viewport);
                            var poly = new PointF[] { new(p0.X, p0.Y), new(p1.X, p1.Y), new(p2.X, p2.Y) };
                            g.FillPolygon(triFill, poly);
                            g.DrawPolygon(triWire, poly);
                        }
                    }

                    // Vòng biên lỗ thủng sau khi xóa đa giác nhăn cũ (Đỏ)
                    if (dbg.SmoothHoleBoundary3D.Count >= 3)
                    {
                        var holeScreen = new PointF[dbg.SmoothHoleBoundary3D.Count + 1];
                        for (int i = 0; i < dbg.SmoothHoleBoundary3D.Count; i++)
                        {
                            Vector3 sp = camera.ProjectToScreen(dbg.SmoothHoleBoundary3D[i], viewport);
                            holeScreen[i] = new PointF(sp.X, sp.Y);
                        }
                        holeScreen[dbg.SmoothHoleBoundary3D.Count] = holeScreen[0];
                        using Pen holePen = new Pen(Color.FromArgb(225, 29, 72), 3.0f);
                        g.DrawLines(holePen, holeScreen);
                    }

                    // Các đường cong vòm Cubic Bezier đa mặt phẳng khớp tiếp tuyến biên (Tím / Cam)
                    using (Pen archPen = new Pen(Color.FromArgb(220, 147, 51, 234), 2.3f))
                    {
                        foreach (var arch in dbg.SmoothBezierArches3D)
                        {
                            if (arch.Length < 2) continue;
                            var archPts = new PointF[arch.Length];
                            for (int i = 0; i < arch.Length; i++)
                            {
                                Vector3 sp = camera.ProjectToScreen(arch[i], viewport);
                                archPts[i] = new PointF(sp.X, sp.Y);
                            }
                            g.DrawLines(archPen, archPts);
                        }
                    }

                    DrawStepLegendBanner(g, viewport,
                        "BƯỚC 7 (TV2-09 & TV3-08, Hình 10 & 21): XÓA LỖ, LƯỚI STEINER-CDT & VÒM CUBIC BEZIER",
                        dbg.SmoothHoleBoundary3D.Count > 0
                            ? $"Biên lỗ đã xóa lưới cũ (đỏ), lưới đắp Steiner-CDT (xanh lá: {dbg.SmoothSteinerTriangles3D.Count} mặt) và vòm tiếp tuyến Cubic Bezier (tím)."
                            : "Chưa có dữ liệu Làm Mượt Bezier! Hãy khoanh vòng chọn trên khối và bấm [Mượt Bezier 3D] rồi xem Bước 7.");
                    break;
                }
            }
        }

        /// <summary>
        /// [TV3-09] Vẽ thẻ thống kê Benchmark Hình học 3D & Kiểm định Euler ở góc trái dưới Viewport.
        /// </summary>
        public static void DrawBenchmarkOverlayCard(
            Graphics g,
            Size viewport,
            TeddyMesh? activeMesh,
            int totalMeshes,
            double lastAlgoMs,
            string lastAlgoName)
        {
            if (viewport.Width < 360 || viewport.Height < 220) return;

            const int w = 310;
            const int h = 122;
            Rectangle card = new Rectangle(14, viewport.Height - h - 14, w, h);

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath gp = UITheme.CreateRoundedRect(card, 9))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(242, 255, 255, 255)))
            using (Pen bd = new Pen(UITheme.AccentPrimary, 1.3f))
            {
                g.FillPath(bg, gp);
                g.DrawPath(bd, gp);
            }

            Rectangle titleRect = new Rectangle(card.X + 10, card.Y + 6, card.Width - 20, 18);
            TextRenderer.DrawText(g, "BENCHMARK HÌNH HỌC 3D (TV1-09 & TV3-09)", UITheme.FontGroupHeader, titleRect, UITheme.AccentPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string line1, line2, line3, line4;
            if (activeMesh == null || activeMesh.IsEmpty)
            {
                line1 = $"Thuật toán: {lastAlgoName} ({lastAlgoMs:F1} ms)";
                line2 = "Chưa có khối 3D đang chọn để kiểm định Euler.";
                line3 = "Bấm [B] hoặc thanh trạng thái góc phải để ẩn/hiện.";
                line4 = $"Tổng số khối trong cảnh: {totalMeshes}";
            }
            else
            {
                var rep = activeMesh.ValidateManifoldAndEuler();
                activeMesh.ComputeTriangleAngleStats(out float minAng, out float avgMinAng);
                string manifoldText = rep.IsClosedWatertightManifold
                    ? "Kín Đa Tạp (Watertight)"
                    : $"Hở {rep.BoundaryEdgeCount} cạnh / NM {rep.NonManifoldEdgeCount}";
                line1 = $"Thuật toán: {lastAlgoName}  |  {lastAlgoMs:F1} ms";
                line2 = $"V={rep.VertexCount}  E={rep.EdgeCount}  F={rep.FaceCount}  |  χ=V-E+F = {rep.EulerCharacteristic}";
                line3 = $"Đa tạp: {manifoldText}";
                line4 = $"Góc nhỏ nhất: {minAng:F1}°  |  TB: {avgMinAng:F1}°  (Khối: {totalMeshes})";
            }

            TextRenderer.DrawText(g, line1, UITheme.FontSmallBold, new Rectangle(card.X + 10, card.Y + 28, card.Width - 20, 18), UITheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, line2, UITheme.FontMono, new Rectangle(card.X + 10, card.Y + 49, card.Width - 20, 18), UITheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, line3, UITheme.FontSmall, new Rectangle(card.X + 10, card.Y + 70, card.Width - 20, 18), UITheme.AccentSuccess, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, line4, UITheme.FontSmall, new Rectangle(card.X + 10, card.Y + 91, card.Width - 20, 18), UITheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            g.SmoothingMode = oldMode;
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

        // =========================================================================
        // HỆ TRỤC TỌA ĐỘ 3D KHÔNG GIAN (WORLD OXYZ AXES) & LA BÀN TRỤC 3D (GIZMO)
        // =========================================================================

        private struct AxisRayItem
        {
            public TeddyAxisSnapView AxisView;
            public string Label;
            public Vector3 WorldDir;
            public Color Color;
            public bool IsPositive;
            public Vector3 ViewDir;
            public PointF ScreenTip;
        }

        private static readonly (TeddyAxisSnapView AxisView, string Label, Vector3 WorldDir, Color Color, bool IsPositive)[] AxisDefinitions =
        {
            (TeddyAxisSnapView.PosX, "+X", Vector3.UnitX,  Color.FromArgb(225, 29, 72),  true),
            (TeddyAxisSnapView.NegX, "-X", -Vector3.UnitX, Color.FromArgb(225, 29, 72),  false),
            (TeddyAxisSnapView.PosY, "+Y", Vector3.UnitY,  Color.FromArgb(16, 185, 129), true),
            (TeddyAxisSnapView.NegY, "-Y", -Vector3.UnitY, Color.FromArgb(16, 185, 129), false),
            (TeddyAxisSnapView.PosZ, "+Z", Vector3.UnitZ,  Color.FromArgb(37, 99, 235),  true),
            (TeddyAxisSnapView.NegZ, "-Z", -Vector3.UnitZ, Color.FromArgb(37, 99, 235),  false),
        };

        private static void GetWorldAxisRayGeometry(
            Size viewport,
            VirtualTrackballCamera camera,
            Vector3 originWorld,
            float axisLength,
            Quaternion? localRotation,
            out PointF originPt,
            out List<AxisRayItem> rays)
        {
            Quaternion meshRot = localRotation ?? Quaternion.Identity;
            Vector3 originScreen3D = camera.ProjectToScreen(originWorld, viewport);
            originPt = new PointF(originScreen3D.X, originScreen3D.Y);

            Vector3 originView = camera.WorldToView(originWorld);
            rays = new List<AxisRayItem>(6);
            foreach (var def in AxisDefinitions)
            {
                Vector3 rotatedDir = Vector3.Normalize(Vector3.Transform(def.WorldDir, meshRot));
                Vector3 worldTip = originWorld + rotatedDir * axisLength;
                Vector3 sp = camera.ProjectToScreen(worldTip, viewport);
                rays.Add(new AxisRayItem
                {
                    AxisView = def.AxisView,
                    Label = def.Label,
                    WorldDir = rotatedDir,
                    Color = def.Color,
                    IsPositive = def.IsPositive,
                    ViewDir = camera.WorldToView(worldTip) - originView,
                    ScreenTip = new PointF(sp.X, sp.Y)
                });
            }

            // Sắp xếp từ xa tới gần theo độ sâu tương đối Z_view
            rays.Sort((a, b) => a.ViewDir.Z.CompareTo(b.ViewDir.Z));
        }

        private static TeddyAxisDragHandle MapAxisViewToDragHandle(TeddyAxisSnapView axisView, Quaternion meshRot, out Vector3 unitAxisWorld)
        {
            switch (axisView)
            {
                case TeddyAxisSnapView.PosX:
                case TeddyAxisSnapView.NegX:
                    unitAxisWorld = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, meshRot));
                    return TeddyAxisDragHandle.AxisX;
                case TeddyAxisSnapView.PosY:
                case TeddyAxisSnapView.NegY:
                    unitAxisWorld = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, meshRot));
                    return TeddyAxisDragHandle.AxisY;
                case TeddyAxisSnapView.PosZ:
                case TeddyAxisSnapView.NegZ:
                    unitAxisWorld = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, meshRot));
                    return TeddyAxisDragHandle.AxisZ;
                default:
                    unitAxisWorld = Vector3.Zero;
                    return TeddyAxisDragHandle.None;
            }
        }

        private static float DistancePointToSegment2D(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-5f)
            {
                float ddx = p.X - a.X;
                float ddy = p.Y - a.Y;
                return MathF.Sqrt(ddx * ddx + ddy * ddy);
            }
            float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
            float qx = a.X + t * dx;
            float qy = a.Y + t * dy;
            return MathF.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
        }

        /// <summary>
        /// Kiểm tra xem con trỏ chuột có đang chạm vào Tâm O hoặc thân/mũi tên Trục X, Y, Z của hình 3D đang chọn để kéo di chuyển khối không.
        /// </summary>
        public static bool HitTestWorldAxesGizmo(
            Size viewport,
            VirtualTrackballCamera camera,
            Vector3 originWorld,
            float referenceRadius,
            Point mousePos,
            out TeddyAxisDragHandle handle,
            out Vector3 axisWorldDir,
            Quaternion? localRotation = null)
        {
            handle = TeddyAxisDragHandle.None;
            axisWorldDir = Vector3.Zero;
            if (viewport.Width < 40 || viewport.Height < 40) return false;

            Quaternion meshRot = localRotation ?? Quaternion.Identity;
            float refRadius = Math.Clamp(referenceRadius, 75f, 360f);
            float axisLength = refRadius * 1.38f;
            GetWorldAxisRayGeometry(viewport, camera, originWorld, axisLength, meshRot, out PointF originPt, out var rays);

            float cdx = mousePos.X - originPt.X;
            float cdy = mousePos.Y - originPt.Y;
            if (cdx * cdx + cdy * cdy <= 13f * 13f)
            {
                handle = TeddyAxisDragHandle.FreeCenterO;
                return true;
            }

            PointF mouseF = mousePos;
            for (int i = rays.Count - 1; i >= 0; i--)
            {
                var ray = rays[i];
                float dx = ray.ScreenTip.X - originPt.X;
                float dy = ray.ScreenTip.Y - originPt.Y;
                float segLen = MathF.Sqrt(dx * dx + dy * dy);
                if (segLen < 6f) continue;

                float ux = dx / segLen;
                float uy = dy / segLen;
                float distToSeg = DistancePointToSegment2D(mouseF, originPt, ray.ScreenTip);

                bool hit = distToSeg <= 10f;
                if (!hit && (ray.IsPositive || segLen >= 20f))
                {
                    float offset = ray.IsPositive ? 14f : 11f;
                    float bx = ray.ScreenTip.X + ux * offset;
                    float by = ray.ScreenTip.Y + uy * offset;
                    float bdx = mouseF.X - bx;
                    float bdy = mouseF.Y - by;
                    if (bdx * bdx + bdy * bdy <= 13f * 13f)
                        hit = true;
                }

                if (hit)
                {
                    handle = MapAxisViewToDragHandle(ray.AxisView, meshRot, out axisWorldDir);
                    return handle != TeddyAxisDragHandle.None;
                }
            }

            return false;
        }

        /// <summary>
        /// Vẽ Hệ Trục Tọa Độ 3D Oxyz (X: Đỏ, Y: Xanh lá, Z: Xanh dương) bám theo tâm & hướng xoay riêng của hình vẽ / khối 3D.
        /// </summary>
        public static void DrawWorldCoordinateAxes3D(
            Graphics g,
            Size viewport,
            VirtualTrackballCamera camera,
            bool isOverlayOnMesh,
            Vector3? customOriginWorld = null,
            float? customReferenceRadius = null,
            (Vector3 Min, Vector3 Max)? meshBounds3D = null,
            TeddyAxisDragHandle highlightedHandle = TeddyAxisDragHandle.None,
            Quaternion? localRotation = null,
            bool isCompactSecondary = false)
        {
            if (viewport.Width < 40 || viewport.Height < 40) return;

            SmoothingMode oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Vector3 originWorld = customOriginWorld ?? Vector3.Zero;
            Quaternion meshRot = localRotation ?? Quaternion.Identity;
            float refRadius = Math.Clamp(customReferenceRadius ?? 135f, 75f, 360f);
            float planeRadius = refRadius * 1.15f;
            float axisLength = isCompactSecondary ? refRadius * 0.95f : refRadius * 1.38f;
            float diagExtent = planeRadius * 0.71f;

            // 1. Khi ở lớp nền trước khi vẽ khối: Vẽ vòng tròn & đường chéo mặt phẳng tham chiếu đáy XZ theo hướng xoay riêng của hình
            if (!isOverlayOnMesh)
            {
                const int ringSegments = 48;
                PointF[] ringPts = new PointF[ringSegments + 1];
                for (int i = 0; i <= ringSegments; i++)
                {
                    float theta = (i % ringSegments) * (MathF.PI * 2f / ringSegments);
                    Vector3 localCirclePt = new Vector3(MathF.Cos(theta) * planeRadius, 0f, MathF.Sin(theta) * planeRadius);
                    Vector3 wp = originWorld + Vector3.Transform(localCirclePt, meshRot);
                    Vector3 sp = camera.ProjectToScreen(wp, viewport);
                    ringPts[i] = new PointF(sp.X, sp.Y);
                }

                using (Pen ringPen = new Pen(Color.FromArgb(120, 148, 163, 184), 1.15f))
                {
                    ringPen.DashPattern = new float[] { 4f, 4f };
                    g.DrawLines(ringPen, ringPts);
                }

                using (Pen diagPen = new Pen(Color.FromArgb(65, 148, 163, 184), 1.0f))
                {
                    diagPen.DashPattern = new float[] { 3f, 4f };
                    Vector3 d1a = camera.ProjectToScreen(originWorld + Vector3.Transform(new Vector3(-diagExtent, 0f, -diagExtent), meshRot), viewport);
                    Vector3 d1b = camera.ProjectToScreen(originWorld + Vector3.Transform(new Vector3(diagExtent, 0f, diagExtent), meshRot), viewport);
                    Vector3 d2a = camera.ProjectToScreen(originWorld + Vector3.Transform(new Vector3(-diagExtent, 0f, diagExtent), meshRot), viewport);
                    Vector3 d2b = camera.ProjectToScreen(originWorld + Vector3.Transform(new Vector3(diagExtent, 0f, -diagExtent), meshRot), viewport);
                    g.DrawLine(diagPen, d1a.X, d1a.Y, d1b.X, d1b.Y);
                    g.DrawLine(diagPen, d2a.X, d2a.Y, d2b.X, d2b.Y);
                }

                g.SmoothingMode = oldSmooth;
                return;
            }

            // 2. Vẽ các bán trục Oxyz xuất phát từ tâm & xoay theo LocalRotation của hình 3D (đã sắp xếp Z-order từ xa tới gần)
            GetWorldAxisRayGeometry(viewport, camera, originWorld, axisLength, meshRot, out PointF originPt, out var rays);

            foreach (var ray in rays)
            {
                if (isCompactSecondary && !ray.IsPositive)
                    continue;

                float dx = ray.ScreenTip.X - originPt.X;
                float dy = ray.ScreenTip.Y - originPt.Y;
                float segLen = MathF.Sqrt(dx * dx + dy * dy);
                bool hasVisibleSpan = segLen >= 6f;

                var dragHandle = MapAxisViewToDragHandle(ray.AxisView, meshRot, out _);
                bool isHighlighted = !isCompactSecondary && highlightedHandle != TeddyAxisDragHandle.None && highlightedHandle == dragHandle;

                if (ray.IsPositive)
                {
                    int alpha = isCompactSecondary ? 155 : (isHighlighted ? 255 : 215);
                    Color lineCol = Color.FromArgb(alpha, ray.Color);

                    if (hasVisibleSpan)
                    {
                        float lineW = isCompactSecondary ? 1.8f : (isHighlighted ? 3.8f : 2.3f);
                        using (Pen haloPen = new Pen(Color.FromArgb(isHighlighted ? 225 : (isCompactSecondary ? 95 : 145), 255, 255, 255), lineW + 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (Pen axisPen = new Pen(lineCol, lineW) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(haloPen, originPt, ray.ScreenTip);
                            g.DrawLine(axisPen, originPt, ray.ScreenTip);
                        }

                        float ux = dx / segLen;
                        float uy = dy / segLen;
                        float px = -uy;
                        float py = ux;

                        if (!isCompactSecondary)
                        {
                            using (Pen tickPen = new Pen(Color.FromArgb(isHighlighted ? 245 : 185, ray.Color), isHighlighted ? 2.0f : 1.5f))
                            {
                                float tickStep = axisLength * 0.25f;
                                for (int s = 1; s <= 3; s++)
                                {
                                    float dist = tickStep * s;
                                    Vector3 tickSp = camera.ProjectToScreen(originWorld + ray.WorldDir * dist, viewport);
                                    g.DrawLine(tickPen,
                                        tickSp.X - px * 4.5f, tickSp.Y - py * 4.5f,
                                        tickSp.X + px * 4.5f, tickSp.Y + py * 4.5f);
                                }
                            }
                        }

                        // Đầu mũi tên 3D tại đỉnh trục dương
                        float arrowLen = isCompactSecondary ? 8.5f : (isHighlighted ? 13.5f : 11f);
                        float arrowHalfW = isCompactSecondary ? 3.6f : (isHighlighted ? 5.6f : 4.5f);
                        PointF arrowTip = ray.ScreenTip;
                        PointF arrowBase = new PointF(arrowTip.X - ux * arrowLen, arrowTip.Y - uy * arrowLen);
                        PointF[] arrowPoly =
                        {
                            arrowTip,
                            new PointF(arrowBase.X + px * arrowHalfW, arrowBase.Y + py * arrowHalfW),
                            new PointF(arrowBase.X - px * arrowHalfW, arrowBase.Y - py * arrowHalfW)
                        };
                        using (SolidBrush arrowBrush = new SolidBrush(lineCol))
                        {
                            g.FillPolygon(arrowBrush, arrowPoly);
                        }

                        // Huy hiệu nhãn "+X", "+Y", "+Z" đặt hơi vượt qua đầu mũi tên
                        PointF badgeCenter = new PointF(arrowTip.X + ux * (isCompactSecondary ? 11f : 14f), arrowTip.Y + uy * (isCompactSecondary ? 11f : 14f));
                        DrawWorldAxisBadge(g, badgeCenter, ray.Label, isCompactSecondary ? Color.FromArgb(190, ray.Color) : ray.Color, isPositive: true, isHighlighted: isHighlighted);
                    }
                    else if (ray.ViewDir.Z > 0f)
                    {
                        DrawWorldAxisBadge(g, new PointF(originPt.X + 16f, originPt.Y - 14f), ray.Label, isCompactSecondary ? Color.FromArgb(190, ray.Color) : ray.Color, isPositive: true, isHighlighted: isHighlighted);
                    }
                }
                else
                {
                    if (hasVisibleSpan)
                    {
                        using (Pen negPen = new Pen(Color.FromArgb(isHighlighted ? 235 : 150, ray.Color), isHighlighted ? 2.4f : 1.5f))
                        {
                            negPen.DashPattern = new float[] { 4f, 3f };
                            g.DrawLine(negPen, originPt, ray.ScreenTip);
                        }

                        if (segLen >= 20f)
                        {
                            float ux = dx / segLen;
                            float uy = dy / segLen;
                            PointF badgeCenter = new PointF(ray.ScreenTip.X + ux * 11f, ray.ScreenTip.Y + uy * 11f);
                            DrawWorldAxisBadge(g, badgeCenter, ray.Label, ray.Color, isPositive: false, isHighlighted: isHighlighted);
                        }
                    }
                    else if (ray.ViewDir.Z > 0f)
                    {
                        DrawWorldAxisBadge(g, new PointF(originPt.X + 16f, originPt.Y - 14f), ray.Label, ray.Color, isPositive: false, isHighlighted: isHighlighted);
                    }
                }

                // Vẽ 6 chấm giao điểm nhỏ tại nơi các trục X, Y, Z xuyên qua 6 mặt hộp bao của khối 3D
                if (!isCompactSecondary && hasVisibleSpan && meshBounds3D.HasValue)
                {
                    var (bMin, bMax) = meshBounds3D.Value;
                    Vector3 halfExt = (bMax - bMin) * 0.5f;
                    float faceExtent = float.MaxValue;
                    if (MathF.Abs(ray.WorldDir.X) > 1e-4f) faceExtent = MathF.Min(faceExtent, halfExt.X / MathF.Abs(ray.WorldDir.X));
                    if (MathF.Abs(ray.WorldDir.Y) > 1e-4f) faceExtent = MathF.Min(faceExtent, halfExt.Y / MathF.Abs(ray.WorldDir.Y));
                    if (MathF.Abs(ray.WorldDir.Z) > 1e-4f) faceExtent = MathF.Min(faceExtent, halfExt.Z / MathF.Abs(ray.WorldDir.Z));

                    if (faceExtent < float.MaxValue * 0.5f && faceExtent > 4f)
                    {
                        Vector3 piercePt3D = originWorld + ray.WorldDir * faceExtent;
                        Vector3 pierceSp = camera.ProjectToScreen(piercePt3D, viewport);
                        using (SolidBrush pierceFill = new SolidBrush(ray.Color))
                        using (Pen pierceBorder = new Pen(Color.White, 1.3f))
                        {
                            g.FillEllipse(pierceFill, pierceSp.X - 3.2f, pierceSp.Y - 3.2f, 6.4f, 6.4f);
                            g.DrawEllipse(pierceBorder, pierceSp.X - 3.2f, pierceSp.Y - 3.2f, 6.4f, 6.4f);
                        }
                    }
                }
            }

            // Vẽ tâm gốc tọa độ O tại tâm hình vẽ / khối 3D
            bool centerHighlighted = !isCompactSecondary && highlightedHandle == TeddyAxisDragHandle.FreeCenterO;
            float centerRadius = isCompactSecondary ? 3.8f : (centerHighlighted ? 6.8f : 4.8f);
            if (centerHighlighted)
            {
                using (SolidBrush haloFill = new SolidBrush(Color.FromArgb(65, 79, 70, 229)))
                {
                    g.FillEllipse(haloFill, originPt.X - 11f, originPt.Y - 11f, 22f, 22f);
                }
            }
            using (SolidBrush centerFill = new SolidBrush(centerHighlighted ? UITheme.AccentPrimarySubtle : Color.White))
            using (Pen centerBorder = new Pen(centerHighlighted ? UITheme.AccentPrimary : (isCompactSecondary ? Color.FromArgb(170, UITheme.TextPrimary) : UITheme.TextPrimary), centerHighlighted ? 2.6f : (isCompactSecondary ? 1.6f : 2.0f)))
            {
                g.FillEllipse(centerFill, originPt.X - centerRadius, originPt.Y - centerRadius, centerRadius * 2f, centerRadius * 2f);
                g.DrawEllipse(centerBorder, originPt.X - centerRadius, originPt.Y - centerRadius, centerRadius * 2f, centerRadius * 2f);
            }

            if (!isCompactSecondary && (originWorld.LengthSquared() > 1f || highlightedHandle != TeddyAxisDragHandle.None))
            {
                string coordText = $"O({(int)MathF.Round(originWorld.X)}, {(int)MathF.Round(originWorld.Y)}, {(int)MathF.Round(originWorld.Z)})";
                Size textSz = TextRenderer.MeasureText(coordText, UITheme.FontSmallBold);
                Rectangle coordRect = new Rectangle((int)originPt.X + 10, (int)originPt.Y + 6, textSz.Width + 6, 17);
                using (GraphicsPath coordGp = UITheme.CreateRoundedRect(coordRect, 5))
                using (SolidBrush coordBg = new SolidBrush(Color.FromArgb(228, 255, 255, 255)))
                using (Pen coordBd = new Pen(centerHighlighted ? UITheme.AccentPrimary : UITheme.BorderDefault, 1.0f))
                {
                    g.FillPath(coordBg, coordGp);
                    g.DrawPath(coordBd, coordGp);
                }
                TextRenderer.DrawText(g, coordText, UITheme.FontSmallBold, coordRect,
                    centerHighlighted ? UITheme.AccentPrimary : UITheme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else if (!isCompactSecondary)
            {
                Rectangle oRect = new Rectangle((int)originPt.X - 18, (int)originPt.Y + 4, 16, 15);
                using (SolidBrush oBg = new SolidBrush(Color.FromArgb(205, 255, 255, 255)))
                {
                    g.FillEllipse(oBg, oRect);
                }
                TextRenderer.DrawText(g, "O", UITheme.FontSmallBold, oRect, UITheme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            g.SmoothingMode = oldSmooth;
        }

        private static void DrawWorldAxisBadge(Graphics g, PointF center, string label, Color axisColor, bool isPositive, bool isHighlighted = false)
        {
            if (isPositive)
            {
                Rectangle badgeRect = new Rectangle((int)MathF.Round(center.X - 12f), (int)MathF.Round(center.Y - 9f), 24, 18);
                using (GraphicsPath gp = UITheme.CreateRoundedRect(badgeRect, 9))
                using (SolidBrush fill = new SolidBrush(axisColor))
                using (Pen border = new Pen(isHighlighted ? UITheme.TextPrimary : Color.White, isHighlighted ? 2.0f : 1.4f))
                {
                    g.FillPath(fill, gp);
                    g.DrawPath(border, gp);
                }
                TextRenderer.DrawText(g, label, UITheme.FontSmallBold, badgeRect, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Rectangle badgeRect = new Rectangle((int)MathF.Round(center.X - 11f), (int)MathF.Round(center.Y - 8f), 22, 16);
                using (GraphicsPath gp = UITheme.CreateRoundedRect(badgeRect, 8))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(isHighlighted ? 250 : 228, 255, 255, 255)))
                using (Pen border = new Pen(isHighlighted ? axisColor : Color.FromArgb(165, axisColor), isHighlighted ? 1.8f : 1.1f))
                {
                    g.FillPath(fill, gp);
                    g.DrawPath(border, gp);
                }
                TextRenderer.DrawText(g, label, UITheme.FontSmallBold, badgeRect, axisColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private static void GetCornerGizmoLayout(
            Size viewport,
            VirtualTrackballCamera camera,
            Quaternion? activeObjectRotation,
            out Rectangle cardBounds,
            out PointF gizmoCenter,
            out Rectangle isoButtonRect,
            out List<AxisRayItem> sortedAxes)
        {
            cardBounds = new Rectangle(Math.Max(10, viewport.Width - 130), 10, 120, 138);
            gizmoCenter = new PointF(cardBounds.X + cardBounds.Width * 0.5f, cardBounds.Y + 64f);
            isoButtonRect = new Rectangle(cardBounds.X + 14, cardBounds.Bottom - 24, cardBounds.Width - 28, 18);
            const float axisRadius = 38f;

            Quaternion combinedRot = Quaternion.Normalize(camera.Rotation * (activeObjectRotation ?? Quaternion.Identity));

            sortedAxes = new List<AxisRayItem>(6);
            foreach (var def in AxisDefinitions)
            {
                Vector3 vd = Vector3.Normalize(Vector3.Transform(def.WorldDir, combinedRot));
                sortedAxes.Add(new AxisRayItem
                {
                    AxisView = def.AxisView,
                    Label = def.IsPositive ? def.Label.TrimStart('+') : def.Label,
                    WorldDir = def.WorldDir,
                    Color = def.Color,
                    IsPositive = def.IsPositive,
                    ViewDir = vd,
                    ScreenTip = new PointF(gizmoCenter.X + vd.X * axisRadius, gizmoCenter.Y - vd.Y * axisRadius)
                });
            }

            // Xa trước -> gần sau
            sortedAxes.Sort((a, b) => a.ViewDir.Z.CompareTo(b.ViewDir.Z));
        }

        /// <summary>
        /// Kiểm tra chuột có đang nằm trên La bàn Trục 3D (hoặc nút đầu trục X/Y/Z/ISO) ở góc trên phải hay không.
        /// </summary>
        public static bool HitTestCornerAxisGizmo(
            Size viewport,
            VirtualTrackballCamera camera,
            Point mousePos,
            out TeddyAxisSnapView hitAxis,
            out Rectangle cardBounds,
            Quaternion? activeObjectRotation = null)
        {
            hitAxis = TeddyAxisSnapView.None;
            if (viewport.Width < 160 || viewport.Height < 170)
            {
                cardBounds = Rectangle.Empty;
                return false;
            }

            GetCornerGizmoLayout(viewport, camera, activeObjectRotation, out cardBounds, out _, out Rectangle isoButtonRect, out var sortedAxes);

            if (!cardBounds.Contains(mousePos))
                return false;

            if (isoButtonRect.Contains(mousePos))
            {
                hitAxis = TeddyAxisSnapView.Isometric;
                return true;
            }

            // Kiểm tra từ gần đến xa (reverse Z-order) để ưu tiên chọn đầu trục phía trước nếu 2 đầu trùng nhau
            for (int i = sortedAxes.Count - 1; i >= 0; i--)
            {
                var axis = sortedAxes[i];
                float hitRadius = axis.IsPositive ? 12.5f : 10f;
                float dx = mousePos.X - axis.ScreenTip.X;
                float dy = mousePos.Y - axis.ScreenTip.Y;
                if (dx * dx + dy * dy <= hitRadius * hitRadius)
                {
                    hitAxis = axis.AxisView;
                    return true;
                }
            }

            return true;
        }

        /// <summary>
        /// Vẽ La bàn Trục 3D Oxyz (Orientation Gizmo) ở góc trên phải Viewport để nhận biết và xoay nhanh hình/camera theo trục X/Y/Z.
        /// </summary>
        public static void DrawCornerAxisGizmo3D(
            Graphics g,
            Size viewport,
            VirtualTrackballCamera camera,
            Point mousePos,
            bool isMouseInViewport,
            out TeddyAxisSnapView hoveredAxis,
            Quaternion? activeObjectRotation = null)
        {
            hoveredAxis = TeddyAxisSnapView.None;
            if (viewport.Width < 160 || viewport.Height < 170) return;

            SmoothingMode oldSmooth = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            GetCornerGizmoLayout(viewport, camera, activeObjectRotation, out Rectangle cardBounds, out PointF gizmoCenter, out Rectangle isoButtonRect, out var sortedAxes);

            if (isMouseInViewport && HitTestCornerAxisGizmo(viewport, camera, mousePos, out TeddyAxisSnapView hit, out _, activeObjectRotation))
            {
                hoveredAxis = hit;
            }

            // Nền thẻ La bàn bán trong suốt (Bright Studio Light)
            using (GraphicsPath cardPath = UITheme.CreateRoundedRect(cardBounds, 10))
            using (SolidBrush cardBg = new SolidBrush(Color.FromArgb(238, 255, 255, 255)))
            using (Pen cardBorder = new Pen(UITheme.BorderDefault, 1.2f))
            {
                g.FillPath(cardBg, cardPath);
                g.DrawPath(cardBorder, cardPath);
            }

            // Tiêu đề thẻ
            Rectangle headerRect = new Rectangle(cardBounds.X + 4, cardBounds.Y + 4, cardBounds.Width - 8, 16);
            TextRenderer.DrawText(g, "HỆ TRỤC 3D OXYZ", UITheme.FontGroupHeader, headerRect, UITheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Vòng tròn quỹ đạo mảnh quanh tâm La bàn
            const float dialRadius = 43f;
            using (SolidBrush dialFill = new SolidBrush(Color.FromArgb(145, 241, 245, 249)))
            using (Pen dialBorder = new Pen(Color.FromArgb(120, 203, 213, 225), 1.0f))
            {
                g.FillEllipse(dialFill, gizmoCenter.X - dialRadius, gizmoCenter.Y - dialRadius, dialRadius * 2f, dialRadius * 2f);
                g.DrawEllipse(dialBorder, gizmoCenter.X - dialRadius, gizmoCenter.Y - dialRadius, dialRadius * 2f, dialRadius * 2f);
            }

            // Vẽ 6 đầu trục sắp xếp theo chiều sâu Z
            foreach (var axis in sortedAxes)
            {
                bool isHovered = hoveredAxis == axis.AxisView;

                if (axis.IsPositive)
                {
                    using (Pen shaftPen = new Pen(axis.Color, isHovered ? 3.2f : 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(shaftPen, gizmoCenter, axis.ScreenTip);
                    }

                    float r = isHovered ? 12.2f : 11f;
                    RectangleF nodeRect = new RectangleF(axis.ScreenTip.X - r, axis.ScreenTip.Y - r, r * 2f, r * 2f);
                    using (SolidBrush nodeFill = new SolidBrush(axis.Color))
                    using (Pen nodeBorder = new Pen(isHovered ? UITheme.TextPrimary : Color.White, isHovered ? 2.0f : 1.5f))
                    {
                        g.FillEllipse(nodeFill, nodeRect);
                        g.DrawEllipse(nodeBorder, nodeRect);
                    }

                    TextRenderer.DrawText(g, axis.Label, UITheme.FontSmallBold, Rectangle.Round(nodeRect), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    using (Pen negShaftPen = new Pen(Color.FromArgb(120, axis.Color), 1.4f))
                    {
                        negShaftPen.DashPattern = new float[] { 3f, 2f };
                        g.DrawLine(negShaftPen, gizmoCenter, axis.ScreenTip);
                    }

                    float r = isHovered ? 9.5f : 8.5f;
                    RectangleF nodeRect = new RectangleF(axis.ScreenTip.X - r, axis.ScreenTip.Y - r, r * 2f, r * 2f);
                    using (SolidBrush nodeFill = new SolidBrush(isHovered ? Color.FromArgb(255, 241, 245, 249) : Color.FromArgb(242, 255, 255, 255)))
                    using (Pen nodeBorder = new Pen(axis.Color, isHovered ? 1.8f : 1.2f))
                    {
                        g.FillEllipse(nodeFill, nodeRect);
                        g.DrawEllipse(nodeBorder, nodeRect);
                    }

                    TextRenderer.DrawText(g, axis.Label, UITheme.FontGroupHeader, Rectangle.Round(nodeRect), axis.Color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            // Chấm tâm O ở giữa La bàn
            using (SolidBrush centerDot = new SolidBrush(UITheme.TextPrimary))
            {
                g.FillEllipse(centerDot, gizmoCenter.X - 3f, gizmoCenter.Y - 3f, 6f, 6f);
            }

            // Nút "Góc 3D (ISO)" ở đáy thẻ La bàn
            bool isoHover = hoveredAxis == TeddyAxisSnapView.Isometric;
            using (GraphicsPath isoPath = UITheme.CreateRoundedRect(isoButtonRect, 5))
            using (SolidBrush isoBg = new SolidBrush(isoHover ? UITheme.AccentPrimarySubtle : UITheme.BgSurface))
            using (Pen isoBorder = new Pen(isoHover ? UITheme.AccentPrimary : UITheme.BorderDefault, 1.0f))
            {
                g.FillPath(isoBg, isoPath);
                g.DrawPath(isoBorder, isoPath);
            }
            TextRenderer.DrawText(g, "Góc 3D (ISO)", UITheme.FontGroupHeader, isoButtonRect,
                isoHover ? UITheme.AccentPrimary : UITheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            g.SmoothingMode = oldSmooth;
        }
    }
}
