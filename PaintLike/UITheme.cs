using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintLike
{
    public enum IconType
    {
        None,
        Line,
        Bezier,
        Rectangle,
        RoundedRectangle,
        Circle,
        Ellipse,
        Triangle,
        Trapezoid,
        Diamond,
        Hexagon,
        Star,
        Arrows,
        Eraser,
        SmartSelect,
        ColorPalette,
        Import,
        Save,
        Export,
        Undo,
        Redo,
        Layers,
        Plus,
        Trash,
        ArrowUp,
        ArrowDown,
        Rename,
        Duplicate,
        Opacity,
        Filter,
        EyeOpen,
        EyeClosed,
        ZoomIn,
        ZoomOut,
        Cube3D,
        CutFreehand,
        CutStraight,
        CutZigzag,
        CutCurved,
        CutWave,
        CutStepped,
        CutChevronV,
        Scissors,
        SplitMesh,
        Axes3D
    }

    public enum ButtonVariant
    {
        Default,
        Primary,
        Accent,
        Success,
        Danger,
        Ghost
    }

    /// <summary>
    /// Centralized Design System Tokens & Vector UI Renderer (ui-ux-pro-max & design-system)
    /// Phiên bản Bright Studio Light — Sáng sủa, tinh gọn, hạn chế tối đa màu đen/tối trên thanh công cụ.
    /// </summary>
    public static class UITheme
    {
        // =========================================================
        // 1. SEMANTIC DESIGN TOKENS (Bright Studio Light Palette)
        // =========================================================
        public static readonly Color BgWorkspace = Color.FromArgb(241, 245, 249);     // #F1F5F9 (Nền vùng làm việc sáng dịu)
        public static readonly Color BgToolbar = Color.FromArgb(248, 250, 252);       // #F8FAFC (Thanh công cụ trắng sáng)
        public static readonly Color BgRibbonGroup = Color.FromArgb(255, 255, 255);   // #FFFFFF (Khung nhóm công cụ trắng tinh)
        public static readonly Color BgStatusBar = Color.FromArgb(241, 245, 249);     // #F1F5F9 (Thanh trạng thái sáng)
        public static readonly Color BgPanel = Color.FromArgb(248, 250, 252);         // #F8FAFC (Nền bảng Layer & Dialog)
        public static readonly Color BgPanelHeader = Color.FromArgb(238, 242, 255);   // #EEF2FF (Tiêu đề xanh Indigo nhạt dịu mắt)
        public static readonly Color BgCard = Color.FromArgb(255, 255, 255);          // #FFFFFF (Mặt thẻ trắng)
        public static readonly Color BgSurface = Color.FromArgb(255, 255, 255);       // #FFFFFF (Nền nút công cụ trắng sáng)
        public static readonly Color BgSurfaceHover = Color.FromArgb(238, 242, 255);  // #EEF2FF (Hover ánh xanh lam sáng)
        public static readonly Color BgSurfacePressed = Color.FromArgb(224, 231, 255);// #E0E7FF (Nhấn nút)
        public static readonly Color BgInput = Color.FromArgb(255, 255, 255);         // #FFFFFF (Ô nhập liệu trắng)

        public static readonly Color AccentPrimary = Color.FromArgb(79, 70, 229);        // #4F46E5 Indigo chủ đạo
        public static readonly Color AccentPrimaryHover = Color.FromArgb(67, 56, 202);   // #4338CA Indigo đậm khi hover
        public static readonly Color AccentPrimarySubtle = Color.FromArgb(224, 231, 255);// #E0E7FF Nền nút công cụ đang chọn
        public static readonly Color AccentSecondary = Color.FromArgb(2, 132, 199);      // #0284C7 Sky Blue
        public static readonly Color AccentSecondaryHover = Color.FromArgb(3, 105, 161); // #0369A1 Sky Blue đậm
        public static readonly Color AccentSuccess = Color.FromArgb(16, 185, 129);       // #10B981 Emerald
        public static readonly Color AccentDanger = Color.FromArgb(225, 29, 72);         // #E11D48 Rose Red
        public static readonly Color AccentDangerHover = Color.FromArgb(190, 18, 60);    // #BE123C
        public static readonly Color AccentWarning = Color.FromArgb(217, 119, 6);        // #D97706 Amber

        // Văn bản & Icon sử dụng tông Xanh Xám (Slate/Navy) thay vì màu đen tuyền (#000000)
        public static readonly Color TextPrimary = Color.FromArgb(30, 58, 95);        // #1E3A5F (Xanh Navy đậm thanh lịch, không dùng đen)
        public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);     // #475569 (Xám xanh trung tính)
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);       // #64748B (Nhãn phụ)
        public static readonly Color IconDefault = Color.FromArgb(51, 65, 85);        // #334155 (Màu icon mặc định dịu mắt, không đen)
        public static readonly Color IconSelected = Color.FromArgb(67, 56, 202);      // #4338CA (Màu icon khi công cụ đang được chọn)

        public static readonly Color BorderSubtle = Color.FromArgb(226, 232, 240);    // #E2E8F0
        public static readonly Color BorderDefault = Color.FromArgb(203, 213, 225);   // #CBD5E1
        public static readonly Color BorderStrong = Color.FromArgb(148, 163, 184);    // #94A3B8
        public static readonly Color BorderFocus = Color.FromArgb(99, 102, 241);      // #6366F1

        // =========================================================
        // 2. TYPOGRAPHY TOKENS
        // =========================================================
        public static readonly Font FontTitle = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        public static readonly Font FontSubtitle = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        public static readonly Font FontGroupHeader = new Font("Segoe UI", 7.25F, FontStyle.Bold);
        public static readonly Font FontBody = new Font("Segoe UI", 8.75F, FontStyle.Regular);
        public static readonly Font FontBodyBold = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 7.75F, FontStyle.Regular);
        public static readonly Font FontSmallBold = new Font("Segoe UI", 7.75F, FontStyle.Bold);
        public static readonly Font FontMono = new Font("Consolas", 8.75F, FontStyle.Regular);
        public static readonly Font FontMonoBold = new Font("Consolas", 8.75F, FontStyle.Bold);

        // Quick Swatch Palette (Bảng màu tươi sáng, dùng Xanh Navy đậm thay vì ô đen)
        public static readonly Color[] QuickPalette = new Color[]
        {
            Color.FromArgb(37, 99, 235),   // Royal Blue (Mặc định)
            Color.FromArgb(124, 58, 237),  // Vibrant Violet
            Color.FromArgb(6, 182, 212),   // Cyan
            Color.FromArgb(16, 185, 129),  // Emerald Green
            Color.FromArgb(245, 158, 11),  // Warm Amber
            Color.FromArgb(249, 115, 22),  // Orange
            Color.FromArgb(239, 68, 68),   // Coral Red
            Color.FromArgb(236, 72, 153),  // Rose Pink
            Color.FromArgb(51, 65, 85),    // Deep Slate (thay cho màu đen)
            Color.FromArgb(255, 255, 255)  // Pure White
        };

        private class ButtonMeta
        {
            public IconType Icon { get; set; } = IconType.None;
            public string Label { get; set; } = "";
            public ButtonVariant Variant { get; set; } = ButtonVariant.Default;
            public bool IconOnly { get; set; } = false;
            public bool IsSelected { get; set; } = false;
            public bool IsHovered { get; set; } = false;
            public bool IsPressed { get; set; } = false;
            public Func<Color>? SwatchColorProvider { get; set; } = null;
        }

        private static readonly Dictionary<Button, ButtonMeta> _buttonMeta = new Dictionary<Button, ButtonMeta>();

        // =========================================================
        // 3. MODERN LIGHT BUTTON STYLING & OWNER DRAW
        // =========================================================
        public static void StyleButton(
            Button btn,
            IconType icon,
            string label,
            ButtonVariant variant = ButtonVariant.Default,
            bool iconOnly = false,
            ToolTip? toolTip = null,
            string? tooltipText = null,
            Func<Color>? swatchColorProvider = null)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.UseVisualStyleBackColor = false;
            btn.BackColor = Color.Transparent;
            btn.ForeColor = TextPrimary;
            btn.Font = FontSmallBold;
            btn.Cursor = Cursors.Hand;
            btn.Text = "";

            var meta = new ButtonMeta
            {
                Icon = icon,
                Label = label,
                Variant = variant,
                IconOnly = iconOnly,
                SwatchColorProvider = swatchColorProvider
            };
            _buttonMeta[btn] = meta;

            if (toolTip != null && !string.IsNullOrEmpty(tooltipText))
            {
                toolTip.SetToolTip(btn, tooltipText);
            }

            btn.MouseEnter -= Btn_MouseEnter;
            btn.MouseLeave -= Btn_MouseLeave;
            btn.MouseDown -= Btn_MouseDown;
            btn.MouseUp -= Btn_MouseUp;
            btn.Paint -= Btn_Paint;

            btn.MouseEnter += Btn_MouseEnter;
            btn.MouseLeave += Btn_MouseLeave;
            btn.MouseDown += Btn_MouseDown;
            btn.MouseUp += Btn_MouseUp;
            btn.Paint += Btn_Paint;
            btn.Invalidate();
        }

        public static void SetButtonSelected(Button btn, bool selected)
        {
            if (_buttonMeta.TryGetValue(btn, out var meta))
            {
                if (meta.IsSelected != selected)
                {
                    meta.IsSelected = selected;
                    btn.Invalidate();
                }
            }
        }

        public static void SetButtonLabel(Button btn, string label)
        {
            if (_buttonMeta.TryGetValue(btn, out var meta))
            {
                meta.Label = label;
                btn.Invalidate();
            }
        }

        private static void Btn_MouseEnter(object? sender, EventArgs e)
        {
            if (sender is Button btn && _buttonMeta.TryGetValue(btn, out var meta))
            {
                meta.IsHovered = true;
                btn.Invalidate();
            }
        }

        private static void Btn_MouseLeave(object? sender, EventArgs e)
        {
            if (sender is Button btn && _buttonMeta.TryGetValue(btn, out var meta))
            {
                meta.IsHovered = false;
                meta.IsPressed = false;
                btn.Invalidate();
            }
        }

        private static void Btn_MouseDown(object? sender, MouseEventArgs e)
        {
            if (sender is Button btn && e.Button == MouseButtons.Left && _buttonMeta.TryGetValue(btn, out var meta))
            {
                meta.IsPressed = true;
                btn.Invalidate();
            }
        }

        private static void Btn_MouseUp(object? sender, MouseEventArgs e)
        {
            if (sender is Button btn && _buttonMeta.TryGetValue(btn, out var meta))
            {
                meta.IsPressed = false;
                btn.Invalidate();
            }
        }

        private static void Btn_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not Button btn || !_buttonMeta.TryGetValue(btn, out var meta))
                return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = btn.Parent?.BackColor ?? BgRibbonGroup;
            if (parentBg == Color.Transparent) parentBg = BgRibbonGroup;
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(1, 1, Math.Max(1, btn.Width - 3), Math.Max(1, btn.Height - 3));
            int radius = 6;

            Color fillColor = BgSurface;
            Color borderColor = BorderSubtle;
            Color fgColor = TextPrimary;
            Color iconColor = IconDefault;

            switch (meta.Variant)
            {
                case ButtonVariant.Primary:
                    fillColor = meta.IsPressed ? Color.FromArgb(55, 48, 163) : (meta.IsHovered ? AccentPrimaryHover : AccentPrimary);
                    borderColor = fillColor;
                    fgColor = Color.White;
                    iconColor = Color.White;
                    break;
                case ButtonVariant.Accent:
                    fillColor = meta.IsPressed ? Color.FromArgb(7, 89, 133) : (meta.IsHovered ? AccentSecondaryHover : AccentSecondary);
                    borderColor = fillColor;
                    fgColor = Color.White;
                    iconColor = Color.White;
                    break;
                case ButtonVariant.Success:
                    fillColor = meta.IsPressed ? Color.FromArgb(5, 150, 105) : (meta.IsHovered ? Color.FromArgb(16, 185, 129) : AccentSuccess);
                    borderColor = fillColor;
                    fgColor = Color.White;
                    iconColor = Color.White;
                    break;
                case ButtonVariant.Danger:
                    fillColor = meta.IsPressed ? Color.FromArgb(254, 205, 211) : (meta.IsHovered ? Color.FromArgb(255, 228, 230) : Color.FromArgb(255, 241, 242));
                    borderColor = meta.IsHovered ? AccentDanger : Color.FromArgb(254, 205, 211);
                    fgColor = AccentDanger;
                    iconColor = AccentDanger;
                    break;
                case ButtonVariant.Ghost:
                    fillColor = meta.IsPressed ? BgSurfacePressed : (meta.IsHovered ? BgSurfaceHover : Color.Transparent);
                    borderColor = meta.IsHovered ? BorderDefault : Color.Transparent;
                    fgColor = meta.IsHovered ? AccentPrimary : TextSecondary;
                    iconColor = fgColor;
                    break;
                default: // Default / Tool button trên nền sáng
                    if (meta.IsSelected)
                    {
                        fillColor = meta.IsHovered ? Color.FromArgb(199, 210, 254) : AccentPrimarySubtle;
                        borderColor = BorderFocus;
                        fgColor = IconSelected;
                        iconColor = IconSelected;
                    }
                    else if (meta.IsPressed)
                    {
                        fillColor = BgSurfacePressed;
                        borderColor = BorderFocus;
                        fgColor = IconSelected;
                        iconColor = IconSelected;
                    }
                    else if (meta.IsHovered)
                    {
                        fillColor = BgSurfaceHover;
                        borderColor = Color.FromArgb(165, 180, 252);
                        fgColor = AccentPrimary;
                        iconColor = AccentPrimary;
                    }
                    else
                    {
                        fillColor = BgSurface;
                        borderColor = BorderSubtle;
                        fgColor = TextPrimary;
                        iconColor = IconDefault;
                    }
                    break;
            }

            if (!btn.Enabled)
            {
                fillColor = Color.FromArgb(241, 245, 249);
                borderColor = BorderSubtle;
                fgColor = TextMuted;
                iconColor = TextMuted;
            }

            using (GraphicsPath path = CreateRoundedRect(rect, radius))
            {
                if (fillColor.A > 0)
                {
                    using (SolidBrush b = new SolidBrush(fillColor))
                        g.FillPath(b, path);
                }

                if (borderColor.A > 0)
                {
                    float borderW = meta.IsSelected ? 1.6f : 1.0f;
                    using (Pen p = new Pen(borderColor, borderW))
                        g.DrawPath(p, path);
                }
            }

            // Thanh chỉ báo màu Indigo dưới đáy nút đang được chọn
            if (meta.IsSelected)
            {
                int barW = Math.Max(12, rect.Width / 2);
                Rectangle barRect = new Rectangle(rect.X + (rect.Width - barW) / 2, rect.Bottom - 2, barW, 2);
                using (SolidBrush barBrush = new SolidBrush(AccentPrimary))
                    g.FillRectangle(barBrush, barRect);
            }

            // Nút chọn màu hiển thị trực tiếp ô màu
            if (meta.SwatchColorProvider != null)
            {
                Color swatchColor = meta.SwatchColorProvider();
                int swatchSize = Math.Min(rect.Height - 11, 15);
                int padLeft = rect.Width <= 72 ? 4 : 7;
                int gap = rect.Width <= 72 ? 3 : 5;
                Rectangle swatchRect = new Rectangle(rect.X + padLeft, rect.Y + (rect.Height - swatchSize) / 2, swatchSize, swatchSize);
                using (GraphicsPath sp = CreateRoundedRect(swatchRect, 4))
                {
                    using (SolidBrush sb = new SolidBrush(swatchColor))
                        g.FillPath(sb, sp);
                    using (Pen penBorder = new Pen(BorderStrong, 1.2f))
                        g.DrawPath(penBorder, sp);
                }

                Rectangle textRect = new Rectangle(swatchRect.Right + gap, rect.Y, rect.Right - swatchRect.Right - gap - 2, rect.Height);
                TextRenderer.DrawText(g, meta.Label, FontSmallBold, textRect, fgColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                return;
            }

            bool hasIcon = meta.Icon != IconType.None;
            bool hasLabel = !meta.IconOnly && !string.IsNullOrEmpty(meta.Label);

            if (hasIcon && !hasLabel)
            {
                int iconSize = Math.Min(Math.Min(rect.Width - 10, rect.Height - 8), 18);
                Rectangle iconRect = new Rectangle(
                    rect.X + (rect.Width - iconSize) / 2,
                    rect.Y + (rect.Height - iconSize) / 2,
                    iconSize,
                    iconSize);
                DrawIcon(g, meta.Icon, iconRect, iconColor, 1.8f);
            }
            else if (hasIcon && hasLabel)
            {
                if (rect.Height >= 46 && rect.Width <= 110)
                {
                    int iconSize = 20;
                    Rectangle iconRect = new Rectangle(
                        rect.X + (rect.Width - iconSize) / 2,
                        rect.Y + 8,
                        iconSize,
                        iconSize);
                    DrawIcon(g, meta.Icon, iconRect, iconColor, 1.8f);

                    Rectangle textRect = new Rectangle(rect.X + 3, iconRect.Bottom + 2, rect.Width - 6, rect.Bottom - iconRect.Bottom - 4);
                    TextRenderer.DrawText(g, meta.Label, FontSmallBold, textRect, fgColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    int iconSize = Math.Min(15, rect.Height - 8);
                    int padLeft = rect.Width <= 72 ? 4 : 7;
                    int gap = rect.Width <= 72 ? 2 : 4;
                    Rectangle iconRect = new Rectangle(
                        rect.X + padLeft,
                        rect.Y + (rect.Height - iconSize) / 2,
                        iconSize,
                        iconSize);
                    DrawIcon(g, meta.Icon, iconRect, iconColor, 1.7f);

                    Rectangle textRect = new Rectangle(iconRect.Right + gap, rect.Y, rect.Right - iconRect.Right - gap - 2, rect.Height);
                    TextRenderer.DrawText(g, meta.Label, FontSmallBold, textRect, fgColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            else if (hasLabel)
            {
                TextRenderer.DrawText(g, meta.Label, FontSmallBold, rect, fgColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        // =========================================================
        // 4. VECTOR ICON RENDERER
        // =========================================================
        public static void DrawIcon(Graphics g, IconType icon, Rectangle r, Color color, float strokeWidth = 1.8f)
        {
            if (icon == IconType.None || r.Width <= 2 || r.Height <= 2) return;

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(color, strokeWidth))
            using (SolidBrush brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                float x = r.X;
                float y = r.Y;
                float w = r.Width;
                float h = r.Height;
                float cx = x + w / 2f;
                float cy = y + h / 2f;

                switch (icon)
                {
                    case IconType.Line:
                        g.DrawLine(pen, x + 2, y + h - 2, x + w - 2, y + 2);
                        g.FillEllipse(brush, x + 0.5f, y + h - 4.5f, 4f, 4f);
                        g.FillEllipse(brush, x + w - 4.5f, y + 0.5f, 4f, 4f);
                        break;

                    case IconType.Bezier:
                        g.DrawBezier(pen,
                            new PointF(x + 1, y + h - 2),
                            new PointF(x + w * 0.25f, y - 2),
                            new PointF(x + w * 0.75f, y + h + 2),
                            new PointF(x + w - 1, y + 2));
                        break;

                    case IconType.Rectangle:
                        g.DrawRectangle(pen, x + 1, y + 3, w - 2, h - 6);
                        break;

                    case IconType.RoundedRectangle:
                        using (GraphicsPath rp = CreateRoundedRect(new Rectangle((int)x + 1, (int)y + 3, (int)w - 2, (int)h - 6), 4))
                            g.DrawPath(pen, rp);
                        break;

                    case IconType.Circle:
                        float d = Math.Min(w, h) - 2;
                        g.DrawEllipse(pen, cx - d / 2f, cy - d / 2f, d, d);
                        break;

                    case IconType.Ellipse:
                        g.DrawEllipse(pen, x + 1, y + 4, w - 2, h - 8);
                        break;

                    case IconType.Triangle:
                        PointF[] tri = new PointF[]
                        {
                            new PointF(cx, y + 2),
                            new PointF(x + 1, y + h - 2),
                            new PointF(x + w - 1, y + h - 2)
                        };
                        g.DrawPolygon(pen, tri);
                        break;

                    case IconType.Trapezoid:
                        PointF[] trap = new PointF[]
                        {
                            new PointF(x + w * 0.25f, y + 3),
                            new PointF(x + w * 0.75f, y + 3),
                            new PointF(x + w - 1, y + h - 3),
                            new PointF(x + 1, y + h - 3)
                        };
                        g.DrawPolygon(pen, trap);
                        break;

                    case IconType.Diamond:
                        PointF[] dia = new PointF[]
                        {
                            new PointF(cx, y + 1),
                            new PointF(x + w - 1, cy),
                            new PointF(cx, y + h - 1),
                            new PointF(x + 1, cy)
                        };
                        g.DrawPolygon(pen, dia);
                        break;

                    case IconType.Hexagon:
                        PointF[] hex = new PointF[6];
                        float rad = Math.Min(w, h) / 2f - 1f;
                        for (int i = 0; i < 6; i++)
                        {
                            double ang = Math.PI / 3.0 * i - Math.PI / 6.0;
                            hex[i] = new PointF(cx + (float)(rad * Math.Cos(ang)), cy + (float)(rad * Math.Sin(ang)));
                        }
                        g.DrawPolygon(pen, hex);
                        break;

                    case IconType.Star:
                        PointF[] star = new PointF[10];
                        float outerR = Math.Min(w, h) / 2f - 0.5f;
                        float innerR = outerR * 0.42f;
                        double startAng = -Math.PI / 2.0;
                        for (int i = 0; i < 10; i++)
                        {
                            float sr = (i % 2 == 0) ? outerR : innerR;
                            star[i] = new PointF(cx + (float)(sr * Math.Cos(startAng)), cy + (float)(sr * Math.Sin(startAng)));
                            startAng += Math.PI / 5.0;
                        }
                        g.DrawPolygon(pen, star);
                        break;

                    case IconType.Arrows:
                        PointF[] arr = new PointF[]
                        {
                            new PointF(cx, y + 1),
                            new PointF(x + w - 2, y + h * 0.45f),
                            new PointF(cx + w * 0.18f, y + h * 0.45f),
                            new PointF(cx + w * 0.18f, y + h - 1),
                            new PointF(cx - w * 0.18f, y + h - 1),
                            new PointF(cx - w * 0.18f, y + h * 0.45f),
                            new PointF(x + 2, y + h * 0.45f)
                        };
                        g.DrawPolygon(pen, arr);
                        break;

                    case IconType.Eraser:
                        PointF[] er = new PointF[]
                        {
                            new PointF(x + w * 0.45f, y + 2),
                            new PointF(x + w - 2, y + h * 0.45f),
                            new PointF(x + w * 0.45f, y + h - 2),
                            new PointF(x + 2, y + h * 0.55f)
                        };
                        g.DrawPolygon(pen, er);
                        g.DrawLine(pen, x + w * 0.25f, y + h * 0.35f, x + w * 0.65f, y + h * 0.75f);
                        g.DrawLine(pen, x + w * 0.45f, y + h - 2, x + w - 1, y + h - 2);
                        break;

                    case IconType.SmartSelect:
                        using (Pen dashPen = new Pen(color, 1.5f))
                        {
                            dashPen.DashPattern = new float[] { 2f, 2f };
                            g.DrawRectangle(dashPen, x + 1, y + 3, w - 5, h - 5);
                        }
                        float sx = x + w - 3;
                        float sy = y + 3;
                        g.DrawLine(pen, sx - 3, sy, sx + 2, sy);
                        g.DrawLine(pen, sx - 0.5f, sy - 2.5f, sx - 0.5f, sy + 2.5f);
                        break;

                    case IconType.Import:
                        g.DrawLine(pen, x + 1, y + 4, x + w * 0.4f, y + 4);
                        g.DrawLine(pen, x + w * 0.4f, y + 4, x + w * 0.52f, y + 6);
                        g.DrawRectangle(pen, x + 1, y + 6, w - 2, h - 8);
                        break;

                    case IconType.Save:
                        g.DrawRectangle(pen, x + 2, y + 2, w - 4, h - 4);
                        g.DrawRectangle(pen, x + 5, y + 2, w - 10, (h - 4) * 0.38f);
                        g.DrawRectangle(pen, x + 4, y + h * 0.58f, w - 8, (h - 4) * 0.35f);
                        break;

                    case IconType.Export:
                        g.DrawLine(pen, x + 2, y + h * 0.6f, x + 2, y + h - 2);
                        g.DrawLine(pen, x + 2, y + h - 2, x + w - 2, y + h - 2);
                        g.DrawLine(pen, x + w - 2, y + h - 2, x + w - 2, y + h * 0.6f);
                        g.DrawLine(pen, cx, y + h - 5, cx, y + 2);
                        g.DrawLine(pen, cx - 3.5f, y + 5.5f, cx, y + 2);
                        g.DrawLine(pen, cx + 3.5f, y + 5.5f, cx, y + 2);
                        break;

                    case IconType.Undo:
                        g.DrawArc(pen, x + 2, y + 4, w - 4, h - 5, 180, 180);
                        g.DrawLine(pen, x + 2, y + 8, x + 2, y + 3);
                        g.DrawLine(pen, x + 2, y + 8, x + 7, y + 8);
                        break;

                    case IconType.Redo:
                        g.DrawArc(pen, x + 2, y + 4, w - 4, h - 5, 180, 180);
                        g.DrawLine(pen, x + w - 2, y + 8, x + w - 2, y + 3);
                        g.DrawLine(pen, x + w - 2, y + 8, x + w - 7, y + 8);
                        break;

                    case IconType.Layers:
                        PointF[] topL = new PointF[]
                        {
                            new PointF(cx, y + 1),
                            new PointF(x + w - 1, y + h * 0.32f),
                            new PointF(cx, y + h * 0.56f),
                            new PointF(x + 1, y + h * 0.32f)
                        };
                        g.DrawPolygon(pen, topL);
                        g.DrawLines(pen, new PointF[]
                        {
                            new PointF(x + 1, y + h * 0.54f),
                            new PointF(cx, y + h * 0.76f),
                            new PointF(x + w - 1, y + h * 0.54f)
                        });
                        g.DrawLines(pen, new PointF[]
                        {
                            new PointF(x + 1, y + h * 0.74f),
                            new PointF(cx, y + h - 1),
                            new PointF(x + w - 1, y + h * 0.74f)
                        });
                        break;

                    case IconType.Plus:
                        g.DrawLine(pen, cx, y + 2, cx, y + h - 2);
                        g.DrawLine(pen, x + 2, cy, x + w - 2, cy);
                        break;

                    case IconType.Trash:
                        g.DrawLine(pen, x + 2, y + 4, x + w - 2, y + 4);
                        g.DrawLine(pen, cx - 2, y + 2, cx + 2, y + 2);
                        g.DrawRectangle(pen, x + 3.5f, y + 4, w - 7, h - 6);
                        break;

                    case IconType.ArrowUp:
                        g.DrawLine(pen, cx, y + h - 2, cx, y + 2);
                        g.DrawLine(pen, cx - 4, y + 6, cx, y + 2);
                        g.DrawLine(pen, cx + 4, y + 6, cx, y + 2);
                        break;

                    case IconType.ArrowDown:
                        g.DrawLine(pen, cx, y + 2, cx, y + h - 2);
                        g.DrawLine(pen, cx - 4, y + h - 6, cx, y + h - 2);
                        g.DrawLine(pen, cx + 4, y + h - 6, cx, y + h - 2);
                        break;

                    case IconType.Rename:
                        PointF[] pencil = new PointF[]
                        {
                            new PointF(x + w - 4, y + 2),
                            new PointF(x + w - 1, y + 5),
                            new PointF(x + 5, y + h - 1),
                            new PointF(x + 1, y + h - 1),
                            new PointF(x + 1, y + h - 5)
                        };
                        g.DrawPolygon(pen, pencil);
                        break;

                    case IconType.Duplicate:
                        g.DrawRectangle(pen, x + 1, y + 1, w - 5, h - 5);
                        g.DrawRectangle(pen, x + 4, y + 4, w - 5, h - 5);
                        break;

                    case IconType.Opacity:
                        g.DrawEllipse(pen, x + 2, y + 2, w - 4, h - 4);
                        g.FillPie(brush, x + 2, y + 2, w - 4, h - 4, 90, 180);
                        break;

                    case IconType.Filter:
                        g.DrawLine(pen, x + 2, y + 4, x + w - 2, y + 4);
                        g.FillEllipse(brush, x + w * 0.6f, y + 2, 4, 4);
                        g.DrawLine(pen, x + 2, cy, x + w - 2, cy);
                        g.FillEllipse(brush, x + w * 0.25f, cy - 2, 4, 4);
                        g.DrawLine(pen, x + 2, y + h - 4, x + w - 2, y + h - 4);
                        g.FillEllipse(brush, x + w * 0.7f, y + h - 6, 4, 4);
                        break;

                    case IconType.EyeOpen:
                        g.DrawArc(pen, x + 1, y + 3, w - 2, h - 4, 200, 140);
                        g.DrawArc(pen, x + 1, y + 1, w - 2, h - 4, 20, 140);
                        g.FillEllipse(brush, cx - 2.2f, cy - 2.2f, 4.4f, 4.4f);
                        break;

                    case IconType.EyeClosed:
                        g.DrawArc(pen, x + 1, y + 1, w - 2, h - 4, 20, 140);
                        g.DrawLine(pen, x + 2, y + h - 3, x + w - 2, y + 3);
                        break;

                    case IconType.ZoomIn:
                        g.DrawEllipse(pen, x + 1, y + 1, w - 5, h - 5);
                        g.DrawLine(pen, x + w - 5, y + h - 5, x + w - 1, y + h - 1);
                        g.DrawLine(pen, cx - 3.5f, cy - 1.5f, cx + 0.5f, cy - 1.5f);
                        g.DrawLine(pen, cx - 1.5f, cy - 3.5f, cx - 1.5f, cy + 0.5f);
                        break;

                    case IconType.ZoomOut:
                        g.DrawEllipse(pen, x + 1, y + 1, w - 5, h - 5);
                        g.DrawLine(pen, x + w - 5, y + h - 5, x + w - 1, y + h - 1);
                        g.DrawLine(pen, cx - 3.5f, cy - 1.5f, cx + 0.5f, cy - 1.5f);
                        break;

                    case IconType.Cube3D:
                        PointF topPt = new PointF(cx, y + 1);
                        PointF leftPt = new PointF(x + 1.5f, y + h * 0.28f);
                        PointF rightPt = new PointF(x + w - 1.5f, y + h * 0.28f);
                        PointF midPt = new PointF(cx, cy + 0.5f);
                        PointF botLeft = new PointF(x + 1.5f, y + h * 0.75f);
                        PointF botRight = new PointF(x + w - 1.5f, y + h * 0.75f);
                        PointF botPt = new PointF(cx, y + h - 1);
                        g.DrawPolygon(pen, new[] { topPt, rightPt, botRight, botPt, botLeft, leftPt });
                        g.DrawLine(pen, leftPt, midPt);
                        g.DrawLine(pen, rightPt, midPt);
                        g.DrawLine(pen, botPt, midPt);
                        break;

                    case IconType.CutFreehand:
                        g.DrawBezier(pen,
                            new PointF(x + 1f, cy + h * 0.22f),
                            new PointF(x + w * 0.3f, y + 1f),
                            new PointF(x + w * 0.65f, y + h - 1f),
                            new PointF(x + w - 1f, cy - h * 0.25f));
                        g.FillEllipse(brush, x, cy + h * 0.22f - 1.5f, 3f, 3f);
                        g.FillEllipse(brush, x + w - 3f, cy - h * 0.25f - 1.5f, 3f, 3f);
                        break;

                    case IconType.CutStraight:
                        g.DrawLine(pen, x + 1f, cy, x + w - 1f, cy);
                        g.FillEllipse(brush, x, cy - 1.8f, 3.6f, 3.6f);
                        g.FillEllipse(brush, x + w - 3.6f, cy - 1.8f, 3.6f, 3.6f);
                        break;

                    case IconType.CutZigzag:
                        PointF[] zz = new PointF[]
                        {
                            new PointF(x + 1f, cy),
                            new PointF(x + w * 0.22f, y + 2f),
                            new PointF(x + w * 0.44f, y + h - 2f),
                            new PointF(x + w * 0.66f, y + 2f),
                            new PointF(x + w * 0.84f, y + h - 2f),
                            new PointF(x + w - 1f, cy)
                        };
                        g.DrawLines(pen, zz);
                        break;

                    case IconType.CutCurved:
                        g.DrawBezier(pen,
                            new PointF(x + 1f, cy + h * 0.25f),
                            new PointF(x + w * 0.32f, y + 1f),
                            new PointF(x + w * 0.68f, y + 1f),
                            new PointF(x + w - 1f, cy + h * 0.25f));
                        break;

                    case IconType.CutWave:
                        PointF[] wavePts = new PointF[13];
                        for (int i = 0; i < wavePts.Length; i++)
                        {
                            float t = i / (float)(wavePts.Length - 1);
                            float wx = x + 1f + t * (w - 2f);
                            float wy = cy - MathF.Sin(t * MathF.PI * 2f) * (h * 0.34f);
                            wavePts[i] = new PointF(wx, wy);
                        }
                        g.DrawLines(pen, wavePts);
                        break;

                    case IconType.CutStepped:
                        PointF[] stepPts = new PointF[]
                        {
                            new PointF(x + 1f, cy + h * 0.25f),
                            new PointF(x + w * 0.32f, cy + h * 0.25f),
                            new PointF(x + w * 0.32f, cy - h * 0.25f),
                            new PointF(x + w * 0.68f, cy - h * 0.25f),
                            new PointF(x + w * 0.68f, cy + h * 0.25f),
                            new PointF(x + w - 1f, cy + h * 0.25f)
                        };
                        g.DrawLines(pen, stepPts);
                        break;

                    case IconType.CutChevronV:
                        PointF[] vPts = new PointF[]
                        {
                            new PointF(x + 1f, y + 3f),
                            new PointF(cx, y + h - 2f),
                            new PointF(x + w - 1f, y + 3f)
                        };
                        g.DrawLines(pen, vPts);
                        break;

                    case IconType.Scissors:
                        g.DrawEllipse(pen, x + 1f, y + 1.5f, 4.5f, 4.5f);
                        g.DrawEllipse(pen, x + 1f, y + h - 6f, 4.5f, 4.5f);
                        g.DrawLine(pen, x + 5f, y + 4.5f, x + w - 1f, y + h - 2.5f);
                        g.DrawLine(pen, x + 5f, y + h - 4.5f, x + w - 1f, y + 2.5f);
                        break;

                    case IconType.SplitMesh:
                        g.DrawRectangle(pen, x + 1f, y + 2f, w * 0.38f, h - 4f);
                        g.DrawRectangle(pen, x + w * 0.62f, y + 2f, w * 0.38f - 1f, h - 4f);
                        using (Pen dashSplit = new Pen(color, 1.3f))
                        {
                            dashSplit.DashPattern = new float[] { 2f, 2f };
                            g.DrawLine(dashSplit, cx, y + 1f, cx, y + h - 1f);
                        }
                        break;

                    case IconType.Axes3D:
                        PointF originPt = new PointF(x + w * 0.34f, y + h * 0.64f);
                        PointF yTip = new PointF(originPt.X, y + 1.5f);
                        PointF xTip = new PointF(x + w - 1.5f, originPt.Y);
                        PointF zTip = new PointF(x + 1.5f, y + h - 1.5f);
                        bool muted = color == TextMuted;
                        Color xCol = muted ? color : AccentDanger;
                        Color yCol = muted ? color : AccentSuccess;
                        Color zCol = muted ? color : Color.FromArgb(37, 99, 235);
                        using (Pen py = new Pen(yCol, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (Pen px = new Pen(xCol, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        using (Pen pz = new Pen(zCol, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(py, originPt, yTip);
                            g.DrawLine(py, yTip.X - 2.2f, yTip.Y + 2.5f, yTip.X, yTip.Y);
                            g.DrawLine(py, yTip.X + 2.2f, yTip.Y + 2.5f, yTip.X, yTip.Y);

                            g.DrawLine(px, originPt, xTip);
                            g.DrawLine(px, xTip.X - 2.5f, xTip.Y - 2.2f, xTip.X, xTip.Y);
                            g.DrawLine(px, xTip.X - 2.5f, xTip.Y + 2.2f, xTip.X, xTip.Y);

                            g.DrawLine(pz, originPt, zTip);
                            g.DrawLine(pz, zTip.X, zTip.Y - 2.8f, zTip.X, zTip.Y);
                            g.DrawLine(pz, zTip.X + 2.8f, zTip.Y, zTip.X, zTip.Y);
                        }
                        g.FillEllipse(brush, originPt.X - 1.6f, originPt.Y - 1.6f, 3.2f, 3.2f);
                        break;
                }
            }

            g.SmoothingMode = oldMode;
        }

        // =========================================================
        // 5. STUDIO LIGHT LAYOUT & GRAPHICS HELPERS
        // =========================================================
        public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawRibbonGroup(Graphics g, Rectangle groupBounds, string title)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = CreateRoundedRect(groupBounds, 8))
            {
                using (SolidBrush bg = new SolidBrush(BgRibbonGroup))
                    g.FillPath(bg, path);
                using (Pen border = new Pen(BorderSubtle, 1f))
                    g.DrawPath(border, path);
            }

            Rectangle titleRect = new Rectangle(groupBounds.X + 4, groupBounds.Bottom - 16, groupBounds.Width - 8, 14);
            TextRenderer.DrawText(g, title, FontGroupHeader, titleRect, TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            g.SmoothingMode = old;
        }

        public static void DrawCheckerboard(Graphics g, Rectangle rect, int cellSize = 10)
        {
            Color c1 = Color.FromArgb(255, 255, 255);
            Color c2 = Color.FromArgb(241, 245, 249);
            using (SolidBrush b1 = new SolidBrush(c1))
            using (SolidBrush b2 = new SolidBrush(c2))
            {
                g.FillRectangle(b1, rect);
                for (int y = rect.Top; y < rect.Bottom; y += cellSize)
                {
                    int row = (y - rect.Top) / cellSize;
                    for (int x = rect.Left; x < rect.Right; x += cellSize)
                    {
                        int col = (x - rect.Left) / cellSize;
                        if ((row + col) % 2 == 1)
                        {
                            int w = Math.Min(cellSize, rect.Right - x);
                            int h = Math.Min(cellSize, rect.Bottom - y);
                            g.FillRectangle(b2, x, y, w, h);
                        }
                    }
                }
            }
        }
    }
}
