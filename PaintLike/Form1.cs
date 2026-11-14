using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PaintLike
{
    // Danh sách các công cụ
    public enum Tool
    {
        Line, Rectangle, Circle, Triangle, Eraser, Star, Trapezoid, WuLine,
        SmartSelect, Hexagon, Diamond, Arrows, Ellipse, Bezier, RoundedRectangle
    }

    public partial class Form1 : Form
    {
        private Bitmap canvas;
        private Point start, end;
        private Pen pen;
        private bool drawing = false;
        private Tool currentTool = Tool.Line;
        private int eraserSize = 10;
        private float zoomScale = 1.0f;
        private Label? lblCoordinates;
        private Bitmap? importedImage;
        private Point importImagePos;
        private int lineOpacity = 255;

        // Studio UI Additions
        private Panel? panelStatusBar;
        private Label? lblActiveTool;
        private Label? lblActiveLayer;
        private Label? lblCanvasSize;
        private Label? lblZoomLevel;
        private Button? btnZoomOut;
        private Button? btnZoomIn;
        private Button? btnClearCanvas;
        private NumericUpDown? nudPenWidth;
        private Panel? panelQuickColors;
        private Button? btnTeddy3D;
        private ToolTip? studioToolTip;
        private Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();

        // Zoom Scrollbars & Tracking
        private HScrollBar? hScrollCanvas;
        private VScrollBar? vScrollCanvas;
        private int scrollX = 0;
        private int scrollY = 0;
        private bool isUpdatingScrollBars = false;
        private bool isSaved = false;

        // Layer System
        private List<Layer> layers = new List<Layer>();
        private LayerPanel? layerPanel;

        // Undo / Redo System
        private List<List<Layer>> undoList = new List<List<Layer>>();
        private Stack<List<Layer>> redoStack = new Stack<List<Layer>>();
        private const int Max_undo_Steps = 5;

        private int currentLayerIndex = 0;

        private Rectangle GetRectangle(Point p1, Point p2)
        {
            return GetRect(p1, p2);
        }

        public Form1()
        {
            InitializeComponent();

            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.WindowState = FormWindowState.Maximized;

            // Khởi tạo bút vẽ với màu Xanh Royal tươi sáng (hạn chế màu đen trên thanh công cụ)
            pen = new Pen(Color.FromArgb(37, 99, 235), 2);
            pen.StartCap = pen.EndCap = LineCap.Round;

            // Khởi tạo thanh trạng thái, thanh trượt & giao diện Studio trước khi tính kích thước Canvas
            InitializeCoordinatesLabel();
            InitializeScrollBars();
            InitializeStudioUI();

            // Tạo Canvas ban đầu
            int w = Math.Max(1, pictureBox1.ClientSize.Width);
            int h = Math.Max(1, pictureBox1.ClientSize.Height);
            canvas = new Bitmap(w, h);

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
            }

            this.MouseWheel += (sender, e) => Form1_MouseWheel(sender, e);
            this.Resize += (sender, e) => Form1_Resize(sender, e);
            this.FormClosing += Form1_FormClosing;

            try { nudEraserSize.Value = eraserSize; } catch { }

            // Khởi tạo Layer System
            InitializeLayerSystem();
            EnsureDockZOrder();

            // Đặt công cụ mặc định và cập nhật trạng thái
            SetActiveTool(Tool.Line);
            UpdateScrollBars();
            UpdateStatusBarInfo();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureCanvasMatchesViewport();
            UpdateScrollBars();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Áp dụng hệ thống Design Tokens & Vector Icons cho toàn bộ thanh công cụ
        /// </summary>
        private void InitializeStudioUI()
        {
            studioToolTip = new ToolTip
            {
                InitialDelay = 250,
                ReshowDelay = 100,
                AutoPopDelay = 8000,
                ShowAlways = true
            };

            // Bật DoubleBuffered cho panelTools để vẽ Ribbon mượt mà
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, panelTools, new object[] { true });

            panelTools.Paint += PanelTools_Paint;

            // 1. Group FILE & HISTORY
            UITheme.StyleButton(btnImportImage, IconType.Import, "Mở", ButtonVariant.Default, false, studioToolTip, "Import ảnh từ máy tính (Ctrl+O)");
            UITheme.StyleButton(btnSave, IconType.Save, "Lưu", ButtonVariant.Default, false, studioToolTip, "Lưu hình ảnh (Ctrl+S)");
            UITheme.StyleButton(btnExport, IconType.Export, "Xuất", ButtonVariant.Accent, false, studioToolTip, "Xuất file PNG chất lượng cao (Ctrl+E)");
            UITheme.StyleButton(btnUndo, IconType.Undo, "Hoàn tác", ButtonVariant.Default, false, studioToolTip, "Hoàn tác bước trước (Ctrl+Z)");
            UITheme.StyleButton(btnRedo, IconType.Redo, "Làm lại", ButtonVariant.Default, false, studioToolTip, "Làm lại bước vừa hoàn tác (Ctrl+Y)");

            // 2. Group TOOLS
            UITheme.StyleButton(btnLine, IconType.Line, "Đường", ButtonVariant.Default, false, studioToolTip, "Vẽ đường thẳng khử răng cưa (Line)");
            UITheme.StyleButton(BttBezier, IconType.Bezier, "Cong", ButtonVariant.Default, false, studioToolTip, "Vẽ đường cong Bezier mượt");
            UITheme.StyleButton(btnEraser, IconType.Eraser, "Tẩy", ButtonVariant.Default, false, studioToolTip, "Tẩy nội dung trên Layer hiện tại (Eraser)");
            UITheme.StyleButton(btnSmartSelect, IconType.SmartSelect, "Tách nền", ButtonVariant.Default, false, studioToolTip, "Tách nền thông minh bằng OpenCV GrabCut (Smart Select)");

            btnClearCanvas = new Button
            {
                Location = new Point(374, 12),
                Size = new Size(68, 66),
                Name = "btnClearCanvas"
            };
            UITheme.StyleButton(btnClearCanvas, IconType.Trash, "Xóa", ButtonVariant.Danger, false,
                studioToolTip, "Xóa trắng nội dung Layer / trang vẽ hiện tại (Clear Canvas)");
            btnClearCanvas.Click += BtnClearCanvas_Click;
            panelTools.Controls.Add(btnClearCanvas);

            // 3. Group SHAPES (Icon-only với vector sắc nét + Tooltip chi tiết)
            UITheme.StyleButton(btnRect, IconType.Rectangle, "", ButtonVariant.Default, true, studioToolTip, "Hình chữ nhật (Rectangle)");
            UITheme.StyleButton(BttRoundedRectangle, IconType.RoundedRectangle, "", ButtonVariant.Default, true, studioToolTip, "Hình chữ nhật bo góc (Rounded Rectangle)");
            UITheme.StyleButton(btnCircle, IconType.Circle, "", ButtonVariant.Default, true, studioToolTip, "Hình tròn (Circle)");
            UITheme.StyleButton(BttEllipse, IconType.Ellipse, "", ButtonVariant.Default, true, studioToolTip, "Hình elip (Ellipse)");
            UITheme.StyleButton(btnTriangle, IconType.Triangle, "", ButtonVariant.Default, true, studioToolTip, "Hình tam giác (Triangle)");
            UITheme.StyleButton(BttDiamond, IconType.Diamond, "", ButtonVariant.Default, true, studioToolTip, "Hình thoi / Kim cương (Diamond)");
            UITheme.StyleButton(btnTrapezoid, IconType.Trapezoid, "", ButtonVariant.Default, true, studioToolTip, "Hình thang cân (Trapezoid)");
            UITheme.StyleButton(BttHexagon, IconType.Hexagon, "", ButtonVariant.Default, true, studioToolTip, "Hình lục giác đều (Hexagon)");
            UITheme.StyleButton(btnStar, IconType.Star, "", ButtonVariant.Default, true, studioToolTip, "Hình ngôi sao 5 cánh (Star)");
            UITheme.StyleButton(BttArrows, IconType.Arrows, "", ButtonVariant.Default, true, studioToolTip, "Hình mũi tên (Arrow)");

            // Đăng ký bảng ánh xạ Tool -> Button để highlight trạng thái đang chọn
            toolButtons[Tool.Line] = btnLine;
            toolButtons[Tool.Bezier] = BttBezier;
            toolButtons[Tool.Eraser] = btnEraser;
            toolButtons[Tool.SmartSelect] = btnSmartSelect;
            toolButtons[Tool.Rectangle] = btnRect;
            toolButtons[Tool.RoundedRectangle] = BttRoundedRectangle;
            toolButtons[Tool.Circle] = btnCircle;
            toolButtons[Tool.Ellipse] = BttEllipse;
            toolButtons[Tool.Triangle] = btnTriangle;
            toolButtons[Tool.Diamond] = BttDiamond;
            toolButtons[Tool.Trapezoid] = btnTrapezoid;
            toolButtons[Tool.Hexagon] = BttHexagon;
            toolButtons[Tool.Star] = btnStar;
            toolButtons[Tool.Arrows] = BttArrows;

            // 4. Group STROKE & COLOR
            UITheme.StyleButton(btnColor, IconType.ColorPalette, "Màu vẽ", ButtonVariant.Default, false,
                studioToolTip, "Chọn màu bút vẽ tùy chỉnh", () => pen.Color);

            // Nhãn & NumericUpDown cho độ dày nét vẽ (Pen Width) — tránh đè chữ lên ô nhập
            Label lblPenW = new Label
            {
                AutoSize = false,
                Size = new Size(34, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Nét:",
                Font = UITheme.FontSmallBold,
                ForeColor = UITheme.TextSecondary,
                BackColor = UITheme.BgRibbonGroup,
                Location = new Point(788, 18)
            };
            panelTools.Controls.Add(lblPenW);

            nudPenWidth = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 50,
                Value = (decimal)pen.Width,
                Location = new Point(824, 15),
                Size = new Size(48, 25),
                BackColor = UITheme.BgInput,
                ForeColor = UITheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UITheme.FontSmallBold
            };
            nudPenWidth.ValueChanged += (s, e) =>
            {
                pen.Width = (float)nudPenWidth.Value;
                pictureBox1.Invalidate();
            };
            studioToolTip.SetToolTip(nudPenWidth, "Độ dày nét vẽ (1 - 50 px)");
            panelTools.Controls.Add(nudPenWidth);
            nudPenWidth.BringToFront();

            // Nhãn cho kích thước cục tẩy (Eraser Size)
            Label lblEraserW = new Label
            {
                AutoSize = false,
                Size = new Size(34, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "Tẩy:",
                Font = UITheme.FontSmallBold,
                ForeColor = UITheme.TextSecondary,
                BackColor = UITheme.BgRibbonGroup,
                Location = new Point(876, 18)
            };
            panelTools.Controls.Add(lblEraserW);

            nudEraserSize.Font = UITheme.FontSmallBold;
            nudEraserSize.BackColor = UITheme.BgInput;
            nudEraserSize.ForeColor = UITheme.TextPrimary;
            nudEraserSize.Location = new Point(912, 15);
            nudEraserSize.Size = new Size(52, 25);
            nudEraserSize.BringToFront();
            studioToolTip.SetToolTip(nudEraserSize, "Kích thước cục tẩy (5 - 300 px)");

            lblOpacity.Font = UITheme.FontSmallBold;
            lblOpacity.BackColor = UITheme.BgRibbonGroup;
            lblOpacity.ForeColor = UITheme.TextSecondary;
            lblOpacity.Location = new Point(968, 18);
            trackBarOpacity.BackColor = UITheme.BgRibbonGroup;
            studioToolTip.SetToolTip(trackBarOpacity, "Độ trong suốt của nét vẽ (0 - 100%)");

            // Bảng màu chọn nhanh (Quick Swatches)
            panelQuickColors = new Panel
            {
                Location = new Point(708, 48),
                Size = new Size(168, 28),
                BackColor = UITheme.BgRibbonGroup
            };
            int swatchX = 0;
            foreach (Color swatchColor in UITheme.QuickPalette)
            {
                Color captured = swatchColor;
                Panel swatch = new Panel
                {
                    Size = new Size(15, 22),
                    Location = new Point(swatchX, 3),
                    BackColor = captured,
                    Cursor = Cursors.Hand
                };
                swatch.Paint += (s, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    bool isCurrent = pen.Color.ToArgb() == captured.ToArgb();
                    using (Pen bp = new Pen(isCurrent ? UITheme.AccentPrimary : UITheme.BorderDefault, isCurrent ? 2f : 1f))
                    {
                        pe.Graphics.DrawRectangle(bp, 0, 0, swatch.Width - 1, swatch.Height - 1);
                    }
                };
                swatch.Click += (s, ce) =>
                {
                    pen.Color = captured;
                    btnColor.Invalidate();
                    foreach (Control c in panelQuickColors.Controls) c.Invalidate();
                };
                studioToolTip.SetToolTip(swatch, $"Chọn nhanh màu #{captured.R:X2}{captured.G:X2}{captured.B:X2}");
                panelQuickColors.Controls.Add(swatch);
                swatchX += 17;
            }
            panelTools.Controls.Add(panelQuickColors);

            // 5. Group WORKSPACE (Layers & Filters Toggle + Teddy 3D Studio)
            btnLayers.Location = new Point(1088, 12);
            btnLayers.Size = new Size(102, 31);
            UITheme.StyleButton(btnLayers, IconType.Layers, "Layers", ButtonVariant.Default, false,
                studioToolTip, "Bật / tắt bảng quản lý Layer và Bộ lọc ảnh OpenCV");
            UITheme.SetButtonSelected(btnLayers, true);

            btnTeddy3D = new Button
            {
                Location = new Point(1088, 47),
                Size = new Size(102, 31),
                Name = "btnTeddy3D"
            };
            UITheme.StyleButton(btnTeddy3D, IconType.Cube3D, "Teddy 3D", ButtonVariant.Primary, false,
                studioToolTip, "Mở không gian dựng hình 3D tự do từ nét vẽ 2D (Teddy SIGGRAPH '99)");
            btnTeddy3D.Click += BtnTeddy3D_Click;
            panelTools.Controls.Add(btnTeddy3D);
        }

        private void PanelTools_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(UITheme.BgToolbar);

            // Vẽ 5 khung nhóm chức năng theo chuẩn Ribbon Studio
            UITheme.DrawRibbonGroup(g, new Rectangle(8, 6, 188, 92), "TỆP & LỊCH SỬ");
            UITheme.DrawRibbonGroup(g, new Rectangle(202, 6, 246, 92), "CÔNG CỤ VẼ");
            UITheme.DrawRibbonGroup(g, new Rectangle(454, 6, 242, 92), "HÌNH HỌC VECTOR");
            UITheme.DrawRibbonGroup(g, new Rectangle(702, 6, 372, 92), "MÀU SẮC, CỠ NÉT & ĐỘ MỜ");
            UITheme.DrawRibbonGroup(g, new Rectangle(1080, 6, 118, 92), "KHÔNG GIAN");

            // Đường viền phân tách dưới đáy thanh công cụ
            using (Pen bottomBorder = new Pen(UITheme.BorderDefault, 1f))
            {
                g.DrawLine(bottomBorder, 0, panelTools.Height - 1, panelTools.Width, panelTools.Height - 1);
            }
        }

        /// <summary>
        /// Khởi tạo Thanh Trạng Thái (Status Bar) chuyên nghiệp ở đáy cửa sổ
        /// </summary>
        private void InitializeCoordinatesLabel()
        {
            panelStatusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = UITheme.BgStatusBar,
                Padding = new Padding(12, 0, 12, 0)
            };
            panelStatusBar.Paint += (s, e) =>
            {
                using (Pen topLine = new Pen(UITheme.BorderDefault, 1f))
                {
                    e.Graphics.DrawLine(topLine, 0, 0, panelStatusBar.Width, 0);
                }
            };

            lblActiveTool = new Label
            {
                AutoSize = false,
                Size = new Size(290, 26),
                Location = new Point(12, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.FontSmallBold,
                ForeColor = UITheme.AccentPrimary,
                Text = "Công cụ: Đường thẳng (Line)"
            };
            panelStatusBar.Controls.Add(lblActiveTool);

            lblCoordinates = new Label
            {
                AutoSize = false,
                Size = new Size(230, 26),
                Location = new Point(310, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.FontMono,
                ForeColor = UITheme.TextPrimary,
                Text = "X: 0, Y: 0"
            };
            panelStatusBar.Controls.Add(lblCoordinates);

            lblActiveLayer = new Label
            {
                AutoSize = false,
                Size = new Size(210, 26),
                Location = new Point(550, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextSecondary,
                Text = "Layer: Background"
            };
            panelStatusBar.Controls.Add(lblActiveLayer);

            lblCanvasSize = new Label
            {
                AutoSize = false,
                Size = new Size(170, 26),
                Location = new Point(770, 1),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextMuted,
                Text = "Canvas: 1220 × 600 px"
            };
            panelStatusBar.Controls.Add(lblCanvasSize);

            // Cụm điều khiển Zoom ở góc phải Status Bar
            Panel zoomPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 156,
                BackColor = Color.Transparent
            };

            btnZoomOut = new Button
            {
                Size = new Size(28, 22),
                Location = new Point(4, 3),
                Enabled = false
            };
            UITheme.StyleButton(btnZoomOut, IconType.ZoomOut, "", ButtonVariant.Ghost, true, studioToolTip, "Thu nhỏ (tối thiểu 100%)");
            btnZoomOut.Click += (s, e) => SetZoom(zoomScale - 0.1f);
            zoomPanel.Controls.Add(btnZoomOut);

            lblZoomLevel = new Label
            {
                AutoSize = false,
                Size = new Size(84, 22),
                Location = new Point(34, 3),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UITheme.FontMonoBold,
                ForeColor = UITheme.TextPrimary,
                Cursor = Cursors.Hand,
                Text = "Zoom: 100%"
            };
            lblZoomLevel.Click += (s, e) => SetZoom(1.0f);
            zoomPanel.Controls.Add(lblZoomLevel);

            btnZoomIn = new Button
            {
                Size = new Size(28, 22),
                Location = new Point(120, 3)
            };
            UITheme.StyleButton(btnZoomIn, IconType.ZoomIn, "", ButtonVariant.Ghost, true, studioToolTip, "Phóng to (tối đa 500%)");
            btnZoomIn.Click += (s, e) => SetZoom(zoomScale + 0.1f);
            zoomPanel.Controls.Add(btnZoomIn);

            panelStatusBar.Controls.Add(zoomPanel);
            this.Controls.Add(panelStatusBar);
        }

        private void InitializeScrollBars()
        {
            hScrollCanvas = new HScrollBar
            {
                Dock = DockStyle.Bottom,
                Height = 18,
                Visible = false,
                Minimum = 0,
                SmallChange = 20
            };
            hScrollCanvas.Scroll += (s, e) =>
            {
                if (isUpdatingScrollBars) return;
                scrollX = e.NewValue;
                pictureBox1.Invalidate();
            };

            vScrollCanvas = new VScrollBar
            {
                Dock = DockStyle.Right,
                Width = 18,
                Visible = false,
                Minimum = 0,
                SmallChange = 20
            };
            vScrollCanvas.Scroll += (s, e) =>
            {
                if (isUpdatingScrollBars) return;
                scrollY = e.NewValue;
                pictureBox1.Invalidate();
            };

            this.Controls.Add(hScrollCanvas);
            this.Controls.Add(vScrollCanvas);
        }

        private void EnsureDockZOrder()
        {
            panelTools.BringToFront();
            panelStatusBar?.BringToFront();
            hScrollCanvas?.BringToFront();
            vScrollCanvas?.BringToFront();
            pictureBox1.BringToFront();
            layerPanel?.BringToFront();
        }

        private void UpdateScrollBars()
        {
            if (canvas == null || hScrollCanvas == null || vScrollCanvas == null || isUpdatingScrollBars)
                return;

            isUpdatingScrollBars = true;
            try
            {
                int availW = Math.Max(1, this.ClientSize.Width);
                int availH = Math.Max(1, this.ClientSize.Height - panelTools.Height - (panelStatusBar?.Height ?? 28));

                int scaledW = (int)Math.Round(canvas.Width * zoomScale);
                int scaledH = (int)Math.Round(canvas.Height * zoomScale);

                if (zoomScale <= 1.0f)
                {
                    hScrollCanvas.Visible = false;
                    vScrollCanvas.Visible = false;
                    scrollX = 0;
                    scrollY = 0;
                    hScrollCanvas.Value = 0;
                    vScrollCanvas.Value = 0;
                    return;
                }

                bool needH = scaledW > availW;
                bool needV = scaledH > availH;
                if (needV) needH = scaledW > Math.Max(1, availW - vScrollCanvas.Width);
                if (needH) needV = scaledH > Math.Max(1, availH - hScrollCanvas.Height);

                hScrollCanvas.Visible = needH;
                vScrollCanvas.Visible = needV;

                int viewW = Math.Max(1, availW - (needV ? vScrollCanvas.Width : 0));
                int viewH = Math.Max(1, availH - (needH ? hScrollCanvas.Height : 0));

                if (needH)
                {
                    int maxScrollX = Math.Max(0, scaledW - viewW);
                    hScrollCanvas.Minimum = 0;
                    hScrollCanvas.SmallChange = 20;
                    hScrollCanvas.LargeChange = Math.Max(1, viewW);
                    hScrollCanvas.Maximum = Math.Max(0, scaledW - 1);
                    scrollX = Math.Max(0, Math.Min(scrollX, maxScrollX));
                    int maxThumbX = Math.Max(0, hScrollCanvas.Maximum - hScrollCanvas.LargeChange + 1);
                    hScrollCanvas.Value = Math.Max(0, Math.Min(scrollX, maxThumbX));
                }
                else
                {
                    scrollX = 0;
                    hScrollCanvas.Value = 0;
                }

                if (needV)
                {
                    int maxScrollY = Math.Max(0, scaledH - viewH);
                    vScrollCanvas.Minimum = 0;
                    vScrollCanvas.SmallChange = 20;
                    vScrollCanvas.LargeChange = Math.Max(1, viewH);
                    vScrollCanvas.Maximum = Math.Max(0, scaledH - 1);
                    scrollY = Math.Max(0, Math.Min(scrollY, maxScrollY));
                    int maxThumbY = Math.Max(0, vScrollCanvas.Maximum - vScrollCanvas.LargeChange + 1);
                    vScrollCanvas.Value = Math.Max(0, Math.Min(scrollY, maxThumbY));
                }
                else
                {
                    scrollY = 0;
                    vScrollCanvas.Value = 0;
                }
            }
            finally
            {
                isUpdatingScrollBars = false;
            }
        }

        private void SetZoom(float newZoom)
        {
            // Khóa mức Zoom Out tối thiểu ở 100% (1.0f) để không làm co nhỏ khung Canvas
            zoomScale = Math.Max(1.0f, Math.Min(5.0f, (float)Math.Round(newZoom, 1)));
            if (btnZoomOut != null) btnZoomOut.Enabled = zoomScale > 1.0f;
            if (btnZoomIn != null) btnZoomIn.Enabled = zoomScale < 5.0f;
            UpdateScrollBars();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private void SetActiveTool(Tool tool)
        {
            currentTool = tool;
            foreach (var kvp in toolButtons)
            {
                UITheme.SetButtonSelected(kvp.Value, kvp.Key == tool);
            }

            pictureBox1.Cursor = (tool == Tool.Eraser) ? Cursors.Default : Cursors.Cross;

            if (lblActiveTool != null)
            {
                string toolDisplay = tool switch
                {
                    Tool.Line => "Đường thẳng (Line)",
                    Tool.WuLine => "Đường thẳng Wu (Anti-Aliased)",
                    Tool.Bezier => "Đường cong (Bezier Curve)",
                    Tool.Eraser => "Cục tẩy (Eraser)",
                    Tool.SmartSelect => "Tách nền AI (GrabCut Selection)",
                    Tool.Rectangle => "Hình chữ nhật (Rectangle)",
                    Tool.RoundedRectangle => "Hình chữ nhật bo góc (Rounded Rect)",
                    Tool.Circle => "Hình tròn (Circle)",
                    Tool.Ellipse => "Hình elip (Ellipse)",
                    Tool.Triangle => "Hình tam giác (Triangle)",
                    Tool.Diamond => "Hình thoi (Diamond)",
                    Tool.Trapezoid => "Hình thang (Trapezoid)",
                    Tool.Hexagon => "Hình lục giác (Hexagon)",
                    Tool.Star => "Hình ngôi sao (Star)",
                    Tool.Arrows => "Hình mũi tên (Arrow)",
                    _ => tool.ToString()
                };
                lblActiveTool.Text = $"Công cụ: {toolDisplay}";
            }

            pictureBox1.Invalidate();
        }

        private void UpdateStatusBarInfo()
        {
            if (lblZoomLevel != null)
                lblZoomLevel.Text = $"Zoom: {(int)Math.Round(zoomScale * 100)}%";

            if (lblCanvasSize != null && canvas != null)
                lblCanvasSize.Text = $"Canvas: {canvas.Width} × {canvas.Height} px";

            if (lblActiveLayer != null && layers != null && currentLayerIndex >= 0 && currentLayerIndex < layers.Count)
                lblActiveLayer.Text = $"Layer: {layers[currentLayerIndex].Name} ({currentLayerIndex + 1}/{layers.Count})";
        }

        // Khởi tạo Layer System
        private void InitializeLayerSystem()
        {
            Layer backgroundLayer = new Layer("Background", canvas.Width, canvas.Height);

            using (Graphics g = Graphics.FromImage(backgroundLayer.Image))
            {
                g.DrawImage(canvas, 0, 0);
            }

            layers.Add(backgroundLayer);

            layerPanel = new LayerPanel(layers);
            int topOffset = panelTools.Height + 12;
            int bottomMargin = (panelStatusBar?.Height ?? 28) + 24;
            int panelX = Math.Max(12, this.ClientSize.Width - layerPanel.Width - 24);
            int panelY = Math.Max(topOffset, this.ClientSize.Height - layerPanel.Height - bottomMargin);
            layerPanel.Location = new Point(panelX, panelY);
            layerPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            layerPanel.SetCanvasDimensions(canvas.Width, canvas.Height);

            layerPanel.BeforeLayerAction += (s, e) =>
            {
                SaveState();
                isSaved = false;
            };
            layerPanel.LayersChanged += LayerPanel_LayersChanged;
            layerPanel.LayerSelectionChanged += LayerPanel_LayerSelectionChanged;
            layerPanel.VisibleChanged += (s, e) => UITheme.SetButtonSelected(btnLayers, layerPanel.Visible);

            this.Controls.Add(layerPanel);
            layerPanel.BringToFront();
        }

        public void SetAdvancedShapeButtonsVisible(bool visible)
        {
            BttHexagon.Visible = visible;
            BttDiamond.Visible = visible;
            BttArrows.Visible = visible;
            BttEllipse.Visible = visible;
            BttBezier.Visible = visible;
            BttRoundedRectangle.Visible = visible;
        }

        public void ToggleAdvancedShapeButtons()
        {
            bool currentVisibility = BttHexagon.Visible;
            SetAdvancedShapeButtonsVisible(!currentVisibility);
        }

        private void LayerPanel_LayersChanged(object? sender, EventArgs e)
        {
            isSaved = false;
            if (layerPanel != null)
            {
                currentLayerIndex = layerPanel.GetCurrentLayerIndex();
            }
            RecomposeCanvas();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private void LayerPanel_LayerSelectionChanged(object? sender, EventArgs e)
        {
            if (layerPanel != null)
            {
                currentLayerIndex = layerPanel.GetCurrentLayerIndex();
            }
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private Point GetCanvasPoint(Point screenPoint)
        {
            int x = (int)Math.Round((screenPoint.X + scrollX) / (double)zoomScale);
            int y = (int)Math.Round((screenPoint.Y + scrollY) / (double)zoomScale);

            x = Math.Max(0, Math.Min(x, canvas.Width - 1));
            y = Math.Max(0, Math.Min(y, canvas.Height - 1));

            return new Point(x, y);
        }

        private Rectangle GetRect(Point p1, Point p2)
        {
            return new Rectangle(
                Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
                Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));
        }

        private Point[] CreateStarPoints(Rectangle bounds, int numPoints = 5, double innerRatio = 0.5)
        {
            var pts = new List<Point>();
            double cx = bounds.X + bounds.Width / 2.0;
            double cy = bounds.Y + bounds.Height / 2.0;
            double outer = Math.Min(bounds.Width, bounds.Height) / 2.0;
            double inner = outer * innerRatio;
            double step = Math.PI / numPoints;
            double angle = -Math.PI / 2;

            for (int i = 0; i < numPoints * 2; i++)
            {
                double r = (i % 2 == 0) ? outer : inner;
                pts.Add(new Point((int)(cx + Math.Cos(angle) * r), (int)(cy + Math.Sin(angle) * r)));
                angle += step;
            }
            return pts.ToArray();
        }

        private Point[] GetTrianglePoints(Rectangle rect)
        {
            return new Point[] {
                new Point(rect.Left + rect.Width / 2, rect.Top),
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Right, rect.Bottom)
            };
        }

        private Point[] GetTrapezoidPoints(Rectangle rect)
        {
            int indent = rect.Width / 4;
            return new Point[] {
                new Point(rect.Left + indent, rect.Top),
                new Point(rect.Right - indent, rect.Top),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left, rect.Bottom)
            };
        }

        private Point[] GetHexagonPoints(Rectangle rect)
        {
            double cx = rect.X + rect.Width / 2.0;
            double cy = rect.Y + rect.Height / 2.0;
            double radius = Math.Min(rect.Width, rect.Height) / 2.0;
            var pts = new List<Point>();
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI / 3 * i - Math.PI / 6;
                double x = cx + radius * Math.Cos(angle);
                double y = cy + radius * Math.Sin(angle);
                pts.Add(new Point((int)x, (int)y));
            }
            return pts.ToArray();
        }

        private Point[] GetDiamondPoints(Rectangle rect)
        {
            return new Point[] {
                new Point(rect.X + rect.Width / 2, rect.Top),
                new Point(rect.Right, rect.Y + rect.Height / 2),
                new Point(rect.X + rect.Width / 2, rect.Bottom),
                new Point(rect.Left, rect.Y + rect.Height / 2)
            };
        }

        private Point[] GetArrowPoints(Rectangle rect)
        {
            int arrowSize = Math.Min(rect.Width, rect.Height) / 3;
            return new Point[] {
                new Point(rect.X + rect.Width / 2, rect.Top),
                new Point(rect.Right - arrowSize, rect.Top + arrowSize),
                new Point(rect.X + rect.Width / 2 + arrowSize / 2, rect.Top + arrowSize),
                new Point(rect.X + rect.Width / 2 + arrowSize / 2, rect.Bottom),
                new Point(rect.X + rect.Width / 2 - arrowSize / 2, rect.Bottom),
                new Point(rect.X + rect.Width / 2 - arrowSize / 2, rect.Top + arrowSize),
                new Point(rect.Left + arrowSize, rect.Top + arrowSize)
            };
        }

        private GraphicsPath GetRoundedRectanglePath(Rectangle rect, int cornerRadius)
        {
            GraphicsPath path = new GraphicsPath();
            int maxRadius = Math.Min(rect.Width, rect.Height) / 2;
            int radius = Math.Min(cornerRadius, maxRadius);

            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        private Color GetColorWithOpacity(Color baseColor)
        {
            return Color.FromArgb(lineOpacity, baseColor.R, baseColor.G, baseColor.B);
        }

        private void UpdateCanvasFromLayers()
        {
            if (layers == null || layers.Count == 0) return;

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.White);
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    Layer layer = layers[i];
                    if (!layer.IsVisible) continue;

                    if (layer.Opacity < 255)
                    {
                        using (ImageAttributes imageAttrs = new ImageAttributes())
                        {
                            float opacity = layer.Opacity / 255f;
                            float[][] colorMatrixElements = new float[][]
                            {
                                new float[] { 1, 0, 0, 0, 0 },
                                new float[] { 0, 1, 0, 0, 0 },
                                new float[] { 0, 0, 1, 0, 0 },
                                new float[] { 0, 0, 0, opacity, 0 },
                                new float[] { 0, 0, 0, 0, 1 }
                            };
                            ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
                            imageAttrs.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                            Rectangle destRect = new Rectangle(0, 0, layer.Image.Width, layer.Image.Height);
                            g.DrawImage(layer.Image, destRect, 0, 0, layer.Image.Width, layer.Image.Height, GraphicsUnit.Pixel, imageAttrs);
                        }
                    }
                    else
                    {
                        g.DrawImage(layer.Image, 0, 0);
                    }
                }
            }
        }

        private void ApplyAntiAliasingToImportedImage(int method)
        {
            if (importedImage == null)
            {
                MessageBox.Show("Chưa import ảnh nào!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Bitmap originalImage = (Bitmap)importedImage.Clone();
                Bitmap? processedImage = method switch
                {
                    1 => ImageProcessor.RemoveAliasing(originalImage),
                    2 => ImageProcessor.RemoveAliasingAdvanced(originalImage),
                    3 => ImageProcessor.RemoveAliasingForCurves(originalImage),
                    4 => ImageProcessor.RemoveAliasingWithStrength(originalImage, 5),
                    _ => ImageProcessor.RemoveAliasing(originalImage)
                };

                originalImage.Dispose();
                importedImage.Dispose();
                importedImage = processedImage;

                RecomposeCanvas();
                pictureBox1.Invalidate();
                MessageBox.Show("Xử lý khử răng cưa thành công!", "Thành công",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Bitmap ApplyImageProcessing(Bitmap image, ImageProcessingDialog settings)
        {
            Bitmap result = (Bitmap)image.Clone();
            bool success = false;

            try
            {
                if (settings.SelectedProcessing > 0)
                {
                    Bitmap oldResult = result;
                    switch (settings.SelectedProcessing)
                    {
                        case 1:
                            result = ImageProcessor.RemoveAliasing(result);
                            break;
                        case 2:
                            result = ImageProcessor.RemoveAliasingAdvanced(result);
                            break;
                        case 3:
                            result = ImageProcessor.RemoveAliasingForCurves(result);
                            break;
                    }
                    if (oldResult != result) oldResult.Dispose();
                }

                if (settings.ApplyContrast)
                {
                    Bitmap oldResult = result;
                    result = ImageProcessor.AdjustContrast(result, settings.ContrastValue / 100.0);
                    if (oldResult != result) oldResult.Dispose();
                }

                if (settings.ApplySharpen)
                {
                    Bitmap oldResult = result;
                    result = ImageProcessor.ApplySharpen(result, settings.SharpenValue);
                    if (oldResult != result) oldResult.Dispose();
                }

                if (settings.ApplyBlur && settings.BlurValue > 0)
                {
                    Bitmap oldResult = result;
                    result = ImageProcessor.ApplyGaussianBlur(result, settings.BlurValue);
                    if (oldResult != result) oldResult.Dispose();
                }

                if (settings.ApplyBrightness && settings.BrightnessValue != 0)
                {
                    Bitmap oldResult = result;
                    result = ImageProcessor.AdjustBrightness(result, settings.BrightnessValue);
                    if (oldResult != result) oldResult.Dispose();
                }

                success = true;
                image.Dispose();
                return result;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi xử lý ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (!success && result != null && result != image)
                    result.Dispose();
                return image;
            }
        }

        private void EnsureCanvasMatchesViewport()
        {
            if (canvas == null) return;

            int viewW = pictureBox1.ClientSize.Width + (vScrollCanvas != null && vScrollCanvas.Visible ? vScrollCanvas.Width : 0);
            int viewH = pictureBox1.ClientSize.Height + (hScrollCanvas != null && hScrollCanvas.Visible ? hScrollCanvas.Height : 0);
            if (viewW <= 0 || viewH <= 0) return;

            if (viewW > canvas.Width || viewH > canvas.Height)
            {
                int newW = Math.Max(canvas.Width, viewW);
                int newH = Math.Max(canvas.Height, viewH);

                Bitmap oldCanvas = canvas;
                Bitmap newCanvas = new Bitmap(newW, newH);
                using (Graphics g = Graphics.FromImage(newCanvas))
                {
                    g.Clear(Color.White);
                    g.DrawImage(oldCanvas, 0, 0);
                }
                canvas = newCanvas;
                oldCanvas.Dispose();

                if (layerPanel != null)
                {
                    layerPanel.UpdateAllLayersSize(newW, newH);
                    layerPanel.SetCanvasDimensions(newW, newH);
                }

                ResizeLayerHistorySnapshots(newW, newH);
                RecomposeCanvas();
                UpdateStatusBarInfo();
            }
        }

        private static void ResizeLayerHistoryList(List<Layer> snapshot, int newW, int newH)
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                Layer layer = snapshot[i];
                if (layer.Image.Width != newW || layer.Image.Height != newH)
                {
                    bool isBg = (i == snapshot.Count - 1) &&
                                string.Equals(layer.Name, "Background", StringComparison.OrdinalIgnoreCase);
                    Bitmap resized = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(resized))
                    {
                        g.Clear(isBg ? Color.White : Color.Transparent);
                        g.DrawImage(layer.Image, 0, 0);
                    }
                    layer.Image.Dispose();
                    layer.Image = resized;
                }
            }
        }

        private void ResizeLayerHistorySnapshots(int newW, int newH)
        {
            foreach (var snapshot in undoList)
                ResizeLayerHistoryList(snapshot, newW, newH);
            foreach (var snapshot in redoStack)
                ResizeLayerHistoryList(snapshot, newW, newH);
        }

        private void Form1_Resize(object? sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
                return;

            EnsureCanvasMatchesViewport();
            UpdateScrollBars();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private void Form1_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (e.Delta > 0) SetZoom(zoomScale + 0.1f);
            else SetZoom(zoomScale - 0.1f);
        }

        private bool IsCurrentLayerBackgroundBase()
        {
            if (layers == null || currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
                return false;
            return (currentLayerIndex == layers.Count - 1) &&
                   string.Equals(layers[currentLayerIndex].Name, "Background", StringComparison.OrdinalIgnoreCase);
        }

        private void EraseSegment(Point p1, Point p2)
        {
            if (layers == null || currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
                return;

            Layer currentLayer = layers[currentLayerIndex];
            Bitmap eraserTarget = currentLayer.Image;
            bool isBackgroundBase = IsCurrentLayerBackgroundBase();
            int actualEraserSize = Math.Max(1, (int)Math.Round(eraserSize / zoomScale));
            Color eraseColor = isBackgroundBase ? Color.White : Color.Transparent;

            using (Graphics g = Graphics.FromImage(eraserTarget))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.CompositingMode = isBackgroundBase ? CompositingMode.SourceOver : CompositingMode.SourceCopy;

                using (Brush b = new SolidBrush(eraseColor))
                {
                    if (p1 == p2)
                    {
                        g.FillEllipse(b, p2.X - actualEraserSize / 2f, p2.Y - actualEraserSize / 2f, actualEraserSize, actualEraserSize);
                    }
                    else
                    {
                        using (Pen erasePen = new Pen(eraseColor, actualEraserSize))
                        {
                            erasePen.StartCap = LineCap.Round;
                            erasePen.EndCap = LineCap.Round;
                            erasePen.LineJoin = LineJoin.Round;
                            g.DrawLine(erasePen, p1, p2);
                        }
                        g.FillEllipse(b, p2.X - actualEraserSize / 2f, p2.Y - actualEraserSize / 2f, actualEraserSize, actualEraserSize);
                    }
                }
            }
        }

        private void BtnClearCanvas_Click(object? sender, EventArgs e)
        {
            if (layers == null || layers.Count == 0 || currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
                return;

            SaveState();
            isSaved = false;

            Layer currentLayer = layers[currentLayerIndex];
            bool isBackgroundBase = IsCurrentLayerBackgroundBase();
            using (Graphics g = Graphics.FromImage(currentLayer.Image))
            {
                g.Clear(isBackgroundBase ? Color.White : Color.Transparent);
            }

            RecomposeCanvas();
            layerPanel?.RefreshLayerThumbnails();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                SaveState();
                isSaved = false;
                drawing = true;
                start = GetCanvasPoint(e.Location);
                end = start;

                if (currentTool == Tool.Eraser)
                {
                    EraseSegment(start, end);
                    pictureBox1.Invalidate();
                }
            }
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (drawing)
            {
                end = GetCanvasPoint(e.Location);

                if (currentTool == Tool.Eraser)
                {
                    EraseSegment(start, end);
                    start = end;
                }

                pictureBox1.Invalidate();
            }
            else if (currentTool == Tool.Eraser)
            {
                pictureBox1.Invalidate();
            }

            Point canvasPos = GetCanvasPoint(e.Location);

            if (drawing)
            {
                int width = Math.Abs(end.X - start.X);
                int height = Math.Abs(end.Y - start.Y);
                if (lblCoordinates != null)
                    lblCoordinates.Text = $"X: {canvasPos.X}, Y: {canvasPos.Y}  |  KT: {width} × {height}";
            }
            else
            {
                if (lblCoordinates != null)
                    lblCoordinates.Text = $"X: {canvasPos.X}, Y: {canvasPos.Y}";
            }
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            if (!drawing) return;
            drawing = false;

            end = GetCanvasPoint(e.Location);
            Rectangle rect = GetRectangle(start, end);

            if (layers == null || layers.Count == 0 || currentLayerIndex < 0 || currentLayerIndex >= layers.Count)
            {
                MessageBox.Show("Lỗi: Không có layer hợp lệ!", "Lỗi Layer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pictureBox1.Invalidate();
                return;
            }

            Layer currentLayer = layers[currentLayerIndex];
            Bitmap drawTarget = currentLayer.Image;

            if (currentTool == Tool.Eraser)
            {
                RecomposeCanvas();
                layerPanel?.RefreshLayerThumbnails();
                pictureBox1.Invalidate();
                return;
            }

            if (currentTool == Tool.SmartSelect)
            {
                if (rect.Width > 5 && rect.Height > 5)
                {
                    Bitmap resultBmp = ImageProcessor.RemoveBackgroundGrabCut(currentLayer.Image, rect);
                    if (currentLayer.Image != null) currentLayer.Image.Dispose();
                    currentLayer.Image = resultBmp;

                    RecomposeCanvas();
                    layerPanel?.RefreshLayerList();
                }
            }
            else
            {
                switch (currentTool)
                {
                    case Tool.WuLine:
                        WuLineDrawer.DrawThickLine(drawTarget, start.X, start.Y, end.X, end.Y, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Rectangle:
                        WuLineDrawer.DrawThickRectangle(drawTarget, rect, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Circle:
                        int r = Math.Min(rect.Width, rect.Height) / 2;
                        WuLineDrawer.DrawThickCircle(drawTarget, rect.X + rect.Width / 2, rect.Y + rect.Height / 2, r, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Star:
                        WuLineDrawer.DrawThickPolygon(drawTarget, CreateStarPoints(rect), GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Triangle:
                        WuLineDrawer.DrawThickPolygon(drawTarget, GetTrianglePoints(rect), GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Trapezoid:
                        WuLineDrawer.DrawThickPolygon(drawTarget, GetTrapezoidPoints(rect), GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Line:
                        using (Graphics g = Graphics.FromImage(drawTarget))
                        {
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            Color c = GetColorWithOpacity(pen.Color);
                            if (start == end)
                            {
                                using (SolidBrush sb = new SolidBrush(c))
                                {
                                    g.FillEllipse(sb, start.X - pen.Width / 2f, start.Y - pen.Width / 2f, pen.Width, pen.Width);
                                }
                            }
                            else
                            {
                                using (Pen opacityPen = new Pen(c, pen.Width))
                                {
                                    opacityPen.StartCap = opacityPen.EndCap = LineCap.Round;
                                    g.DrawLine(opacityPen, start, end);
                                }
                            }
                        }
                        break;
                    case Tool.Hexagon:
                        WuLineDrawer.DrawThickHexagon(drawTarget, rect, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Diamond:
                        WuLineDrawer.DrawThickDiamond(drawTarget, rect, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Arrows:
                        WuLineDrawer.DrawThickArrows(drawTarget, rect, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Ellipse:
                        WuLineDrawer.DrawThickEllipse(drawTarget, rect, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.Bezier:
                        Point[] bezierPoints = GetBezierControlPoints(rect);
                        WuLineDrawer.DrawThickBezier(drawTarget, bezierPoints[0], bezierPoints[1], bezierPoints[2], bezierPoints[3], GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                    case Tool.RoundedRectangle:
                        int cornerRadius = Math.Min(rect.Width, rect.Height) / 4;
                        WuLineDrawer.DrawThickRoundedRectangle(drawTarget, rect, cornerRadius, GetColorWithOpacity(pen.Color), (int)pen.Width);
                        break;
                }

                RecomposeCanvas();
                layerPanel?.RefreshLayerThumbnails();
            }

            pictureBox1.Invalidate();
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.TranslateTransform(-scrollX, -scrollY);
            e.Graphics.ScaleTransform(zoomScale, zoomScale);

            // Vẽ nền checkerboard cho vùng Canvas để thấy rõ độ trong suốt khi tẩy / tách nền
            if (canvas != null)
            {
                Rectangle canvasRect = new Rectangle(0, 0, canvas.Width, canvas.Height);
                UITheme.DrawCheckerboard(e.Graphics, canvasRect, 12);
            }

            if (layers != null && layers.Count > 0)
            {
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    Layer layer = layers[i];
                    if (!layer.IsVisible) continue;

                    if (layer.Opacity < 255)
                    {
                        using (ImageAttributes imageAttrs = new ImageAttributes())
                        {
                            float opacity = layer.Opacity / 255f;
                            float[][] colorMatrixElements = new float[][]
                            {
                                new float[] { 1, 0, 0, 0, 0 },
                                new float[] { 0, 1, 0, 0, 0 },
                                new float[] { 0, 0, 1, 0, 0 },
                                new float[] { 0, 0, 0, opacity, 0 },
                                new float[] { 0, 0, 0, 0, 1 }
                            };
                            ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
                            imageAttrs.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                            Rectangle destRect = new Rectangle(0, 0, layer.Image.Width, layer.Image.Height);
                            e.Graphics.DrawImage(layer.Image, destRect, 0, 0, layer.Image.Width, layer.Image.Height, GraphicsUnit.Pixel, imageAttrs);
                        }
                    }
                    else
                    {
                        e.Graphics.DrawImage(layer.Image, 0, 0);
                    }
                }
            }
            else if (canvas != null)
            {
                e.Graphics.DrawImage(canvas, 0, 0);
            }

            // Viền mảnh bao quanh khung Canvas
            if (canvas != null)
            {
                using (Pen borderPen = new Pen(UITheme.BorderDefault, 1f / zoomScale))
                {
                    e.Graphics.DrawRectangle(borderPen, 0, 0, canvas.Width - 1, canvas.Height - 1);
                }
            }

            // Vẽ khung chọn SmartSelect
            if (currentTool == Tool.SmartSelect && drawing)
            {
                Rectangle selRect = GetRectangle(start, end);
                using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(35, 6, 182, 212)))
                {
                    e.Graphics.FillRectangle(fillBrush, selRect);
                }
                using (Pen dashPen = new Pen(UITheme.AccentSecondaryHover, 2f / zoomScale))
                {
                    dashPen.DashStyle = DashStyle.Dash;
                    e.Graphics.DrawRectangle(dashPen, selRect);
                }
            }

            // Vẽ hình Preview khi đang kéo chuột
            if (drawing && currentTool != Tool.Eraser && currentTool != Tool.SmartSelect)
            {
                DrawPreview(e.Graphics);
            }

            // Vẽ vòng tròn tâm cục tẩy
            if (currentTool == Tool.Eraser)
            {
                Point mousePos = pictureBox1.PointToClient(Cursor.Position);
                Point canvasPos = GetCanvasPoint(mousePos);
                int actualSize = Math.Max(1, (int)Math.Round(eraserSize / zoomScale));

                using (Pen p = new Pen(UITheme.AccentPrimaryHover, 1.5f / zoomScale))
                {
                    p.DashStyle = DashStyle.Dot;
                    e.Graphics.DrawEllipse(p, canvasPos.X - actualSize / 2f, canvasPos.Y - actualSize / 2f, actualSize, actualSize);
                }
            }
        }

        private void DrawPreview(Graphics g)
        {
            Rectangle rect = GetRect(start, end);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float previewPenWidth = Math.Max(pen.Width, 1f);

            switch (currentTool)
            {
                case Tool.Line:
                case Tool.WuLine:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        opacityPen.StartCap = opacityPen.EndCap = LineCap.Round;
                        g.DrawLine(opacityPen, start, end);
                    }
                    break;
                case Tool.Rectangle:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawRectangle(opacityPen, rect);
                    }
                    break;
                case Tool.Circle:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        int radius = Math.Min(rect.Width, rect.Height) / 2;
                        if (radius > 0)
                        {
                            int cx = rect.X + rect.Width / 2;
                            int cy = rect.Y + rect.Height / 2;
                            g.DrawEllipse(opacityPen, cx - radius, cy - radius, radius * 2, radius * 2);
                        }
                    }
                    break;
                case Tool.Triangle:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, GetTrianglePoints(rect));
                    }
                    break;
                case Tool.Star:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, CreateStarPoints(rect));
                    }
                    break;
                case Tool.Trapezoid:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, GetTrapezoidPoints(rect));
                    }
                    break;
                case Tool.Hexagon:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, GetHexagonPoints(rect));
                    }
                    break;
                case Tool.Diamond:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, GetDiamondPoints(rect));
                    }
                    break;
                case Tool.Arrows:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawPolygon(opacityPen, GetArrowPoints(rect));
                    }
                    break;
                case Tool.Ellipse:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        g.DrawEllipse(opacityPen, rect);
                    }
                    break;
                case Tool.Bezier:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        Point[] curvePoints = GetBezierControlPoints(rect);
                        if (curvePoints.Length >= 4)
                            g.DrawBeziers(opacityPen, curvePoints);
                    }
                    break;
                case Tool.RoundedRectangle:
                    using (Pen opacityPen = new Pen(GetColorWithOpacity(pen.Color), previewPenWidth))
                    {
                        int cornerRadius = Math.Min(rect.Width, rect.Height) / 4;
                        using (GraphicsPath path = GetRoundedRectanglePath(rect, cornerRadius))
                        {
                            g.DrawPath(opacityPen, path);
                        }
                    }
                    break;
            }
        }

        private Point[] GetBezierControlPoints(Rectangle rect)
        {
            return new Point[] {
                new Point(rect.Left, rect.Top + rect.Height / 2),
                new Point(rect.Left + rect.Width / 3, rect.Top),
                new Point(rect.Left + 2 * rect.Width / 3, rect.Bottom),
                new Point(rect.Right, rect.Top + rect.Height / 2)
            };
        }

        private void SaveState()
        {
            var snapshot = CloneCurrentLayers();

            if (snapshot != null && snapshot.Count > 0)
            {
                undoList.Add(snapshot);
                ClearStack(redoStack);

                if (undoList.Count > Max_undo_Steps)
                {
                    foreach (var layer in undoList[0])
                    {
                        layer.Dispose();
                    }
                    undoList.RemoveAt(0);
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                Redo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                btnSave_Click(this, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                btnImportImage_Click(this, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.E))
            {
                btnExport_Click(this, EventArgs.Empty);
                return true;
            }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
            {
                SetZoom(1.0f);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void Undo()
        {
            if (undoList.Count == 0) return;

            try
            {
                redoStack.Push(CloneCurrentLayers());

                int lastIdx = undoList.Count - 1;
                foreach (var l in layers) l.Image?.Dispose();

                layers = undoList[lastIdx];
                undoList.RemoveAt(lastIdx);
                currentLayerIndex = Math.Max(0, Math.Min(currentLayerIndex, Math.Max(0, layers.Count - 1)));
                isSaved = false;

                RecomposeCanvas();
                layerPanel?.RefreshLayersList(layers);
                layerPanel?.SetSelectedLayerIndex(currentLayerIndex);
                UpdateStatusBarInfo();
                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Undo error: " + ex.Message);
            }
        }

        public void Redo()
        {
            if (redoStack == null || redoStack.Count == 0) return;

            try
            {
                undoList.Add(CloneCurrentLayers());

                if (undoList.Count > Max_undo_Steps)
                {
                    foreach (var l in undoList[0])
                    {
                        l.Dispose();
                    }
                    undoList.RemoveAt(0);
                }

                if (layers != null)
                {
                    foreach (var l in layers)
                    {
                        l.Dispose();
                    }
                }

                layers = redoStack.Pop();
                currentLayerIndex = Math.Max(0, Math.Min(currentLayerIndex, Math.Max(0, layers.Count - 1)));
                isSaved = false;

                RecomposeCanvas();
                layerPanel?.RefreshLayersList(layers);
                layerPanel?.SetSelectedLayerIndex(currentLayerIndex);
                UpdateStatusBarInfo();
                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Redo error: " + ex.Message);
            }
        }

        private void RecomposeCanvas()
        {
            if (layers == null || layers.Count == 0) return;

            try
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White);

                    for (int i = layers.Count - 1; i >= 0; i--)
                    {
                        Layer layer = layers[i];
                        if (!layer.IsVisible) continue;

                        if (layer.Opacity < 255)
                        {
                            using (ImageAttributes imageAttrs = new ImageAttributes())
                            {
                                float opacity = layer.Opacity / 255f;
                                float[][] colorMatrixElements = new float[][]
                                {
                                    new float[] { 1, 0, 0, 0, 0 },
                                    new float[] { 0, 1, 0, 0, 0 },
                                    new float[] { 0, 0, 1, 0, 0 },
                                    new float[] { 0, 0, 0, opacity, 0 },
                                    new float[] { 0, 0, 0, 0, 1 }
                                };
                                ColorMatrix colorMatrix = new ColorMatrix(colorMatrixElements);
                                imageAttrs.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                                Rectangle destRect = new Rectangle(0, 0, layer.Image.Width, layer.Image.Height);
                                g.DrawImage(layer.Image, destRect, 0, 0, layer.Image.Width, layer.Image.Height, GraphicsUnit.Pixel, imageAttrs);
                            }
                        }
                        else
                        {
                            g.DrawImage(layer.Image, 0, 0);
                        }
                    }
                }
                pictureBox1.Invalidate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecomposeCanvas] Error: {ex.Message}");
            }
        }

        private List<Layer> CloneLayers(List<Layer> source)
        {
            List<Layer> newList = new List<Layer>();
            foreach (var l in source)
            {
                if (l == null || l.Image == null) continue;

                try
                {
                    int w = l.Image.Width;
                    int h = l.Image.Height;

                    Layer copy = new Layer(l.Name, w, h);
                    copy.IsVisible = l.IsVisible;
                    copy.Opacity = l.Opacity;
                    using (Graphics g = Graphics.FromImage(copy.Image))
                    {
                        g.DrawImage(l.Image, 0, 0);
                    }
                    newList.Add(copy);
                }
                catch
                {
                    newList.Add(new Layer(l.Name, canvas.Width, canvas.Height));
                }
            }
            return newList;
        }

        private void ClearStack(Stack<List<Layer>> stack)
        {
            while (stack.Count > 0)
            {
                var list = stack.Pop();
                foreach (var l in list)
                {
                    l.Dispose();
                }
            }
        }

        private void ClearUndoList()
        {
            foreach (var snapshot in undoList)
            {
                foreach (var l in snapshot)
                {
                    l.Dispose();
                }
            }
            undoList.Clear();
        }

        // Tool Button Click Handlers (cập nhật trạng thái active trực quan)
        private void btnLine_Click(object sender, EventArgs e) => SetActiveTool(Tool.Line);
        private void btnWuLine_Click(object sender, EventArgs e) => SetActiveTool(Tool.WuLine);
        private void btnRect_Click(object sender, EventArgs e) => SetActiveTool(Tool.Rectangle);
        private void btnCircle_Click(object sender, EventArgs e) => SetActiveTool(Tool.Circle);
        private void btnTriangle_Click(object sender, EventArgs e) => SetActiveTool(Tool.Triangle);
        private void btnEraser_Click(object sender, EventArgs e) => SetActiveTool(Tool.Eraser);
        private void btnStar_Click(object sender, EventArgs e) => SetActiveTool(Tool.Star);
        private void btnTrapezoid_Click(object sender, EventArgs e) => SetActiveTool(Tool.Trapezoid);
        private void BttHexagon_Click(object sender, EventArgs e) => SetActiveTool(Tool.Hexagon);
        private void BttArrows_Click(object sender, EventArgs e) => SetActiveTool(Tool.Arrows);
        private void BttDiamond_Click(object sender, EventArgs e) => SetActiveTool(Tool.Diamond);
        private void BttEllipse_Click(object sender, EventArgs e) => SetActiveTool(Tool.Ellipse);
        private void BttBezier_Click(object sender, EventArgs e) => SetActiveTool(Tool.Bezier);
        private void BttRoundedRectangle_Click(object sender, EventArgs e) => SetActiveTool(Tool.RoundedRectangle);
        private void btnSmartSelect_Click(object sender, EventArgs e) => SetActiveTool(Tool.SmartSelect);

        private void btnColor_Click(object sender, EventArgs e)
        {
            colorDialog1.Color = pen.Color;
            colorDialog1.FullOpen = true;
            if (colorDialog1.ShowDialog() == DialogResult.OK)
            {
                pen.Color = colorDialog1.Color;
                btnColor.Invalidate();
                if (panelQuickColors != null)
                {
                    foreach (Control c in panelQuickColors.Controls) c.Invalidate();
                }
            }
        }

        private void nudEraserSize_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                eraserSize = (int)nudEraserSize.Value;
                pictureBox1.Invalidate();
            }
            catch { }
        }

        private void btnImportImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "Chọn ảnh để import vào Layer mới";
                ofd.Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files (*.*)|*.*";
                ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    SaveState();
                    try
                    {
                        Bitmap loadedImage = new Bitmap(ofd.FileName);

                        if (loadedImage.Width > canvas.Width * 0.8 || loadedImage.Height > canvas.Height * 0.8)
                        {
                            int maxWidth = (int)(canvas.Width * 0.8);
                            int maxHeight = (int)(canvas.Height * 0.8);
                            float scale = Math.Min((float)maxWidth / loadedImage.Width, (float)maxHeight / loadedImage.Height);
                            int newWidth = (int)(loadedImage.Width * scale);
                            int newHeight = (int)(loadedImage.Height * scale);

                            Bitmap resizedImage = new Bitmap(newWidth, newHeight);
                            using (Graphics g = Graphics.FromImage(resizedImage))
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(loadedImage, 0, 0, newWidth, newHeight);
                            }
                            loadedImage.Dispose();
                            importedImage = resizedImage;
                        }
                        else
                        {
                            importedImage = loadedImage;
                        }

                        using (ImageProcessingDialog processingDialog = new ImageProcessingDialog())
                        {
                            if (processingDialog.ShowDialog(this) == DialogResult.OK)
                            {
                                importedImage = ApplyImageProcessing(importedImage, processingDialog);
                            }
                        }

                        importImagePos = new Point(
                            (canvas.Width - importedImage.Width) / 2,
                            (canvas.Height - importedImage.Height) / 2);

                        string imageLayerName = $"Ảnh ({DateTime.Now:HH:mm:ss})";
                        Layer imageLayer = new Layer(imageLayerName, canvas.Width, canvas.Height);

                        using (Graphics g = Graphics.FromImage(imageLayer.Image))
                        {
                            g.DrawImage(importedImage, importImagePos);
                        }

                        importedImage?.Dispose();

                        layers.Insert(0, imageLayer);
                        currentLayerIndex = 0;
                        isSaved = false;

                        if (layerPanel != null)
                        {
                            layerPanel.RefreshLayersList(layers);
                            layerPanel.SetSelectedLayerIndex(0);
                        }

                        RecomposeCanvas();
                        UpdateStatusBarInfo();
                        pictureBox1.Invalidate();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi khi load ảnh: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void trackBarOpacity_ValueChanged(object sender, EventArgs e)
        {
            lineOpacity = trackBarOpacity.Value;
            int opacityPercent = (int)Math.Round(lineOpacity / 255.0 * 100);
            lblOpacity.Text = $"Độ mờ: {opacityPercent}%";
            pictureBox1.Invalidate();
        }

        private void btnLayers_Click(object sender, EventArgs e)
        {
            if (layerPanel != null)
            {
                layerPanel.Visible = !layerPanel.Visible;
                UITheme.SetButtonSelected(btnLayers, layerPanel.Visible);
                if (layerPanel.Visible)
                {
                    layerPanel.BringToFront();
                }
            }
        }

        private void BtnTeddy3D_Click(object? sender, EventArgs e)
        {
            using (var teddyDialog = new PaintLike.Teddy.TeddyStudioDialog(InsertTeddy3DSnapshotToLayer))
            {
                teddyDialog.ShowDialog(this);
            }
        }

        private void InsertTeddy3DSnapshotToLayer(Bitmap rendered3DBitmap, string styleTag)
        {
            if (rendered3DBitmap == null) return;

            SaveState();
            string layerName = $"{styleTag} ({DateTime.Now:HH:mm:ss})";
            Layer teddyLayer = new Layer(layerName, canvas.Width, canvas.Height);

            using (Graphics g = Graphics.FromImage(teddyLayer.Image))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(1.0f, Math.Min((float)canvas.Width / rendered3DBitmap.Width, (float)canvas.Height / rendered3DBitmap.Height));
                int drawW = (int)(rendered3DBitmap.Width * scale);
                int drawH = (int)(rendered3DBitmap.Height * scale);
                int posX = (canvas.Width - drawW) / 2;
                int posY = (canvas.Height - drawH) / 2;
                g.DrawImage(rendered3DBitmap, posX, posY, drawW, drawH);
            }

            rendered3DBitmap.Dispose();

            layers.Insert(0, teddyLayer);
            currentLayerIndex = 0;
            isSaved = false;

            if (layerPanel != null)
            {
                layerPanel.RefreshLayersList(layers);
                layerPanel.SetSelectedLayerIndex(0);
            }

            RecomposeCanvas();
            UpdateStatusBarInfo();
            pictureBox1.Invalidate();
        }

        private List<Layer> CloneCurrentLayers()
        {
            if (layers == null) return new List<Layer>();
            return CloneLayers(layers);
        }

        private void btnUndo_Click(object sender, EventArgs e)
        {
            Undo();
        }

        private void btnRedo_Click(object sender, EventArgs e)
        {
            Redo();
        }

        private bool ExecuteSaveAction(string title, string filter)
        {
            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Title = title;
                saveFileDialog.Filter = filter;
                saveFileDialog.DefaultExt = "png";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        RecomposeCanvas();

                        string ext = System.IO.Path.GetExtension(saveFileDialog.FileName).ToLower();
                        ImageFormat format = ImageFormat.Png;

                        if (ext == ".jpg" || ext == ".jpeg")
                            format = ImageFormat.Jpeg;
                        else if (ext == ".bmp")
                            format = ImageFormat.Bmp;

                        canvas.Save(saveFileDialog.FileName, format);
                        isSaved = true;

                        MessageBox.Show("Lưu file thành công!", "Thông báo",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Có lỗi khi lưu file: " + ex.Message, "Lỗi",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            return false;
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (isSaved) return;

            DialogResult result = MessageBox.Show(
                "Bạn có muốn lưu bức vẽ trước khi thoát không?\n(Do you want to save changes before exiting?)",
                "Xác nhận thoát - PaintLike 3D",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (result == DialogResult.Yes)
            {
                if (!ExecuteSaveAction("Lưu hình ảnh", "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp"))
                {
                    e.Cancel = true;
                }
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            ExecuteSaveAction("Lưu hình ảnh", "PNG|*.png|JPEG|*.jpg|Bitmap|*.bmp");
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            ExecuteSaveAction("Xuất hình ảnh chất lượng cao", "PNG Image|*.png");
        }
    }
}
