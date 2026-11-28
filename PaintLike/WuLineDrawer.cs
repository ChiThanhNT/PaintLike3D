using System.Drawing.Drawing2D;

namespace PaintLike
{
    public class WuLineDrawer
    {
        // ==========================================
        // PHẦN 1: CÁC HÀM CỐT LÕI (PLOT & BLEND)
        // ==========================================

        private static void Plot(Bitmap bmp, int x, int y, double brightness, Color color)
        {
            // Kiểm tra toạ độ
            if (x < 0 || x >= bmp.Width || y < 0 || y >= bmp.Height) return;

            double clampedBrightness = Math.Max(0.0, Math.Min(1.0, brightness));
            int alpha = (int)Math.Round(clampedBrightness * color.A);
            alpha = Math.Min(255, Math.Max(0, alpha));
            if (alpha <= 0) return;

            // Màu nền hiện tại tại điểm ảnh
            Color backColor = bmp.GetPixel(x, y);
            Color finalColor = Blend(color, backColor, alpha);
            bmp.SetPixel(x, y, finalColor);
        }

        // Trộn Alpha chuẩn SourceOver (hỗ trợ cả nền trong suốt trên Layer)
        private static Color Blend(Color fore, Color back, int alpha)
        {
            if (alpha <= 0) return back;
            if (back.A == 0) return Color.FromArgb(alpha, fore.R, fore.G, fore.B);
            if (alpha >= 255) return Color.FromArgb(255, fore.R, fore.G, fore.B);

            float srcA = alpha / 255f;
            float dstA = back.A / 255f;
            float outA = srcA + dstA * (1.0f - srcA);
            if (outA <= 0f) return Color.Transparent;

            int r = Math.Clamp((int)Math.Round((fore.R * srcA + back.R * dstA * (1.0f - srcA)) / outA), 0, 255);
            int g = Math.Clamp((int)Math.Round((fore.G * srcA + back.G * dstA * (1.0f - srcA)) / outA), 0, 255);
            int b = Math.Clamp((int)Math.Round((fore.B * srcA + back.B * dstA * (1.0f - srcA)) / outA), 0, 255);
            int a = Math.Clamp((int)Math.Round(outA * 255f), 0, 255);

            return Color.FromArgb(a, r, g, b);
        }

        private static Graphics CreateAaGraphics(Bitmap bmp)
        {
            Graphics g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        private static Pen CreateThickPen(Color color, int thickness)
        {
            return new Pen(color, thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }

        private static void DrawDot(Graphics g, Color color, float x, float y, int thickness)
        {
            float half = thickness / 2f;
            using (SolidBrush b = new(color))
            {
                g.FillEllipse(b, x - half, y - half, thickness, thickness);
            }
        }

        private static int IPart(double x) => (int)Math.Floor(x);
        private static int Round(double x) => (int)Math.Round(x);
        private static double FPart(double x) => x - Math.Floor(x);
        private static double RFPart(double x) => 1.0 - FPart(x);

        
        // Vẽ đường thẳng khử răng cưa (Wu's Line Algorithm)
        public static void DrawLine(Bitmap bmp, int x0, int y0, int x1, int y1, Color color)
        {
            bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);

            if (steep) { (x0, y0) = (y0, x0); (x1, y1) = (y1, x1); }
            if (x0 > x1) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); }

            double dx = x1 - x0;
            double dy = y1 - y0;
            double gradient = (dx == 0) ? 1.0 : dy / dx;

    
            int xend = Round(x0);
            double yend = y0 + gradient * (xend - x0);
            double xgap = RFPart(x0 + 0.5);
            int xpxl1 = xend;
            int ypxl1 = IPart(yend);

            if (steep)
            {
                Plot(bmp, ypxl1, xpxl1, RFPart(yend) * xgap, color);
                Plot(bmp, ypxl1 + 1, xpxl1, FPart(yend) * xgap, color);
            }
            else
            {
                Plot(bmp, xpxl1, ypxl1, RFPart(yend) * xgap, color);
                Plot(bmp, xpxl1, ypxl1 + 1, FPart(yend) * xgap, color);
            }

