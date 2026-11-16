using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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

        private readonly List<TeddyMesh> _sceneMeshes = new List<TeddyMesh>();
        private TeddyMesh? _selectedMesh = null;
        private TeddySurfaceLine? _selectedSurfaceLine = null;
        private readonly TeddyMesh _emptyMesh = new TeddyMesh();

        private TeddyMesh CurrentMesh => _selectedMesh ?? (_sceneMeshes.Count > 0 ? _sceneMeshes[_sceneMeshes.Count - 1] : _emptyMesh);
        private TeddyMesh ActiveMesh => CurrentMesh;
        private TeddyMesh _currentMesh
        {
            get => CurrentMesh;
            set
            {
                if (value == null || value.IsEmpty)
                {
                    _sceneMeshes.Clear();
                    _selectedMesh = null;
                }
                else
                {
                    if (!_sceneMeshes.Contains(value))
                    {
                        _sceneMeshes.Add(value);
                    }
                    _selectedMesh = value;
                }
            }
        }

        private readonly VirtualTrackballCamera _camera = new VirtualTrackballCamera();
        private TeddyRenderStyle _renderStyle = TeddyRenderStyle.PenAndInk;
        private TeddyGestureMode _gestureMode = TeddyGestureMode.Creation;
        private TeddyAlgorithmStepView _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;

        // Undo / Redo 3D Stack
        private readonly Stack<List<TeddyMesh>> _undoStack = new Stack<List<TeddyMesh>>();
        private readonly Stack<List<TeddyMesh>> _redoStack = new Stack<List<TeddyMesh>>();

        // Trạng thái vẽ nét 2D & Bending 2 nét
        private readonly List<PointF> _activeStroke = new List<PointF>();
        private readonly List<PointF> _bendReferenceStroke = new List<PointF>();
        private bool _isDrawingStroke = false;
        private bool _isBaseRingFromCut = false;
        private bool _hasSentCurrentMeshToLayer = false;
        private Color _surfacePaintColor = Color.FromArgb(37, 99, 235);
        private Color _modelBaseColor = Color.FromArgb(244, 162, 97);

        // Click chọn & Xóa nét vẽ 3D / Khối 3D
        private Point _mouseDownPos = Point.Empty;
        private bool _hasMovedSignificantly = false;

        // Trạng thái & Mẫu đường cắt 3D (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do)
        private TeddyCutPattern _activeCutPattern = TeddyCutPattern.Freehand;
        private TeddyCutActionMode _cutActionMode = TeddyCutActionMode.RemoveOneSide;
        private TeddyCutPieceVisibility _cutPieceVisibility = TeddyCutPieceVisibility.Hidden;
        private TeddyCutSurfaceViewMode _cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection;
        private Color _cutFillColor = Color.FromArgb(251, 146, 60); // Màu tô nổi bật tại vị trí bị cắt
        private bool _isCutToolActive = false;
        private bool _flipCutRemoveSide = false;
        private float _cutAmplitudePx = 22f;
        private int _cutPeriodCount = 5;

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
        private readonly Dictionary<TeddyCutPattern, Button> _cutPatternButtons = new Dictionary<TeddyCutPattern, Button>();

        private Button _btnCutFillColor = null!;
        private Button _btnCutSurfaceViewMode = null!;
        private Button _btnFlipCutSide = null!;
        private Button _btnQuickCutNow = null!;

        private Button _btnInitBlank = null!;
        private Button _btnSphere = null!;
        private Button _btnCube = null!;
        private Button _btnBendMode = null!;
        private Button _btnUndo = null!;
        private Button _btnRedo = null!;
        private Button _btnDeleteLine = null!;
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
            UpdateGestureModeState("Sẵn sàng: Giữ Chuột Trái hoặc Chuột Phải để xoay 3D, Con lăn để Zoom, hoặc giữ Shift + Chuột Trái để vẽ nét.");
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

            // --- Nhóm 1: KHỞI TẠO, BIẾN DẠNG & LỊCH SỬ (X: 8..322) ---
            _btnInitBlank = CreateRibbonButton(new Rectangle(14, 12, 72, 30), IconType.Plus, "Init Mới", ButtonVariant.Primary,
                "Khởi tạo vùng vẽ mới để phác thảo nét 2D tạo khối 3D (Press 'Init' button - Hình 1 & 4)");
            _btnInitBlank.Click += (s, e) => ResetToBlankCreationMode();

            _btnSphere = CreateRibbonButton(new Rectangle(89, 12, 72, 30), IconType.Circle, "Khối Cầu", ButtonVariant.Default,
                "Tạo sẵn khối Cầu 3D mẫu (Sphere - Mục 4.2)");
            _btnSphere.Click += (s, e) => LoadPrimitiveMesh(TeddyMesh.CreateSphere(135f, 28, 18), "Đã nạp Khối Cầu 3D mẫu. Khoanh tròn trên mặt để Đùn (Extrude), hoặc kẻ xuyên qua để Cắt (Cut).");

            _btnCube = CreateRibbonButton(new Rectangle(164, 12, 72, 30), IconType.Rectangle, "Khối Hộp", ButtonVariant.Default,
                "Tạo sẵn khối Lập phương chia lưới mịn (Subdivided Cube - Mục 4.2)");
            _btnCube.Click += (s, e) => LoadPrimitiveMesh(TeddyMesh.CreateCube(105f, 6), "Đã nạp Khối Hộp 3D mẫu. Khoanh tròn trên mặt để Đùn (Extrude), hoặc kẻ xuyên qua để Cắt (Cut).");

            _btnBendMode = CreateRibbonButton(new Rectangle(239, 12, 76, 30), IconType.Bezier, "Uốn (Bend)", ButtonVariant.Default,
                "Chế độ Uốn biến dạng mô hình bằng 2 nét vẽ: Nét 1 (Trục chuẩn Reference) -> Nét 2 (Trục đích Target) (Mục 4.7 - Hình 11)");
            _btnBendMode.Click += (s, e) => ToggleBendingMode();

            _btnUndo = CreateRibbonButton(new Rectangle(14, 46, 58, 28), IconType.Undo, "Hoàn tác", ButtonVariant.Default,
                "Hoàn tác bước dựng hình 3D trước đó (Ctrl+Z)");
            _btnUndo.Click += (s, e) => PerformUndo();

            _btnRedo = CreateRibbonButton(new Rectangle(75, 46, 58, 28), IconType.Redo, "Làm lại", ButtonVariant.Default,
                "Làm lại bước vừa hoàn tác (Ctrl+Y)");
            _btnRedo.Click += (s, e) => PerformRedo();

            _btnSmoothMesh = CreateRibbonButton(new Rectangle(136, 46, 58, 28), IconType.Filter, "Làm mịn", ButtonVariant.Default,
                "Làm mịn toàn bộ bề mặt lưới 3D bằng thuật toán Taubin Surface Fairing (Mục 5.1 & 5.5)");
            _btnSmoothMesh.Click += (s, e) =>
            {
                if (_sceneMeshes.Count == 0) return;
                PushUndoState();
                if (_selectedMesh != null)
                {
                    _selectedMesh.SmoothMesh(2, 0.42f, -0.44f);
                    UpdateGestureModeState("Đã làm mịn bề mặt khối 3D đang chọn (Taubin Fairing).");
                }
                else
                {
                    foreach (var m in _sceneMeshes) m.SmoothMesh(2, 0.42f, -0.44f);
                    UpdateGestureModeState("Đã làm mịn toàn bộ các khối 3D trong không gian (Taubin Fairing).");
                }
                _viewportPanel.Invalidate();
            };

            Button btnFillRegion = CreateRibbonButton(new Rectangle(197, 46, 58, 28), IconType.ColorPalette, "Tô vùng", ButtonVariant.Default,
                "Tô màu các mặt tam giác nằm trong Vòng chọn đỏ bằng Màu bút hiện tại (Phím tắt: F - Hình 2 bài báo)");
            btnFillRegion.Click += (s, e) => FillSelectedBaseRingWithColor();

            _btnDeleteLine = CreateRibbonButton(new Rectangle(258, 46, 57, 28), IconType.Trash, "Xóa", ButtonVariant.Default,
                "Click chọn một nét vẽ hoặc khối 3D để xóa (Phím tắt: Delete hoặc Backspace)");
            _btnDeleteLine.Enabled = false;
            _btnDeleteLine.Click += (s, e) => DeleteSelectedEntity();

            // --- Nhóm 2: KIỂU HIỂN THỊ 3D & CAMERA (X: 328..614) ---
            _btnStylePenInk = CreateRibbonButton(new Rectangle(334, 12, 88, 30), IconType.Rename, "Pen & Ink", ButtonVariant.Default,
                "Chế độ phác họa nét mực + đường viền đen Silhouette thời gian thực (Hình 1 bài báo SIGGRAPH '99)");
            _btnStylePenInk.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.PenAndInk);

            _btnStyleSmooth = CreateRibbonButton(new Rectangle(425, 12, 88, 30), IconType.Opacity, "Tô bóng 3D", ButtonVariant.Default,
                "Chế độ tô bóng khối Studio 3D mượt (Phong / Lambert + Rim Light)");
            _btnStyleSmooth.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.SmoothShaded);

            _btnStyleWire = CreateRibbonButton(new Rectangle(516, 12, 92, 30), IconType.Hexagon, "Lưới + Spine", ButtonVariant.Default,
                "Hiển thị cấu trúc lưới tam giác và Trục Xương Sống (Chordal Axis / Spine - Hình 12 & 13e)");
            _btnStyleWire.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.WireframeSpine);

            _btnResetCam = CreateRibbonButton(new Rectangle(334, 46, 88, 28), IconType.ZoomIn, "Reset Góc", ButtonVariant.Default,
                "Đưa góc xoay Camera Trackball và độ phóng đại về mặc định");
            _btnResetCam.Click += (s, e) =>
            {
                _camera.Reset();
                _viewportPanel.Invalidate();
            };

            _btnModelColor = new Button { Bounds = new Rectangle(425, 46, 88, 28) };
            UITheme.StyleButton(_btnModelColor, IconType.ColorPalette, "Màu khối", ButtonVariant.Default, false,
                _toolTip, "Đổi màu sắc chất liệu của mô hình 3D", () => _modelBaseColor);
            _btnModelColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _modelBaseColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _modelBaseColor = cd.Color;
                    if (_selectedMesh != null)
                    {
                        _selectedMesh.BaseColor = _modelBaseColor;
                    }
                    else
                    {
                        foreach (var m in _sceneMeshes) m.BaseColor = _modelBaseColor;
                    }
                    _btnModelColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnModelColor);

            _btnPaintColor = new Button { Bounds = new Rectangle(516, 46, 92, 28) };
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

            // --- Nhóm 3: MẪU ĐƯỜNG CẮT 3D (X: 620..1044) ---
            AddCutPatternButton(new Rectangle(626, 12, 66, 30), IconType.CutFreehand, "Tự Do", TeddyCutPattern.Freehand,
                "Cắt tự do (Freehand Cut): Kéo chuột vẽ đường cắt tự do bất kỳ xuyên qua vật thể 3D");
            AddCutPatternButton(new Rectangle(695, 12, 64, 30), IconType.CutStraight, "Thẳng", TeddyCutPattern.Straight,
                "Đường cắt Thẳng (Straight Cut): Kéo tạo mặt cắt phẳng thẳng tắp ngang qua vật thể 3D");
            AddCutPatternButton(new Rectangle(762, 12, 68, 30), IconType.CutZigzag, "Zíc-zắc", TeddyCutPattern.Zigzag,
                "Đường cắt Zíc-zắc / Răng cưa (Zigzag Cut): Cắt ngang vật thể theo biên dạng răng cưa nhọn");
            AddCutPatternButton(new Rectangle(833, 12, 62, 30), IconType.CutCurved, "Cong", TeddyCutPattern.Curved,
                "Đường cắt Cong (Curved Arch Cut): Cắt ngang vật thể theo cung vòm cong mềm mại");
            AddCutPatternButton(new Rectangle(898, 12, 64, 30), IconType.CutWave, "Sóng", TeddyCutPattern.Wave,
                "Đường cắt Lượn Sóng (Sine Wave Cut): Cắt ngang vật thể theo đường sóng hình Sin");
            AddCutPatternButton(new Rectangle(965, 12, 73, 30), IconType.CutStepped, "Bậc Thang", TeddyCutPattern.Stepped,
                "Đường cắt Bậc Thang (Stepped Cut): Cắt ngang vật thể theo rãnh răng vuông bậc thang");

            AddCutPatternButton(new Rectangle(626, 46, 58, 28), IconType.CutChevronV, "Chữ V", TeddyCutPattern.ChevronV,
                "Đường cắt Chữ V (Chevron V-Cut): Cắt vát mũi nhọn chữ V vào giữa vật thể 3D");

            _btnCutFillColor = new Button { Bounds = new Rectangle(687, 46, 82, 28) };
            UITheme.StyleButton(_btnCutFillColor, IconType.ColorPalette, "Màu cắt", ButtonVariant.Default, false,
                _toolTip, "Chọn màu tô thêm vào diện tích tại vị trí bị cắt", () => _cutFillColor);
            _btnCutFillColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _cutFillColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _cutFillColor = cd.Color;
                    _cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection;
                    UpdateCutPatternToolbarState();
                    _btnCutFillColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnCutFillColor);

            _btnCutSurfaceViewMode = new Button { Bounds = new Rectangle(772, 46, 108, 28) };
            _btnCutSurfaceViewMode.Click += (s, e) =>
            {
                _cutSurfaceViewMode = _cutSurfaceViewMode == TeddyCutSurfaceViewMode.HighlightedCrossSection
                    ? TeddyCutSurfaceViewMode.OpenHollowInterior
                    : TeddyCutSurfaceViewMode.HighlightedCrossSection;
                UpdateCutPatternToolbarState();
                string surfMsg = _cutSurfaceViewMode == TeddyCutSurfaceViewMode.HighlightedCrossSection
                    ? "Chế độ [Vị trí cắt: Hiện]: Đang tô màu nổi bật vào diện tích tại vị trí bị cắt."
                    : "Chế độ [Vị trí cắt: Ẩn]: Đã ẩn lớp tô mặt cắt để bạn nhìn thẳng vào bên trong vị trí bị cắt!";
                UpdateGestureModeState(surfMsg);
                _viewportPanel.Invalidate();
            };
            _panelTopRibbon.Controls.Add(_btnCutSurfaceViewMode);

            _btnFlipCutSide = new Button { Bounds = new Rectangle(883, 46, 66, 28) };
            UITheme.StyleButton(_btnFlipCutSide, IconType.Arrows, "Đảo bên", ButtonVariant.Default, false,
                _toolTip, "Đảo ngược phía bị cắt (Trái <-> Phải / Trên <-> Dưới)");
            _btnFlipCutSide.Click += (s, e) =>
            {
                _flipCutRemoveSide = !_flipCutRemoveSide;
                UITheme.SetButtonSelected(_btnFlipCutSide, _flipCutRemoveSide);
                UpdateGestureModeState(_flipCutRemoveSide
                    ? "Đã bật [Đảo bên cắt]: Phía bị cắt đã được đổi sang chiều ngược lại."
                    : "Đã trở về chiều cắt mặc định (cắt phía bên trái hướng kéo chuột).");
                _viewportPanel.Invalidate();
            };
            _panelTopRibbon.Controls.Add(_btnFlipCutSide);

            _btnQuickCutNow = new Button { Bounds = new Rectangle(952, 46, 86, 28) };
            UITheme.StyleButton(_btnQuickCutNow, IconType.Scissors, "Cắt Ngang", ButtonVariant.Danger, false,
                _toolTip, "Thực hiện cắt ngang qua chính giữa vật thể 3D ngay lập tức theo Mẫu Đường Cắt đang chọn!");
            _btnQuickCutNow.Click += (s, e) => PerformQuickHorizontalCut();
            _panelTopRibbon.Controls.Add(_btnQuickCutNow);

            // --- Nhóm 4: TỆP .OBJ & TÍCH HỢP LAYER 2D (X: 1050..1212) ---
            _btnImportObj = CreateRibbonButton(new Rectangle(1056, 12, 72, 30), IconType.Import, "Mở .OBJ", ButtonVariant.Default,
                "Mở file mô hình 3D định dạng chuẩn Wavefront .OBJ từ máy tính");
            _btnImportObj.Click += BtnImportObj_Click;

            _btnExportObj = CreateRibbonButton(new Rectangle(1131, 12, 75, 30), IconType.Save, "Lưu .OBJ", ButtonVariant.Accent,
                "Xuất mô hình 3D hiện tại ra file .OBJ tiêu chuẩn (Mục 6 bài báo)");
            _btnExportObj.Click += BtnExportObj_Click;

            _btnSendToLayer = CreateRibbonButton(new Rectangle(1056, 46, 150, 28), IconType.Layers, "Chèn vào Layer 2D", ButtonVariant.Success,
                "Kết xuất góc nhìn 3D hiện tại (nền trong suốt) và thêm thành một Layer mới trong PaintLike");
            _btnSendToLayer.Click += BtnSendToLayer_Click;

            UpdateCutPatternToolbarState();

            // 1B. Thanh Trực Quan Hóa Từng Bước Thuật Toán (Hình 13, 14, 15) & Mẫu Thử Bài Báo (Hình 1 & 5)
            _panelAlgoStepBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = UITheme.BgPanelHeader
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
                ForeColor = UITheme.AccentPrimary,
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
                Cursor = Cursors.Default
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

        private void AddCutPatternButton(Rectangle bounds, IconType icon, string label, TeddyCutPattern pattern, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, icon, label, ButtonVariant.Default, false, _toolTip, tooltip);
            btn.Click += (s, e) => SelectCutPattern(pattern);
            _panelTopRibbon.Controls.Add(btn);
            _cutPatternButtons[pattern] = btn;
        }

        private void SelectCutPattern(TeddyCutPattern pattern)
        {
            if (_isCutToolActive && _activeCutPattern == pattern)
            {
                // Bấm lại vào nút mẫu đang bật -> Tắt chế độ kéo cắt trực tiếp (về chế độ Chuột Trái xoay 3D)
                _isCutToolActive = false;
                UpdateCutPatternToolbarState();
                UpdateGestureModeState($"Đã tắt công cụ kéo cắt trực tiếp (Mẫu hiện tại: {TeddyCutPatternGenerator.GetPatternDisplayName(_activeCutPattern)}). Giữ Shift + Chuột Trái nếu muốn vẽ/cắt.");
                _viewportPanel?.Invalidate();
                return;
            }

            _activeCutPattern = pattern;
            _isCutToolActive = true;
            if (_gestureMode == TeddyGestureMode.Bending)
            {
                _bendReferenceStroke.Clear();
                UITheme.SetButtonSelected(_btnBendMode, false);
                _gestureMode = _sceneMeshes.Count == 0 ? TeddyGestureMode.Creation : TeddyGestureMode.Painting;
            }

            UpdateCutPatternToolbarState();
            string pName = TeddyCutPatternGenerator.GetPatternDisplayName(pattern);
            UpdateGestureModeState($"Đang chọn mẫu cắt [{pName}]: Kéo Chuột Trái ngang qua vật thể 3D để cắt (Chuột Phải xoay 3D), hoặc bấm 'Cắt Ngang Ngay'!");
            _viewportPanel?.Invalidate();
        }

        private void UpdateCutPatternToolbarState()
        {
            foreach (var kvp in _cutPatternButtons)
            {
                bool isSelected = kvp.Key == _activeCutPattern && _isCutToolActive;
                UITheme.SetButtonSelected(kvp.Value, isSelected);
            }

            if (_btnCutSurfaceViewMode != null)
            {
                if (_cutSurfaceViewMode == TeddyCutSurfaceViewMode.HighlightedCrossSection)
                {
                    UITheme.StyleButton(_btnCutSurfaceViewMode, IconType.EyeOpen, "Vị trí cắt: Hiện", ButtonVariant.Accent, false,
                        _toolTip, "Đang HIỆN màu tô tại vị trí bị cắt. Bấm để ẨN nắp vết cắt và nhìn vào bên trong vị trí bị cắt!");
                }
                else
                {
                    UITheme.StyleButton(_btnCutSurfaceViewMode, IconType.EyeClosed, "Vị trí cắt: Ẩn", ButtonVariant.Default, false,
                        _toolTip, "Đang ẨN mặt tô tại vị trí bị cắt để nhìn xuyên vào lòng khối. Bấm để HIỆN lại màu tô tại vị trí bị cắt!");
                }
            }
        }

        private void PerformQuickHorizontalCut()
        {
            Size vp = _viewportPanel.ClientSize;
            if (vp.Width < 100 || vp.Height < 100) vp = new Size(1000, 520);

            // Nếu chưa có khối 3D nào trong không gian, tự động khởi tạo Khối Cầu 3D để cắt thử ngay
            if (_sceneMeshes.Count == 0 || _sceneMeshes.All(m => m.IsEmpty))
            {
                var sphere = TeddyMesh.CreateSphere(135f, 30, 20);
                sphere.BaseColor = _modelBaseColor;
                _sceneMeshes.Clear();
                _sceneMeshes.Add(sphere);
                _selectedMesh = sphere;
                _camera.Reset();
                _camera.Rotation = Quaternion.CreateFromYawPitchRoll(0.35f, -0.22f, 0f);
                _gestureMode = TeddyGestureMode.Painting;
            }

            TeddyMesh target = _selectedMesh ?? _sceneMeshes.FirstOrDefault(m => !m.IsEmpty) ?? CurrentMesh;
            List<PointF> autoStroke = TeddyCutPatternGenerator.CreateQuickHorizontalCutStroke(target, _camera, vp);
            ExecuteCutWithActivePattern(autoStroke, target);
            _viewportPanel.Invalidate();
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
                if (_sceneMeshes.Count > 0) PushUndoState();
                _sceneMeshes.Clear();
                TeddyMeshOptimizer.ApplyPaperPresetPainting(created, presetKey);
                _sceneMeshes.Add(created);
                _selectedMesh = created;
                _selectedSurfaceLine = null;
                UpdateDeleteButtonState();
                _isBaseRingFromCut = false;
                _hasSentCurrentMeshToLayer = false;
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

            UITheme.DrawRibbonGroup(g, new Rectangle(8, 5, 314, 85), "KHỞI TẠO & BIẾN ĐỔI (TV1 & TV2)");
            UITheme.DrawRibbonGroup(g, new Rectangle(328, 5, 286, 85), "CAMERA TRACKBALL & RENDER (TV3)");
            UITheme.DrawRibbonGroup(g, new Rectangle(620, 5, 424, 85), "MẪU ĐƯỜNG CẮT 3D (THẲNG, ZIKZAC, CONG, TỰ DO)");
            UITheme.DrawRibbonGroup(g, new Rectangle(1050, 5, 162, 85), "XUẤT .OBJ & LAYER 2D");

            using Pen bottomLine = new Pen(UITheme.BorderDefault, 1f);
            g.DrawLine(bottomLine, 0, _panelTopRibbon.Height - 1, _panelTopRibbon.Width, _panelTopRibbon.Height - 1);
        }

        // =========================================================================
        // XỬ LÝ TƯƠNG TÁC CHUỘT & MÁY TRẠNG THÁI CỬ CHỈ ĐẦY ĐỦ (HÌNH 4 BÀI BÁO)
        // =========================================================================

        private void ViewportPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            _mouseDownPos = e.Location;
            _hasMovedSignificantly = false;
            bool isShift = Control.ModifierKeys.HasFlag(Keys.Shift);

            if (e.Button == MouseButtons.Middle)
            {
                _camera.StartPan(e.Location);
                _viewportPanel.Cursor = Cursors.Hand;
            }
            else if (e.Button == MouseButtons.Right)
            {
                _camera.StartRotate(e.Location);
                _viewportPanel.Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (isShift || _isCutToolActive)
                {
                    // Giữ Shift + Chuột Trái hoặc đang bật Công cụ Mẫu Đường Cắt: Vẽ nét hoặc kéo đường cắt
                    _isDrawingStroke = true;
                    _activeStroke.Clear();
                    _activeStroke.Add(e.Location);
                    _viewportPanel.Cursor = Cursors.Cross;
                    _viewportPanel.Invalidate();
                }
                else
                {
                    // Chuột Trái thông thường (không giữ Shift): Xoay vật thể 3D tự do (giống Chuột Phải) hoặc Click chọn nét
                    _camera.StartRotate(e.Location);
                    _viewportPanel.Cursor = Cursors.SizeAll;
                }
            }
        }

        private void ViewportPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_hasMovedSignificantly && e.Button != MouseButtons.None)
            {
                if (Math.Abs(e.X - _mouseDownPos.X) > 4 || Math.Abs(e.Y - _mouseDownPos.Y) > 4)
                {
                    _hasMovedSignificantly = true;
                }
            }

            if (_camera.IsInteracting)
            {
                if (_hasMovedSignificantly || e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
                {
                    _camera.OnMouseMove(e.Location, _viewportPanel.ClientSize);
                    _viewportPanel.Invalidate();
                }
            }
            else if (_isDrawingStroke && e.Button == MouseButtons.Left)
            {
                if (_activeStroke.Count == 0 || StrokeProcessor.Distance(_activeStroke[_activeStroke.Count - 1], e.Location) >= 2.5f)
                {
                    _activeStroke.Add(e.Location);
                    _viewportPanel.Invalidate();
                }
            }
            else
            {
                // Cập nhật con trỏ chuột khi rê chuột tự do trên viewport
                bool isShift = Control.ModifierKeys.HasFlag(Keys.Shift);
                _viewportPanel.Cursor = (isShift || _isCutToolActive) ? Cursors.Cross : Cursors.Default;
            }
        }

        private void ViewportPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_camera.IsInteracting)
            {
                _camera.StopInteraction();
                bool isShift = Control.ModifierKeys.HasFlag(Keys.Shift);
                _viewportPanel.Cursor = (isShift || _isCutToolActive) ? Cursors.Cross : Cursors.Default;

                // Nếu là Chuột Trái không giữ Shift và chuột hầu như không di chuyển (Click đơn tại chỗ) -> Chọn nét vẽ
                if (e.Button == MouseButtons.Left && !isShift && !_hasMovedSignificantly)
                {
                    HandleViewportClick(e.Location);
                }
                return;
            }

            if (e.Button == MouseButtons.Left && _isDrawingStroke)
            {
                _isDrawingStroke = false;
                bool isShift = Control.ModifierKeys.HasFlag(Keys.Shift);
                if (_isCutToolActive && !isShift && !_hasMovedSignificantly)
                {
                    _activeStroke.Clear();
                    HandleViewportClick(e.Location);
                    _viewportPanel.Cursor = Cursors.Cross;
                    _viewportPanel.Invalidate();
                    return;
                }

                ClearSelection();
                ProcessCompletedUserStroke(_activeStroke);
                _activeStroke.Clear();
                _viewportPanel.Cursor = (isShift || _isCutToolActive) ? Cursors.Cross : Cursors.Default;
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
            // 0. CHẾ ĐỘ CÔNG CỤ MẪU ĐƯỜNG CẮT ĐANG BẬT (_isCutToolActive)
            // ---------------------------------------------------------------------
            if (_isCutToolActive)
            {
                float dragLen = StrokeProcessor.ComputePolylineLength(rawStroke, false);
                if (dragLen < 16f)
                {
                    return;
                }

                // Nếu chưa có khối 3D nào, tự động tạo Khối Cầu 3D ở giữa để cắt ngay
                if (_sceneMeshes.Count == 0 || _sceneMeshes.All(m => m.IsEmpty))
                {
                    var sphere = TeddyMesh.CreateSphere(135f, 30, 20);
                    sphere.BaseColor = _modelBaseColor;
                    _sceneMeshes.Clear();
                    _sceneMeshes.Add(sphere);
                    _selectedMesh = sphere;
                    _camera.Reset();
                    _camera.Rotation = Quaternion.CreateFromYawPitchRoll(0.35f, -0.22f, 0f);
                    _gestureMode = TeddyGestureMode.Painting;
                }

                ExecuteCutWithActivePattern(rawStroke, _selectedMesh);
                return;
            }

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
                    _hasSentCurrentMeshToLayer = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
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
                        RestoreSceneFromUndo();
                        _bendReferenceStroke.Clear();
                        UpdateGestureModeState(bendMsg);
                    }
                }
                return;
            }

            // ---------------------------------------------------------------------
            // 3. CHẾ ĐỘ EXTRUSION: Đang có vòng chọn màu đỏ (ActiveBaseRing) (Mục 4.4, 4.6, 5.3, 5.5)
            // ---------------------------------------------------------------------
            TeddyMesh? ringMesh = _sceneMeshes.FirstOrDefault(m => m.ActiveBaseRing != null) ?? CurrentMesh;
            if (_gestureMode == TeddyGestureMode.Extrusion && ringMesh.ActiveBaseRing != null)
            {
                float strokeLen = StrokeProcessor.ComputePolylineLength(rawStroke, false);

                // 3a. Click đơn: Kết thúc cắt (Hình 3q) hoặc đổi vòng đỏ thành nét vẽ bề mặt (Hình 7d-e)
                if (rawStroke.Count <= 4 || strokeLen < 14f)
                {
                    PushUndoState();
                    if (!_isBaseRingFromCut)
                    {
                        var ringAsPaint = ringMesh.ActiveBaseRing.Clone();
                        ringAsPaint.StrokeColor = _surfacePaintColor;
                        ringAsPaint.StrokeWidth = 2.8f;
                        ringMesh.SurfaceLines.Add(ringAsPaint);
                        UpdateGestureModeState("Click đơn: Đã chuyển vòng chọn đỏ thành nét vẽ bề mặt thông thường (Hình 7d-e).");
                    }
                    else
                    {
                        UpdateGestureModeState("Click đơn: Đã hoàn tất phép Cắt khối (Cutting - Hình 3q & Hình 8d).");
                    }

                    ringMesh.ActiveBaseRing = null;
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    return;
                }

                // 3b. Cử chỉ gạch zíc-zắc (Scribbling: sl/pl > 1.5) -> Làm mượt vùng chọn bằng nội suy Bezier (Mục 4.6 & 5.5)
                if (StrokeProcessor.IsScribbling(rawStroke, out float scribbleRatio))
                {
                    PushUndoState();
                    if (TeddySmoothBendOp.TrySmoothRegionInBaseRing(ringMesh, out string smoothMsg))
                    {
                        _isBaseRingFromCut = false;
                        _gestureMode = TeddyGestureMode.Painting;
                        UpdateGestureModeState($"Scribbling (sl/pl={scribbleRatio:F2}): {smoothMsg}");
                    }
                    else
                    {
                        RestoreSceneFromUndo();
                        UpdateGestureModeState(smoothMsg);
                    }
                    return;
                }

                // 3c. Nét vẽ thông thường -> Đùn khối lồi (Extrusion) hoặc Khoét lõm (Cavity) bằng thuật toán 2 con trỏ (Mục 5.3)
                PushUndoState();
                if (TeddyExtrudeCutOp.TryExtrudeOrDigFromBaseRing(
                    ringMesh,
                    rawStroke,
                    _camera,
                    _viewportPanel.ClientSize,
                    out TeddyMesh? extrudedMesh,
                    out string extrudeMsg))
                {
                    if (extrudedMesh != null)
                    {
                        _sceneMeshes.Add(extrudedMesh);
                        _selectedMesh = extrudedMesh;
                        _selectedSurfaceLine = null;
                        UpdateDeleteButtonState();
                    }
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState(extrudeMsg);
                }
                else
                {
                    RestoreSceneFromUndo();
                    UpdateGestureModeState(extrudeMsg);
                }
                return;
            }

            // ---------------------------------------------------------------------
            // 4. CHẾ ĐỘ PAINTING & ĐA VẬT THỂ:
            // ---------------------------------------------------------------------
            TeddyMesh? targetMesh = null;
            PaintingStrokeIntent intent = PaintingStrokeIntent.None;
            List<Vector3> projectedSurfacePts = null!;
            float ratioSlPl = 0f;

            // Kiểm tra tương tác với các khối hiện có trong scene (từ gần tới xa camera)
            var sortedForRaycast = _sceneMeshes.Where(m => !m.IsEmpty).OrderByDescending(m => GetMeshAverageViewZ(m)).ToList();
            foreach (var m in sortedForRaycast)
            {
                var mIntent = TeddySurfacePaintOp.AnalyzeStrokeIntent(
                    m, rawStroke, _camera, _viewportPanel.ClientSize,
                    out var pts, out var r);
                if (mIntent != PaintingStrokeIntent.None)
                {
                    targetMesh = m;
                    intent = mIntent;
                    projectedSurfacePts = pts;
                    ratioSlPl = r;
                    break;
                }
            }

            switch (intent)
            {
                case PaintingStrokeIntent.ErasePaintedStrokes:
                    PushUndoState();
                    int erased = TeddyRaycaster.EraseSurfaceLines(targetMesh!, rawStroke, _camera, _viewportPanel.ClientSize);
                    UpdateGestureModeState($"Cử chỉ Scribbling: Đã xóa {erased} nét vẽ trên bề mặt.");
                    break;

                case PaintingStrokeIntent.CutObject:
                    ExecuteCutWithActivePattern(rawStroke, targetMesh);
                    break;

                case PaintingStrokeIntent.SelectExtrusionRing:
                    PushUndoState();
                    Vector3 ringNormal = TeddySurfacePaintOp.SetExtrusionBaseRing(targetMesh!, projectedSurfacePts);
                    _isBaseRingFromCut = false;
                    _gestureMode = TeddyGestureMode.Extrusion;
                    UpdateGestureModeState($"Chế độ EXTRUSION (Vòng đỏ): Hãy xoay Chuột Phải nghiêng mô hình rồi vẽ nét vòm để Đùn lồi / Khoét lõm!");
                    break;

                case PaintingStrokeIntent.PaintOnSurface:
                    PushUndoState();
                    TeddySurfacePaintOp.AddSurfaceLine(targetMesh!, projectedSurfacePts, _surfacePaintColor, 2.8f);
                    _gestureMode = TeddyGestureMode.Painting;
                    UpdateGestureModeState($"Đã vẽ nét 3D bám trên bề mặt ({projectedSurfacePts.Count} đoạn - Mục 5.2).");
                    break;

                default:
                    // Nếu nét vẽ nằm ngoài hoặc là nét vẽ khép kín: Tạo một Khối 3D mới độc lập!
                    if (TeddyInflationEngine.TryCreateMeshFromStroke(
                        rawStroke,
                        _camera,
                        _viewportPanel.ClientSize,
                        _modelBaseColor,
                        out TeddyMesh? newMesh,
                        out string newMeshMsg) && newMesh != null)
                    {
                        PushUndoState();
                        _sceneMeshes.Add(newMesh);
                        _selectedMesh = newMesh;
                        _selectedSurfaceLine = null;
                        UpdateDeleteButtonState();
                        UpdateGestureModeState($"Đã tạo thêm khối 3D mới (Vật thể #{_sceneMeshes.Count}). Bấm Delete để xóa nếu chưa ưng ý.");
                    }
                    else
                    {
                        UpdateGestureModeState("Nét vẽ chưa tạo được khối 3D mới hoặc chưa chạm vào mặt vật thể. Hãy vẽ đường khép kín để tạo khối 3D mới, hoặc vẽ trên bề mặt để tô nét/khoanh đùn.");
                    }
                    break;
            }
        }

        private void ExecuteCutWithActivePattern(List<PointF> rawStroke, TeddyMesh? preferredTarget)
        {
            var candidates = new List<TeddyMesh>();
            if (preferredTarget != null && !preferredTarget.IsEmpty && !(preferredTarget.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
            {
                candidates.Add(preferredTarget);
            }
            foreach (var m in _sceneMeshes.Where(m => !m.IsEmpty && !m.IsCutOffPiece && m != preferredTarget).OrderByDescending(m => GetMeshAverageViewZ(m)))
            {
                candidates.Add(m);
            }

            if (candidates.Count == 0)
            {
                foreach (var m in _sceneMeshes.Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden)))
                    candidates.Add(m);
            }

            if (candidates.Count == 0) return;

            PushUndoState();
            int cutSuccessCount = 0;
            string lastMsg = "";
            var splitAdditions = new List<TeddyMesh>();

            foreach (var mesh in candidates)
            {
                if (TeddyExtrudeCutOp.TryCutOrSplitMeshWithPattern(
                    mesh,
                    rawStroke,
                    _activeCutPattern,
                    _cutAmplitudePx,
                    _cutPeriodCount,
                    _flipCutRemoveSide,
                    _cutActionMode,
                    _camera,
                    _viewportPanel.ClientSize,
                    out TeddyMesh? secondHalf,
                    out string msg))
                {
                    cutSuccessCount++;
                    lastMsg = msg;
                    if (secondHalf != null)
                    {
                        splitAdditions.Add(secondHalf);
                    }
                }
                else if (string.IsNullOrEmpty(lastMsg))
                {
                    lastMsg = msg;
                }
            }

            if (cutSuccessCount > 0)
            {
                if (splitAdditions.Count > 0)
                {
                    // Xóa các phần bị cắt cũ nếu người dùng đang ở chế độ Ẩn phần cắt để khung cảnh gọn gàng, chỉ giữ phần bị cắt mới nhất
                    if (_cutPieceVisibility == TeddyCutPieceVisibility.Hidden)
                    {
                        _sceneMeshes.RemoveAll(m => m.IsCutOffPiece);
                    }
                    _sceneMeshes.AddRange(splitAdditions);
                }

                _selectedMesh = null;
                _selectedSurfaceLine = null;
                UpdateDeleteButtonState();
                _isBaseRingFromCut = true;
                _gestureMode = _isCutToolActive ? TeddyGestureMode.Painting : TeddyGestureMode.Extrusion;
                UpdateGestureModeState(lastMsg);
            }
            else
            {
                RestoreSceneFromUndo();
                UpdateGestureModeState(lastMsg);
            }
        }

        private void ViewportPanel_Paint(object? sender, PaintEventArgs e)
        {
            if (_algorithmStepView != TeddyAlgorithmStepView.Final3DModel && ActiveMesh != null && !ActiveMesh.IsEmpty && ActiveMesh.DebugInfo != null)
            {
                TeddyRenderer.RenderAlgorithmStep(
                    e.Graphics,
                    _viewportPanel.ClientSize,
                    ActiveMesh,
                    _camera,
                    _algorithmStepView);
                return;
            }

            bool isPreviewingCut = _isDrawingStroke && _activeStroke.Count >= 2 && _isCutToolActive;

            Color activeStrokePreviewColor = _gestureMode switch
            {
                TeddyGestureMode.Creation => Color.FromArgb(220, 38, 38),
                TeddyGestureMode.Extrusion => Color.FromArgb(239, 68, 68),
                TeddyGestureMode.Bending => _bendReferenceStroke.Count == 0
                    ? Color.FromArgb(6, 182, 212)
                    : Color.FromArgb(245, 158, 11),
                _ => _surfacePaintColor
            };

            var visibleMeshes = _sceneMeshes
                .Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                .OrderBy(m => GetMeshAverageViewZ(m))
                .ToList();

            if (visibleMeshes.Count == 0)
            {
                TeddyRenderer.RenderScene(
                    e.Graphics,
                    _viewportPanel.ClientSize,
                    null,
                    _camera,
                    _renderStyle,
                    (_isDrawingStroke && !isPreviewingCut) ? _activeStroke : null,
                    activeStrokePreviewColor,
                    drawBackground: true,
                    cutPieceVisibility: _cutPieceVisibility,
                    cutSurfaceViewMode: _cutSurfaceViewMode,
                    cutFillColor: _cutFillColor);
            }
            else
            {
                bool isFirst = true;
                for (int idx = 0; idx < visibleMeshes.Count; idx++)
                {
                    var mesh = visibleMeshes[idx];
                    bool isLast = (idx == visibleMeshes.Count - 1);
                    TeddyRenderer.RenderScene(
                        e.Graphics,
                        _viewportPanel.ClientSize,
                        mesh,
                        _camera,
                        _renderStyle,
                        (isLast && _isDrawingStroke && !isPreviewingCut) ? _activeStroke : null,
                        activeStrokePreviewColor,
                        drawBackground: isFirst,
                        cutPieceVisibility: _cutPieceVisibility,
                        cutSurfaceViewMode: _cutSurfaceViewMode,
                        cutFillColor: _cutFillColor);
                    isFirst = false;
                }

                // 1. Viền sáng và Bounding box 2D cho Nét vẽ 2D đang chọn
                if (_selectedSurfaceLine != null)
                {
                    DrawSelectedLineHighlight(e.Graphics, _viewportPanel.ClientSize);
                }

                // 2. Khung viền hộp 3D Bounding Box cho Khối 3D đang chọn
                if (_selectedMesh != null && !(_selectedMesh.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                {
                    DrawSelectedMesh3DBoundingBox(e.Graphics, _viewportPanel.ClientSize, _selectedMesh);
                }
            }

            // Hiển thị Live Preview của Mẫu Đường Cắt (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do)
            if (isPreviewingCut)
            {
                DrawCutPreviewOverlay(e.Graphics);
            }

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
        }

        private void DrawCutPreviewOverlay(Graphics g)
        {
            if (_activeStroke.Count < 2) return;

            List<PointF> poly = TeddyCutPatternGenerator.BuildPatternedCutPolyline(
                _activeStroke,
                _activeCutPattern,
                _cutAmplitudePx,
                _cutPeriodCount,
                _flipCutRemoveSide,
                out PointF startPt,
                out PointF endPt,
                out Vector2 removeNormal);

            if (poly.Count < 2) return;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Đường trục chuẩn đứt nét nối điểm đầu -> điểm cuối
            using (Pen guidePen = new Pen(Color.FromArgb(120, 100, 116, 139), 1.4f))
            {
                guidePen.DashPattern = new float[] { 4f, 4f };
                g.DrawLine(guidePen, startPt, endPt);
            }

            // 2. Lớp phát sáng (Glow) bao quanh mẫu đường cắt
            using (Pen glowPen = new Pen(Color.FromArgb(72, 239, 68, 68), 7.0f))
            {
                glowPen.StartCap = LineCap.Round;
                glowPen.EndCap = LineCap.Round;
                glowPen.LineJoin = LineJoin.Round;
                g.DrawLines(glowPen, poly.ToArray());
            }

            // 3. Đường cắt chính sắc nét màu Đỏ Coral / Rose
            Color mainCutColor = _cutActionMode == TeddyCutActionMode.SplitIntoTwoMeshes
                ? Color.FromArgb(2, 132, 199)
                : Color.FromArgb(225, 29, 72);

            using (Pen cutPen = new Pen(mainCutColor, 3.0f))
            {
                cutPen.StartCap = LineCap.Round;
                cutPen.EndCap = LineCap.Round;
                cutPen.LineJoin = LineJoin.Round;
                g.DrawLines(cutPen, poly.ToArray());
            }

            // 4. Hai nút neo tròn ở Điểm đầu và Điểm cuối
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (Pen anchorPen = new Pen(mainCutColor, 2.2f))
            {
                g.FillEllipse(whiteBrush, startPt.X - 5f, startPt.Y - 5f, 10f, 10f);
                g.DrawEllipse(anchorPen, startPt.X - 5f, startPt.Y - 5f, 10f, 10f);
                g.FillEllipse(whiteBrush, endPt.X - 5f, endPt.Y - 5f, 10f, 10f);
                g.DrawEllipse(anchorPen, endPt.X - 5f, endPt.Y - 5f, 10f, 10f);
            }

            // 5. Mũi tên và nhãn chỉ báo phía bị xóa bỏ hoặc tách đôi tại trung điểm đường cắt
            PointF midPt = poly[poly.Count / 2];
            if (StrokeProcessor.Distance(startPt, endPt) > 36f)
            {
                PointF arrowEnd = new PointF(midPt.X + removeNormal.X * 28f, midPt.Y + removeNormal.Y * 28f);
                using (Pen arrowPen = new Pen(mainCutColor, 2.2f))
                {
                    arrowPen.EndCap = LineCap.ArrowAnchor;
                    g.DrawLine(arrowPen, midPt, arrowEnd);
                    if (_cutActionMode == TeddyCutActionMode.SplitIntoTwoMeshes)
                    {
                        PointF arrowEnd2 = new PointF(midPt.X - removeNormal.X * 28f, midPt.Y - removeNormal.Y * 28f);
                        g.DrawLine(arrowPen, midPt, arrowEnd2);
                    }
                }

                string badgeText = _cutActionMode == TeddyCutActionMode.SplitIntoTwoMeshes
                    ? $"TÁCH ĐÔI ({TeddyCutPatternGenerator.GetPatternDisplayName(_activeCutPattern)})"
                    : $"XÓA PHÍA NÀY ({TeddyCutPatternGenerator.GetPatternDisplayName(_activeCutPattern)})";

                Size textSize = TextRenderer.MeasureText(badgeText, UITheme.FontSmallBold);
                Rectangle badgeRect = new Rectangle(
                    (int)(arrowEnd.X - textSize.Width * 0.5f - 6),
                    (int)(arrowEnd.Y + (removeNormal.Y >= 0 ? 4 : -24)),
                    textSize.Width + 12,
                    20);

                using (GraphicsPath bp = UITheme.CreateRoundedRect(badgeRect, 5))
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                using (Pen bBorder = new Pen(mainCutColor, 1.2f))
                {
                    g.FillPath(bb, bp);
                    g.DrawPath(bBorder, bp);
                }

                TextRenderer.DrawText(g, badgeText, UITheme.FontSmallBold, badgeRect, mainCutColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            g.SmoothingMode = old;
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
                _isCutToolActive = false;
                UpdateCutPatternToolbarState();
                _currentMesh.ActiveBaseRing = null;
                _gestureMode = TeddyGestureMode.Bending;
                UITheme.SetButtonSelected(_btnBendMode, true);
                UpdateGestureModeState("Chế độ BENDING (Mục 4.7 - Hình 11): Hãy vẽ Nét 1 (Trục chuẩn Reference) dọc theo phần mô hình muốn uốn.");
            }
            _viewportPanel.Invalidate();
        }

        private void ResetToBlankCreationMode()
        {
            if (_sceneMeshes.Count > 0)
                PushUndoState();

            _sceneMeshes.Clear();
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
            _camera.Reset();
            _bendReferenceStroke.Clear();
            _isBaseRingFromCut = false;
            _isCutToolActive = false;
            UpdateCutPatternToolbarState();
            _gestureMode = TeddyGestureMode.Creation;
            UITheme.SetButtonSelected(_btnBendMode, false);
            UpdateGestureModeState("Chế độ CREATION: Giữ Shift + Chuột Trái để vẽ đường viền tạo khối 3D mới, hoặc chọn các khối mẫu ở trên.");
            _viewportPanel.Invalidate();
        }

        private void LoadPrimitiveMesh(TeddyMesh primitive, string message)
        {
            if (_sceneMeshes.Count > 0)
                PushUndoState();

            _sceneMeshes.Clear();
            _sceneMeshes.Add(primitive);
            _selectedMesh = primitive;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
            _modelBaseColor = primitive.BaseColor;
            _camera.Reset();
            _camera.Rotation = Quaternion.CreateFromYawPitchRoll(0.35f, -0.25f, 0f);
            _bendReferenceStroke.Clear();
            _isBaseRingFromCut = false;
            _hasSentCurrentMeshToLayer = false;
            _gestureMode = TeddyGestureMode.Painting;
            UITheme.SetButtonSelected(_btnBendMode, false);
            _btnModelColor.Invalidate();
            SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
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

            if (_isCutToolActive)
            {
                _lblModeBadge.Text = $"CẮT: {_activeCutPattern.ToString().ToUpper()}";
                _lblModeBadge.BackColor = UITheme.AccentDanger;
            }
            else
            {
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
            }

            if (_gestureMode == TeddyGestureMode.Creation || _currentMesh.IsEmpty)
            {
                _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;
                foreach (var kvp in _stepViewButtons)
                    UITheme.SetButtonSelected(kvp.Value, kvp.Key == TeddyAlgorithmStepView.Final3DModel);
            }

            _lblStatusText.Text = statusMessage;
            int totalVerts = _sceneMeshes.Sum(m => m.Vertices.Count);
            int totalFaces = _sceneMeshes.Sum(m => m.Triangles.Count);
            _lblMeshStats.Text = $"Vật thể: {_sceneMeshes.Count} | Đỉnh: {totalVerts} | Mặt: {totalFaces}";
        }

        private static List<TeddyMesh> CloneScene(List<TeddyMesh> scene)
        {
            var list = new List<TeddyMesh>(scene.Count);
            foreach (var m in scene) list.Add(m.Clone());
            return list;
        }

        private void PushUndoState()
        {
            _undoStack.Push(CloneScene(_sceneMeshes));
            _redoStack.Clear();
            _hasSentCurrentMeshToLayer = false;
        }

        private void RestoreSceneFromUndo()
        {
            if (_undoStack.Count == 0) return;
            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(CloneScene(_undoStack.Pop()));
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(CloneScene(_sceneMeshes));
            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(CloneScene(_undoStack.Pop()));
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
            _bendReferenceStroke.Clear();
            _hasSentCurrentMeshToLayer = false;
            UITheme.SetButtonSelected(_btnBendMode, false);
            _gestureMode = _sceneMeshes.Count == 0 ? TeddyGestureMode.Creation :
                (CurrentMesh.ActiveBaseRing != null ? TeddyGestureMode.Extrusion : TeddyGestureMode.Painting);
            UpdateGestureModeState("Đã hoàn tác bước dựng hình 3D trước đó.");
            _viewportPanel.Invalidate();
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(CloneScene(_sceneMeshes));
            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(CloneScene(_redoStack.Pop()));
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
            _bendReferenceStroke.Clear();
            _hasSentCurrentMeshToLayer = false;
            UITheme.SetButtonSelected(_btnBendMode, false);
            _gestureMode = _sceneMeshes.Count == 0 ? TeddyGestureMode.Creation :
                (CurrentMesh.ActiveBaseRing != null ? TeddyGestureMode.Extrusion : TeddyGestureMode.Painting);
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
                RestoreSceneFromUndo();
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
            if (keyData == Keys.Delete || keyData == Keys.Back)
            {
                if (_selectedSurfaceLine != null || _selectedMesh != null)
                {
                    DeleteSelectedEntity();
                    return true;
                }
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
                if (_isCutToolActive)
                {
                    _isCutToolActive = false;
                    UpdateCutPatternToolbarState();
                    UpdateGestureModeState("Đã thoát công cụ kéo cắt trực tiếp.");
                    _viewportPanel.Invalidate();
                    return true;
                }
                if (_selectedSurfaceLine != null || _selectedMesh != null)
                {
                    ClearSelection();
                    UpdateGestureModeState("Đã hủy chọn.");
                    return true;
                }
                CurrentMesh.ActiveBaseRing = null;
                _bendReferenceStroke.Clear();
                UITheme.SetButtonSelected(_btnBendMode, false);
                _gestureMode = _sceneMeshes.Count == 0 ? TeddyGestureMode.Creation : TeddyGestureMode.Painting;
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

        private void UpdateDeleteButtonState()
        {
            if (_btnDeleteLine != null)
            {
                if (_selectedSurfaceLine != null)
                {
                    _btnDeleteLine.Enabled = true;
                    UITheme.StyleButton(_btnDeleteLine, IconType.Trash, "Xóa nét", ButtonVariant.Danger, false, _toolTip, "Xóa nét vẽ 2D đang chọn (Phím tắt: Delete hoặc Backspace)");
                }
                else if (_selectedMesh != null)
                {
                    _btnDeleteLine.Enabled = true;
                    UITheme.StyleButton(_btnDeleteLine, IconType.Trash, "Xóa khối", ButtonVariant.Danger, false, _toolTip, "Xóa khối 3D đang chọn (Phím tắt: Delete hoặc Backspace)");
                }
                else
                {
                    _btnDeleteLine.Enabled = false;
                    UITheme.StyleButton(_btnDeleteLine, IconType.Trash, "Xóa", ButtonVariant.Default, false, _toolTip, "Click chọn một nét vẽ hoặc khối 3D để xóa");
                }
                _btnDeleteLine.Invalidate();
            }
        }

        private void ClearSelection()
        {
            if (_selectedSurfaceLine != null || _selectedMesh != null)
            {
                _selectedSurfaceLine = null;
                _selectedMesh = null;
                UpdateDeleteButtonState();
                _viewportPanel.Invalidate();
            }
        }

        private void DeleteSelectedEntity()
        {
            if (_selectedSurfaceLine != null)
            {
                PushUndoState();
                foreach (var mesh in _sceneMeshes)
                {
                    if (mesh.SurfaceLines.Remove(_selectedSurfaceLine)) break;
                    if (mesh.ActiveBaseRing == _selectedSurfaceLine) { mesh.ActiveBaseRing = null; break; }
                }
                _selectedSurfaceLine = null;
                UpdateDeleteButtonState();
                UpdateGestureModeState("Đã xóa nét vẽ 2D đã chọn. Bấm Ctrl+Z để hoàn tác.");
                _viewportPanel.Invalidate();
                return;
            }

            if (_selectedMesh != null)
            {
                PushUndoState();
                _sceneMeshes.Remove(_selectedMesh);
                _selectedMesh = null;
                UpdateDeleteButtonState();
                UpdateGestureModeState("Đã xóa khối 3D đã chọn. Các khối còn lại được giữ nguyên 100%. Bấm Ctrl+Z để hoàn tác.");
                _viewportPanel.Invalidate();
                return;
            }
        }

        private void HandleViewportClick(Point clickPt)
        {
            if (_sceneMeshes.Count == 0)
            {
                ClearSelection();
                return;
            }

            // Ưu tiên 1: Nét vẽ 2D (Surface Line)
            if (HitTestSurfaceLine(clickPt, _viewportPanel.ClientSize, out var hitLine, out _))
            {
                // Nếu nét này đã được chọn trước đó mà người dùng click tiếp -> Chuyển sang chọn Khối 3D bên dưới
                if (_selectedSurfaceLine == hitLine && HitTestMesh(clickPt, _viewportPanel.ClientSize, out var underMesh))
                {
                    _selectedMesh = underMesh;
                    _selectedSurfaceLine = null;
                    UpdateDeleteButtonState();
                    UpdateGestureModeState($"Đã chọn Khối 3D ({underMesh!.Vertices.Count} đỉnh). Nhấn Delete hoặc nút 'Xóa khối' để xóa riêng khối này.");
                    _viewportPanel.Invalidate();
                    return;
                }

                _selectedSurfaceLine = hitLine;
                _selectedMesh = null;
                UpdateDeleteButtonState();
                UpdateGestureModeState("Đã chọn nét vẽ 2D. Nhấn Delete hoặc nút 'Xóa nét' để xóa, Esc để hủy.");
                _viewportPanel.Invalidate();
                return;
            }

            // Ưu tiên 2: Khối 3D
            if (HitTestMesh(clickPt, _viewportPanel.ClientSize, out var hitMesh))
            {
                _selectedMesh = hitMesh;
                _selectedSurfaceLine = null;
                UpdateDeleteButtonState();
                UpdateGestureModeState($"Đã chọn Khối 3D ({hitMesh!.Vertices.Count} đỉnh). Nhấn Delete hoặc nút 'Xóa khối' để xóa riêng khối này.");
                _viewportPanel.Invalidate();
                return;
            }

            // Ưu tiên 3: Click ra ngoài khoảng trống -> Hủy chọn
            if (_selectedSurfaceLine != null || _selectedMesh != null)
            {
                _selectedSurfaceLine = null;
                _selectedMesh = null;
                UpdateDeleteButtonState();
                UpdateGestureModeState("Đã hủy chọn.");
                _viewportPanel.Invalidate();
            }
        }

        private bool HitTestMesh(Point mousePos, Size viewport, out TeddyMesh? hitMesh)
        {
            hitMesh = null;
            if (_sceneMeshes.Count == 0) return false;

            float closestT = float.MaxValue;
            var (rayOrigin, rayDir) = _camera.ScreenPointToRay(mousePos, viewport);
            Vector3 camForward = _camera.GetCameraForwardInWorld();

            foreach (var mesh in _sceneMeshes)
            {
                if (mesh == null || mesh.IsEmpty) continue;
                if (mesh.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden) continue;

                for (int i = 0; i < mesh.Triangles.Count; i++)
                {
                    var tri = mesh.Triangles[i];
                    if (Vector3.Dot(tri.FaceNormal, camForward) < -0.05f) continue;

                    Vector3 p0 = mesh.Vertices[tri.V0];
                    Vector3 p1 = mesh.Vertices[tri.V1];
                    Vector3 p2 = mesh.Vertices[tri.V2];

                    if (TeddyRaycaster.RayIntersectsTriangle(rayOrigin, rayDir, p0, p1, p2, out float t, out _))
                    {
                        if (t < closestT)
                        {
                            closestT = t;
                            hitMesh = mesh;
                        }
                    }
                }
            }

            return hitMesh != null;
        }

        private bool HitTestSurfaceLine(Point mousePos, Size viewport, out TeddySurfaceLine? hitLine, out TeddyMesh? parentMesh)
        {
            hitLine = null;
            parentMesh = null;
            if (_sceneMeshes.Count == 0) return false;

            float bestDist = float.MaxValue;
            float bestAvgDepth = float.MinValue;

            foreach (var mesh in _sceneMeshes)
            {
                if (mesh == null || mesh.IsEmpty) continue;

                var candidates = new List<TeddySurfaceLine>(mesh.SurfaceLines);
                if (mesh.ActiveBaseRing != null) candidates.Add(mesh.ActiveBaseRing);

                foreach (var line in candidates)
                {
                    if (line.Points.Count == 0) continue;

                    if (line.Points.Count == 1)
                    {
                        Vector3 sp = _camera.ProjectToScreen(line.Points[0], viewport);
                        float d = StrokeProcessor.Distance(mousePos, new PointF(sp.X, sp.Y));
                        float thresh = Math.Max(12f, line.StrokeWidth + 6f);
                        if (d <= thresh)
                        {
                            if (hitLine == null || d < bestDist - 2f || (Math.Abs(d - bestDist) <= 2f && sp.Z > bestAvgDepth))
                            {
                                hitLine = line;
                                parentMesh = mesh;
                                bestDist = d;
                                bestAvgDepth = sp.Z;
                            }
                        }
                        continue;
                    }

                    var screenPts = new List<Vector3>(line.Points.Count);
                    float sumZ = 0f;
                    for (int i = 0; i < line.Points.Count; i++)
                    {
                        Vector3 sp = _camera.ProjectToScreen(line.Points[i], viewport);
                        screenPts.Add(sp);
                        sumZ += sp.Z;
                    }
                    float avgZ = sumZ / line.Points.Count;

                    float minLineDist = float.MaxValue;
                    for (int i = 0; i < screenPts.Count - 1; i++)
                    {
                        float d = DistanceToSegment(
                            mousePos,
                            new PointF(screenPts[i].X, screenPts[i].Y),
                            new PointF(screenPts[i + 1].X, screenPts[i + 1].Y));
                        if (d < minLineDist) minLineDist = d;
                    }

                    if (line.IsClosedRing && screenPts.Count > 2)
                    {
                        float d = DistanceToSegment(
                            mousePos,
                            new PointF(screenPts[screenPts.Count - 1].X, screenPts[screenPts.Count - 1].Y),
                            new PointF(screenPts[0].X, screenPts[0].Y));
                        if (d < minLineDist) minLineDist = d;
                    }

                    float threshold = Math.Max(12f, line.StrokeWidth + 6f);
                    if (minLineDist <= threshold)
                    {
                        if (hitLine == null || minLineDist < bestDist - 2f || (Math.Abs(minLineDist - bestDist) <= 2f && avgZ > bestAvgDepth))
                        {
                            hitLine = line;
                            parentMesh = mesh;
                            bestDist = minLineDist;
                            bestAvgDepth = avgZ;
                        }
                    }
                }
            }

            return hitLine != null;
        }

        private static float DistanceToSegment(PointF pt, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-5f)
            {
                return StrokeProcessor.Distance(pt, a);
            }
            float t = ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / lenSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            PointF proj = new PointF(a.X + t * dx, a.Y + t * dy);
            return StrokeProcessor.Distance(pt, proj);
        }

        private void DrawSelectedLineHighlight(Graphics g, Size viewport)
        {
            if (_selectedSurfaceLine == null || _selectedSurfaceLine.Points.Count == 0) return;

            var screenPts = new List<PointF>(_selectedSurfaceLine.Points.Count + 1);
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var pt3D in _selectedSurfaceLine.Points)
            {
                Vector3 sp = _camera.ProjectToScreen(pt3D, viewport);
                screenPts.Add(new PointF(sp.X, sp.Y));
                if (sp.X < minX) minX = sp.X;
                if (sp.Y < minY) minY = sp.Y;
                if (sp.X > maxX) maxX = sp.X;
                if (sp.Y > maxY) maxY = sp.Y;
            }

            if (_selectedSurfaceLine.IsClosedRing && screenPts.Count > 2)
            {
                screenPts.Add(screenPts[0]);
            }

            if (screenPts.Count < 2) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Glow effect sáng bao quanh nét vẽ 3D
            using (Pen glowPen = new Pen(Color.FromArgb(120, 6, 182, 212), _selectedSurfaceLine.StrokeWidth + 6f))
            {
                glowPen.StartCap = LineCap.Round;
                glowPen.EndCap = LineCap.Round;
                glowPen.LineJoin = LineJoin.Round;
                g.DrawLines(glowPen, screenPts.ToArray());
            }

            // 2. Khung viền đứt nét Bounding Box bao quanh
            float pad = 8f;
            RectangleF bbox = RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);

            using (Pen boxPen = new Pen(Color.FromArgb(6, 182, 212), 1.5f))
            {
                boxPen.DashStyle = DashStyle.Dash;
                boxPen.DashPattern = new float[] { 4f, 3f };
                g.DrawRectangle(boxPen, bbox.X, bbox.Y, bbox.Width, bbox.Height);
            }

            // 3. Bốn dấu góc định vị ở 4 góc Bounding Box
            float handleSize = 6f;
            using (SolidBrush cornerBrush = new SolidBrush(Color.FromArgb(6, 182, 212)))
            using (Pen cornerBorder = new Pen(Color.White, 1f))
            {
                PointF[] corners = new PointF[]
                {
                    new PointF(bbox.Left, bbox.Top),
                    new PointF(bbox.Right, bbox.Top),
                    new PointF(bbox.Left, bbox.Bottom),
                    new PointF(bbox.Right, bbox.Bottom)
                };

                foreach (var c in corners)
                {
                    RectangleF cr = new RectangleF(c.X - handleSize * 0.5f, c.Y - handleSize * 0.5f, handleSize, handleSize);
                    g.FillRectangle(cornerBrush, cr);
                    g.DrawRectangle(cornerBorder, cr.X, cr.Y, cr.Width, cr.Height);
                }
            }
        }

        private void DrawSelectedMesh3DBoundingBox(Graphics g, Size viewport, TeddyMesh mesh)
        {
            if (mesh == null || mesh.IsEmpty || mesh.Vertices.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            foreach (var v in mesh.Vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z;
                if (v.Z > maxZ) maxZ = v.Z;
            }

            Vector3[] corners3D = new Vector3[8]
            {
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, minY, minZ),
                new Vector3(maxX, maxY, minZ),
                new Vector3(minX, maxY, minZ),
                new Vector3(minX, minY, maxZ),
                new Vector3(maxX, minY, maxZ),
                new Vector3(maxX, maxY, maxZ),
                new Vector3(minX, maxY, maxZ)
            };

            PointF[] screenCorners = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = _camera.ProjectToScreen(corners3D[i], viewport);
                screenCorners[i] = new PointF(sp.X, sp.Y);
            }

            int[][] edges = new int[][]
            {
                new int[] {0, 1}, new int[] {1, 2}, new int[] {2, 3}, new int[] {3, 0},
                new int[] {4, 5}, new int[] {5, 6}, new int[] {6, 7}, new int[] {7, 4},
                new int[] {0, 4}, new int[] {1, 5}, new int[] {2, 6}, new int[] {3, 7}
            };

            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen boxPen = new Pen(Color.FromArgb(6, 182, 212), 1.6f))
            {
                boxPen.DashStyle = DashStyle.Dash;
                boxPen.DashPattern = new float[] { 4f, 3f };
                foreach (var edge in edges)
                {
                    g.DrawLine(boxPen, screenCorners[edge[0]], screenCorners[edge[1]]);
                }
            }

            float handleSize = 6f;
            using (SolidBrush cornerBrush = new SolidBrush(Color.FromArgb(6, 182, 212)))
            using (Pen cornerBorder = new Pen(Color.White, 1f))
            {
                foreach (var sc in screenCorners)
                {
                    RectangleF cr = new RectangleF(sc.X - handleSize * 0.5f, sc.Y - handleSize * 0.5f, handleSize, handleSize);
                    g.FillRectangle(cornerBrush, cr);
                    g.DrawRectangle(cornerBorder, cr.X, cr.Y, cr.Width, cr.Height);
                }
            }
        }

        private float GetMeshAverageViewZ(TeddyMesh mesh)
        {
            if (mesh == null || mesh.Vertices.Count == 0) return 0f;
            float sumZ = 0f;
            foreach (var v in mesh.Vertices)
            {
                sumZ += _camera.WorldToView(v).Z;
            }
            return sumZ / mesh.Vertices.Count;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.ShiftKey && !_camera.IsInteracting && !_isDrawingStroke)
            {
                _viewportPanel.Cursor = Cursors.Cross;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.ShiftKey && !_camera.IsInteracting && !_isDrawingStroke)
            {
                _viewportPanel.Cursor = Cursors.Default;
            }
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
                    _sceneMeshes.Clear();
                    var loaded = ObjExporter.LoadFromFile(ofd.FileName);
                    _sceneMeshes.Add(loaded);
                    _selectedMesh = loaded;
                    _selectedSurfaceLine = null;
                    UpdateDeleteButtonState();
                    _camera.Reset();
                    _bendReferenceStroke.Clear();
                    _isBaseRingFromCut = false;
                    _hasSentCurrentMeshToLayer = false;
                    UITheme.SetButtonSelected(_btnBendMode, false);
                    _gestureMode = TeddyGestureMode.Painting;
                    SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
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
            if (_sceneMeshes.Count == 0 || _sceneMeshes.All(m => m.IsEmpty))
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
                    ObjExporter.SaveToFile(_sceneMeshes, sfd.FileName);
                    UpdateGestureModeState($"Đã xuất file .OBJ thành công ({_sceneMeshes.Count(m => !m.IsEmpty)} vật thể): {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lưu file .OBJ:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnSendToLayer_Click(object? sender, EventArgs e)
        {
            if (_sceneMeshes.Count == 0 || _sceneMeshes.All(m => m.IsEmpty))
            {
                MessageBox.Show("Chưa có mô hình 3D để chèn vào Layer!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Bitmap snapshot = TeddyRenderer.RenderTransparentSnapshot(
                _viewportPanel.ClientSize.Width,
                _viewportPanel.ClientSize.Height,
                _sceneMeshes,
                _camera,
                _renderStyle,
                _cutPieceVisibility,
                _cutSurfaceViewMode,
                _cutFillColor);

            _onInsertToLayer?.Invoke(snapshot, $"Teddy 3D ({_renderStyle} - {_sceneMeshes.Count} vật thể)");
            _hasSentCurrentMeshToLayer = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;

            if (_sceneMeshes.Count > 0 && !_sceneMeshes.All(m => m.IsEmpty) && !_hasSentCurrentMeshToLayer && this.DialogResult != DialogResult.OK)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "Mô hình 3D hiện tại chưa được chèn về Layer 2D.\nBạn có muốn chèn mô hình 3D vào Layer 2D trước khi đóng cửa sổ 3D không?",
                    "Xác nhận đóng 3D Studio",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (result == DialogResult.Yes)
                {
                    Bitmap snapshot = TeddyRenderer.RenderTransparentSnapshot(
                        _viewportPanel.ClientSize.Width,
                        _viewportPanel.ClientSize.Height,
                        _sceneMeshes,
                        _camera,
                        _renderStyle,
                        _cutPieceVisibility,
                        _cutSurfaceViewMode,
                        _cutFillColor);

                    _onInsertToLayer?.Invoke(snapshot, $"Teddy 3D ({_renderStyle} - {_sceneMeshes.Count} vật thể)");
                    _hasSentCurrentMeshToLayer = true;
                    this.DialogResult = DialogResult.OK;
                }
            }
        }
    }
}
