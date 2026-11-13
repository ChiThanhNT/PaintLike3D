using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV3-05] Cửa sổ Không Gian Dựng Hình 3D Tự Do Teddy (ACM SIGGRAPH '99)
    /// Tích hợp đầy đủ toàn bộ các phép dựng hình của bài báo (Hình 4 trang 3):
    /// 1. CREATION: Vẽ nét khép kín tạo khối 3D tự do (CDT + Pruning + Chordal Axis + Oval Inflation).
    /// 2. PAINTING: Vẽ nét 3D lên bề mặt, Gạch xóa (Scribble), Cắt khối (Cutting), Khoanh vùng đỏ (Base Ring).
    /// 3. EXTRUSION: Đùn khối lồi 2 con trỏ (Extrusion), Khoét lõm (Cavity), Làm mượt Bezier (Smoothing), Đùn sau cắt.
    /// 4. BENDING: Uốn biến dạng mô hình bằng 2 nét vẽ (Reference Stroke -> Target Stroke).
    /// </summary>
    public class TeddyStudioDialog : Form
    {
        private readonly Action<Bitmap, string>? _onInsertToLayer;

        private TeddyMesh _currentMesh = new TeddyMesh();
        private readonly VirtualTrackballCamera _camera = new VirtualTrackballCamera();
        private TeddyRenderStyle _renderStyle = TeddyRenderStyle.PenAndInk;
        private TeddyGestureMode _gestureMode = TeddyGestureMode.Creation;
        private TeddyAlgorithmStepView _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;

        // Undo / Redo 3D Stack
        private readonly Stack<TeddyMesh> _undoStack = new Stack<TeddyMesh>();
        private readonly Stack<TeddyMesh> _redoStack = new Stack<TeddyMesh>();

        // Trạng thái vẽ nét 2D & Bending 2 nét
        private readonly List<PointF> _activeStroke = new List<PointF>();
        private readonly List<PointF> _bendReferenceStroke = new List<PointF>();
        private bool _isDrawingStroke = false;
        private bool _isBaseRingFromCut = false;
        private Color _surfacePaintColor = Color.FromArgb(30, 41, 59);
        private Color _modelBaseColor = Color.FromArgb(244, 162, 97);

        // UI Controls
        private Panel _panelTopRibbon = null!;
        private Panel _panelAlgoStepBar = null!;
        private Panel _viewportPanel = null!;
        private Panel _panelStatusBar = null!;
        private Label _lblModeBadge = null!;
        private Label _lblStatusText = null!;
        private Label _lblMeshStats = null!;
        private ToolTip _toolTip = null!;
        private readonly Dictionary<TeddyAlgorithmStepView, Button> _stepViewButtons = new Dictionary<TeddyAlgorithmStepView, Button>();

        private Button _btnInitBlank = null!;
        private Button _btnSphere = null!;
        private Button _btnCube = null!;
        private Button _btnBendMode = null!;
        private Button _btnUndo = null!;
        private Button _btnRedo = null!;
        private Button _btnStylePenInk = null!;
        private Button _btnStyleSmooth = null!;
        private Button _btnStyleWire = null!;
        private Button _btnModelColor = null!;
        private Button _btnPaintColor = null!;
        private Button _btnSmoothMesh = null!;
        private Button _btnResetCam = null!;
        private Button _btnImportObj = null!;
        private Button _btnExportObj = null!;
        private Button _btnSendToLayer = null!;

        public TeddyStudioDialog(Action<Bitmap, string>? onInsertToLayer = null)
        {
            _onInsertToLayer = onInsertToLayer;

            this.Text = "Teddy 3D Freeform Modeler — Dựng Hình 3D Tự Do Từ Nét Vẽ 2D (SIGGRAPH '99)";
            this.ClientSize = new Size(1220, 740);
            this.MinimumSize = new Size(1024, 620);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = UITheme.BgWorkspace;
            this.ForeColor = UITheme.TextPrimary;
            this.Font = UITheme.FontBody;
            this.DoubleBuffered = true;
            this.KeyPreview = true;

            InitializeStudioLayout();
            UpdateStyleButtonSelection();
            UpdateGestureModeState("Sẵn sàng: Vẽ một nét khép kín bằng Chuột Trái để tạo mô hình 3D, hoặc chọn Khối Cầu / Khối Hộp mẫu.");
        }

        private void InitializeStudioLayout()
        {
            _toolTip = new ToolTip
            {
                InitialDelay = 200,
                ReshowDelay = 100,
                AutoPopDelay = 8000,
                ShowAlways = true
            };

            // 1. Thanh công cụ Ribbon phía trên
            _panelTopRibbon = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = UITheme.BgToolbar
            };
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, _panelTopRibbon, new object[] { true });
            _panelTopRibbon.Paint += PanelTopRibbon_Paint;

            // --- Nhóm 1: KHỞI TẠO, BIẾN DẠNG & LỊCH SỬ (X: 10..350) ---
            _btnInitBlank = CreateRibbonButton(new Rectangle(18, 12, 80, 30), IconType.Plus, "Init Mới", ButtonVariant.Primary,
                "Khởi tạo vùng vẽ mới để phác thảo nét 2D tạo khối 3D (Press 'Init' button - Hình 1 & 4)");
            _btnInitBlank.Click += (s, e) => ResetToBlankCreationMode();

            _btnSphere = CreateRibbonButton(new Rectangle(102, 12, 78, 30), IconType.Circle, "Khối Cầu", ButtonVariant.Default,
                "Tạo sẵn khối Cầu 3D mẫu (Sphere - Mục 4.2)");
            _btnSphere.Click += (s, e) => LoadPrimitiveMesh(TeddyMesh.CreateSphere(135f, 28, 18), "Đã nạp Khối Cầu 3D mẫu. Khoanh tròn trên mặt để Đùn (Extrude), hoặc kẻ xuyên qua để Cắt (Cut).");

            _btnCube = CreateRibbonButton(new Rectangle(184, 12, 78, 30), IconType.Rectangle, "Khối Hộp", ButtonVariant.Default,
                "Tạo sẵn khối Lập phương chia lưới mịn (Subdivided Cube - Mục 4.2)");
            _btnCube.Click += (s, e) => LoadPrimitiveMesh(TeddyMesh.CreateCube(105f, 6), "Đã nạp Khối Hộp 3D mẫu. Khoanh tròn trên mặt để Đùn (Extrude), hoặc kẻ xuyên qua để Cắt (Cut).");

            _btnBendMode = CreateRibbonButton(new Rectangle(266, 12, 78, 30), IconType.Bezier, "Uốn (Bend)", ButtonVariant.Default,
                "Chế độ Uốn biến dạng mô hình bằng 2 nét vẽ: Nét 1 (Trục chuẩn Reference) -> Nét 2 (Trục đích Target) (Mục 4.7 - Hình 11)");
            _btnBendMode.Click += (s, e) => ToggleBendingMode();

            _btnUndo = CreateRibbonButton(new Rectangle(18, 46, 80, 28), IconType.Undo, "Hoàn tác", ButtonVariant.Default,
                "Hoàn tác bước dựng hình 3D trước đó (Ctrl+Z)");
            _btnUndo.Click += (s, e) => PerformUndo();

            _btnRedo = CreateRibbonButton(new Rectangle(102, 46, 78, 28), IconType.Redo, "Làm lại", ButtonVariant.Default,
                "Làm lại bước vừa hoàn tác (Ctrl+Y)");
            _btnRedo.Click += (s, e) => PerformRedo();

            _btnSmoothMesh = CreateRibbonButton(new Rectangle(184, 46, 78, 28), IconType.Filter, "Làm mịn", ButtonVariant.Default,
                "Làm mịn toàn bộ bề mặt lưới 3D bằng thuật toán Taubin Surface Fairing (Mục 5.1 & 5.5)");
            _btnSmoothMesh.Click += (s, e) =>
            {
                if (_currentMesh.IsEmpty) return;
                PushUndoState();
                _currentMesh.SmoothMesh(2, 0.42f, -0.44f);
                UpdateGestureModeState("Đã làm mịn toàn bộ bề mặt lưới 3D (Taubin Fairing).");
                _viewportPanel.Invalidate();
            };

            Button btnFillRegion = CreateRibbonButton(new Rectangle(266, 46, 78, 28), IconType.ColorPalette, "Tô vùng", ButtonVariant.Default,
                "Tô màu các mặt tam giác nằm trong Vòng chọn đỏ bằng Màu bút hiện tại (Phím tắt: F - Hình 2 bài báo)");
            btnFillRegion.Click += (s, e) => FillSelectedBaseRingWithColor();

            // --- Nhóm 2: KIỂU HIỂN THỊ 3D & CAMERA (X: 360..704) ---
            _btnStylePenInk = CreateRibbonButton(new Rectangle(368, 12, 106, 30), IconType.Rename, "Pen & Ink", ButtonVariant.Default,
                "Chế độ phác họa nét mực + đường viền đen Silhouette thời gian thực (Hình 1 bài báo SIGGRAPH '99)");
            _btnStylePenInk.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.PenAndInk);

            _btnStyleSmooth = CreateRibbonButton(new Rectangle(478, 12, 106, 30), IconType.Opacity, "Tô bóng 3D", ButtonVariant.Default,
                "Chế độ tô bóng khối Studio 3D mượt (Phong / Lambert + Rim Light)");
            _btnStyleSmooth.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.SmoothShaded);

            _btnStyleWire = CreateRibbonButton(new Rectangle(588, 12, 108, 30), IconType.Hexagon, "Lưới + Spine", ButtonVariant.Default,
                "Hiển thị cấu trúc lưới tam giác và Trục Xương Sống (Chordal Axis / Spine - Hình 12 & 13e)");
            _btnStyleWire.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.WireframeSpine);

            _btnResetCam = CreateRibbonButton(new Rectangle(368, 46, 106, 28), IconType.ZoomIn, "Reset Góc", ButtonVariant.Default,
                "Đưa góc xoay Camera Trackball và độ phóng đại về mặc định");
            _btnResetCam.Click += (s, e) =>
            {
                _camera.Reset();
                _viewportPanel.Invalidate();
            };

            _btnModelColor = new Button { Bounds = new Rectangle(478, 46, 106, 28) };
            UITheme.StyleButton(_btnModelColor, IconType.ColorPalette, "Màu khối", ButtonVariant.Default, false,
                _toolTip, "Đổi màu sắc chất liệu của mô hình 3D", () => _modelBaseColor);
            _btnModelColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _modelBaseColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _modelBaseColor = cd.Color;
                    _currentMesh.BaseColor = _modelBaseColor;
                    _btnModelColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnModelColor);

            _btnPaintColor = new Button { Bounds = new Rectangle(588, 46, 108, 28) };
            UITheme.StyleButton(_btnPaintColor, IconType.ColorPalette, "Màu bút", ButtonVariant.Default, false,
                _toolTip, "Đổi màu bút vẽ trang trí trực tiếp lên bề mặt 3D (Surface Painting - Mục 4.3)", () => _surfacePaintColor);
            _btnPaintColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _surfacePaintColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _surfacePaintColor = cd.Color;
                    _btnPaintColor.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnPaintColor);

            // --- Nhóm 3: TỆP .OBJ & TÍCH HỢP LAYER 2D (X: 712..916) ---
            _btnImportObj = CreateRibbonButton(new Rectangle(720, 12, 90, 30), IconType.Import, "Mở .OBJ", ButtonVariant.Default,
                "Mở file mô hình 3D định dạng chuẩn Wavefront .OBJ từ máy tính");
            _btnImportObj.Click += BtnImportObj_Click;

            _btnExportObj = CreateRibbonButton(new Rectangle(814, 12, 94, 30), IconType.Save, "Lưu .OBJ", ButtonVariant.Accent,
                "Xuất mô hình 3D hiện tại ra file .OBJ tiêu chuẩn (Mục 6 bài báo)");
            _btnExportObj.Click += BtnExportObj_Click;

            _btnSendToLayer = CreateRibbonButton(new Rectangle(720, 46, 188, 28), IconType.Layers, "Chèn vào Layer 2D", ButtonVariant.Success,
                "Kết xuất góc nhìn 3D hiện tại (nền trong suốt) và thêm thành một Layer mới trong PaintLike");
            _btnSendToLayer.Click += BtnSendToLayer_Click;

            // 1B. Thanh Trực Quan Hóa Từng Bước Thuật Toán (Hình 13, 14, 15) & Mẫu Thử Bài Báo (Hình 1 & 5)
            _panelAlgoStepBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(12, 18, 34)
            };
            _panelAlgoStepBar.Paint += (s, e) =>
            {
                using Pen bottomLine = new Pen(UITheme.BorderDefault, 1f);
                e.Graphics.DrawLine(bottomLine, 0, _panelAlgoStepBar.Height - 1, _panelAlgoStepBar.Width, _panelAlgoStepBar.Height - 1);
            };

            Label lblPresets = new Label
            {
                AutoSize = true,
                Location = new Point(12, 10),
                Font = UITheme.FontGroupHeader,
                ForeColor = UITheme.AccentPrimaryHover,
                Text = "MẪU BÀI BÁO:"
            };
            _panelAlgoStepBar.Controls.Add(lblPresets);

            CreatePresetButton(new Rectangle(100, 5, 86, 25), "Gấu Teddy", "TeddySilhouette", Color.FromArgb(244, 162, 97),
                "Tạo tự động mẫu Gấu Teddy từ đường viền 2D (Hình 1 & Hình 2 bài báo)");
            CreatePresetButton(new Rectangle(190, 5, 92, 25), "Quả Cherry", "Cherry", Color.FromArgb(239, 68, 68),
                "Mẫu Quả Cherry (Hình 5c): Minh họa vùng rộng phồng to (quả), vùng hẹp phồng mỏng (cuống)");
            CreatePresetButton(new Rectangle(286, 5, 82, 25), "Con Rắn", "Snake", Color.FromArgb(34, 197, 94),
                "Mẫu Con Rắn uốn lượn (Hình 5a): Minh họa trục xương sống Chordal Axis uốn cong");

            Label lblSteps = new Label
            {
                AutoSize = true,
                Location = new Point(382, 10),
                Font = UITheme.FontGroupHeader,
                ForeColor = UITheme.TextSecondary,
                Text = "MINH HỌA THUẬT TOÁN (HÌNH 13–15):"
            };
            _panelAlgoStepBar.Controls.Add(lblSteps);

            AddStepViewButton(new Rectangle(596, 5, 96, 25), "0. Khối 3D", TeddyAlgorithmStepView.Final3DModel,
                "Hiển thị mô hình 3D hoàn chỉnh sau khi khâu lưới và làm mịn");
            AddStepViewButton(new Rectangle(696, 5, 100, 25), "1. Đa giác 13a", TeddyAlgorithmStepView.Step1_InitialPolygon2D,
                "Hình 13a: Đa giác phẳng 2D với các cạnh biên có độ dài đều nhau (Uniform External Edges)");
            AddStepViewButton(new Rectangle(800, 5, 106, 25), "2. CDT T/S/J 13b", TeddyAlgorithmStepView.Step2_CDT_T_S_J,
                "Hình 13b: Lưới Constrained Delaunay Triangulation & Phân loại tam giác Terminal (T), Sleeve (S), Junction (J)");
            AddStepViewButton(new Rectangle(910, 5, 100, 25), "3. Trục Thô 13c", TeddyAlgorithmStepView.Step3_RawChordalAxis,
                "Hình 13c: Trục Chordal Axis thô trước khi cắt tỉa nhánh phụ");
            AddStepViewButton(new Rectangle(1014, 5, 98, 25), "4. Spine 13e-f", TeddyAlgorithmStepView.Step4_PruningAndSpine,
                "Hình 13e-f & Hình 14: Cắt tỉa nửa đường tròn (Pruning), chia quạt Fan và rút trích trục Spine");
            AddStepViewButton(new Rectangle(1116, 5, 94, 25), "5. Oval 3D 15c", TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D,
                "Hình 15b-c: Nâng cao độ Z cho trục Spine và tạo các cung 1/4 hình elip (Xoay được bằng Chuột Phải!)");

            SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);

            // 2. Thanh trạng thái phía dưới (Status Bar)
            _panelStatusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = UITheme.BgStatusBar
            };
            _panelStatusBar.Paint += (s, e) =>
            {
                using Pen topBorder = new Pen(UITheme.BorderDefault, 1f);
                e.Graphics.DrawLine(topBorder, 0, 0, _panelStatusBar.Width, 0);
            };

            _lblModeBadge = new Label
            {
                AutoSize = false,
                Location = new Point(10, 4),
                Size = new Size(155, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UITheme.FontSmallBold,
                BackColor = UITheme.AccentPrimary,
                ForeColor = Color.White,
                Text = "MODE: CREATION"
            };
            _panelStatusBar.Controls.Add(_lblModeBadge);

            _lblStatusText = new Label
            {
                AutoSize = false,
                Location = new Point(174, 2),
                Size = new Size(780, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UITheme.FontSmall,
                ForeColor = UITheme.TextPrimary,
                Text = ""
            };
            _panelStatusBar.Controls.Add(_lblStatusText);

            _lblMeshStats = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 240,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 12, 0),
                Font = UITheme.FontMono,
                ForeColor = UITheme.TextSecondary,
                Text = "Vertices: 0 | Faces: 0"
            };
            _panelStatusBar.Controls.Add(_lblMeshStats);

            // 3. Khung Viewport 3D trung tâm
            _viewportPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UITheme.BgWorkspace,
                Cursor = Cursors.Cross
            };
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, _viewportPanel, new object[] { true });

            _viewportPanel.Paint += ViewportPanel_Paint;
            _viewportPanel.MouseDown += ViewportPanel_MouseDown;
            _viewportPanel.MouseMove += ViewportPanel_MouseMove;
            _viewportPanel.MouseUp += ViewportPanel_MouseUp;
            this.MouseWheel += ViewportPanel_MouseWheel;

            this.Controls.Add(_viewportPanel);
            this.Controls.Add(_panelAlgoStepBar);
            this.Controls.Add(_panelTopRibbon);
            this.Controls.Add(_panelStatusBar);
        }

        private void CreatePresetButton(Rectangle bounds, string label, string presetKey, Color presetColor, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, IconType.Star, label, ButtonVariant.Default, false, _toolTip, tooltip);
            btn.Click += (s, e) => RunPaperPreset(presetKey, presetColor, label);
            _panelAlgoStepBar.Controls.Add(btn);
        }

        private void AddStepViewButton(Rectangle bounds, string label, TeddyAlgorithmStepView step, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, IconType.None, label, ButtonVariant.Default, false, _toolTip, tooltip);
            btn.Click += (s, e) => SetAlgorithmStepView(step);
            _panelAlgoStepBar.Controls.Add(btn);
            _stepViewButtons[step] = btn;
        }

        private void SetAlgorithmStepView(TeddyAlgorithmStepView step)
        {
            if (step != TeddyAlgorithmStepView.Final3DModel && (_currentMesh.IsEmpty || _currentMesh.DebugInfo == null))
            {
                UpdateGestureModeState("Hãy vẽ một nét 2D hoặc bấm nút Mẫu Bài Báo ('Gấu Teddy', 'Quả Cherry', 'Con Rắn') để xem từng bước thuật toán Hình 13–15!");
                return;
            }

            _algorithmStepView = step;
            foreach (var kvp in _stepViewButtons)
            {
                UITheme.SetButtonSelected(kvp.Value, kvp.Key == step);
            }
            _viewportPanel?.Invalidate();
        }

        private void RunPaperPreset(string presetKey, Color presetColor, string displayName)
        {
            Size vp = _viewportPanel.ClientSize;
            if (vp.Width < 100 || vp.Height < 100) vp = new Size(1000, 560);

            _camera.Reset();
            _modelBaseColor = presetColor;
            _btnModelColor.Invalidate();

            List<PointF> sampleStroke = TeddyMeshOptimizer.CreatePaperSampleStroke(presetKey, vp);
            if (TeddyInflationEngine.TryCreateMeshFromStroke(
                sampleStroke,
                _camera,
                vp,
                _modelBaseColor,
                out TeddyMesh? created,
                out string msg) && created != null)
            {
                if (!_currentMesh.IsEmpty) PushUndoState();
                TeddyMeshOptimizer.ApplyPaperPresetPainting(created, presetKey);
                _currentMesh = created;
                _isBaseRingFromCut = false;
                _gestureMode = TeddyGestureMode.Painting;
                // Xoay nhẹ góc 3D để nhìn rõ độ phồng của mẫu bài báo
                _camera.Rotation = Quaternion.CreateFromYawPitchRoll(0.32f, -0.18f, 0f);
                SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
                UpdateGestureModeState($"Đã dựng mẫu bài báo '{displayName}' (Hình 2 & Hình 5): {msg} Bấm phím 1 → 5 để xem từng bước giải thuật!");
                _viewportPanel.Invalidate();
            }
        }

        private Button CreateRibbonButton(Rectangle bounds, IconType icon, string text, ButtonVariant variant, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, icon, text, variant, false, _toolTip, tooltip);
            _panelTopRibbon.Controls.Add(btn);
            return btn;
        }

        private void PanelTopRibbon_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(UITheme.BgToolbar);

            UITheme.DrawRibbonGroup(g, new Rectangle(10, 5, 342, 85), "KHỞI TẠO & PHÉP BIẾN ĐỔI (TV1 & TV2)");
            UITheme.DrawRibbonGroup(g, new Rectangle(360, 5, 344, 85), "CAMERA TRACKBALL & RENDER (TV3)");
            UITheme.DrawRibbonGroup(g, new Rectangle(712, 5, 204, 85), "XUẤT .OBJ & LAYER 2D");

            // Bảng tóm tắt cử chỉ Hình 4 bên phải thanh công cụ
            Rectangle helpRect = new Rectangle(924, 6, Math.Max(220, _panelTopRibbon.Width - 934), 83);
            using (GraphicsPath hp = UITheme.CreateRoundedRect(helpRect, 8))
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(20, 29, 47)))
            using (Pen border = new Pen(Color.FromArgb(38, 52, 77), 1f))
            {
                g.FillPath(hb, hp);
                g.DrawPath(border, hp);
            }

            string guide =
                "• Khoanh tròn kín trên mặt → Vòng đỏ (Extrusion Mode)\r\n" +
                "• Từ Vòng đỏ: Vẽ vòm để Đùn lồi / Khoét lõm, Zíc-zắc để Làm mượt\r\n" +
                "• Kẻ xuyên qua vật thể → Cắt bỏ bên trái (Cutting - Mục 4.5)\r\n" +
                "• Bấm 'Uốn (Bend)' → Vẽ 2 nét (Trục gốc → Trục đích) để uốn";
            TextRenderer.DrawText(g, guide, UITheme.FontSmall, new Rectangle(helpRect.X + 8, helpRect.Y + 3, helpRect.Width - 12, helpRect.Height - 6),
                UITheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            using Pen bottomLine = new Pen(UITheme.BorderDefault, 1f);
            g.DrawLine(bottomLine, 0, _panelTopRibbon.Height - 1, _panelTopRibbon.Width, _panelTopRibbon.Height - 1);
        }

        // =========================================================================
        // XỬ LÝ TƯƠNG TÁC CHUỘT & MÁY TRẠNG THÁI CỬ CHỈ ĐẦY ĐỦ (HÌNH 4 BÀI BÁO)
        // =========================================================================

        private void ViewportPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _camera.StartRotate(e.Location);
                _viewportPanel.Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _camera.StartPan(e.Location);
                _viewportPanel.Cursor = Cursors.Hand;
            }
            else if (e.Button == MouseButtons.Left)
            {
                _isDrawingStroke = true;
                _activeStroke.Clear();
                _activeStroke.Add(e.Location);
                _viewportPanel.Invalidate();
            }
        }

        private void ViewportPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _camera.OnMouseMove(e.Location, _viewportPanel.ClientSize);
                _viewportPanel.Invalidate();
            }
            else if (_isDrawingStroke && e.Button == MouseButtons.Left)
            {
                if (_activeStroke.Count == 0 || StrokeProcessor.Distance(_activeStroke[_activeStroke.Count - 1], e.Location) >= 2.5f)
                {
                    _activeStroke.Add(e.Location);
                    _viewportPanel.Invalidate();
                }
            }
        }

        private void ViewportPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _camera.StopInteraction();
                _viewportPanel.Cursor = Cursors.Cross;
                return;
            }

            if (e.Button == MouseButtons.Left && _isDrawingStroke)
            {
                _isDrawingStroke = false;
                ProcessCompletedUserStroke(_activeStroke);
                _activeStroke.Clear();
                _viewportPanel.Invalidate();
            }
        }

        private void ViewportPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            _camera.OnMouseWheel(e.Delta);
            _viewportPanel.Invalidate();
        }

        /// <summary>
        /// Máy trạng thái cử chỉ hoàn chỉnh theo Sơ đồ Hình 4 (trang 3 bài báo Teddy SIGGRAPH '99):
        /// - CREATION MODE -> Tạo vật thể mới.
        /// - BENDING MODE -> Nét 1 (Reference) -> Nét 2 (Target) -> Uốn mô hình.
        /// - EXTRUSION MODE -> Click (Thoát/Hoàn tất cắt), Scribble (Làm mượt vùng chọn), Normal Stroke (Đùn khối / Khoét lõm).
        /// - PAINTING MODE -> Cắt khối (Cutting), Gạch xóa (Erasing), Khoanh vùng đùn (Select Ring), Vẽ trên bề mặt (Surface Paint).
        /// </summary>
        private void ProcessCompletedUserStroke(List<PointF> rawStroke)
        {
            // ---------------------------------------------------------------------
            // 1. CHẾ ĐỘ CREATION: Tạo mô hình 3D mới từ đường viền 2D (Mục 4.2 & 5.1)
            // ---------------------------------------------------------------------
            if (_currentMesh.IsEmpty || _gestureMode == TeddyGestureMode.Creation)
            {
                if (TeddyInflationEngine.TryCreateMeshFromStroke(
                    rawStroke,
                    _camera,
                    _viewportPanel.ClientSize,
                    _modelBaseColor,
                    out TeddyMesh? createdMesh,
                    out string msg) && createdMesh != null)
                {
                    PushUndoState();
                    _currentMesh = createdMesh;
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState(msg);
                }
                else
                {
                    UpdateGestureModeState(msg);
                }
                return;
            }

            // ---------------------------------------------------------------------
            // 2. CHẾ ĐỘ BENDING: Uốn biến dạng bằng 2 nét vẽ (Mục 4.7 - Hình 11)
            // ---------------------------------------------------------------------
            if (_gestureMode == TeddyGestureMode.Bending)
            {
                float len = StrokeProcessor.ComputePolylineLength(rawStroke, false);
                if (len < 20f)
                {
                    // Click đơn trong chế độ Bending -> Thoát chế độ Bending
                    _bendReferenceStroke.Clear();
                    _gestureMode = TeddyGestureMode.Painting;
                    UITheme.SetButtonSelected(_btnBendMode, false);
                    UpdateGestureModeState("Đã hủy chế độ Uốn (Bending) và quay lại chế độ PAINTING.");
                    return;
                }

                if (_bendReferenceStroke.Count == 0)
                {
                    // Nét thứ 1: Lưu Reference Stroke
                    _bendReferenceStroke.AddRange(StrokeProcessor.ResampleUniform(rawStroke, 8f, false));
                    UpdateGestureModeState("Đã ghi nhận Nét 1 (Trục chuẩn Reference). Hãy vẽ tiếp Nét 2 (Trục đích Target) để uốn mô hình!");
                }
                else
                {
                    // Nét thứ 2: Thực hiện biến dạng từ Reference Stroke sang Target Stroke
                    PushUndoState();
                    if (TeddySmoothBendOp.TryBendMeshWithStrokes(
                        _currentMesh,
                        _bendReferenceStroke,
                        rawStroke,
                        _camera,
                        _viewportPanel.ClientSize,
                        out string bendMsg))
                    {
                        _bendReferenceStroke.Clear();
                        _gestureMode = TeddyGestureMode.Painting;
                        UITheme.SetButtonSelected(_btnBendMode, false);
                        UpdateGestureModeState(bendMsg);
                    }
                    else
                    {
                        _redoStack.Clear();
                        if (_undoStack.Count > 0) _currentMesh = _undoStack.Pop();
                        _bendReferenceStroke.Clear();
                        UpdateGestureModeState(bendMsg);
                    }
                }
                return;
            }

            // ---------------------------------------------------------------------
            // 3. CHẾ ĐỘ EXTRUSION: Đang có vòng chọn màu đỏ (ActiveBaseRing) (Mục 4.4, 4.6, 5.3, 5.5)
            // ---------------------------------------------------------------------
            if (_gestureMode == TeddyGestureMode.Extrusion && _currentMesh.ActiveBaseRing != null)
            {
                float strokeLen = StrokeProcessor.ComputePolylineLength(rawStroke, false);

                // 3a. Click đơn: Kết thúc cắt (Hình 3q) hoặc đổi vòng đỏ thành nét vẽ bề mặt (Hình 7d-e)
                if (rawStroke.Count <= 4 || strokeLen < 14f)
                {
                    PushUndoState();
                    if (!_isBaseRingFromCut)
                    {
                        var ringAsPaint = _currentMesh.ActiveBaseRing.Clone();
                        ringAsPaint.StrokeColor = _surfacePaintColor;
                        ringAsPaint.StrokeWidth = 2.8f;
                        _currentMesh.SurfaceLines.Add(ringAsPaint);
                        UpdateGestureModeState("Click đơn: Đã chuyển vòng chọn đỏ thành nét vẽ bề mặt thông thường (Hình 7d-e).");
                    }
                    else
                    {
                        UpdateGestureModeState("Click đơn: Đã hoàn tất phép Cắt khối (Cutting - Hình 3q & Hình 8d).");
                    }

                    _currentMesh.ActiveBaseRing = null;
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    return;
                }

                // 3b. Cử chỉ gạch zíc-zắc (Scribbling: sl/pl > 1.5) -> Làm mượt vùng chọn bằng nội suy Bezier (Mục 4.6 & 5.5)
                if (StrokeProcessor.IsScribbling(rawStroke, out float scribbleRatio))
                {
                    PushUndoState();
                    if (TeddySmoothBendOp.TrySmoothRegionInBaseRing(_currentMesh, out string smoothMsg))
                    {
                        _isBaseRingFromCut = false;
                        _gestureMode = TeddyGestureMode.Painting;
                        UpdateGestureModeState($"Scribbling (sl/pl={scribbleRatio:F2}): {smoothMsg}");
                    }
                    else
                    {
                        if (_undoStack.Count > 0) _currentMesh = _undoStack.Pop();
                        UpdateGestureModeState(smoothMsg);
                    }
                    return;
                }

                // 3c. Nét vẽ thông thường -> Đùn khối lồi (Extrusion) hoặc Khoét lõm (Cavity) bằng thuật toán 2 con trỏ (Mục 5.3)
                PushUndoState();
                if (TeddyExtrudeCutOp.TryExtrudeOrDigFromBaseRing(
                    _currentMesh,
                    rawStroke,
                    _camera,
                    _viewportPanel.ClientSize,
                    out string extrudeMsg))
                {
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState(extrudeMsg);
                }
                else
                {
                    if (_undoStack.Count > 0) _currentMesh = _undoStack.Pop();
                    UpdateGestureModeState(extrudeMsg);
                }
                return;
            }

            // ---------------------------------------------------------------------
            // 4. CHẾ ĐỘ PAINTING: Phân tích ý định nét vẽ theo Sơ đồ Hình 4 (Mục 4.3, 4.4, 4.5)
            // ---------------------------------------------------------------------
            PaintingStrokeIntent intent = TeddySurfacePaintOp.AnalyzeStrokeIntent(
                _currentMesh,
                rawStroke,
                _camera,
                _viewportPanel.ClientSize,
                out List<Vector3> projectedSurfacePts,
                out float ratioSlPl);

            switch (intent)
            {
                case PaintingStrokeIntent.ErasePaintedStrokes:
                    PushUndoState();
                    int erased = TeddyRaycaster.EraseSurfaceLines(_currentMesh, rawStroke, _camera, _viewportPanel.ClientSize);
                    UpdateGestureModeState($"Cử chỉ Scribbling (sl/pl = {ratioSlPl:F2} > 1.5): Đã xóa {erased} nét vẽ trên bề mặt (Mục 4.3 - Hình 3u-w).");
                    break;

                case PaintingStrokeIntent.CutObject:
                    PushUndoState();
                    if (TeddyExtrudeCutOp.TryCutMeshWithStroke(
                        _currentMesh,
                        rawStroke,
                        _camera,
                        _viewportPanel.ClientSize,
                        out string cutMsg))
                    {
                        _isBaseRingFromCut = true;
                        _gestureMode = TeddyGestureMode.Extrusion;
                        UpdateGestureModeState(cutMsg);
                    }
                    else
                    {
                        if (_undoStack.Count > 0) _currentMesh = _undoStack.Pop();
                        UpdateGestureModeState(cutMsg);
                    }
                    break;

                case PaintingStrokeIntent.SelectExtrusionRing:
                    PushUndoState();
                    Vector3 ringNormal = TeddySurfacePaintOp.SetExtrusionBaseRing(_currentMesh, projectedSurfacePts);
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Extrusion;
                    UpdateGestureModeState($"Chế độ EXTRUSION (Vòng đỏ): Hãy xoay Chuột Phải nghiêng mô hình rồi vẽ nét vòm để Đùn lồi / Khoét lõm, hoặc gạch Zíc-zắc để Làm mượt!");
                    break;

                case PaintingStrokeIntent.PaintOnSurface:
                    PushUndoState();
                    TeddySurfacePaintOp.AddSurfaceLine(_currentMesh, projectedSurfacePts, _surfacePaintColor, 2.8f);
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState($"Đã vẽ nét 3D bám trên bề mặt ({projectedSurfacePts.Count} đoạn - Mục 5.2).");
                    break;

                default:
                    UpdateGestureModeState("Nét vẽ nằm ngoài vật thể. Hãy vẽ trên bề mặt để tô nét/khoanh vùng đùn, hoặc kẻ xuyên qua vật thể để Cắt khối.");
                    break;
            }
        }

        private void ViewportPanel_Paint(object? sender, PaintEventArgs e)
        {
            if (_algorithmStepView != TeddyAlgorithmStepView.Final3DModel && !_currentMesh.IsEmpty && _currentMesh.DebugInfo != null)
            {
                TeddyRenderer.RenderAlgorithmStep(
                    e.Graphics,
                    _viewportPanel.ClientSize,
                    _currentMesh,
                    _camera,
                    _algorithmStepView);
                return;
            }

            Color activeStrokePreviewColor = _gestureMode switch
            {
                TeddyGestureMode.Creation => Color.FromArgb(220, 38, 38),
                TeddyGestureMode.Extrusion => Color.FromArgb(239, 68, 68),
                TeddyGestureMode.Bending => _bendReferenceStroke.Count == 0
                    ? Color.FromArgb(6, 182, 212)
                    : Color.FromArgb(245, 158, 11),
                _ => _surfacePaintColor
            };

            TeddyRenderer.RenderScene(
                e.Graphics,
                _viewportPanel.ClientSize,
                _currentMesh,
                _camera,
                _renderStyle,
                _isDrawingStroke ? _activeStroke : null,
                activeStrokePreviewColor,
                drawBackground: true);

            // Nếu đang ở chế độ BENDING và đã vẽ Nét 1 (Reference Stroke), hiển thị đường chuẩn màu Cyan đứt nét
            if (_gestureMode == TeddyGestureMode.Bending && _bendReferenceStroke.Count > 1)
            {
                using (Pen refPen = new Pen(Color.FromArgb(6, 182, 212), 3.0f))
                {
                    refPen.DashPattern = new float[] { 3f, 2f };
                    refPen.StartCap = LineCap.Round;
                    refPen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(refPen, _bendReferenceStroke.ToArray());
                }
            }

            if (_currentMesh.IsEmpty && !_isDrawingStroke)
            {
                DrawEmptyCanvasHint(e.Graphics, _viewportPanel.ClientSize);
            }
        }

        private static void DrawEmptyCanvasHint(Graphics g, Size vp)
        {
            int boxW = Math.Min(560, vp.Width - 40);
            int boxH = 114;
            Rectangle box = new Rectangle((vp.Width - boxW) / 2, (vp.Height - boxH) / 2, boxW, boxH);

            using (GraphicsPath path = UITheme.CreateRoundedRect(box, 12))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(215, 15, 23, 42)))
            using (Pen border = new Pen(Color.FromArgb(140, 99, 102, 241), 1.5f))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            string title = "CHẾ ĐỘ TẠO KHỐI TỰ DO (TEDDY CREATION MODE)";
            string sub = "Giữ Chuột Trái vẽ một đường viền khép kín bất kỳ trên vùng vẽ\r\n" +
                         "Hệ thống sẽ tự động chia lưới Delaunay, rút trích xương sống Spine và thổi phồng thành mô hình 3D!";

            TextRenderer.DrawText(g, title, UITheme.FontSubtitle,
                new Rectangle(box.X + 12, box.Y + 14, box.Width - 24, 24),
                Color.FromArgb(129, 140, 248),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(g, sub, UITheme.FontBody,
                new Rectangle(box.X + 16, box.Y + 42, box.Width - 32, 56),
                Color.FromArgb(226, 232, 240),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        // =========================================================================
        // CÁC HÀM QUẢN LÝ TRẠNG THÁI, BENDING, UNDO/REDO & XUẤT FILE
        // =========================================================================

        private void ToggleBendingMode()
        {
            if (_currentMesh.IsEmpty)
            {
                UpdateGestureModeState("Hãy tạo mô hình 3D trước khi dùng chức năng Uốn (Bend)!");
                return;
            }

            _bendReferenceStroke.Clear();
            if (_gestureMode == TeddyGestureMode.Bending)
            {
                _gestureMode = _currentMesh.ActiveBaseRing != null ? TeddyGestureMode.Extrusion : TeddyGestureMode.Painting;
                UITheme.SetButtonSelected(_btnBendMode, false);
                UpdateGestureModeState("Đã tắt chế độ Uốn (Bending).");
            }
            else
            {
                _currentMesh.ActiveBaseRing = null;
                _gestureMode = TeddyGestureMode.Bending;
                UITheme.SetButtonSelected(_btnBendMode, true);
                UpdateGestureModeState("Chế độ BENDING (Mục 4.7 - Hình 11): Hãy vẽ Nét 1 (Trục chuẩn Reference) dọc theo phần mô hình muốn uốn.");
            }
            _viewportPanel.Invalidate();
        }

        private void ResetToBlankCreationMode()
        {
            if (!_currentMesh.IsEmpty)
                PushUndoState();

            _currentMesh = new TeddyMesh { BaseColor = _modelBaseColor };
            _camera.Reset();
            _bendReferenceStroke.Clear();
            _isBaseRingFromCut = false;
            _gestureMode = TeddyGestureMode.Creation;
            UITheme.SetButtonSelected(_btnBendMode, false);
            UpdateGestureModeState("Chế độ CREATION: Hãy vẽ một nét khép kín bằng Chuột Trái để tạo mô hình 3D mới.");
            _viewportPanel.Invalidate();
        }

        private void LoadPrimitiveMesh(TeddyMesh primitive, string message)
        {
            if (!_currentMesh.IsEmpty)
                PushUndoState();

            _currentMesh = primitive;
            _modelBaseColor = primitive.BaseColor;
            _camera.Reset();
            _camera.Rotation = Quaternion.CreateFromYawPitchRoll(0.35f, -0.25f, 0f);
            _bendReferenceStroke.Clear();
            _isBaseRingFromCut = false;
            _gestureMode = TeddyGestureMode.Painting;
            UITheme.SetButtonSelected(_btnBendMode, false);
            _btnModelColor.Invalidate();
            UpdateGestureModeState(message);
            _viewportPanel.Invalidate();
        }

        private void SetRenderStyle(TeddyRenderStyle style)
        {
            _renderStyle = style;
            UpdateStyleButtonSelection();
            _viewportPanel.Invalidate();
        }

        private void UpdateStyleButtonSelection()
        {
            UITheme.SetButtonSelected(_btnStylePenInk, _renderStyle == TeddyRenderStyle.PenAndInk);
            UITheme.SetButtonSelected(_btnStyleSmooth, _renderStyle == TeddyRenderStyle.SmoothShaded);
            UITheme.SetButtonSelected(_btnStyleWire, _renderStyle == TeddyRenderStyle.WireframeSpine);
        }

        private void UpdateGestureModeState(string statusMessage)
        {
            if (_currentMesh.IsEmpty)
                _gestureMode = TeddyGestureMode.Creation;

            switch (_gestureMode)
            {
                case TeddyGestureMode.Creation:
                    _lblModeBadge.Text = "MODE: CREATION";
                    _lblModeBadge.BackColor = UITheme.AccentPrimary;
                    break;
                case TeddyGestureMode.Painting:
                    _lblModeBadge.Text = "MODE: PAINTING";
                    _lblModeBadge.BackColor = UITheme.AccentSecondary;
                    break;
                case TeddyGestureMode.Extrusion:
                    _lblModeBadge.Text = "MODE: EXTRUSION";
                    _lblModeBadge.BackColor = UITheme.AccentDanger;
                    break;
                case TeddyGestureMode.Bending:
                    _lblModeBadge.Text = "MODE: BENDING";
                    _lblModeBadge.BackColor = UITheme.AccentWarning;
                    break;
            }

            if (_gestureMode == TeddyGestureMode.Creation || _currentMesh.IsEmpty)
            {
                _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;
                foreach (var kvp in _stepViewButtons)
                    UITheme.SetButtonSelected(kvp.Value, kvp.Key == TeddyAlgorithmStepView.Final3DModel);
            }

            _lblStatusText.Text = statusMessage;
            _lblMeshStats.Text = $"Vertices: {_currentMesh.Vertices.Count} | Faces: {_currentMesh.Triangles.Count}";
        }

        private void PushUndoState()
        {
            _undoStack.Push(_currentMesh.Clone());
            _redoStack.Clear();
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(_currentMesh.Clone());
            _currentMesh = _undoStack.Pop();
            _bendReferenceStroke.Clear();
            UITheme.SetButtonSelected(_btnBendMode, false);
            _gestureMode = _currentMesh.IsEmpty ? TeddyGestureMode.Creation :
                (_currentMesh.ActiveBaseRing != null ? TeddyGestureMode.Extrusion : TeddyGestureMode.Painting);
            UpdateGestureModeState("Đã hoàn tác bước dựng hình 3D trước đó.");
            _viewportPanel.Invalidate();
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(_currentMesh.Clone());
            _currentMesh = _redoStack.Pop();
            _bendReferenceStroke.Clear();
            UITheme.SetButtonSelected(_btnBendMode, false);
            _gestureMode = _currentMesh.IsEmpty ? TeddyGestureMode.Creation :
                (_currentMesh.ActiveBaseRing != null ? TeddyGestureMode.Extrusion : TeddyGestureMode.Painting);
            UpdateGestureModeState("Đã làm lại bước dựng hình 3D.");
            _viewportPanel.Invalidate();
        }

        private void FillSelectedBaseRingWithColor()
        {
            if (_currentMesh.IsEmpty || _currentMesh.ActiveBaseRing == null)
            {
                UpdateGestureModeState("Hãy khoanh một vòng tròn khép kín (Vòng đỏ) trên bề mặt mô hình trước, sau đó bấm 'Tô vùng' (hoặc phím F)!");
                return;
            }

            PushUndoState();
            if (TeddySmoothBendOp.TryFillColorInBaseRing(_currentMesh, _surfacePaintColor, out string fillMsg))
            {
                _isBaseRingFromCut = false;
                _gestureMode = TeddyGestureMode.Painting;
                UpdateGestureModeState(fillMsg);
                _viewportPanel.Invalidate();
            }
            else
            {
                if (_undoStack.Count > 0) _currentMesh = _undoStack.Pop();
                UpdateGestureModeState(fillMsg);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                PerformUndo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                PerformRedo();
                return true;
            }
            if (keyData == Keys.F)
            {
                FillSelectedBaseRingWithColor();
                return true;
            }
            if (keyData == Keys.Space)
            {
                _camera.Reset();
                _viewportPanel.Invalidate();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                _currentMesh.ActiveBaseRing = null;
                _bendReferenceStroke.Clear();
                UITheme.SetButtonSelected(_btnBendMode, false);
                _gestureMode = _currentMesh.IsEmpty ? TeddyGestureMode.Creation : TeddyGestureMode.Painting;
                SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
                UpdateGestureModeState("Đã hủy thao tác đang chọn và quay về chế độ mặc định.");
                _viewportPanel.Invalidate();
                return true;
            }
            if (keyData >= Keys.D0 && keyData <= Keys.D5)
            {
                int stepIdx = keyData - Keys.D0;
                SetAlgorithmStepView((TeddyAlgorithmStepView)stepIdx);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BtnImportObj_Click(object? sender, EventArgs e)
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Mở Mô Hình 3D Wavefront (.OBJ)",
                Filter = "Wavefront OBJ (*.obj)|*.obj|All Files (*.*)|*.*"
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    PushUndoState();
                    _currentMesh = ObjExporter.LoadFromFile(ofd.FileName);
                    _camera.Reset();
                    _bendReferenceStroke.Clear();
                    UITheme.SetButtonSelected(_btnBendMode, false);
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState($"Đã nạp file .OBJ thành công: {System.IO.Path.GetFileName(ofd.FileName)}");
                    _viewportPanel.Invalidate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi đọc file .OBJ:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportObj_Click(object? sender, EventArgs e)
        {
            if (_currentMesh.IsEmpty)
            {
                MessageBox.Show("Chưa có mô hình 3D nào để xuất! Hãy vẽ một nét khép kín hoặc chọn Khối Cầu/Khối Hộp trước.",
                    "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Lưu Mô Hình 3D Ra File .OBJ",
                Filter = "Wavefront OBJ (*.obj)|*.obj",
                FileName = "teddy_model.obj"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    ObjExporter.SaveToFile(_currentMesh, sfd.FileName);
                    UpdateGestureModeState($"Đã xuất file .OBJ thành công: {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lưu file .OBJ:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnSendToLayer_Click(object? sender, EventArgs e)
        {
            if (_currentMesh.IsEmpty)
            {
                MessageBox.Show("Chưa có mô hình 3D để chèn vào Layer!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Bitmap snapshot = TeddyRenderer.RenderTransparentSnapshot(
                _viewportPanel.ClientSize.Width,
                _viewportPanel.ClientSize.Height,
                _currentMesh,
                _camera,
                _renderStyle);

            _onInsertToLayer?.Invoke(snapshot, $"Teddy 3D ({_renderStyle})");
            UpdateGestureModeState("Đã chèn hình chụp mô hình 3D (nền trong suốt) vào một Layer mới trong PaintLike!");
        }
    }
}