            double intery = yend + gradient;

       
            xend = Round(x1);
            yend = y1 + gradient * (xend - x1);
            xgap = FPart(x1 + 0.5);
            int xpxl2 = xend;
            int ypxl2 = IPart(yend);

            if (steep)
            {
                Plot(bmp, ypxl2, xpxl2, RFPart(yend) * xgap, color);
                Plot(bmp, ypxl2 + 1, xpxl2, FPart(yend) * xgap, color);
            }
            else
            {
                Plot(bmp, xpxl2, ypxl2, RFPart(yend) * xgap, color);
                Plot(bmp, xpxl2, ypxl2 + 1, FPart(yend) * xgap, color);
            }

   
            if (steep)
            {
                for (int x = xpxl1 + 1; x < xpxl2; x++)
                {
                    Plot(bmp, IPart(intery), x, RFPart(intery), color);
                    Plot(bmp, IPart(intery) + 1, x, FPart(intery), color);
                    intery += gradient;
                }
            }
            else
            {
                for (int x = xpxl1 + 1; x < xpxl2; x++)
                {
                    Plot(bmp, x, IPart(intery), RFPart(intery), color);
                    Plot(bmp, x, IPart(intery) + 1, FPart(intery), color);
                    intery += gradient;
                }
            }
        }

       
        /// Vẽ đường thẳng có độ dày (Phóng to nét vẽ)
        public static void DrawThickLine(Bitmap bmp, int x0, int y0, int x1, int y1, Color color, int thickness)
        {
            if (thickness <= 1)
            {
                DrawLine(bmp, x0, y0, x1, y1, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (x0 == x1 && y0 == y1)
                {
                    DrawDot(g, color, x0, y0, thickness);
                }
                else
                {
                    g.DrawLine(p, x0, y0, x1, y1);
                }
            }
        }

        /// Vẽ hình chữ nhật
        public static void DrawRectangle(Bitmap bmp, Rectangle rect, Color color)
        {
            DrawLine(bmp, rect.Left, rect.Top, rect.Right, rect.Top, color);
            DrawLine(bmp, rect.Right, rect.Top, rect.Right, rect.Bottom, color);
            DrawLine(bmp, rect.Right, rect.Bottom, rect.Left, rect.Bottom, color);
            DrawLine(bmp, rect.Left, rect.Bottom, rect.Left, rect.Top, color);
        }

        /// Thêm độ dày cho hình chữ nhật
        public static void DrawThickRectangle(Bitmap bmp, Rectangle rect, Color color, int thickness)
        {
            if (thickness <= 1)
            {
                DrawRectangle(bmp, rect, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (rect.Width <= 0 && rect.Height <= 0)
                    DrawDot(g, color, rect.X, rect.Y, thickness);
                else if (rect.Width <= 0 || rect.Height <= 0)
                    g.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Bottom);
                else
                    g.DrawRectangle(p, rect);
            }
        }

        /// Vẽ đa giác (Dùng cho Tam giác, Ngôi sao, Hình thang)
        public static void DrawPolygon(Bitmap bmp, Point[] points, Color color)
        {
            if (points.Length < 2) return;
            for (int i = 0; i < points.Length - 1; i++)
            {
                DrawLine(bmp, points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, color);
            }
            // Nối điểm cuối về đầu
            DrawLine(bmp, points[points.Length - 1].X, points[points.Length - 1].Y, points[0].X, points[0].Y, color);
        }

        /// Vẽ đa giác có độ dày (Dùng cho Tam giác, Ngôi sao, Hình thang)
        public static void DrawThickPolygon(Bitmap bmp, Point[] points, Color color, int thickness)
        {
            if (points == null || points.Length == 0) return;
            if (thickness <= 1)
            {
                DrawPolygon(bmp, points, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (points.Length == 1)
                {
                    DrawDot(g, color, points[0].X, points[0].Y, thickness);
                }
                else if (points.Length == 2)
                {
                    if (points[0] == points[1])
                        DrawDot(g, color, points[0].X, points[0].Y, thickness);
                    else
                        g.DrawLine(p, points[0], points[1]);
                }
                else
                {
                    bool allSame = true;
                    for (int i = 1; i < points.Length; i++)
                    {
                        if (points[i] != points[0]) { allSame = false; break; }
                    }
                    if (allSame)
                        DrawDot(g, color, points[0].X, points[0].Y, thickness);
                    else
                        g.DrawPolygon(p, points);
                }
            }
        }

        // VẼ HÌNH TRÒN WU (Dùng để vẽ Ellipse, Hình tròn)
        private static void PlotCirclePoints(Bitmap bmp, int cx, int cy, int x, int y, double alpha, Color color)
        {
            Plot(bmp, cx + x, cy + y, alpha, color);
            Plot(bmp, cx + x, cy - y, alpha, color);
            Plot(bmp, cx - x, cy + y, alpha, color);
            Plot(bmp, cx - x, cy - y, alpha, color);
            Plot(bmp, cx + y, cy + x, alpha, color);
            Plot(bmp, cx + y, cy - x, alpha, color);
            Plot(bmp, cx - y, cy + x, alpha, color);
            Plot(bmp, cx - y, cy - x, alpha, color);
        }

        public static void DrawCircle(Bitmap bmp, int cx, int cy, int radius, Color color)
        {
            if (radius <= 0)
            {
                Plot(bmp, cx, cy, 1.0, color);
                return;
            }

            int x = 0;
            double y = radius;
            PlotCirclePoints(bmp, cx, cy, 0, radius, 1.0, color);

            while (x <= y)
            {
                x++;
                y = Math.Sqrt(radius * radius - x * x);
                double alpha = y - Math.Floor(y);
                PlotCirclePoints(bmp, cx, cy, x, (int)Math.Floor(y), 1.0 - alpha, color);
                PlotCirclePoints(bmp, cx, cy, x, (int)Math.Floor(y) + 1, alpha, color);
            }
        }

        // Vẽ độ dày cho hình tròn
        public static void DrawThickCircle(Bitmap bmp, int cx, int cy, int radius, Color color, int thickness)
        {
            if (thickness <= 1)
            {
                DrawCircle(bmp, cx, cy, radius, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (radius <= 0)
                    DrawDot(g, color, cx, cy, thickness);
                else
                    g.DrawEllipse(p, cx - radius, cy - radius, radius * 2, radius * 2);
            }
        }

        // ==========================================
        // PHẦN 2: CÁC THUẬT TOÁN CHO CÁC SHAPE MỚI
        // ==========================================

        // Helper: Calculate hexagon points
        private static Point[] GetHexagonPoints(Rectangle rect)
        {
            double cx = rect.X + rect.Width / 2.0;
            double cy = rect.Y + rect.Height / 2.0;
            double radius = Math.Min(rect.Width, rect.Height) / 2.0;
            Point[] points = new Point[6];

            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI / 3 * i - Math.PI / 6;
                double x = cx + radius * Math.Cos(angle);
                double y = cy + radius * Math.Sin(angle);
                points[i] = new Point((int)x, (int)y);
            }
            return points;
        }

        // LỤC GIÁC - Dùng thuật toán vẽ đa giác dựa trên đường thẳng
        public static void DrawHexagon(Bitmap bmp, Rectangle rect, Color color)
            => DrawPolygon(bmp, GetHexagonPoints(rect), color);

        public static void DrawThickHexagon(Bitmap bmp, Rectangle rect, Color color, int thickness)
            => DrawThickPolygon(bmp, GetHexagonPoints(rect), color, thickness);

        // Helper: Calculate diamond points
        private static Point[] GetDiamondPoints(Rectangle rect)
        {
            return
            [
                new(rect.X + rect.Width / 2, rect.Top),
                new(rect.Right, rect.Y + rect.Height / 2),
                new(rect.X + rect.Width / 2, rect.Bottom),
                new(rect.Left, rect.Y + rect.Height / 2)
            ];
        }

        // KIM CƯƠNG - Dùng thuật toán vẽ đa giác 4 cạnh
        public static void DrawDiamond(Bitmap bmp, Rectangle rect, Color color)
            => DrawPolygon(bmp, GetDiamondPoints(rect), color);

        public static void DrawThickDiamond(Bitmap bmp, Rectangle rect, Color color, int thickness)
            => DrawThickPolygon(bmp, GetDiamondPoints(rect), color, thickness);

        // Helper: Calculate arrow points
        private static Point[] GetArrowPoints(Rectangle rect)
        {
            int arrowSize = Math.Min(rect.Width, rect.Height) / 3;
            return
            [
                new(rect.X + rect.Width / 2, rect.Top),
                new(rect.Right - arrowSize, rect.Top + arrowSize),
                new(rect.X + rect.Width / 2 + arrowSize / 2, rect.Top + arrowSize),
                new(rect.X + rect.Width / 2 + arrowSize / 2, rect.Bottom),
                new(rect.X + rect.Width / 2 - arrowSize / 2, rect.Bottom),
                new(rect.X + rect.Width / 2 - arrowSize / 2, rect.Top + arrowSize),
                new(rect.Left + arrowSize, rect.Top + arrowSize)
            ];
        }

        // MŨI TÊN - Dùng thuật toán vẽ đa giác 7 cạnh
        public static void DrawArrows(Bitmap bmp, Rectangle rect, Color color)
            => DrawPolygon(bmp, GetArrowPoints(rect), color);

        public static void DrawThickArrows(Bitmap bmp, Rectangle rect, Color color, int thickness)
            => DrawThickPolygon(bmp, GetArrowPoints(rect), color, thickness);

        // ELLIPSE - Dùng thuật toán vẽ ellipse dựa trên thuật toán tròn + Wu's antialiasing
        public static void DrawEllipse(Bitmap bmp, Rectangle rect, Color color)
        {
            int a = rect.Width / 2;  // Semi-major axis (horizontal)
            int b = rect.Height / 2; // Semi-minor axis (vertical)
            int cx = rect.X + a;
            int cy = rect.Y + b;

            if (a <= 0 || b <= 0)
            {
                DrawLine(bmp, rect.Left, rect.Top, rect.Right, rect.Bottom, color);
                return;
            }

            int x = 0;
            double y = b;

            // Midpoint Ellipse Algorithm
            double d1 = (b * b) - (a * a * b) + (0.25 * a * a);

            while ((a * a * (y - 0.5)) > (b * b * (x + 1)))
            {
                if (d1 < 0)
                {
                    d1 += (2 * b * b * (2 * x + 3));
                }
                else
                {
                    d1 += (2 * b * b * (2 * x + 3)) - (4 * a * a * (y - 1));
                    y--;
                }
                x++;

                // Plot 8 symmetric points với Wu's antialiasing
                int y_int = (int)Math.Floor(y);
                double alpha = y - y_int;

                Plot(bmp, cx + x, cy + y_int, 1.0 - alpha, color);
                Plot(bmp, cx + x, cy + y_int + 1, alpha, color);
                Plot(bmp, cx - x, cy + y_int, 1.0 - alpha, color);
                Plot(bmp, cx - x, cy + y_int + 1, alpha, color);
                Plot(bmp, cx + x, cy - y_int, 1.0 - alpha, color);
                Plot(bmp, cx + x, cy - y_int - 1, alpha, color);
                Plot(bmp, cx - x, cy - y_int, 1.0 - alpha, color);
                Plot(bmp, cx - x, cy - y_int - 1, alpha, color);
            }

            double d2 = (b * b * (x + 0.5) * (x + 0.5)) + (a * a * (y - 1) * (y - 1)) - (a * a * b * b);

            while (y > 0)
            {
                if (d2 < 0)
                {
                    d2 += (2 * b * b * (2 * x + 2)) - (4 * a * a * (y - 1));
                    x++;
                }
                else
                {
                    d2 -= (4 * a * a * (y - 1));
                }
                y--;

                // Plot 8 symmetric points với Wu's antialiasing
                int y_int = (int)Math.Floor(y);
                double alpha = y - y_int;

                Plot(bmp, cx + x, cy + y_int, 1.0 - alpha, color);
                Plot(bmp, cx + x, cy + y_int + 1, alpha, color);
                Plot(bmp, cx - x, cy + y_int, 1.0 - alpha, color);
                Plot(bmp, cx - x, cy + y_int + 1, alpha, color);
                Plot(bmp, cx + x, cy - y_int, 1.0 - alpha, color);
                Plot(bmp, cx + x, cy - y_int - 1, alpha, color);
                Plot(bmp, cx - x, cy - y_int, 1.0 - alpha, color);
                Plot(bmp, cx - x, cy - y_int - 1, alpha, color);
            }
        }

        public static void DrawThickEllipse(Bitmap bmp, Rectangle rect, Color color, int thickness)
        {
            if (thickness <= 1)
            {
                DrawEllipse(bmp, rect, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (rect.Width <= 0 && rect.Height <= 0)
                    DrawDot(g, color, rect.X, rect.Y, thickness);
                else if (rect.Width <= 0 || rect.Height <= 0)
                    g.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Bottom);
                else
                    g.DrawEllipse(p, rect);
            }
        }

        // BEZIER CURVE - Dùng thuật toán De Casteljau + Wu's antialiasing
        public static void DrawBezier(Bitmap bmp, Point p0, Point p1, Point p2, Point p3, Color color, int steps = 50)
        {
            Point prevPoint = p0;

            // Vẽ steps+1 điểm để bao gồm cả điểm cuối (t=1.0)
            for (int i = 0; i <= steps; i++)
            {
                double t = (i == steps) ? 1.0 : (double)i / steps;
                double t_inv = 1.0 - t;

                // De Casteljau's algorithm: B(t) = (1-t)³P₀ + 3(1-t)²tP₁ + 3(1-t)t²P₂ + t³P₃
                double coef0 = t_inv * t_inv * t_inv;
                double coef1 = 3 * t * t_inv * t_inv;
                double coef2 = 3 * t * t * t_inv;
                double coef3 = t * t * t;

                double x = (coef0 * p0.X) + (coef1 * p1.X) + (coef2 * p2.X) + (coef3 * p3.X);
                double y = (coef0 * p0.Y) + (coef1 * p1.Y) + (coef2 * p2.Y) + (coef3 * p3.Y);

                Point currentPoint = new((int)Math.Round(x), (int)Math.Round(y));

                // Vẽ đường nối từ điểm trước đến điểm hiện tại
                if (i > 0)
                {
                    DrawLine(bmp, prevPoint.X, prevPoint.Y, currentPoint.X, currentPoint.Y, color);
                }

                prevPoint = currentPoint;
            }
        }

        public static void DrawThickBezier(Bitmap bmp, Point p0, Point p1, Point p2, Point p3, Color color, int thickness, int steps = 50)
        {
            if (thickness <= 1)
            {
                DrawBezier(bmp, p0, p1, p2, p3, color, steps);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (p0 == p1 && p1 == p2 && p2 == p3)
                    DrawDot(g, color, p0.X, p0.Y, thickness);
                else
                    g.DrawBezier(p, p0, p1, p2, p3);
            }
        }

        // FIX Bug #18: Vẽ 1/4 cung tròn thay vì nguyên circle
        // Hàm helper vẽ quarter arc tại vị trí (cx, cy) với bán kính r
        private static void DrawQuarterArc(Bitmap bmp, int cx, int cy, int radius, int quadrant, Color color)
        {
            // quadrant: 0=top-left, 1=top-right, 2=bottom-right, 3=bottom-left
            int x = 0;
            double y = radius;

            while (x <= y)
            {
                double realY = Math.Sqrt((double)radius * radius - (double)x * x);
                double alpha = realY - Math.Floor(realY);
                int y_int = (int)Math.Floor(realY);

                // Vẽ 2 pixel cho mỗi x (anti-aliased)
                switch (quadrant)
                {
                    case 0: // Top-left
                        Plot(bmp, cx - x, cy - y_int, 1.0 - alpha, color);
                        Plot(bmp, cx - x, cy - y_int - 1, alpha, color);
                        Plot(bmp, cx - y_int, cy - x, 1.0 - alpha, color);
                        Plot(bmp, cx - y_int - 1, cy - x, alpha, color);
                        break;
                    case 1: // Top-right
                        Plot(bmp, cx + x, cy - y_int, 1.0 - alpha, color);
                        Plot(bmp, cx + x, cy - y_int - 1, alpha, color);
                        Plot(bmp, cx + y_int, cy - x, 1.0 - alpha, color);
                        Plot(bmp, cx + y_int + 1, cy - x, alpha, color);
                        break;
                    case 2: // Bottom-right
                        Plot(bmp, cx + x, cy + y_int, 1.0 - alpha, color);
                        Plot(bmp, cx + x, cy + y_int + 1, alpha, color);
                        Plot(bmp, cx + y_int, cy + x, 1.0 - alpha, color);
                        Plot(bmp, cx + y_int + 1, cy + x, alpha, color);
                        break;
                    case 3: // Bottom-left
                        Plot(bmp, cx - x, cy + y_int, 1.0 - alpha, color);
                        Plot(bmp, cx - x, cy + y_int + 1, alpha, color);
                        Plot(bmp, cx - y_int, cy + x, 1.0 - alpha, color);
                        Plot(bmp, cx - y_int - 1, cy + x, alpha, color);
                        break;
                }

                x++;
                y = realY;
            }
        }

        // HÌNH CHỮ NHẬT BO GÓC - Kết hợp đường thẳng + 1/4 cung tròn
        public static void DrawRoundedRectangle(Bitmap bmp, Rectangle rect, int cornerRadius, Color color)
        {
            int maxRadius = Math.Min(rect.Width, rect.Height) / 2;
            int r = Math.Min(cornerRadius, maxRadius);

            if (r <= 0)
            {
                DrawRectangle(bmp, rect, color);
                return;
            }

            // Vẽ 4 cạnh thẳng
            DrawLine(bmp, rect.Left + r, rect.Top, rect.Right - r, rect.Top, color);      // Top
            DrawLine(bmp, rect.Right, rect.Top + r, rect.Right, rect.Bottom - r, color);  // Right
            DrawLine(bmp, rect.Right - r, rect.Bottom, rect.Left + r, rect.Bottom, color); // Bottom
            DrawLine(bmp, rect.Left, rect.Bottom - r, rect.Left, rect.Top + r, color);    // Left

            // FIX: Vẽ 4 GÓC TRÒN 1/4 cung (không phải nguyên circle)
            DrawQuarterArc(bmp, rect.Left + r, rect.Top + r, r, 0, color);     // Top-left
            DrawQuarterArc(bmp, rect.Right - r, rect.Top + r, r, 1, color);    // Top-right
            DrawQuarterArc(bmp, rect.Right - r, rect.Bottom - r, r, 2, color); // Bottom-right
            DrawQuarterArc(bmp, rect.Left + r, rect.Bottom - r, r, 3, color);  // Bottom-left
        }

        public static void DrawThickRoundedRectangle(Bitmap bmp, Rectangle rect, int cornerRadius, Color color, int thickness)
        {
            if (thickness <= 1)
            {
                DrawRoundedRectangle(bmp, rect, cornerRadius, color);
                return;
            }

            using (Graphics g = CreateAaGraphics(bmp))
            using (Pen p = CreateThickPen(color, thickness))
            {
                if (rect.Width <= 0 && rect.Height <= 0)
                {
                    DrawDot(g, color, rect.X, rect.Y, thickness);
                    return;
                }
                if (rect.Width <= 0 || rect.Height <= 0)
                {
                    g.DrawLine(p, rect.Left, rect.Top, rect.Right, rect.Bottom);
                    return;
                }

                int maxRadius = Math.Min(rect.Width, rect.Height) / 2;
                int r = Math.Min(cornerRadius, maxRadius);
                if (r <= 0)
                {
                    g.DrawRectangle(p, rect);
                    return;
                }

                int d = r * 2;
                using (GraphicsPath path = new())
                {
                    Rectangle arc = new(rect.Location, new Size(d, d));
                    path.AddArc(arc, 180, 90);
                    arc.X = rect.Right - d;
                    path.AddArc(arc, 270, 90);
                    arc.Y = rect.Bottom - d;
                    path.AddArc(arc, 0, 90);
                    arc.X = rect.Left;
                    path.AddArc(arc, 90, 90);
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
            }
        }
    }
}