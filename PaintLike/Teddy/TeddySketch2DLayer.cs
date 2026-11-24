using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Công cụ thao tác trên Lớp Phác Thảo 2D (Stage A trước khi click nút thực thi thuật toán 3D).
    /// </summary>
    public enum Sketch2DToolMode
    {
        DrawStroke,     // Vẽ nét phác thảo 2D bằng Chuột Trái
        EraseStroke     // Tẩy xóa nét nháp 2D bằng Chuột Trái
    }

    /// <summary>
    /// Một nét phác thảo 2D tạm thời trong bộ đệm trước khi người dùng click nút chức năng 3D.
    /// </summary>
    public class SketchStroke2D
    {
        public List<PointF> Points { get; set; } = new List<PointF>();
        public Color StrokeColor { get; set; } = Color.FromArgb(37, 99, 235);
        public float StrokeWidth { get; set; } = 3.0f;
        public bool IsClosed { get; set; } = false;
        public bool IsSelfIntersecting { get; set; } = false;
        public TeddyCutPattern CutPattern { get; set; } = TeddyCutPattern.Freehand;
        public Vector2 CutRemoveNormal { get; set; } = new Vector2(0, -1);
        public string Label { get; set; } = "Nét #1";

        public void RecalculateProperties()
        {
            if (Points == null || Points.Count < 2)
            {
                IsClosed = false;
                IsSelfIntersecting = false;
                return;
            }

            if (CutPattern != TeddyCutPattern.Freehand)
            {
                IsClosed = false;
                IsSelfIntersecting = false;
                return;
            }

            float endToStart = StrokeProcessor.Distance(Points[0], Points[Points.Count - 1]);
            IsClosed = endToStart < 3.0f || StrokeProcessor.IsClosedSurfaceStroke(Points);
            IsSelfIntersecting = Points.Count >= 4 && StrokeProcessor.IsSelfIntersecting(Points, IsClosed);
        }

        public SketchStroke2D Clone()
        {
            return new SketchStroke2D
            {
                Points = new List<PointF>(this.Points),
                StrokeColor = this.StrokeColor,
                StrokeWidth = this.StrokeWidth,
                IsClosed = this.IsClosed,
                IsSelfIntersecting = this.IsSelfIntersecting,
                CutPattern = this.CutPattern,
                CutRemoveNormal = this.CutRemoveNormal,
                Label = this.Label
            };
        }
    }

    /// <summary>
    /// [TV3-06B] Lớp Phác Thảo 2D Độc Lập (Persistent 2D Sketch Overlay Buffer).
    /// Quản lý toàn bộ nét vẽ nháp 2D khi kéo Chuột Trái, kiểm tra tự cắt, làm mượt/tẩy/nối kín 2D
    /// và hiển thị trực quan đè lên không gian 3D trước khi người dùng click nút áp dụng thuật toán 3D.
    /// </summary>
    public class TeddySketch2DLayer
    {
        private int _nextStrokeSeq = 1;

        public List<SketchStroke2D> Strokes { get; } = new List<SketchStroke2D>();
        public SketchStroke2D? SelectedStroke { get; set; } = null;

        public bool HasStrokes => Strokes.Count > 0;

        public SketchStroke2D? AddRawStroke(
            IReadOnlyList<PointF> rawPoints,
            Color color,
            float width,
            TeddyCutPattern cutPattern,
            float cutAmp,
            int cutPeriods,
            bool flipCut)
        {
            if (rawPoints == null || rawPoints.Count < 2)
                return null;

            float totalLen = StrokeProcessor.ComputePolylineLength(rawPoints, false);
            if (totalLen < 10f)
                return null;

            List<PointF> processedPts;
            Vector2 removeNorm = new Vector2(0, -1);

            if (cutPattern != TeddyCutPattern.Freehand)
            {
                processedPts = TeddyCutPatternGenerator.BuildPatternedCutPolyline(
                    rawPoints,
                    cutPattern,
                    cutAmp,
                    cutPeriods,
                    flipCut,
                    out _,
                    out _,
                    out removeNorm);

                if (processedPts.Count < 2)
                    return null;
            }
            else
            {
                // Làm mượt nhẹ và giữ mật độ điểm đồng đều cho nét vẽ tự do 2D
                var resampled = StrokeProcessor.ResampleUniform(rawPoints, 6.0f, false);
                processedPts = Smooth2DPolyline(resampled, 1);
                if (processedPts.Count < 2)
                    processedPts = new List<PointF>(rawPoints);
            }

            var stroke = new SketchStroke2D
            {
                Points = processedPts,
                StrokeColor = cutPattern != TeddyCutPattern.Freehand ? Color.FromArgb(225, 29, 72) : color,
                StrokeWidth = width,
                CutPattern = cutPattern,
                CutRemoveNormal = removeNorm,
                Label = cutPattern != TeddyCutPattern.Freehand
                    ? $"Cắt #{_nextStrokeSeq} ({TeddyCutPatternGenerator.GetPatternDisplayName(cutPattern)})"
                    : $"Nét #{_nextStrokeSeq}"
            };
            _nextStrokeSeq++;
            stroke.RecalculateProperties();

            Strokes.Add(stroke);
            SelectedStroke = stroke;
            return stroke;
        }

        public SketchStroke2D? HitTest(PointF pt, float threshold = 11f)
        {
            SketchStroke2D? best = null;
            float bestDist = threshold;

            for (int idx = Strokes.Count - 1; idx >= 0; idx--)
            {
                var s = Strokes[idx];
                if (s.Points.Count < 2) continue;

                float minD = MinDistanceToStroke(pt, s.Points, s.IsClosed);
                if (minD <= bestDist)
                {
                    bestDist = minD;
                    best = s;
                }
            }

            return best;
        }

        public bool EraseAt(PointF pt, float radius = 16f)
        {
            bool changed = false;
            for (int i = Strokes.Count - 1; i >= 0; i--)
            {
                var s = Strokes[i];
                if (MinDistanceToStroke(pt, s.Points, s.IsClosed) <= radius)
                {
                    if (SelectedStroke == s) SelectedStroke = null;
                    Strokes.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        public bool CloseSelectedOrLastStroke()
        {
            var target = SelectedStroke ?? (Strokes.Count > 0 ? Strokes[Strokes.Count - 1] : null);
            if (target == null || target.Points.Count < 3) return false;

            target.CutPattern = TeddyCutPattern.Freehand;
            PointF first = target.Points[0];
            PointF last = target.Points[target.Points.Count - 1];
            if (StrokeProcessor.Distance(first, last) > 2.0f)
            {
                // Nối thêm đoạn đóng từ điểm cuối về điểm đầu một cách mượt mà
                var bridge = TeddyCutPatternGenerator.SubdivideSharpPolyline(new[] { last, first }, 8f);
                for (int i = 1; i < bridge.Count; i++)
                    target.Points.Add(bridge[i]);
            }

            target.IsClosed = true;
            target.IsSelfIntersecting = StrokeProcessor.IsSelfIntersecting(target.Points, true);
            return true;
        }

        public bool SmoothSelectedOrLastStroke()
        {
            var target = SelectedStroke ?? (Strokes.Count > 0 ? Strokes[Strokes.Count - 1] : null);
            if (target == null || target.Points.Count < 4) return false;

            target.Points = Smooth2DPolyline(target.Points, 2);
            target.RecalculateProperties();
            return true;
        }

        public bool RemoveSelectedOrLastStroke()
        {
            if (SelectedStroke != null && Strokes.Remove(SelectedStroke))
            {
                SelectedStroke = Strokes.Count > 0 ? Strokes[Strokes.Count - 1] : null;
                return true;
            }
            if (Strokes.Count > 0)
            {
                Strokes.RemoveAt(Strokes.Count - 1);
                SelectedStroke = Strokes.Count > 0 ? Strokes[Strokes.Count - 1] : null;
                return true;
            }
            return false;
        }

        public void Clear()
        {
            Strokes.Clear();
            SelectedStroke = null;
        }

        public List<SketchStroke2D> CloneStrokes()
        {
            var list = new List<SketchStroke2D>(Strokes.Count);
            foreach (var s in Strokes) list.Add(s.Clone());
            return list;
        }

        public void RestoreStrokes(IEnumerable<SketchStroke2D> source)
        {
            Strokes.Clear();
            if (source != null)
            {
                foreach (var s in source) Strokes.Add(s.Clone());
            }
            SelectedStroke = Strokes.Count > 0 ? Strokes[Strokes.Count - 1] : null;
            _nextStrokeSeq = Math.Max(_nextStrokeSeq, Strokes.Count + 1);
        }

        public void RenderOverlay(
            Graphics g,
            IReadOnlyList<PointF>? activeDragStroke,
            Color activeColor,
            Sketch2DToolMode toolMode,
            Point mousePos,
            bool isMouseInside,
            TeddyCutPattern activeCutPattern,
            float cutAmp,
            int cutPeriods,
            bool flipCut)
        {
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Vẽ tất cả các nét nháp 2D đã lưu trong bộ đệm
            for (int idx = 0; idx < Strokes.Count; idx++)
            {
                var stroke = Strokes[idx];
                if (stroke.Points.Count < 2) continue;

                bool isSelected = stroke == SelectedStroke;
                bool isCut = stroke.CutPattern != TeddyCutPattern.Freehand;

                // Nếu nét được chọn: Vẽ hào quang Cyan + Bounding box
                if (isSelected)
                {
                    using (Pen glowPen = new Pen(Color.FromArgb(90, 6, 182, 212), stroke.StrokeWidth + 7f))
                    {
                        glowPen.StartCap = LineCap.Round;
                        glowPen.EndCap = LineCap.Round;
                        glowPen.LineJoin = LineJoin.Round;
                        g.DrawLines(glowPen, stroke.Points.ToArray());
                    }

                    DrawStrokeBoundingBox(g, stroke.Points);
                }

                // Nếu nét tự cắt nhau: Vẽ nền cảnh báo đỏ cam
                Color drawColor = stroke.IsSelfIntersecting
                    ? Color.FromArgb(225, 29, 72)
                    : stroke.StrokeColor;

                using (Pen p = new Pen(drawColor, stroke.StrokeWidth))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    p.LineJoin = LineJoin.Round;
                    if (isCut)
                    {
                        p.DashPattern = new float[] { 4.0f, 2.8f };
                    }
                    else if (stroke.IsSelfIntersecting)
                    {
                        p.DashPattern = new float[] { 2.5f, 2.0f };
                    }
                    g.DrawLines(p, stroke.Points.ToArray());
                }

                // Nếu nét là nét đóng gần khép kín nhưng chưa nối khít điểm đầu cuối -> Vẽ nét đứt mảnh gợi ý khép kín
                if (stroke.IsClosed && !isCut && stroke.Points.Count > 2)
                {
                    PointF first = stroke.Points[0];
                    PointF last = stroke.Points[stroke.Points.Count - 1];
                    if (StrokeProcessor.Distance(first, last) > 3f)
                    {
                        using (Pen closeHint = new Pen(Color.FromArgb(140, drawColor), 1.8f))
                        {
                            closeHint.DashPattern = new float[] { 3f, 3f };
                            g.DrawLine(closeHint, last, first);
                        }
                    }
                }

                // Nếu là mẫu cắt: Vẽ mũi tên hướng phần cắt
                if (isCut && stroke.Points.Count >= 2)
                {
                    PointF mid = stroke.Points[stroke.Points.Count / 2];
                    PointF arrowTip = new PointF(
                        mid.X + stroke.CutRemoveNormal.X * 22f,
                        mid.Y + stroke.CutRemoveNormal.Y * 22f);
                    using (Pen arrPen = new Pen(Color.FromArgb(225, 29, 72), 2.0f))
                    {
                        arrPen.EndCap = LineCap.ArrowAnchor;
                        g.DrawLine(arrPen, mid, arrowTip);
                    }
                }

                // Điểm đầu nét & nhãn đánh số thứ tự (#1, #2...) + cảnh báo tự cắt nếu có
                PointF startPt = stroke.Points[0];
                using (SolidBrush dotBrush = new SolidBrush(drawColor))
                using (Pen whiteBorder = new Pen(Color.White, 1.5f))
                {
                    g.FillEllipse(dotBrush, startPt.X - 4.5f, startPt.Y - 4.5f, 9f, 9f);
                    g.DrawEllipse(whiteBorder, startPt.X - 4.5f, startPt.Y - 4.5f, 9f, 9f);
                }

                string badge = stroke.IsSelfIntersecting
                    ? $"{stroke.Label} (⚠ Tự cắt)"
                    : (stroke.IsClosed && !isCut ? $"{stroke.Label} (Kín)" : stroke.Label);
                DrawTinyBadge(g, new Point((int)startPt.X + 8, (int)startPt.Y - 18), badge,
                    stroke.IsSelfIntersecting ? Color.FromArgb(225, 29, 72) : Color.FromArgb(30, 58, 95));
            }

            // 2. Vẽ nét đang kéo trực tiếp bằng Chuột Trái
            if (activeDragStroke != null && activeDragStroke.Count >= 2)
            {
                if (activeCutPattern != TeddyCutPattern.Freehand)
                {
                    List<PointF> patternPoly = TeddyCutPatternGenerator.BuildPatternedCutPolyline(
                        activeDragStroke,
                        activeCutPattern,
                        cutAmp,
                        cutPeriods,
                        flipCut,
                        out PointF sp,
                        out PointF ep,
                        out Vector2 rn);

                    if (patternPoly.Count >= 2)
                    {
                        using (Pen guidePen = new Pen(Color.FromArgb(120, 100, 116, 139), 1.3f))
                        {
                            guidePen.DashPattern = new float[] { 4f, 4f };
                            g.DrawLine(guidePen, sp, ep);
                        }

                        Color cutCol = Color.FromArgb(225, 29, 72);
                        using (Pen glow = new Pen(Color.FromArgb(65, 239, 68, 68), 6.5f))
                        {
                            glow.StartCap = LineCap.Round;
                            glow.EndCap = LineCap.Round;
                            glow.LineJoin = LineJoin.Round;
                            g.DrawLines(glow, patternPoly.ToArray());
                        }
                        using (Pen cutPen = new Pen(cutCol, 2.8f))
                        {
                            cutPen.StartCap = LineCap.Round;
                            cutPen.EndCap = LineCap.Round;
                            cutPen.LineJoin = LineJoin.Round;
                            cutPen.DashPattern = new float[] { 4.0f, 2.8f };
                            g.DrawLines(cutPen, patternPoly.ToArray());
                        }
                    }
                }
                else
                {
                    bool liveSelfIntersect = activeDragStroke.Count >= 6 && StrokeProcessor.IsSelfIntersecting(activeDragStroke, false);
                    Color liveCol = liveSelfIntersect ? Color.FromArgb(225, 29, 72) : activeColor;
                    using (Pen livePen = new Pen(liveCol, 3.0f))
                    {
                        livePen.StartCap = LineCap.Round;
                        livePen.EndCap = LineCap.Round;
                        livePen.LineJoin = LineJoin.Round;
                        PointF[] arr = new PointF[activeDragStroke.Count];
                        for (int i = 0; i < activeDragStroke.Count; i++) arr[i] = activeDragStroke[i];
                        g.DrawLines(livePen, arr);
                    }

                    PointF s0 = activeDragStroke[0];
                    using (SolidBrush sb = new SolidBrush(liveCol))
                        g.FillEllipse(sb, s0.X - 4f, s0.Y - 4f, 8f, 8f);

                    if (liveSelfIntersect)
                    {
                        PointF last = activeDragStroke[activeDragStroke.Count - 1];
                        DrawTinyBadge(g, new Point((int)last.X + 10, (int)last.Y - 22), "⚠ Nét tự cắt nhau!", Color.FromArgb(225, 29, 72));
                    }
                }
            }

            // 3. Nếu đang dùng Tẩy 2D, vẽ vòng tròn đầu tẩy theo con trỏ chuột
            if (toolMode == Sketch2DToolMode.EraseStroke && isMouseInside)
            {
                using (Pen eraserPen = new Pen(Color.FromArgb(225, 29, 72), 1.6f))
                using (SolidBrush eraserFill = new SolidBrush(Color.FromArgb(35, 225, 29, 72)))
                {
                    eraserPen.DashPattern = new float[] { 3f, 2f };
                    RectangleF erRect = new RectangleF(mousePos.X - 16f, mousePos.Y - 16f, 32f, 32f);
                    g.FillEllipse(eraserFill, erRect);
                    g.DrawEllipse(eraserPen, erRect);
                }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawStrokeBoundingBox(Graphics g, List<PointF> pts)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            float pad = 7f;
            using (Pen boxPen = new Pen(Color.FromArgb(6, 182, 212), 1.3f))
            {
                boxPen.DashPattern = new float[] { 4f, 3f };
                g.DrawRectangle(boxPen, minX - pad, minY - pad, (maxX - minX) + pad * 2f, (maxY - minY) + pad * 2f);
            }
        }

        private static void DrawTinyBadge(Graphics g, Point pos, string text, Color textColor)
        {
            Size sz = TextRenderer.MeasureText(text, UITheme.FontSmallBold);
            Rectangle r = new Rectangle(pos.X, pos.Y, sz.Width + 8, 17);
            using (GraphicsPath gp = UITheme.CreateRoundedRect(r, 4))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(232, 255, 255, 255)))
            using (Pen bd = new Pen(Color.FromArgb(140, textColor), 1f))
            {
                g.FillPath(bg, gp);
                g.DrawPath(bd, gp);
            }
            TextRenderer.DrawText(g, text, UITheme.FontSmallBold, r, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static float MinDistanceToStroke(PointF pt, List<PointF> pts, bool closed)
        {
            if (pts == null || pts.Count == 0) return float.MaxValue;
            if (pts.Count == 1) return StrokeProcessor.Distance(pt, pts[0]);

            float minDist = float.MaxValue;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float d = DistanceToSegment(pt, pts[i], pts[i + 1]);
                if (d < minDist) minDist = d;
            }
            if (closed && pts.Count > 2)
            {
                float d = DistanceToSegment(pt, pts[pts.Count - 1], pts[0]);
                if (d < minDist) minDist = d;
            }
            return minDist;
        }

        private static float DistanceToSegment(PointF pt, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-5f) return StrokeProcessor.Distance(pt, a);
            float t = Math.Clamp(((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / lenSq, 0f, 1f);
            return StrokeProcessor.Distance(pt, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        private static List<PointF> Smooth2DPolyline(IReadOnlyList<PointF> pts, int iterations)
        {
            if (pts == null || pts.Count <= 3)
                return pts == null ? new List<PointF>() : new List<PointF>(pts);

            var curr = new List<PointF>(pts);
            for (int iter = 0; iter < iterations; iter++)
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
}
