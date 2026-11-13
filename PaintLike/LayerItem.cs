using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintLike
{
    /// <summary>
    /// Hiển thị một Layer trong bảng LayerPanel theo chuẩn Studio Dark UI:
    /// Nút con mắt ẩn/hiện vector + Thumbnail preview trực tiếp + Tên Layer + Badge Opacity (%)
    /// </summary>
    public partial class LayerItem : UserControl
    {
        private Layer? layer;
        private bool isSelected = false;
        private bool isHovered = false;
        private bool isEyeHovered = false;

        public event EventHandler? VisibilityChanged;
        public event EventHandler? SelectionChanged;

        private Rectangle EyeRect => new Rectangle(10, (this.Height - 20) / 2, 22, 20);
        private Rectangle ThumbRect => new Rectangle(38, (this.Height - 26) / 2, 36, 26);

        public LayerItem(Layer layer)
        {
            InitializeComponent();
            this.layer = layer;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Cursor = Cursors.Hand;
            this.Margin = new Padding(0, 0, 0, 5);
            this.Size = new Size(340, 42);
            this.BackColor = Color.Transparent;

            InitializeControls();
        }

        private void InitializeControls()
        {
            this.Controls.Clear();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            isEyeHovered = false;
            this.Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overEye = EyeRect.Contains(e.Location);
            if (overEye != isEyeHovered)
            {
                isEyeHovered = overEye;
                this.Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (EyeRect.Contains(e.Location))
            {
                if (layer != null)
                {
                    layer.IsVisible = !layer.IsVisible;
                    this.Invalidate();
                    VisibilityChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else
            {
                isSelected = true;
                this.Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = this.Parent?.BackColor ?? UITheme.BgCard;
            g.Clear(parentBg);

            Rectangle cardRect = new Rectangle(1, 1, Math.Max(1, this.Width - 3), Math.Max(1, this.Height - 3));
            Color bgFill = isSelected
                ? UITheme.AccentPrimarySubtle
                : (isHovered ? UITheme.BgSurfaceHover : Color.White);
            Color borderColor = isSelected
                ? UITheme.BorderFocus
                : (isHovered ? UITheme.BorderStrong : UITheme.BorderSubtle);

            using (GraphicsPath path = UITheme.CreateRoundedRect(cardRect, 6))
            {
                using (SolidBrush b = new SolidBrush(bgFill))
                    g.FillPath(b, path);
                using (Pen p = new Pen(borderColor, isSelected ? 1.5f : 1f))
                    g.DrawPath(p, path);
            }

            // Thanh chỉ báo bên trái khi Layer đang được chọn
            if (isSelected)
            {
                Rectangle indicator = new Rectangle(cardRect.X + 2, cardRect.Y + 6, 3, cardRect.Height - 12);
                using (SolidBrush sb = new SolidBrush(UITheme.AccentPrimary))
                    g.FillRectangle(sb, indicator);
            }

            // 1. Nút Con Mắt (Visibility Toggle)
            bool visible = layer?.IsVisible ?? true;
            Rectangle eyeBox = EyeRect;
            if (isEyeHovered)
            {
                using (GraphicsPath ep = UITheme.CreateRoundedRect(eyeBox, 4))
                using (SolidBrush eb = new SolidBrush(Color.FromArgb(224, 231, 255)))
                    g.FillPath(eb, ep);
            }
            Rectangle eyeIconRect = new Rectangle(eyeBox.X + 3, eyeBox.Y + 2, 16, 16);
            Color eyeColor = visible ? UITheme.AccentSecondary : UITheme.TextMuted;
            UITheme.DrawIcon(g, visible ? IconType.EyeOpen : IconType.EyeClosed, eyeIconRect, eyeColor, 1.7f);

            // 2. Thumbnail Preview trực tiếp của Layer
            Rectangle thumb = ThumbRect;
            UITheme.DrawCheckerboard(g, thumb, 4);
            if (layer?.Image != null)
            {
                try
                {
                    g.InterpolationMode = InterpolationMode.Low;
                    g.DrawImage(layer.Image, thumb);
                }
                catch { }
            }
            using (Pen tp = new Pen(isSelected ? UITheme.AccentPrimary : UITheme.BorderDefault, 1f))
            {
                g.DrawRectangle(tp, thumb);
            }

            // 3. Badge Opacity (%) bên phải
            int opacityVal = layer?.Opacity ?? 255;
            int opacityPct = (int)Math.Round(opacityVal / 255.0 * 100);
            Rectangle badgeRect = new Rectangle(this.Width - 54, (this.Height - 20) / 2, 44, 20);
            using (GraphicsPath bp = UITheme.CreateRoundedRect(badgeRect, 4))
            {
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(241, 245, 249)))
                    g.FillPath(bb, bp);
                using (Pen bPen = new Pen(UITheme.BorderSubtle, 1f))
                    g.DrawPath(bPen, bp);
            }
            TextRenderer.DrawText(g, $"{opacityPct}%", UITheme.FontSmallBold, badgeRect,
                opacityPct < 100 ? UITheme.AccentWarning : UITheme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 4. Tên Layer
            int nameX = thumb.Right + 10;
            int nameW = Math.Max(40, badgeRect.Left - nameX - 6);
            Rectangle nameRect = new Rectangle(nameX, 0, nameW, this.Height);
            Color nameColor = visible ? (isSelected ? UITheme.IconSelected : UITheme.TextPrimary) : UITheme.TextMuted;
            Font nameFont = isSelected ? UITheme.FontBodyBold : UITheme.FontBody;
            TextRenderer.DrawText(g, layer?.Name ?? "Layer", nameFont, nameRect, nameColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void UpdateSelection()
        {
            this.Invalidate();
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            UpdateSelection();
        }

        public bool IsSelected => isSelected;

        public Layer? Layer => layer;

        public void UpdateLayerName(string newName)
        {
            if (layer != null)
            {
                layer.Name = newName;
                this.Invalidate();
            }
        }

        public void UpdateOpacity(int opacity)
        {
            if (layer != null)
            {
                layer.Opacity = opacity;
                this.Invalidate();
            }
        }
    }

    partial class LayerItem
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
        }
    }
}
