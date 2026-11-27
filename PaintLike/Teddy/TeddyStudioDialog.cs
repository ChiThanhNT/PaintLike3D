using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV3-06A & TV3-06B] Cửa sổ Không Gian Dựng Hình 3D Độc Lập (Teddy 3D Studio - ACM SIGGRAPH '99)
    /// Áp dụng kiến trúc 2 giai đoạn tường minh:
    /// - GIAI ĐOẠN 1 (Chỉ Vẽ 2D): Kéo Chuột Trái để phác thảo nét 2D vào lớp TeddySketch2DLayer (Chuột Phải xoay 3D, Con lăn Zoom).
    /// - GIAI ĐOẠN 2 (Click Nút Chạy 3D): Chỉ khi người dùng bấm vào các nút thuật toán 3D
    ///   (Tạo Khối 3D, Dán Nét / Tô Vùng, Đặt Vòng Đế, Đùn / Khoét 3D, Cắt Khối 3D, Mượt Bezier 3D, Uốn 3D)
    ///   thì thuật toán tương ứng mới được thực thi trên khối 3D.
    /// </summary>
    public class TeddyStudioDialog : Form
    {
        private class StudioHistoryState
        {
            public List<TeddyMesh> Meshes { get; set; } = new List<TeddyMesh>();
            public List<SketchStroke2D> SketchStrokes { get; set; } = new List<SketchStroke2D>();
        }

        private readonly List<TeddyMesh> _sceneMeshes = new List<TeddyMesh>();
        private TeddyMesh? _selectedMesh = null;
        private TeddySurfaceLine? _selectedSurfaceLine = null;
        private readonly TeddyMesh _emptyMesh = new TeddyMesh();

        private TeddyMesh CurrentMesh => _selectedMesh ?? (_sceneMeshes.Count > 0 ? _sceneMeshes[_sceneMeshes.Count - 1] : _emptyMesh);
        private TeddyMesh ActiveMesh => CurrentMesh;

        private readonly VirtualTrackballCamera _camera = new VirtualTrackballCamera();
        private TeddyRenderStyle _renderStyle = TeddyRenderStyle.PenAndInk;
        private TeddyAlgorithmStepView _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;

        // Lớp Phác Thảo 2D độc lập (Stage A)
        private readonly TeddySketch2DLayer _sketchLayer = new TeddySketch2DLayer();
        private Sketch2DToolMode _sketchToolMode = Sketch2DToolMode.DrawStroke;

        // Undo / Redo Kép (3D Mesh + 2D Sketch Buffer)
        private readonly Stack<StudioHistoryState> _undoStack = new Stack<StudioHistoryState>();
        private readonly Stack<StudioHistoryState> _redoStack = new Stack<StudioHistoryState>();

        // Trạng thái vẽ nét tạm thời trên Viewport
        private readonly List<PointF> _activeStroke = new List<PointF>();
        private bool _isDrawingStroke = false;
        private Point _currentMousePos = Point.Empty;
        private bool _isMouseInViewport = false;
        private Color _surfacePaintColor = Color.FromArgb(37, 99, 235);
        private Color _modelBaseColor = Color.FromArgb(244, 162, 97);

        // Click chọn đối tượng 2D / 3D
        private Point _mouseDownPos = Point.Empty;
        private bool _hasMovedSignificantly = false;

        // Trạng thái Hệ Trục Tọa Độ 3D (Oxyz) & La bàn Trục 3D & Di chuyển Khối 3D
        private bool _show3DAxes = true;
        private TeddyAxisSnapView _hoveredGizmoAxis = TeddyAxisSnapView.None;
        private TeddyAxisDragHandle _hoveredWorldAxisHandle = TeddyAxisDragHandle.None;
        private bool _isDraggingMesh3D = false;
        private bool _dragMoved = false;
        private TeddyAxisDragHandle _activeAxisDragHandle = TeddyAxisDragHandle.None;
        private Vector3 _dragAxisWorldDir = Vector3.Zero;
        private readonly List<TeddyMesh> _draggingTargetMeshes = new List<TeddyMesh>();
        private Point _lastDragMousePos = Point.Empty;

        // Xoay độc lập từng hình 3D quanh tâm O riêng (không xoay các hình khác)
        private bool _isRotatingObject3D = false;
        private bool _rotateMoved = false;
        private Point _lastRotateMousePos = Point.Empty;
        private readonly List<TeddyMesh> _rotatingTargetMeshes = new List<TeddyMesh>();
        private Vector3 _rotatingPivotWorld = Vector3.Zero;

        // Trạng thái & Mẫu đường cắt 3D (Thẳng, Zíc-zắc, Cong, Lượn sóng, Bậc thang, Chữ V, Cắt tự do)
        private TeddyCutPattern _activeCutPattern = TeddyCutPattern.Freehand;
        private TeddyCutActionMode _cutActionMode = TeddyCutActionMode.SplitIntoTwoMeshes;
        private TeddyCutPieceVisibility _cutPieceVisibility = TeddyCutPieceVisibility.Visible;
        private TeddyCutSurfaceViewMode _cutSurfaceViewMode = TeddyCutSurfaceViewMode.HighlightedCrossSection;
        private Color? _cutFillColor = null;
        private bool _flipCutRemoveSide = false;
        private float _cutAmplitudePx = 22f;
        private int _cutPeriodCount = 5;

        // UI Controls
        private Panel _panelTopRibbon = null!;
        private Panel _panelActionBar = null!;
        private Panel _panelAlgoStepBar = null!;
        private Panel _viewportPanel = null!;
        private Panel _panelStatusBar = null!;
        private Label _lblModeBadge = null!;
        private Label _lblStatusText = null!;
        private Label _lblMeshStats = null!;
        private ToolTip _toolTip = null!;
        private readonly Dictionary<TeddyAlgorithmStepView, Button> _stepViewButtons = new Dictionary<TeddyAlgorithmStepView, Button>();
        private readonly Dictionary<TeddyCutPattern, Button> _cutPatternButtons = new Dictionary<TeddyCutPattern, Button>();

        // Nút Khởi tạo & Quản lý Nháp 2D / Lịch sử
        private Button _btnInitBlank = null!;
        private Button _btnSphere = null!;
        private Button _btnCube = null!;
        private Button _btnToolDraw2D = null!;
        private Button _btnToolErase2D = null!;
        private Button _btnClose2DStroke = null!;
        private Button _btnClearSketch2D = null!;
        private Button _btnUndo = null!;
        private Button _btnRedo = null!;
        private Button _btnDeleteSelected = null!;

        // Nút Camera & Render & Màu
        private Button _btnStylePenInk = null!;
        private Button _btnStyleSmooth = null!;
        private Button _btnStyleWire = null!;
        private Button _btnModelColor = null!;
        private Button _btnPaintColor = null!;
        private Button _btnResetCam = null!;
        private Button _btnToggle3DAxes = null!;

        // Nút Cắt & Xuất .OBJ / .PNG
        private Button _btnCutFillColor = null!;
        private Button _btnCutPieceVis = null!;
        private Button _btnFlipCutSide = null!;
        private Button _btnImportObj = null!;
        private Button _btnExportObj = null!;
        private Button _btnExportPng = null!;

        // 7 Nút Click-to-Apply Thuật Toán 3D (Stage B)
        private Button _btnApplyInflate3D = null!;
        private Button _btnApplyPaint3D = null!;
        private Button _btnApplyFillColor3D = null!;
        private Button _btnApplyBaseRing3D = null!;
        private Button _btnApplyExtrude3D = null!;
        private Button _btnApplyCut3D = null!;
        private Button _btnApplyBezierSmooth3D = null!;
        private Button _btnApplyBend3D = null!;
        private Button _btnToolMove3D = null!;

        public TeddyStudioDialog()
        {
            this.Text = "Teddy 3D Freeform Modeler — Dựng Hình 3D Độc Lập Từ Phác Thảo 2D (SIGGRAPH '99)";
            this.ClientSize = new Size(1280, 800);
            this.MinimumSize = new Size(1160, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = UITheme.BgWorkspace;
            this.ForeColor = UITheme.TextPrimary;
            this.Font = UITheme.FontBody;
            this.DoubleBuffered = true;
            this.KeyPreview = true;

            InitializeStudioLayout();
            UpdateStyleButtonSelection();
            UITheme.SetButtonSelected(_btnToggle3DAxes, _show3DAxes);
            Update2DToolSelection();
            UpdateCutPatternToolbarState();
            UpdateDeleteButtonState();
            UpdateStatus("Chế độ Phác thảo 2D: Kéo Chuột Trái vẽ nét 2D (Chuột Phải xoay 3D), sau đó bấm nút thuật toán 3D trên thanh công cụ để tạo/biến đổi khối!");
        }

        private void InitializeStudioLayout()
        {
            _toolTip = new ToolTip
            {
                InitialDelay = 180,
                ReshowDelay = 80,
                AutoPopDelay = 9000,
                ShowAlways = true
            };

            // =========================================================================
            // 1A. THANH RIBBON TRÊN CÙNG: KHỞI TẠO, PHÁC THẢO 2D, CAMERA, CẮT & XUẤT
            // =========================================================================
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

            // --- Nhóm 1: KHỞI TẠO & PHÁC THẢO 2D (X: 8..372) ---
            _btnInitBlank = CreateRibbonButton(_panelTopRibbon, new Rectangle(14, 11, 66, 29), IconType.Plus, "Init Mới", ButtonVariant.Primary,
                "Xóa sạch không gian để bắt đầu phác thảo 2D mới");
            _btnInitBlank.Click += (s, e) => ResetToBlankScene();

            _btnSphere = CreateRibbonButton(_panelTopRibbon, new Rectangle(83, 11, 70, 29), IconType.Circle, "Khối Cầu", ButtonVariant.Default,
                "Triển khai hình vẽ 2D Hình Tròn (Khối Cầu) lên lớp phác thảo. Bấm [Tạo Khối 3D] để dựng thành Khối Cầu 3D!");
            _btnSphere.Click += (s, e) => DeployPresetAs2DSketch("Sphere", Color.FromArgb(244, 162, 97), "Khối Cầu");

            _btnCube = CreateRibbonButton(_panelTopRibbon, new Rectangle(156, 11, 70, 29), IconType.Rectangle, "Khối Hộp", ButtonVariant.Default,
                "Triển khai hình vẽ 2D Hình Vuông (Khối Hộp) lên lớp phác thảo. Bấm [Tạo Khối 3D] để dựng thành Khối Hộp 3D!");
            _btnCube.Click += (s, e) => DeployPresetAs2DSketch("Cube", Color.FromArgb(99, 179, 237), "Khối Hộp");

            _btnToolDraw2D = CreateRibbonButton(_panelTopRibbon, new Rectangle(229, 11, 66, 29), IconType.Rename, "Vẽ 2D", ButtonVariant.Default,
                "Chế độ vẽ phác thảo 2D bằng Chuột Trái (không tự chạy 3D khi nhả chuột)");
            _btnToolDraw2D.Click += (s, e) =>
            {
                _sketchToolMode = Sketch2DToolMode.DrawStroke;
                Update2DToolSelection();
                UpdateStatus("Đang bật [Vẽ 2D]: Kéo Chuột Trái vẽ nét nháp 2D, Chuột Phải xoay 3D, Con lăn Zoom.");
            };

            _btnToolErase2D = CreateRibbonButton(_panelTopRibbon, new Rectangle(298, 11, 66, 29), IconType.Eraser, "Tẩy 2D", ButtonVariant.Default,
                "Chế độ tẩy xóa nét nháp 2D trên lớp phác thảo");
            _btnToolErase2D.Click += (s, e) =>
            {
                _sketchToolMode = Sketch2DToolMode.EraseStroke;
                Update2DToolSelection();
                UpdateStatus("Đang bật [Tẩy 2D]: Kéo Chuột Trái qua các nét nháp 2D để xóa nét vẽ phác thảo.");
            };

            _btnUndo = CreateRibbonButton(_panelTopRibbon, new Rectangle(14, 44, 66, 28), IconType.Undo, "Hoàn tác", ButtonVariant.Default,
                "Hoàn tác bước vẽ 2D hoặc dựng hình 3D trước đó (Ctrl+Z)");
            _btnUndo.Click += (s, e) => PerformUndo();

            _btnRedo = CreateRibbonButton(_panelTopRibbon, new Rectangle(83, 44, 66, 28), IconType.Redo, "Làm lại", ButtonVariant.Default,
                "Làm lại bước vừa hoàn tác (Ctrl+Y)");
            _btnRedo.Click += (s, e) => PerformRedo();

            _btnClose2DStroke = CreateRibbonButton(_panelTopRibbon, new Rectangle(152, 44, 72, 28), IconType.Circle, "Khép kín", ButtonVariant.Default,
                "Nối khép kín điểm đầu và điểm cuối của nét phác thảo 2D đang chọn (hoặc nét cuối cùng)");
            _btnClose2DStroke.Click += (s, e) =>
            {
                if (!_sketchLayer.HasStrokes)
                {
                    UpdateStatus("Chưa có nét 2D nào để khép kín.");
                    return;
                }
                PushUndoState();
                if (_sketchLayer.CloseSelectedOrLastStroke())
                {
                    UpdateStatus("Đã nối khép kín nét 2D! Bấm [Tạo Khối 3D] hoặc [Đặt Vòng Đế] để áp dụng 3D.");
                    _viewportPanel.Invalidate();
                }
            };

            _btnClearSketch2D = CreateRibbonButton(_panelTopRibbon, new Rectangle(227, 44, 72, 28), IconType.Eraser, "Xóa nháp", ButtonVariant.Default,
                "Xóa toàn bộ các nét phác thảo 2D tạm thời trong bộ đệm");
            _btnClearSketch2D.Click += (s, e) =>
            {
                if (!_sketchLayer.HasStrokes) return;
                PushUndoState();
                _sketchLayer.Clear();
                UpdateDeleteButtonState();
                UpdateStatus("Đã xóa toàn bộ nét phác thảo 2D.");
                _viewportPanel.Invalidate();
            };

            _btnDeleteSelected = CreateRibbonButton(_panelTopRibbon, new Rectangle(302, 44, 62, 28), IconType.Trash, "Xóa", ButtonVariant.Default,
                "Xóa nét 2D, nét bề mặt 3D hoặc khối 3D đang được chọn (Phím Delete)");
            _btnDeleteSelected.Enabled = false;
            _btnDeleteSelected.Click += (s, e) => DeleteSelectedEntity();

            // --- Nhóm 2: CAMERA TRACKBALL & RENDER (X: 378..698) ---
            _btnStylePenInk = CreateRibbonButton(_panelTopRibbon, new Rectangle(384, 11, 98, 29), IconType.Rename, "Pen & Ink", ButtonVariant.Default,
                "Phong cách phác họa nét mực + viền đen Silhouette thời gian thực (Hình 1 bài báo)");
            _btnStylePenInk.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.PenAndInk);

            _btnStyleSmooth = CreateRibbonButton(_panelTopRibbon, new Rectangle(486, 11, 101, 29), IconType.Opacity, "Tô bóng 3D", ButtonVariant.Default,
                "Chế độ tô bóng khối 3D Studio mượt (Blinn-Phong + Rim Light)");
            _btnStyleSmooth.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.SmoothShaded);

            _btnStyleWire = CreateRibbonButton(_panelTopRibbon, new Rectangle(591, 11, 101, 29), IconType.Hexagon, "Lưới + Spine", ButtonVariant.Default,
                "Hiển thị lưới tam giác và Trục Xương Sống (Chordal Axis / Spine)");
            _btnStyleWire.Click += (s, e) => SetRenderStyle(TeddyRenderStyle.WireframeSpine);

            _btnResetCam = CreateRibbonButton(_panelTopRibbon, new Rectangle(384, 44, 74, 28), IconType.ZoomIn, "Góc gốc", ButtonVariant.Default,
                "Đưa hình đang chọn (và góc nhìn Camera) về hướng mặc định ban đầu (Space)");
            _btnResetCam.Click += (s, e) => ResetActiveRotationAndCamera();

            _btnToggle3DAxes = CreateRibbonButton(_panelTopRibbon, new Rectangle(461, 44, 71, 28), IconType.Axes3D, "Trục 3D", ButtonVariant.Default,
                "Bật / Tắt Hệ trục tọa độ 3D Oxyz (X Đỏ, Y Xanh lá, Z Xanh dương) & La bàn Trục 3D (Phím A)");
            _btnToggle3DAxes.Click += (s, e) => Toggle3DAxes();

            _btnModelColor = new Button { Bounds = new Rectangle(535, 44, 81, 28) };
            UITheme.StyleButton(_btnModelColor, IconType.ColorPalette, "Màu khối", ButtonVariant.Default, false,
                _toolTip, "Đổi màu sắc chất liệu của khối 3D", () => _modelBaseColor);
            _btnModelColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _modelBaseColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _modelBaseColor = cd.Color;
                    if (_selectedMesh != null) _selectedMesh.BaseColor = _modelBaseColor;
                    else foreach (var m in _sceneMeshes) m.BaseColor = _modelBaseColor;
                    _btnModelColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnModelColor);

            _btnPaintColor = new Button { Bounds = new Rectangle(619, 44, 73, 28) };
            UITheme.StyleButton(_btnPaintColor, IconType.ColorPalette, "Màu bút", ButtonVariant.Default, false,
                _toolTip, "Đổi màu bút vẽ 2D / trang trí bề mặt 3D", () => _surfacePaintColor);
            _btnPaintColor.Click += (s, e) =>
            {
                using var cd = new ColorDialog { Color = _surfacePaintColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _surfacePaintColor = cd.Color;
                    _btnPaintColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnPaintColor);

            // --- Nhóm 3: MẪU ĐƯỜNG CẮT 3D (X: 703..1101) ---
            AddCutPatternButton(new Rectangle(709, 11, 62, 29), IconType.CutFreehand, "Tự Do", TeddyCutPattern.Freehand,
                "Mẫu vẽ nét 2D tự do (dùng để tạo khối 3D, vẽ mặt hoặc cắt tự do)");
            AddCutPatternButton(new Rectangle(774, 11, 60, 29), IconType.CutStraight, "Thẳng", TeddyCutPattern.Straight,
                "Mẫu đường cắt Thẳng: Kéo Chuột Trái tạo đường cắt 2D thẳng, sau đó bấm [Cắt Khối 3D]");
            AddCutPatternButton(new Rectangle(837, 11, 65, 29), IconType.CutZigzag, "Zíc-zắc", TeddyCutPattern.Zigzag,
                "Mẫu đường cắt Zíc-zắc: Kéo Chuột Trái tạo đường răng cưa 2D, sau đó bấm [Cắt Khối 3D]");
            AddCutPatternButton(new Rectangle(905, 11, 58, 29), IconType.CutCurved, "Cong", TeddyCutPattern.Curved,
                "Mẫu đường cắt Cong: Kéo Chuột Trái tạo đường cong vòm 2D, sau đó bấm [Cắt Khối 3D]");
            AddCutPatternButton(new Rectangle(966, 11, 58, 29), IconType.CutWave, "Sóng", TeddyCutPattern.Wave,
                "Mẫu đường cắt Lượn Sóng: Kéo Chuột Trái tạo đường sóng Sin 2D, sau đó bấm [Cắt Khối 3D]");
            AddCutPatternButton(new Rectangle(1027, 11, 68, 29), IconType.CutStepped, "Bậc Thang", TeddyCutPattern.Stepped,
                "Mẫu đường cắt Bậc Thang: Kéo Chuột Trái tạo đường bậc thang 2D, sau đó bấm [Cắt Khối 3D]");

            AddCutPatternButton(new Rectangle(709, 44, 62, 28), IconType.CutChevronV, "Chữ V", TeddyCutPattern.ChevronV,
                "Mẫu đường cắt Chữ V: Kéo Chuột Trái tạo đường gấp khúc chữ V 2D, sau đó bấm [Cắt Khối 3D]");

            _btnCutFillColor = new Button { Bounds = new Rectangle(774, 44, 86, 28) };
            UITheme.StyleButton(_btnCutFillColor, IconType.ColorPalette, "Màu trong", ButtonVariant.Default, false,
                _toolTip, "Màu phía trong hình khối tại vết cắt (mặc định sẫm màu hơn bên ngoài)",
                () => _cutFillColor ?? Color.FromArgb(
                    (int)(_modelBaseColor.R * 0.56f),
                    (int)(_modelBaseColor.G * 0.52f),
                    (int)(_modelBaseColor.B * 0.54f)));
            _btnCutFillColor.Click += (s, e) =>
            {
                Color initColor = _cutFillColor ?? Color.FromArgb(
                    (int)(_modelBaseColor.R * 0.56f),
                    (int)(_modelBaseColor.G * 0.52f),
                    (int)(_modelBaseColor.B * 0.54f));
                using var cd = new ColorDialog { Color = initColor, FullOpen = true };
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    _cutFillColor = cd.Color;
                    _btnCutFillColor.Invalidate();
                    _viewportPanel.Invalidate();
                }
            };
            _panelTopRibbon.Controls.Add(_btnCutFillColor);

            _btnCutPieceVis = new Button { Bounds = new Rectangle(863, 44, 112, 28) };
            _btnCutPieceVis.Click += (s, e) =>
            {
                _cutPieceVisibility = _cutPieceVisibility == TeddyCutPieceVisibility.Visible
                    ? TeddyCutPieceVisibility.Hidden
                    : TeddyCutPieceVisibility.Visible;
                UpdateCutPatternToolbarState();
                UpdateStatus(_cutPieceVisibility == TeddyCutPieceVisibility.Visible
                    ? "Đang GIỮ & HIỆN phần bị cắt cùng mặt trong tô sẫm màu."
                    : "Đang ẨN tạm phần bị cắt để nhìn trực diện vào mặt trong tô sẫm màu.");
                _viewportPanel.Invalidate();
            };
            _panelTopRibbon.Controls.Add(_btnCutPieceVis);

            _btnFlipCutSide = new Button { Bounds = new Rectangle(978, 44, 72, 28) };
            UITheme.StyleButton(_btnFlipCutSide, IconType.Arrows, "Đảo bên", ButtonVariant.Default, false,
                _toolTip, "Đảo chiều phía bị cắt (Trái <-> Phải)");
            _btnFlipCutSide.Click += (s, e) =>
            {
                _flipCutRemoveSide = !_flipCutRemoveSide;
                UITheme.SetButtonSelected(_btnFlipCutSide, _flipCutRemoveSide);
                UpdateStatus(_flipCutRemoveSide ? "Đã đảo phía bị cắt sang chiều ngược lại." : "Đã trở về chiều cắt mặc định.");
                _viewportPanel.Invalidate();
            };
            _panelTopRibbon.Controls.Add(_btnFlipCutSide);

            // --- Nhóm 4: XUẤT / NHẬP .OBJ & XUẤT ẢNH .PNG ĐỘC LẬP (X: 1106..1274) ---
            _btnImportObj = CreateRibbonButton(_panelTopRibbon, new Rectangle(1112, 11, 76, 29), IconType.Import, "Mở .OBJ", ButtonVariant.Default,
                "Mở file mô hình 3D định dạng Wavefront .OBJ");
            _btnImportObj.Click += BtnImportObj_Click;

            _btnExportObj = CreateRibbonButton(_panelTopRibbon, new Rectangle(1191, 11, 77, 29), IconType.Save, "Lưu .OBJ", ButtonVariant.Accent,
                "Xuất mô hình 3D ra file .OBJ tiêu chuẩn (Mục 6 bài báo)");
            _btnExportObj.Click += BtnExportObj_Click;

            _btnExportPng = CreateRibbonButton(_panelTopRibbon, new Rectangle(1112, 44, 156, 28), IconType.Export, "Xuất Ảnh PNG", ButtonVariant.Success,
                "Kết xuất mô hình 3D hiện tại ra file ảnh .PNG nền trong suốt (RGBA)");
            _btnExportPng.Click += BtnExportPng_Click;

            // =========================================================================
            // 1B. THANH NÚT THỰC THI THUẬT TOÁN 3D (STAGE 2: CLICK TO APPLY 3D)
            // =========================================================================
            _panelActionBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(245, 248, 255)
            };
            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, _panelActionBar, new object[] { true });
            _panelActionBar.Paint += (s, e) =>
            {
                using Pen bottomLine = new Pen(UITheme.BorderDefault, 1f);
                e.Graphics.DrawLine(bottomLine, 0, _panelActionBar.Height - 1, _panelActionBar.Width, _panelActionBar.Height - 1);
            };

            Label lblRun3D = new Label
            {
                AutoSize = true,
                Location = new Point(10, 12),
                Font = UITheme.FontGroupHeader,
                ForeColor = UITheme.AccentPrimary,
                Text = "THUẬT TOÁN & DI CHUYỂN 3D:"
            };
            _panelActionBar.Controls.Add(lblRun3D);

            _btnApplyInflate3D = CreateRibbonButton(_panelActionBar, new Rectangle(176, 6, 110, 28), IconType.Cube3D, "Tạo Khối 3D", ButtonVariant.Primary,
                "Lấy nét phác thảo 2D khép kín trong bộ đệm → Thổi phồng thành khối 3D (Mục 5.1: CDT + Pruning + Oval)");
            _btnApplyInflate3D.Click += (s, e) => ApplyInflate3DFromSketch();

            _btnApplyPaint3D = CreateRibbonButton(_panelActionBar, new Rectangle(290, 6, 104, 28), IconType.Rename, "Dán Nét 3D", ButtonVariant.Default,
                "Chiếu các nét phác thảo 2D đang nằm trên bóng khối 3D xuống bề mặt vật thể (Mục 5.2)");
            _btnApplyPaint3D.Click += (s, e) => ApplySurfacePaintFromSketch();

            _btnApplyFillColor3D = CreateRibbonButton(_panelActionBar, new Rectangle(398, 6, 102, 28), IconType.ColorPalette, "Tô Vùng 3D", ButtonVariant.Default,
                "Tô màu các mặt tam giác bên trong Vòng chọn đỏ (hoặc nét khoanh 2D khép kín) bằng Màu bút (Phím F)");
            _btnApplyFillColor3D.Click += (s, e) => ApplyFillRegionFromSketch();

            _btnApplyBaseRing3D = CreateRibbonButton(_panelActionBar, new Rectangle(504, 6, 110, 28), IconType.Circle, "Đặt Vòng Đế", ButtonVariant.Default,
                "Lấy nét 2D khép kín trên mặt khối → Chiếu thành Vòng Đế đỏ (ActiveBaseRing) để chuẩn bị Đùn/Khoét/Làm mượt");
            _btnApplyBaseRing3D.Click += (s, e) => ApplySetBaseRingFromSketch();

            _btnApplyExtrude3D = CreateRibbonButton(_panelActionBar, new Rectangle(618, 6, 120, 28), IconType.Arrows, "Đùn / Khoét 3D", ButtonVariant.Accent,
                "Từ Vòng Đế đỏ + nét vòm 2D → Thực hiện Đùn lồi (Extrusion) hoặc Khoét lõm (Cavity) 2 con trỏ (Mục 5.3)");
            _btnApplyExtrude3D.Click += (s, e) => ApplyExtrudeOrCavityFromSketch();

            _btnApplyCut3D = CreateRibbonButton(_panelActionBar, new Rectangle(742, 6, 106, 28), IconType.Scissors, "Cắt Khối 3D", ButtonVariant.Danger,
                "Dùng đường cắt 2D trong bộ đệm (hoặc cắt ngang tâm khối theo mẫu đang chọn) để Cắt vật thể 3D (Mục 5.4)");
            _btnApplyCut3D.Click += (s, e) => ApplyCut3DFromSketch();

            _btnApplyBezierSmooth3D = CreateRibbonButton(_panelActionBar, new Rectangle(852, 6, 122, 28), IconType.Filter, "Mượt Bezier 3D", ButtonVariant.Default,
                "Làm mượt bề mặt trong Vòng Đế đỏ / nét khoanh 2D bằng nội suy Bezier & Taubin Fairing (Mục 5.5)");
            _btnApplyBezierSmooth3D.Click += (s, e) => ApplyBezierSmoothFromSketch();

            _btnApplyBend3D = CreateRibbonButton(_panelActionBar, new Rectangle(978, 6, 106, 28), IconType.Bezier, "Uốn Khối 3D", ButtonVariant.Default,
                "Lấy 2 nét 2D trong bộ đệm (Nét #1 Reference → Nét #2 Target) để Uốn biến dạng mô hình 3D (Mục 4.7)");
            _btnApplyBend3D.Click += (s, e) => ApplyBend3DFromSketch();

            _btnToolMove3D = CreateRibbonButton(_panelActionBar, new Rectangle(1088, 6, 120, 28), IconType.Arrows, "Di Chuyển 3D", ButtonVariant.Default,
                "Bật chế độ Di chuyển Khối 3D sang vị trí khác (Phím M). Hoặc nắm kéo trực tiếp tâm O / mũi tên trục X, Y, Z trên khối!");
            _btnToolMove3D.Click += (s, e) => ToggleMove3DMode();

            // =========================================================================
            // 1C. THANH MẪU BÀI BÁO & TRỰC QUAN HÓA TỪNG BƯỚC THUẬT TOÁN (HÌNH 13–15)
            // =========================================================================
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
                "Triển khai hình vẽ 2D Gấu Teddy (Hình 1 & 2 bài báo). Bấm [Tạo Khối 3D] để thổi phồng thành mô hình 3D!");
            CreatePresetButton(new Rectangle(190, 5, 92, 25), "Quả Cherry", "Cherry", Color.FromArgb(239, 68, 68),
                "Triển khai hình vẽ 2D Quả Cherry (Hình 5c). Bấm [Tạo Khối 3D] để dựng thành mô hình 3D!");
            CreatePresetButton(new Rectangle(286, 5, 82, 25), "Con Rắn", "Snake", Color.FromArgb(34, 197, 94),
                "Triển khai hình vẽ 2D Con Rắn (Hình 5a). Bấm [Tạo Khối 3D] để dựng thành mô hình 3D!");

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
                "Hình 13a: Đa giác phẳng 2D với các cạnh biên có độ dài đều nhau");
            AddStepViewButton(new Rectangle(800, 5, 106, 25), "2. CDT T/S/J 13b", TeddyAlgorithmStepView.Step2_CDT_T_S_J,
                "Hình 13b: Lưới CDT & Phân loại tam giác Terminal (T), Sleeve (S), Junction (J)");
            AddStepViewButton(new Rectangle(910, 5, 100, 25), "3. Trục Thô 13c", TeddyAlgorithmStepView.Step3_RawChordalAxis,
                "Hình 13c: Trục Chordal Axis thô trước khi cắt tỉa nhánh phụ");
            AddStepViewButton(new Rectangle(1014, 5, 98, 25), "4. Spine 13e-f", TeddyAlgorithmStepView.Step4_PruningAndSpine,
                "Hình 13e-f & Hình 14: Cắt tỉa nửa đường tròn (Pruning) và rút trích trục Spine");
            AddStepViewButton(new Rectangle(1116, 5, 94, 25), "5. Oval 3D 15c", TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D,
                "Hình 15b-c: Nâng cao độ Z cho trục Spine và tạo các cung 1/4 elip (Xoay bằng Chuột Phải!)");

            SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);

            // =========================================================================
            // 2. THANH TRẠNG THÁI PHÍA DƯỚI (STATUS BAR)
            // =========================================================================
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
                Size = new Size(165, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UITheme.FontSmallBold,
                BackColor = UITheme.AccentPrimary,
                ForeColor = Color.White,
                Text = "2D SKETCH MODE"
            };
            _panelStatusBar.Controls.Add(_lblModeBadge);

            _lblStatusText = new Label
            {
                AutoSize = false,
                Location = new Point(184, 2),
                Size = new Size(820, 26),
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
                Width = 280,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 12, 0),
                Font = UITheme.FontMono,
                ForeColor = UITheme.TextSecondary,
                Text = "Nét 2D: 0 | Khối 3D: 0"
            };
            _panelStatusBar.Controls.Add(_lblMeshStats);

            // =========================================================================
            // 3. KHUNG VIEWPORT 3D + LỚP PHÁC THẢO 2D TRONG SUỐT ĐÈ BÊN TRÊN
            // =========================================================================
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
            _viewportPanel.MouseEnter += (s, e) => { _isMouseInViewport = true; _viewportPanel.Invalidate(); };
            _viewportPanel.MouseLeave += (s, e) =>
            {
                _isMouseInViewport = false;
                _hoveredGizmoAxis = TeddyAxisSnapView.None;
                _hoveredWorldAxisHandle = TeddyAxisDragHandle.None;
                _viewportPanel.Invalidate();
            };
            this.MouseWheel += ViewportPanel_MouseWheel;

            this.Controls.Add(_viewportPanel);
            this.Controls.Add(_panelAlgoStepBar);
            this.Controls.Add(_panelActionBar);
            this.Controls.Add(_panelTopRibbon);
            this.Controls.Add(_panelStatusBar);
        }

        private static Button CreateRibbonButton(Panel parent, Rectangle bounds, IconType icon, string text, ButtonVariant variant, string tooltip, ToolTip? tt = null)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, icon, text, variant, false, tt, tooltip);
            parent.Controls.Add(btn);
            return btn;
        }

        private Button CreateRibbonButton(Panel parent, Rectangle bounds, IconType icon, string text, ButtonVariant variant, string tooltip)
        {
            return CreateRibbonButton(parent, bounds, icon, text, variant, tooltip, _toolTip);
        }

        private void AddCutPatternButton(Rectangle bounds, IconType icon, string label, TeddyCutPattern pattern, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, icon, label, ButtonVariant.Default, false, _toolTip, tooltip);
            btn.Click += (s, e) =>
            {
                _activeCutPattern = pattern;
                _sketchToolMode = Sketch2DToolMode.DrawStroke;
                Update2DToolSelection();
                UpdateCutPatternToolbarState();
                string pName = TeddyCutPatternGenerator.GetPatternDisplayName(pattern);
                UpdateStatus($"Đã chọn nét 2D mẫu [{pName}]: Kéo Chuột Trái vẽ lên vùng nhìn, rồi bấm [Cắt Khối 3D] (hoặc [Tạo Khối 3D] nếu là Tự Do).");
                _viewportPanel.Invalidate();
            };
            _panelTopRibbon.Controls.Add(btn);
            _cutPatternButtons[pattern] = btn;
        }

        private void CreatePresetButton(Rectangle bounds, string label, string presetKey, Color presetColor, string tooltip)
        {
            Button btn = new Button { Bounds = bounds };
            UITheme.StyleButton(btn, IconType.Star, label, ButtonVariant.Default, false, _toolTip, tooltip);
            btn.Click += (s, e) => DeployPresetAs2DSketch(presetKey, presetColor, label);
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

        private void PanelTopRibbon_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(UITheme.BgToolbar);

            UITheme.DrawRibbonGroup(g, new Rectangle(8, 4, 364, 86), "KHỞI TẠO & PHÁC THẢO 2D");
            UITheme.DrawRibbonGroup(g, new Rectangle(378, 4, 320, 86), "CAMERA TRACKBALL & RENDER");
            UITheme.DrawRibbonGroup(g, new Rectangle(703, 4, 398, 86), "MẪU ĐƯỜNG CẮT 3D & HIỂN THỊ MẶT CẮT");
            UITheme.DrawRibbonGroup(g, new Rectangle(1106, 4, 168, 86), "XUẤT .OBJ & ẢNH .PNG");

            using Pen bottomLine = new Pen(UITheme.BorderDefault, 1f);
            g.DrawLine(bottomLine, 0, _panelTopRibbon.Height - 1, _panelTopRibbon.Width, _panelTopRibbon.Height - 1);
        }

        // =========================================================================
        // GIAI ĐOẠN 1: VẼ & CHỈNH SỬA PHÁC THẢO 2D (KHÔNG TỰ CHẠY 3D TRÊN MOUSEUP)
        // =========================================================================

        private void ViewportPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            _mouseDownPos = e.Location;
            _currentMousePos = e.Location;
            _hasMovedSignificantly = false;
            bool isAlt = Control.ModifierKeys.HasFlag(Keys.Alt);

            if (e.Button == MouseButtons.Middle)
            {
                _camera.StartPan(e.Location);
                _viewportPanel.Cursor = Cursors.Hand;
            }
            else if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && isAlt))
            {
                if (_algorithmStepView == TeddyAlgorithmStepView.Final3DModel && HasVisibleMeshes())
                {
                    if (HitTestMesh(e.Location, _viewportPanel.ClientSize, out var hitRotateMesh) && hitRotateMesh != null)
                    {
                        _sketchLayer.SelectedStroke = null;
                        _selectedSurfaceLine = null;
                        _selectedMesh = hitRotateMesh;
                        UpdateDeleteButtonState();
                    }

                    var targetGroup = GetMeshGroup(_selectedMesh ?? ActiveMesh);
                    if (targetGroup.Count > 0 && ComputeMeshesBounds(targetGroup, out Vector3 rMin, out Vector3 rMax))
                    {
                        _rotatingTargetMeshes.Clear();
                        _rotatingTargetMeshes.AddRange(targetGroup);
                        _rotatingPivotWorld = (rMin + rMax) * 0.5f;
                        PushUndoState();
                        _isRotatingObject3D = true;
                        _rotateMoved = false;
                        _lastRotateMousePos = e.Location;
                        _viewportPanel.Cursor = Cursors.SizeAll;
                        UpdateStatus($"Đang xoay độc lập hình 3D đang chọn quanh tâm O({(int)MathF.Round(_rotatingPivotWorld.X)}, {(int)MathF.Round(_rotatingPivotWorld.Y)}, {(int)MathF.Round(_rotatingPivotWorld.Z)}) (hình khác đứng yên).");
                        _viewportPanel.Invalidate();
                        return;
                    }
                }

                SyncCameraPivotToActiveShape();
                _camera.StartRotate(e.Location);
                _viewportPanel.Cursor = Cursors.SizeAll;
            }
            else if (e.Button == MouseButtons.Left)
            {
                Quaternion? activeGizmoObjRot = null;
                if (_show3DAxes &&
                    (_algorithmStepView == TeddyAlgorithmStepView.Final3DModel || _algorithmStepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D))
                {
                    GetActiveShapeAxesAnchor(out _, out _, out var gBounds, out Quaternion gRot,
                        _algorithmStepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D ? ActiveMesh : null);
                    if (gBounds.HasValue) activeGizmoObjRot = gRot;

                    if (TeddyRenderer.HitTestCornerAxisGizmo(_viewportPanel.ClientSize, _camera, e.Location, out var hitAxis, out var gizmoCard, activeGizmoObjRot) &&
                        gizmoCard.Contains(e.Location))
                    {
                        if (hitAxis != TeddyAxisSnapView.None)
                        {
                            SnapCameraToAxisView(hitAxis);
                        }
                        return;
                    }
                }

                // Kiểm tra kéo trực tiếp vào Tâm O hoặc Trục X, Y, Z của khối 3D đang chọn
                bool isCtrl = Control.ModifierKeys.HasFlag(Keys.Control);
                if (_show3DAxes &&
                    _algorithmStepView == TeddyAlgorithmStepView.Final3DModel &&
                    HasVisibleMeshes())
                {
                    GetActiveShapeAxesAnchor(out Vector3 hOrigin, out float hRadius, out var hBounds, out Quaternion hRot);
                    if (hBounds.HasValue &&
                        TeddyRenderer.HitTestWorldAxesGizmo(_viewportPanel.ClientSize, _camera, hOrigin, hRadius, e.Location, out var dragHandle, out Vector3 axisDir, hRot))
                    {
                        StartDraggingMesh3D(dragHandle, axisDir, e.Location);
                        return;
                    }
                }

                // Kiểm tra chế độ [Di Chuyển 3D] hoặc giữ Ctrl + Kéo Chuột Trái lên khối 3D
                if ((_sketchToolMode == Sketch2DToolMode.Move3DMesh || isCtrl) &&
                    _algorithmStepView == TeddyAlgorithmStepView.Final3DModel &&
                    HasVisibleMeshes())
                {
                    if (HitTestMesh(e.Location, _viewportPanel.ClientSize, out var hitMesh) && hitMesh != null)
                    {
                        _sketchLayer.SelectedStroke = null;
                        _selectedSurfaceLine = null;
                        _selectedMesh = hitMesh;
                        UpdateDeleteButtonState();
                        StartDraggingMesh3D(TeddyAxisDragHandle.FreeCenterO, Vector3.Zero, e.Location, hitMesh);
                        return;
                    }
                    else if (_sketchToolMode == Sketch2DToolMode.Move3DMesh)
                    {
                        StartDraggingMesh3D(TeddyAxisDragHandle.FreeCenterO, Vector3.Zero, e.Location);
                        return;
                    }
                }

                if (_sketchToolMode == Sketch2DToolMode.EraseStroke)
                {
                    PushUndoState();
                    _isDrawingStroke = true;
                    if (_sketchLayer.EraseAt(e.Location))
                    {
                        UpdateDeleteButtonState();
                        UpdateStatus("Đã tẩy nét phác thảo 2D.");
                    }
                    _viewportPanel.Invalidate();
                }
                else
                {
                    // Chuột Trái trực tiếp: Vẽ nét 2D vào lớp phác thảo (không cần Shift, không tự chạy 3D)
                    _isDrawingStroke = true;
                    _activeStroke.Clear();
                    _activeStroke.Add(e.Location);
                    _viewportPanel.Cursor = Cursors.Cross;
                    _viewportPanel.Invalidate();
                }
            }
        }

        private void ViewportPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            _currentMousePos = e.Location;

            if (!_hasMovedSignificantly && e.Button != MouseButtons.None)
            {
                if (Math.Abs(e.X - _mouseDownPos.X) > 4 || Math.Abs(e.Y - _mouseDownPos.Y) > 4)
                {
                    _hasMovedSignificantly = true;
                }
            }

            if (_isRotatingObject3D)
            {
                UpdateRotatingObject3D(e.Location);
            }
            else if (_camera.IsInteracting)
            {
                _camera.OnMouseMove(e.Location, _viewportPanel.ClientSize);
                _viewportPanel.Invalidate();
            }
            else if (_isDraggingMesh3D && e.Button == MouseButtons.Left)
            {
                UpdateDraggingMesh3D(e.Location);
            }
            else if (_isDrawingStroke && e.Button == MouseButtons.Left)
            {
                if (_sketchToolMode == Sketch2DToolMode.EraseStroke)
                {
                    if (_sketchLayer.EraseAt(e.Location))
                    {
                        UpdateDeleteButtonState();
                        _viewportPanel.Invalidate();
                    }
                    else
                    {
                        _viewportPanel.Invalidate();
                    }
                }
                else
                {
                    if (_activeStroke.Count == 0 || StrokeProcessor.Distance(_activeStroke[_activeStroke.Count - 1], e.Location) >= 2.5f)
                    {
                        _activeStroke.Add(e.Location);
                        _viewportPanel.Invalidate();
                    }
                }
            }
            else
            {
                TeddyAxisSnapView hoverAxis = TeddyAxisSnapView.None;
                TeddyAxisDragHandle hoverWorldHandle = TeddyAxisDragHandle.None;

                if (_show3DAxes &&
                    (_algorithmStepView == TeddyAlgorithmStepView.Final3DModel || _algorithmStepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D))
                {
                    GetActiveShapeAxesAnchor(out Vector3 hOrigin, out float hRadius, out var hBounds, out Quaternion hRot,
                        _algorithmStepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D ? ActiveMesh : null);
                    Quaternion? activeGizmoObjRot = hBounds.HasValue ? hRot : (Quaternion?)null;

                    TeddyRenderer.HitTestCornerAxisGizmo(_viewportPanel.ClientSize, _camera, e.Location, out hoverAxis, out _, activeGizmoObjRot);

                    if (hoverAxis == TeddyAxisSnapView.None && _algorithmStepView == TeddyAlgorithmStepView.Final3DModel && HasVisibleMeshes() && hBounds.HasValue)
                    {
                        TeddyRenderer.HitTestWorldAxesGizmo(_viewportPanel.ClientSize, _camera, hOrigin, hRadius, e.Location, out hoverWorldHandle, out _, hRot);
                    }
                }

                if (hoverAxis != TeddyAxisSnapView.None)
                {
                    _viewportPanel.Cursor = Cursors.Hand;
                }
                else if (hoverWorldHandle == TeddyAxisDragHandle.FreeCenterO)
                {
                    _viewportPanel.Cursor = Cursors.SizeAll;
                }
                else if (hoverWorldHandle != TeddyAxisDragHandle.None)
                {
                    _viewportPanel.Cursor = Cursors.Hand;
                }
                else
                {
                    _viewportPanel.Cursor = GetDefaultModeCursor();
                }

                if (hoverAxis != _hoveredGizmoAxis || hoverWorldHandle != _hoveredWorldAxisHandle)
                {
                    _hoveredGizmoAxis = hoverAxis;
                    _hoveredWorldAxisHandle = hoverWorldHandle;
                    _viewportPanel.Invalidate();
                }
                else if (_sketchToolMode == Sketch2DToolMode.EraseStroke)
                {
                    _viewportPanel.Invalidate();
                }
            }
        }

        private void ViewportPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_isRotatingObject3D)
            {
                FinishRotatingObject3D();
                return;
            }

            if (_camera.IsInteracting)
            {
                _camera.StopInteraction();
                _viewportPanel.Cursor = GetDefaultModeCursor();

                if (e.Button == MouseButtons.Left && !_hasMovedSignificantly)
                {
                    HandleViewportClick(e.Location);
                    _viewportPanel.Invalidate();
                }
                return;
            }

            if (e.Button == MouseButtons.Left && _isDraggingMesh3D)
            {
                FinishDraggingMesh3D(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left && _isDrawingStroke)
            {
                _isDrawingStroke = false;

                if (_sketchToolMode == Sketch2DToolMode.EraseStroke)
                {
                    _viewportPanel.Invalidate();
                    return;
                }

                // Nếu click nhẹ tại chỗ (không kéo vẽ) -> Chọn nét 2D hoặc Khối 3D
                if (!_hasMovedSignificantly)
                {
                    _activeStroke.Clear();
                    HandleViewportClick(e.Location);
                    _viewportPanel.Invalidate();
                    return;
                }

                // Kéo vẽ nét 2D xong -> CHỈ LƯU VÀO LỚP PHÁC THẢO 2D, TUYỆT ĐỐI KHÔNG TỰ CHẠY 3D
                float strokeLen = StrokeProcessor.ComputePolylineLength(_activeStroke, false);
                if (_activeStroke.Count >= 2 && strokeLen >= 10f)
                {
                    PushUndoState();
                    var added = _sketchLayer.AddRawStroke(
                        _activeStroke,
                        _surfacePaintColor,
                        3.0f,
                        _activeCutPattern,
                        _cutAmplitudePx,
                        _cutPeriodCount,
                        _flipCutRemoveSide);

                    if (added != null)
                    {
                        _selectedSurfaceLine = null;
                        // Nếu vẽ nét tự do mới ở vùng trống bên ngoài khối đang chọn -> hủy chọn khối cũ để trục 3D đi theo nét mới!
                        if (added.CutPattern == TeddyCutPattern.Freehand && _selectedMesh != null && !StrokeHitsMesh(_activeStroke, _selectedMesh))
                        {
                            _selectedMesh = null;
                        }
                        UpdateDeleteButtonState();

                        if (added.IsSelfIntersecting)
                        {
                            UpdateStatus($"⚠ {added.Label} bị TỰ CẮT NHAU! Hãy tẩy/xóa nét hoặc vẽ lại đường trơn trước khi bấm [Tạo Khối 3D].");
                        }
                        else if (added.CutPattern != TeddyCutPattern.Freehand)
                        {
                            UpdateStatus($"Đã ghi nhận {added.Label} vào bộ đệm 2D. Bấm nút [Cắt Khối 3D] ở thanh dưới để thực thi phép cắt!");
                        }
                        else if (_sceneMeshes.Count == 0)
                        {
                            UpdateStatus($"Đã lưu {added.Label} vào lớp phác thảo 2D. Bấm [Tạo Khối 3D] để thổi phồng thành mô hình 3D!");
                        }
                        else
                        {
                            UpdateStatus($"Đã lưu {added.Label} vào lớp phác thảo 2D. Hãy chọn thao tác 3D: [Tạo Khối 3D], [Dán Nét 3D], [Đặt Vòng Đế], [Đùn / Khoét 3D], [Cắt Khối 3D] hoặc [Uốn Khối 3D].");
                        }
                    }
                    else
                    {
                        RestoreFromUndoWithoutRedo();
                    }
                }

                _activeStroke.Clear();
                _viewportPanel.Invalidate();
            }
        }

        private void ViewportPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            _camera.OnMouseWheel(e.Delta);
            _viewportPanel.Invalidate();
        }

        // =========================================================================
        // GIAI ĐOẠN 2: CÁC NÚT BẤM THỰC THI THUẬT TOÁN 3D TƯỜNG MINH (CLICK-TO-APPLY)
        // =========================================================================

        private SketchStroke2D? GetTargetSketchStroke()
        {
            return _sketchLayer.SelectedStroke ?? (_sketchLayer.Strokes.Count > 0 ? _sketchLayer.Strokes[_sketchLayer.Strokes.Count - 1] : null);
        }

        private void ConsumeSketchStroke(SketchStroke2D stroke)
        {
            _sketchLayer.Strokes.Remove(stroke);
            _sketchLayer.SelectedStroke = _sketchLayer.Strokes.Count > 0 ? _sketchLayer.Strokes[_sketchLayer.Strokes.Count - 1] : null;
        }

        private TeddyMesh? PickTargetMeshForStroke(IReadOnlyList<PointF> pts)
        {
            var sorted = _sceneMeshes.Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                                     .OrderByDescending(m => GetMeshAverageViewZ(m))
                                     .ToList();
            if (sorted.Count == 0) return null;

            if (pts != null && pts.Count > 0)
            {
                foreach (var m in sorted)
                {
                    foreach (var p in pts)
                    {
                        if (TeddyRaycaster.RaycastMesh(m, p, _camera, _viewportPanel.ClientSize, out _, out _, out _))
                            return m;
                    }
                }
            }

            return (_selectedMesh != null && !_selectedMesh.IsEmpty) ? _selectedMesh : sorted[0];
        }

        /// <summary>
        /// Nút 1: [Tạo Khối 3D] — Mục 5.1 & TV1-06 (Thổi phồng đồng loạt toàn bộ nét 2D hợp lệ:
        /// CDT + Oriented Semicircle Pruning + Topological Sewing + Quarter-Oval Elevation).
        /// </summary>
        private void ApplyInflate3DFromSketch()
        {
            var candidates = _sketchLayer.Strokes.Where(s => s.CutPattern == TeddyCutPattern.Freehand).ToList();
            if (candidates.Count == 0)
            {
                UpdateStatus("Chưa có nét phác thảo 2D nào! Hãy vẽ nét 2D hoặc triển khai hình mẫu 2D trước khi bấm [Tạo Khối 3D].");
                return;
            }

            PushUndoState();
            int createdCount = 0;
            int failedCount = 0;
            string lastMsg = "";
            string lastFailMsg = "";
            var consumed = new List<SketchStroke2D>();
            Size vp = _viewportPanel.ClientSize;

            foreach (var sk in candidates)
            {
                Color meshColor = sk.PresetColor ?? _modelBaseColor;

                if (sk.PresetKey == "Sphere")
                {
                    TeddyMesh sphere = CreateSphereMeshFrom2DStroke(sk.Points, vp, meshColor);
                    _sceneMeshes.Add(sphere);
                    _selectedMesh = sphere;
                    _selectedSurfaceLine = null;
                    createdCount++;
                    lastMsg = $"Khối Cầu 3D chuẩn tròn ({sphere.Vertices.Count} đỉnh, {sphere.Triangles.Count} mặt)";
                    consumed.Add(sk);
                    continue;
                }

                if (sk.PresetKey == "Cube")
                {
                    TeddyMesh cube = CreateCubeMeshFrom2DStroke(sk.Points, vp, meshColor);
                    _sceneMeshes.Add(cube);
                    _selectedMesh = cube;
                    _selectedSurfaceLine = null;
                    createdCount++;
                    lastMsg = "Khối Hộp 3D (Subdivided Cube)";
                    consumed.Add(sk);
                    continue;
                }

                if (TeddyInflationEngine.TryCreateMeshFromStroke(
                    sk.Points,
                    _camera,
                    vp,
                    meshColor,
                    out TeddyMesh? created,
                    out string msg) && created != null)
                {
                    if (sk.PresetKey == "TeddySilhouette" || sk.PresetKey == "Cherry" || sk.PresetKey == "Snake")
                    {
                        ApplyPresetPaintingViewAligned(created, sk.PresetKey);
                    }

                    _sceneMeshes.Add(created);
                    _selectedMesh = created;
                    _selectedSurfaceLine = null;
                    createdCount++;
                    lastMsg = msg;
                    consumed.Add(sk);
                }
                else
                {
                    failedCount++;
                    lastFailMsg = msg;
                }
            }

            if (createdCount > 0)
            {
                foreach (var c in consumed) ConsumeSketchStroke(c);
                if (_selectedMesh != null)
                {
                    _modelBaseColor = _selectedMesh.BaseColor;
                    _btnModelColor.Invalidate();
                }
                SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
                SyncCameraPivotToActiveShape();
                UpdateDeleteButtonState();
                string summary = createdCount == 1
                    ? $"Đã dựng 1 khối 3D thành công: {lastMsg}"
                    : $"Đã dựng đồng loạt {createdCount} khối 3D độc lập thành công!";
                if (failedCount > 0)
                    summary += $" (Bỏ qua {failedCount} nét 2D không hợp lệ: {lastFailMsg})";
                UpdateStatus(summary);
                _viewportPanel.Invalidate();
            }
            else
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus($"Không thể tạo khối 3D: {lastFailMsg}");
            }
        }

        private TeddyMesh CreateSphereMeshFrom2DStroke(IReadOnlyList<PointF> stroke2D, Size viewport, Color color)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in stroke2D)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            PointF center2D = new PointF((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            Vector3 centerWorld = _camera.ScreenToWorldPoint(center2D, viewport, 0f);
            float radius = Math.Clamp(
                MathF.Max(maxX - minX, maxY - minY) * 0.5f / MathF.Max(0.35f, _camera.Zoom),
                35f,
                240f);

            var sphere = TeddyMesh.CreateSphere(radius, 32, 20);
            sphere.BaseColor = color;
            sphere.Translate(centerWorld);

            // Gắn kèm DebugInfo từ đường tròn 2D để người dùng vẫn xem được minh họa 5 bước giải thuật (Hình 13–15)
            if (TeddyInflationEngine.TryCreateMeshFromStroke(stroke2D, _camera, viewport, color, out TeddyMesh? debugRef, out _) && debugRef != null)
            {
                sphere.DebugInfo = debugRef.DebugInfo;
            }

            return sphere;
        }

        private TeddyMesh CreateCubeMeshFrom2DStroke(IReadOnlyList<PointF> stroke2D, Size viewport, Color color)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in stroke2D)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            PointF center2D = new PointF((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            Vector3 centerWorld = _camera.ScreenToWorldPoint(center2D, viewport, 0f);
            float halfSize = Math.Clamp(
                MathF.Max(maxX - minX, maxY - minY) * 0.5f / MathF.Max(0.35f, _camera.Zoom),
                35f,
                220f);

            var cube = TeddyMesh.CreateCube(halfSize, 6);
            cube.BaseColor = color;
            cube.Translate(centerWorld);

            Quaternion cubeTilt = Quaternion.Normalize(
                Quaternion.Conjugate(_camera.Rotation) * Quaternion.CreateFromYawPitchRoll(0.32f, -0.22f, 0f));
            cube.SetLocalRotationAroundPivot(cubeTilt, centerWorld);
            return cube;
        }

        private void ApplyPresetPaintingViewAligned(TeddyMesh mesh, string presetKey)
        {
            if (mesh == null || mesh.IsEmpty) return;

            bool needsAlign = MathF.Abs(_camera.Rotation.W) < 0.9999f;
            Vector3 pivot = Vector3.Zero;
            Quaternion origLocalRot = mesh.LocalRotation;

            if (needsAlign && ComputeMeshesBounds(new[] { mesh }, out Vector3 min, out Vector3 max))
            {
                pivot = (min + max) * 0.5f;
                mesh.RotateAroundPivot(_camera.Rotation, pivot);
            }

            TeddyMeshOptimizer.ApplyPaperPresetPainting(mesh, presetKey);

            if (needsAlign)
            {
                mesh.RotateAroundPivot(Quaternion.Conjugate(_camera.Rotation), pivot);
                mesh.LocalRotation = origLocalRot;
            }
        }

        /// <summary>
        /// Nút 2: [Dán Nét 3D] — Mục 5.2 (Chiếu nét phác thảo 2D bám lên bề mặt mô hình 3D).
        /// </summary>
        private void ApplySurfacePaintFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào trong không gian. Hãy bấm [Tạo Khối 3D] hoặc nạp Khối Cầu/Hộp trước!");
                return;
            }

            var sketch = GetTargetSketchStroke();
            if (sketch == null || sketch.Points.Count < 2)
            {
                UpdateStatus("Hãy kéo Chuột Trái vẽ nét 2D lên bề mặt mô hình 3D trước khi bấm [Dán Nét 3D]!");
                return;
            }

            TeddyMesh? targetMesh = PickTargetMeshForStroke(sketch.Points);
            if (targetMesh == null)
            {
                UpdateStatus("Không tìm thấy khối 3D nằm dưới nét 2D.");
                return;
            }

            List<PointF> resampled = StrokeProcessor.ResampleUniform(sketch.Points, 6.5f, false);
            var surfacePts = new List<Vector3>();
            foreach (var pt in resampled)
            {
                if (TeddyRaycaster.RaycastMesh(targetMesh, pt, _camera, _viewportPanel.ClientSize, out _, out Vector3 hp, out Vector3 hn))
                {
                    surfacePts.Add(hp + hn * 1.2f);
                }
            }

            if (surfacePts.Count < 2)
            {
                UpdateStatus("Nét vẽ 2D chưa nằm trên bề mặt khối 3D! Hãy xoay khối 3D bằng Chuột Phải hoặc vẽ lại nét 2D lên mặt khối.");
                return;
            }

            PushUndoState();
            TeddySurfacePaintOp.AddSurfaceLine(targetMesh, surfacePts, _surfacePaintColor, 2.8f);
            ConsumeSketchStroke(sketch);
            UpdateDeleteButtonState();
            UpdateStatus($"Đã dán nét vẽ 3D lên bề mặt ({surfacePts.Count} đoạn - Mục 5.2).");
            _viewportPanel.Invalidate();
        }

        /// <summary>
        /// Nút 3: [Tô Vùng 3D] — Tô màu vùng mặt tam giác nằm trong Vòng Đế Đỏ hoặc nét 2D khép kín.
        /// </summary>
        private void ApplyFillRegionFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào để tô vùng!");
                return;
            }

            TeddyMesh? ringMesh = _sceneMeshes.FirstOrDefault(m => m.ActiveBaseRing != null);
            SketchStroke2D? consumedSketch = null;

            PushUndoState();

            if (ringMesh == null)
            {
                var sketch = GetTargetSketchStroke();
                if (sketch != null && sketch.Points.Count >= 6)
                {
                    TeddyMesh? target = PickTargetMeshForStroke(sketch.Points);
                    if (target != null)
                    {
                        var pts = ProjectStrokeToMeshSurface(target, sketch.Points, true);
                        if (pts.Count >= 5)
                        {
                            TeddySurfacePaintOp.SetExtrusionBaseRing(target, pts);
                            ringMesh = target;
                            consumedSketch = sketch;
                        }
                    }
                }
            }

            if (ringMesh == null || ringMesh.ActiveBaseRing == null)
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus("Hãy khoanh nét 2D khép kín trên mặt khối 3D (hoặc bấm [Đặt Vòng Đế]) trước khi bấm [Tô Vùng 3D]!");
                return;
            }

            if (TeddySmoothBendOp.TryFillColorInBaseRing(ringMesh, _surfacePaintColor, out string fillMsg))
            {
                if (consumedSketch != null) ConsumeSketchStroke(consumedSketch);
                UpdateDeleteButtonState();
                UpdateStatus(fillMsg);
                _viewportPanel.Invalidate();
            }
            else
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus(fillMsg);
            }
        }

        /// <summary>
        /// Nút 4: [Đặt Vòng Đế] — Chiếu nét khoanh 2D khép kín lên bề mặt khối thành ActiveBaseRing màu đỏ.
        /// </summary>
        private void ApplySetBaseRingFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào! Hãy tạo khối 3D trước.");
                return;
            }

            var sketch = GetTargetSketchStroke();
            if (sketch == null || sketch.Points.Count < 4)
            {
                UpdateStatus("Hãy kéo Chuột Trái khoanh một vòng tròn 2D trên bề mặt khối 3D, rồi bấm [Đặt Vòng Đế].");
                return;
            }

            TeddyMesh? target = PickTargetMeshForStroke(sketch.Points);
            if (target == null) return;

            var surfacePts = ProjectStrokeToMeshSurface(target, sketch.Points, closeLoop: true);
            if (surfacePts.Count < 5)
            {
                UpdateStatus("Vòng khoanh 2D chưa bám đủ trên bề mặt khối 3D (cần ít nhất 5 điểm chạm mặt khối).");
                return;
            }

            PushUndoState();
            foreach (var m in _sceneMeshes) m.ActiveBaseRing = null;
            TeddySurfacePaintOp.SetExtrusionBaseRing(target, surfacePts);
            _selectedMesh = target;
            ConsumeSketchStroke(sketch);
            UpdateDeleteButtonState();
            UpdateStatus("Đã đặt Vòng Đế Đỏ trên bề mặt! Bây giờ hãy xoay 3D nghiêng góc bằng Chuột Phải, vẽ nét vòm 2D rồi bấm [Đùn / Khoét 3D] hoặc [Mượt Bezier 3D].");
            _viewportPanel.Invalidate();
        }

        /// <summary>
        /// Nút 5: [Đùn / Khoét 3D] — Mục 5.3 (Two-Pointer Sweep Extrusion / Cavity từ Vòng Đế Đỏ + Nét vòm 2D).
        /// </summary>
        private void ApplyExtrudeOrCavityFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào để Đùn hoặc Khoét lõm!");
                return;
            }

            TeddyMesh? ringMesh = _sceneMeshes.FirstOrDefault(m => m.ActiveBaseRing != null);

            // Tiện lợi thông minh: Nếu chưa đặt Vòng Đế nhưng có đúng 2 nét 2D -> Nét #1 làm Vòng Đế, Nét #2 làm nét Đùn
            if (ringMesh == null && _sketchLayer.Strokes.Count >= 2)
            {
                var ringSketch = _sketchLayer.Strokes[_sketchLayer.Strokes.Count - 2];
                var profileSketch = _sketchLayer.Strokes[_sketchLayer.Strokes.Count - 1];
                TeddyMesh? candidate = PickTargetMeshForStroke(ringSketch.Points);
                if (candidate != null)
                {
                    var pts = ProjectStrokeToMeshSurface(candidate, ringSketch.Points, closeLoop: true);
                    if (pts.Count >= 5)
                    {
                        PushUndoState();
                        TeddySurfacePaintOp.SetExtrusionBaseRing(candidate, pts);
                        if (TeddyExtrudeCutOp.TryExtrudeOrDigFromBaseRing(
                            candidate, profileSketch.Points, _camera, _viewportPanel.ClientSize,
                            out TeddyMesh? extMesh, out string extMsg))
                        {
                            if (extMesh != null)
                            {
                                _sceneMeshes.Add(extMesh);
                                _selectedMesh = extMesh;
                            }
                            ConsumeSketchStroke(ringSketch);
                            ConsumeSketchStroke(profileSketch);
                            UpdateDeleteButtonState();
                            UpdateStatus(extMsg);
                            _viewportPanel.Invalidate();
                            return;
                        }
                        else
                        {
                            RestoreFromUndoWithoutRedo();
                            UpdateStatus(extMsg);
                            return;
                        }
                    }
                }
            }

            if (ringMesh == null || ringMesh.ActiveBaseRing == null)
            {
                UpdateStatus("Chưa có Vòng Đế Đỏ! Hãy khoanh vòng 2D trên mặt khối → bấm [Đặt Vòng Đế] → xoay nghiêng → vẽ nét vòm 2D → bấm [Đùn / Khoét 3D].");
                return;
            }

            var extrudeSketch = GetTargetSketchStroke();
            if (extrudeSketch == null || extrudeSketch.Points.Count < 4)
            {
                UpdateStatus("Đã có Vòng Đế Đỏ! Hãy xoay 3D nghiêng góc bằng Chuột Phải và vẽ một nét vòm 2D xuất phát từ vòng đỏ rồi bấm [Đùn / Khoét 3D].");
                return;
            }

            PushUndoState();
            if (TeddyExtrudeCutOp.TryExtrudeOrDigFromBaseRing(
                ringMesh,
                extrudeSketch.Points,
                _camera,
                _viewportPanel.ClientSize,
                out TeddyMesh? extrudedMesh,
                out string msg))
            {
                if (extrudedMesh != null)
                {
                    _sceneMeshes.Add(extrudedMesh);
                    _selectedMesh = extrudedMesh;
                }
                ConsumeSketchStroke(extrudeSketch);
                UpdateDeleteButtonState();
                UpdateStatus(msg);
                _viewportPanel.Invalidate();
            }
            else
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus(msg);
            }
        }

        /// <summary>
        /// Nút 6: [Cắt Khối 3D] — Mục 5.4 (Cắt ngang vật thể 3D theo nét 2D hoặc cắt nhanh theo mẫu đang chọn).
        /// </summary>
        private void ApplyCut3DFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào để cắt! Hãy vẽ hoặc triển khai hình 2D rồi bấm [Tạo Khối 3D] trước khi cắt.");
                return;
            }

            var sketch = GetTargetSketchStroke();
            List<PointF> cutRawStroke;
            TeddyCutPattern patternToUse = _activeCutPattern;
            bool usedSketch = false;

            if (sketch != null && sketch.Points.Count >= 2)
            {
                // Nếu người dùng đã vẽ nét cắt 2D trong bộ đệm: Dùng chính nét đó (với mẫu Freehand để không biến dạng lại lần 2 nếu đã sinh mẫu)
                cutRawStroke = new List<PointF>(sketch.Points);
                patternToUse = TeddyCutPattern.Freehand;
                usedSketch = true;
            }
            else
            {
                // Nếu chưa vẽ nét 2D: Tạo đường cắt ngang chính giữa khối 3D theo mẫu đang chọn (Thẳng/Zíc-zắc/Cong/Sóng/Bậc thang/Chữ V)
                TeddyMesh refMesh = _selectedMesh ?? _sceneMeshes.First(m => !m.IsEmpty);
                cutRawStroke = TeddyCutPatternGenerator.CreateQuickHorizontalCutStroke(refMesh, _camera, _viewportPanel.ClientSize);
            }

            TeddyMesh? preferred = PickTargetMeshForStroke(cutRawStroke);
            if (preferred == null) return;

            var visibleMeshes = _sceneMeshes
                .Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                .ToList();

            List<TeddyMesh> candidates;
            if (usedSketch)
            {
                var uniformHitTestPts = StrokeProcessor.ResampleUniform(cutRawStroke, 10f, false);
                candidates = visibleMeshes
                    .Where(m => m.GroupId == preferred.GroupId || StrokeHitsMesh(uniformHitTestPts, m))
                    .OrderByDescending(m => m == preferred)
                    .ThenByDescending(m => GetMeshAverageViewZ(m))
                    .ToList();
                if (candidates.Count == 0) candidates.Add(preferred);
            }
            else
            {
                candidates = GetMeshGroup(preferred);
            }

            if (candidates.Count == 0) return;

            PushUndoState();
            int successCount = 0;
            string lastMsg = "";
            var splitAdditions = new List<TeddyMesh>();

            foreach (var mesh in candidates)
            {
                if (TeddyExtrudeCutOp.TryCutOrSplitMeshWithPattern(
                    mesh,
                    cutRawStroke,
                    patternToUse,
                    _cutAmplitudePx,
                    _cutPeriodCount,
                    _flipCutRemoveSide,
                    _cutActionMode,
                    _camera,
                    _viewportPanel.ClientSize,
                    out TeddyMesh? secondHalf,
                    out string cutMsg))
                {
                    successCount++;
                    lastMsg = cutMsg;
                    if (secondHalf != null) splitAdditions.Add(secondHalf);
                }
                else if (string.IsNullOrEmpty(lastMsg))
                {
                    lastMsg = cutMsg;
                }
            }

            if (successCount > 0)
            {
                if (splitAdditions.Count > 0)
                {
                    _sceneMeshes.AddRange(splitAdditions);
                }
                if (usedSketch && sketch != null)
                {
                    ConsumeSketchStroke(sketch);
                }
                _selectedMesh = preferred;
                _selectedSurfaceLine = null;
                SyncCameraPivotToActiveShape();
                UpdateDeleteButtonState();
                UpdateStatus(lastMsg);
                _viewportPanel.Invalidate();
            }
            else
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus(lastMsg);
            }
        }

        /// <summary>
        /// Nút 7: [Mượt Bezier 3D] — Mục 5.5 (Làm mượt vùng Vòng Đế Đỏ hoặc nét khoanh 2D bằng nội suy Bezier / Taubin).
        /// </summary>
        private void ApplyBezierSmoothFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào để làm mượt!");
                return;
            }

            TeddyMesh? ringMesh = _sceneMeshes.FirstOrDefault(m => m.ActiveBaseRing != null);
            SketchStroke2D? consumedSketch = null;

            PushUndoState();

            if (ringMesh == null)
            {
                var sketch = GetTargetSketchStroke();
                if (sketch != null && sketch.Points.Count >= 6)
                {
                    TeddyMesh? target = PickTargetMeshForStroke(sketch.Points);
                    if (target != null)
                    {
                        var pts = ProjectStrokeToMeshSurface(target, sketch.Points, closeLoop: true);
                        if (pts.Count >= 5)
                        {
                            TeddySurfacePaintOp.SetExtrusionBaseRing(target, pts);
                            ringMesh = target;
                            consumedSketch = sketch;
                        }
                    }
                }
            }

            if (ringMesh != null && ringMesh.ActiveBaseRing != null)
            {
                if (TeddySmoothBendOp.TrySmoothRegionInBaseRing(ringMesh, out string smoothMsg))
                {
                    if (consumedSketch != null) ConsumeSketchStroke(consumedSketch);
                    UpdateDeleteButtonState();
                    UpdateStatus(smoothMsg);
                    _viewportPanel.Invalidate();
                    return;
                }
            }

            // Fallback: Làm mịn Taubin toàn khối nếu không khoanh vùng cụ thể
            if (_selectedMesh != null)
            {
                _selectedMesh.SmoothMesh(2, 0.42f, -0.44f);
                UpdateStatus("Đã làm mịn bề mặt khối 3D đang chọn (Taubin Fairing).");
            }
            else
            {
                foreach (var m in _sceneMeshes) m.SmoothMesh(2, 0.42f, -0.44f);
                UpdateStatus("Đã làm mịn bề mặt toàn bộ các khối 3D (Taubin Fairing).");
            }
            _viewportPanel.Invalidate();
        }

        /// <summary>
        /// Nút 8: [Uốn Khối 3D] — Mục 4.7 (Lấy 2 nét 2D: Reference & Target trong lớp phác thảo để uốn biến dạng khối).
        /// </summary>
        private void ApplyBend3DFromSketch()
        {
            if (_sceneMeshes.Count == 0)
            {
                UpdateStatus("Chưa có khối 3D nào để Uốn (Bend)!");
                return;
            }

            var bendStrokes = _sketchLayer.Strokes.Where(s => s.CutPattern == TeddyCutPattern.Freehand).ToList();
            if (bendStrokes.Count < 2)
            {
                UpdateStatus("Cần vẽ đủ 2 nét 2D lên bộ đệm để Uốn: Nét #1 (Trục chuẩn Reference dọc thân khối) và Nét #2 (Trục đích Target muốn uốn tới)!");
                return;
            }

            var refSketch = bendStrokes[bendStrokes.Count - 2];
            var targetSketch = bendStrokes[bendStrokes.Count - 1];
            TeddyMesh? targetMesh = PickTargetMeshForStroke(refSketch.Points) ?? CurrentMesh;

            PushUndoState();
            if (TeddySmoothBendOp.TryBendMeshWithStrokes(
                targetMesh,
                refSketch.Points,
                targetSketch.Points,
                _camera,
                _viewportPanel.ClientSize,
                out string bendMsg))
            {
                ConsumeSketchStroke(refSketch);
                ConsumeSketchStroke(targetSketch);
                UpdateDeleteButtonState();
                UpdateStatus(bendMsg);
                _viewportPanel.Invalidate();
            }
            else
            {
                RestoreFromUndoWithoutRedo();
                UpdateStatus(bendMsg);
            }
        }

        private List<Vector3> ProjectStrokeToMeshSurface(TeddyMesh mesh, IReadOnlyList<PointF> stroke, bool closeLoop)
        {
            List<PointF> resampled = StrokeProcessor.ResampleUniform(stroke, 7.5f, closeLoop);
            var surfacePts = new List<Vector3>();
            foreach (var pt in resampled)
            {
                if (TeddyRaycaster.RaycastMesh(mesh, pt, _camera, _viewportPanel.ClientSize, out _, out Vector3 hp, out Vector3 hn))
                {
                    surfacePts.Add(hp + hn * 1.2f);
                }
            }
            return surfacePts;
        }

        // =========================================================================
        // RENDER VIEWPORT 3D + LỚP PHÁC THẢO 2D OVERLAY
        // =========================================================================

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

                if (_show3DAxes && _algorithmStepView == TeddyAlgorithmStepView.Step5_QuarterOvalRibs3D)
                {
                    GetActiveShapeAxesAnchor(out Vector3 stepOrigin, out float stepRadius, out var stepBounds, out Quaternion stepRot, ActiveMesh);
                    TeddyRenderer.DrawWorldCoordinateAxes3D(e.Graphics, _viewportPanel.ClientSize, _camera, isOverlayOnMesh: false, stepOrigin, stepRadius, stepBounds, TeddyAxisDragHandle.None, stepRot);
                    TeddyRenderer.DrawWorldCoordinateAxes3D(e.Graphics, _viewportPanel.ClientSize, _camera, isOverlayOnMesh: true, stepOrigin, stepRadius, stepBounds, TeddyAxisDragHandle.None, stepRot);
                    TeddyRenderer.DrawCornerAxisGizmo3D(e.Graphics, _viewportPanel.ClientSize, _camera, _currentMousePos, _isMouseInViewport, out _hoveredGizmoAxis, stepBounds.HasValue ? stepRot : (Quaternion?)null);
                }
            }
            else
            {
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
                        null,
                        null,
                        drawBackground: true,
                        cutPieceVisibility: _cutPieceVisibility,
                        cutSurfaceViewMode: _cutSurfaceViewMode,
                        cutFillColor: _cutFillColor);
                }
                else
                {
                    bool isFirst = true;
                    foreach (var mesh in visibleMeshes)
                    {
                        TeddyRenderer.RenderScene(
                            e.Graphics,
                            _viewportPanel.ClientSize,
                            mesh,
                            _camera,
                            _renderStyle,
                            null,
                            null,
                            drawBackground: isFirst,
                            cutPieceVisibility: _cutPieceVisibility,
                            cutSurfaceViewMode: _cutSurfaceViewMode,
                            cutFillColor: _cutFillColor);
                        isFirst = false;
                    }

                    if (_selectedSurfaceLine != null)
                    {
                        DrawSelectedLineHighlight(e.Graphics, _viewportPanel.ClientSize);
                    }

                    if (_selectedMesh != null && !(_selectedMesh.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                    {
                        DrawSelectedMesh3DBoundingBox(e.Graphics, _viewportPanel.ClientSize, _selectedMesh);
                    }
                }

                if (_show3DAxes)
                {
                    GetActiveShapeAxesAnchor(out Vector3 axesOrigin, out float axesRadius, out var axesBounds, out Quaternion axesRot);
                    TeddyAxisDragHandle activeHighlight = _isDraggingMesh3D ? _activeAxisDragHandle : _hoveredWorldAxisHandle;

                    // Vẽ hệ trục 3D thu gọn riêng cho từng hình khác (không phải hình/nhóm đang chọn)
                    if (visibleMeshes.Count > 0)
                    {
                        var activeGroup = axesBounds.HasValue ? GetMeshGroup(_selectedMesh ?? ActiveMesh) : new List<TeddyMesh>();
                        Guid? activeGroupId = activeGroup.Count > 0 ? activeGroup[0].GroupId : (Guid?)null;

                        foreach (var grp in visibleMeshes.GroupBy(m => m.GroupId))
                        {
                            if (activeGroupId.HasValue && grp.Key == activeGroupId.Value) continue;
                            var grpList = grp.ToList();
                            if (ComputeMeshesBounds(grpList, out Vector3 oMin, out Vector3 oMax))
                            {
                                Vector3 oOrigin = (oMin + oMax) * 0.5f;
                                Vector3 oExt = (oMax - oMin) * 0.5f;
                                float oRadius = Math.Clamp(MathF.Max(oExt.X, MathF.Max(oExt.Y, oExt.Z)), 80f, 340f);
                                TeddyRenderer.DrawWorldCoordinateAxes3D(
                                    e.Graphics, _viewportPanel.ClientSize, _camera,
                                    isOverlayOnMesh: true,
                                    oOrigin, oRadius, (oMin, oMax),
                                    TeddyAxisDragHandle.None, grpList[0].LocalRotation,
                                    isCompactSecondary: true);
                            }
                        }
                    }

                    // Vẽ hệ trục 3D đầy đủ cho hình đang chọn / đang phác thảo
                    TeddyRenderer.DrawWorldCoordinateAxes3D(e.Graphics, _viewportPanel.ClientSize, _camera, isOverlayOnMesh: false, axesOrigin, axesRadius, axesBounds, TeddyAxisDragHandle.None, axesRot);
                    TeddyRenderer.DrawWorldCoordinateAxes3D(e.Graphics, _viewportPanel.ClientSize, _camera, isOverlayOnMesh: true, axesOrigin, axesRadius, axesBounds, activeHighlight, axesRot);
                    TeddyRenderer.DrawCornerAxisGizmo3D(e.Graphics, _viewportPanel.ClientSize, _camera, _currentMousePos, _isMouseInViewport, out _hoveredGizmoAxis, axesBounds.HasValue ? axesRot : (Quaternion?)null);
                }
            }

            // Vẽ đè toàn bộ Lớp Phác Thảo 2D (các nét nháp 2D + nét đang kéo + tẩy 2D) lên trên cùng khung nhìn 3D
            _sketchLayer.RenderOverlay(
                e.Graphics,
                (_isDrawingStroke && _sketchToolMode == Sketch2DToolMode.DrawStroke) ? _activeStroke : null,
                _surfacePaintColor,
                _sketchToolMode,
                _currentMousePos,
                _isMouseInViewport,
                _activeCutPattern,
                _cutAmplitudePx,
                _cutPeriodCount,
                _flipCutRemoveSide);
        }

        // =========================================================================
        // QUẢN LÝ TRẠNG THÁI, MẪU BÀI BÁO, UNDO/REDO & XUẤT .OBJ / .PNG
        // =========================================================================

        private void ResetToBlankScene()
        {
            if (_sceneMeshes.Count > 0 || _sketchLayer.HasStrokes)
                PushUndoState();

            _sceneMeshes.Clear();
            _sketchLayer.Clear();
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            _camera.Reset();
            SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
            UpdateDeleteButtonState();
            UpdateStatus("Đã khởi tạo không gian mới. Kéo Chuột Trái vẽ nét phác thảo 2D rồi bấm [Tạo Khối 3D]!");
            _viewportPanel.Invalidate();
        }

        private void DeployPresetAs2DSketch(string presetKey, Color presetColor, string displayName)
        {
            Size vp = _viewportPanel.ClientSize;
            if (vp.Width < 100 || vp.Height < 100) vp = new Size(1000, 560);

            PushUndoState();

            _sketchToolMode = Sketch2DToolMode.DrawStroke;
            _activeCutPattern = TeddyCutPattern.Freehand;
            _algorithmStepView = TeddyAlgorithmStepView.Final3DModel;
            foreach (var kvp in _stepViewButtons)
            {
                UITheme.SetButtonSelected(kvp.Value, kvp.Key == TeddyAlgorithmStepView.Final3DModel);
            }

            List<PointF> sampleStroke = TeddyMeshOptimizer.CreatePaperSampleStroke(presetKey, vp);
            _sketchLayer.AddPresetStroke(sampleStroke, presetColor, presetColor, presetKey, displayName, vp);

            _selectedMesh = null;
            _selectedSurfaceLine = null;
            SyncCameraPivotToActiveShape();
            Update2DToolSelection();
            UpdateCutPatternToolbarState();
            UpdateDeleteButtonState();
            UpdateStatus($"Đã triển khai hình vẽ 2D [{displayName}] (chưa dựng 3D). Bấm [Tạo Khối 3D] để chuyển thành mô hình 3D!");
            _viewportPanel.Invalidate();
        }

        private void SetAlgorithmStepView(TeddyAlgorithmStepView step)
        {
            if (step != TeddyAlgorithmStepView.Final3DModel && (CurrentMesh.IsEmpty || CurrentMesh.DebugInfo == null))
            {
                UpdateStatus("Hãy bấm [Tạo Khối 3D] hoặc chọn Mẫu Bài Báo ('Gấu Teddy', 'Quả Cherry', 'Con Rắn') để xem từng bước thuật toán Hình 13–15!");
                return;
            }

            _algorithmStepView = step;
            foreach (var kvp in _stepViewButtons)
            {
                UITheme.SetButtonSelected(kvp.Value, kvp.Key == step);
            }
            _viewportPanel?.Invalidate();
        }

        private void SetRenderStyle(TeddyRenderStyle style)
        {
            _renderStyle = style;
            UpdateStyleButtonSelection();
            _viewportPanel.Invalidate();
        }

        private void Toggle3DAxes()
        {
            _show3DAxes = !_show3DAxes;
            UITheme.SetButtonSelected(_btnToggle3DAxes, _show3DAxes);
            if (!_show3DAxes)
            {
                _hoveredGizmoAxis = TeddyAxisSnapView.None;
                _hoveredWorldAxisHandle = TeddyAxisDragHandle.None;
            }
            UpdateStatus(_show3DAxes
                ? "Đang HIỆN Hệ Trục Tọa Độ 3D (X: Đỏ, Y: Xanh lá, Z: Xanh dương) & La bàn Trục 3D."
                : "Đã ẨN Hệ Trục Tọa Độ 3D Oxyz. Bấm lại [Trục 3D] hoặc phím [A] để bật lại.");
            _viewportPanel.Invalidate();
        }

        private void SnapCameraToAxisView(TeddyAxisSnapView snapView)
        {
            Quaternion targetViewRot;
            string desc;
            switch (snapView)
            {
                case TeddyAxisSnapView.PosX:
                    targetViewRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI * 0.5f);
                    desc = "trục +X (Nhìn từ Cạnh Phải — thấy mặt phẳng YZ)";
                    break;
                case TeddyAxisSnapView.NegX:
                    targetViewRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f);
                    desc = "trục -X (Nhìn từ Cạnh Trái — thấy mặt phẳng YZ)";
                    break;
                case TeddyAxisSnapView.PosY:
                    targetViewRot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f);
                    desc = "trục +Y (Nhìn từ Trên xuống — thấy mặt phẳng XZ)";
                    break;
                case TeddyAxisSnapView.NegY:
                    targetViewRot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI * 0.5f);
                    desc = "trục -Y (Nhìn từ Dưới lên — thấy mặt phẳng XZ)";
                    break;
                case TeddyAxisSnapView.PosZ:
                    targetViewRot = Quaternion.Identity;
                    desc = "trục +Z (Nhìn Chính Diện — thấy mặt phẳng XY)";
                    break;
                case TeddyAxisSnapView.NegZ:
                    targetViewRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
                    desc = "trục -Z (Nhìn từ Phía Sau — thấy mặt phẳng XY)";
                    break;
                case TeddyAxisSnapView.Isometric:
                    targetViewRot = Quaternion.CreateFromYawPitchRoll(-0.55f, 0.38f, 0f);
                    desc = "góc Phối Cảnh 3D Isometric (thấy rõ cả 3 trục +X, +Y, +Z)";
                    break;
                default:
                    return;
            }

            if (_algorithmStepView == TeddyAlgorithmStepView.Final3DModel && HasVisibleMeshes())
            {
                var targetGroup = GetMeshGroup(_selectedMesh ?? ActiveMesh);
                if (targetGroup.Count > 0 && ComputeMeshesBounds(targetGroup, out Vector3 gMin, out Vector3 gMax))
                {
                    Vector3 pivot = (gMin + gMax) * 0.5f;
                    Quaternion targetLocalRot = Quaternion.Normalize(Quaternion.Conjugate(_camera.Rotation) * targetViewRot);
                    PushUndoState();
                    foreach (var m in targetGroup)
                    {
                        m.SetLocalRotationAroundPivot(targetLocalRot, pivot);
                    }
                    SyncCameraPivotToActiveShape();
                    UpdateStatus($"Đã xoay độc lập hình đang chọn nhìn theo {desc} (hình khác đứng yên).");
                    _viewportPanel.Invalidate();
                    return;
                }
            }

            SyncCameraPivotToActiveShape();
            _camera.Rotation = targetViewRot;
            UpdateStatus($"Đã xoay Camera nhìn theo {desc}.");
            _viewportPanel.Invalidate();
        }

        private void ResetActiveRotationAndCamera()
        {
            if (_algorithmStepView == TeddyAlgorithmStepView.Final3DModel && HasVisibleMeshes())
            {
                int groupCount = _sceneMeshes
                    .Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                    .Select(m => m.GroupId)
                    .Distinct()
                    .Count();

                var targetGroup = GetMeshGroup(_selectedMesh ?? ActiveMesh);
                if (targetGroup.Count > 0 && ComputeMeshesBounds(targetGroup, out Vector3 gMin, out Vector3 gMax))
                {
                    Vector3 pivot = (gMin + gMax) * 0.5f;
                    PushUndoState();

                    if (groupCount > 1)
                    {
                        Quaternion uprightLocal = Quaternion.Normalize(Quaternion.Conjugate(_camera.Rotation));
                        foreach (var m in targetGroup)
                        {
                            m.SetLocalRotationAroundPivot(uprightLocal, pivot);
                        }
                        SyncCameraPivotToActiveShape();
                        UpdateStatus("Đã đưa hướng trục hình đang chọn về mặc định (các hình khác đứng yên).");
                        _viewportPanel.Invalidate();
                        return;
                    }

                    foreach (var m in targetGroup)
                    {
                        m.SetLocalRotationAroundPivot(Quaternion.Identity, pivot);
                    }
                }
            }

            _camera.Reset();
            SyncCameraPivotToActiveShape();
            UpdateStatus("Đã đưa hướng trục hình đang chọn & góc nhìn Camera về mặc định.");
            _viewportPanel.Invalidate();
        }

        private static bool ComputeMeshesBounds(IEnumerable<TeddyMesh> meshes, out Vector3 min, out Vector3 max)
        {
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            bool hasVerts = false;

            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.IsEmpty || mesh.Vertices.Count == 0) continue;
                foreach (var v in mesh.Vertices)
                {
                    if (v.X < minX) minX = v.X;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.Y > maxY) maxY = v.Y;
                    if (v.Z < minZ) minZ = v.Z;
                    if (v.Z > maxZ) maxZ = v.Z;
                    hasVerts = true;
                }
            }

            if (!hasVerts)
            {
                min = Vector3.Zero;
                max = Vector3.Zero;
                return false;
            }

            min = new Vector3(minX, minY, minZ);
            max = new Vector3(maxX, maxY, maxZ);
            return true;
        }

        private void SyncCameraPivotToActiveShape()
        {
            GetActiveShapeAxesAnchor(out Vector3 anchorOrigin, out _, out _);
            _camera.SetPivotKeepScreenPosition(anchorOrigin);
        }

        private bool HasVisibleMeshes()
        {
            return _sceneMeshes.Any(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden));
        }

        private bool StrokeHitsMesh(IReadOnlyList<PointF> stroke, TeddyMesh? mesh)
        {
            if (mesh == null || mesh.IsEmpty || stroke == null || stroke.Count == 0)
                return false;

            int step = Math.Max(1, stroke.Count / 12);
            for (int i = 0; i < stroke.Count; i += step)
            {
                if (TeddyRaycaster.RaycastMesh(mesh, stroke[i], _camera, _viewportPanel.ClientSize, out _, out _, out _))
                    return true;
            }
            return TeddyRaycaster.RaycastMesh(mesh, stroke[stroke.Count - 1], _camera, _viewportPanel.ClientSize, out _, out _, out _);
        }

        private Cursor GetDefaultModeCursor()
        {
            return _sketchToolMode switch
            {
                Sketch2DToolMode.EraseStroke => Cursors.NoMove2D,
                Sketch2DToolMode.Move3DMesh => Cursors.SizeAll,
                _ => Cursors.Cross
            };
        }

        private void ToggleMove3DMode()
        {
            _sketchToolMode = _sketchToolMode == Sketch2DToolMode.Move3DMesh
                ? Sketch2DToolMode.DrawStroke
                : Sketch2DToolMode.Move3DMesh;
            Update2DToolSelection();
            UpdateStatus(_sketchToolMode == Sketch2DToolMode.Move3DMesh
                ? "Đang bật [Di Chuyển 3D]: Kéo Chuột Trái vào khối 3D (hoặc kéo tâm O / trục X, Y, Z) để dời khối sang vị trí mới! (Shift + Kéo để dời tất cả)"
                : "Đã quay về chế độ [Vẽ 2D]: Kéo Chuột Trái vẽ nét 2D mới, hoặc nắm trực tiếp tâm O / trục X, Y, Z để di chuyển khối.");
            _viewportPanel.Invalidate();
        }

        private List<TeddyMesh> GetMeshGroup(TeddyMesh? seedMesh)
        {
            var visible = _sceneMeshes
                .Where(m => !m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                .ToList();
            if (visible.Count == 0) return visible;

            TeddyMesh target = (seedMesh != null && visible.Contains(seedMesh))
                ? seedMesh
                : visible[visible.Count - 1];

            return visible.Where(m => m.GroupId == target.GroupId).ToList();
        }

        private void UpdateRotatingObject3D(Point mousePos)
        {
            if (!_isRotatingObject3D || mousePos == _lastRotateMousePos || _rotatingTargetMeshes.Count == 0)
                return;

            Vector3 sp = _camera.ProjectToScreen(_rotatingPivotWorld, _viewportPanel.ClientSize);
            PointF centerScreen = new PointF(sp.X, sp.Y);
            Quaternion deltaViewRot = ComputeTrackballDelta(_lastRotateMousePos, mousePos, centerScreen, _viewportPanel.ClientSize);
            _lastRotateMousePos = mousePos;

            if (MathF.Abs(deltaViewRot.W) < 0.999999f || deltaViewRot.LengthSquared() > 0f)
            {
                Quaternion camRot = _camera.Rotation;
                Quaternion invCam = Quaternion.Conjugate(camRot);
                Quaternion deltaWorldRot = Quaternion.Normalize(invCam * deltaViewRot * camRot);

                if (MathF.Abs(deltaWorldRot.W) < 0.99999f)
                {
                    _rotateMoved = true;
                    foreach (var mesh in _rotatingTargetMeshes)
                    {
                        mesh.RotateAroundPivot(deltaWorldRot, _rotatingPivotWorld);
                    }
                    _viewportPanel.Invalidate();
                }
            }
        }

        private void FinishRotatingObject3D()
        {
            bool didRotate = _rotateMoved;
            _isRotatingObject3D = false;
            _rotateMoved = false;
            _rotatingTargetMeshes.Clear();
            _viewportPanel.Cursor = GetDefaultModeCursor();

            if (!didRotate)
            {
                if (_undoStack.Count > 0) _undoStack.Pop();
            }
            else
            {
                SyncCameraPivotToActiveShape();
                UpdateStatus("Đã xoay xong hình 3D trên trục riêng (các hình khác giữ nguyên).");
            }

            _viewportPanel.Invalidate();
        }

        private static Quaternion ComputeTrackballDelta(Point prevPt, Point currPt, PointF centerScreen, Size viewport)
        {
            if (prevPt == currPt) return Quaternion.Identity;
            float radius = MathF.Max(1f, MathF.Min(viewport.Width, viewport.Height) * 0.425f);

            Vector3 MapPt(Point pt)
            {
                float x = (pt.X - centerScreen.X) / radius;
                float y = (centerScreen.Y - pt.Y) / radius;
                float d2 = x * x + y * y;
                float z = d2 <= 0.5f ? MathF.Sqrt(1.0f - d2) : 0.5f / MathF.Sqrt(d2);
                return Vector3.Normalize(new Vector3(x, y, z));
            }

            Vector3 v1 = MapPt(prevPt);
            Vector3 v2 = MapPt(currPt);
            Vector3 axis = Vector3.Cross(v1, v2);
            float axisLen = axis.Length();
            if (axisLen <= 1e-5f) return Quaternion.Identity;

            axis /= axisLen;
            float dot = Math.Clamp(Vector3.Dot(v1, v2), -1.0f, 1.0f);
            float angle = MathF.Acos(dot) * 1.85f;
            return Quaternion.CreateFromAxisAngle(axis, angle);
        }

        private void StartDraggingMesh3D(TeddyAxisDragHandle handle, Vector3 axisDir, Point mousePos, TeddyMesh? explicitSingleMesh = null)
        {
            _draggingTargetMeshes.Clear();
            bool moveAllVisible = Control.ModifierKeys.HasFlag(Keys.Shift);

            if (moveAllVisible)
            {
                foreach (var m in _sceneMeshes)
                {
                    if (!m.IsEmpty && !(m.IsCutOffPiece && _cutPieceVisibility == TeddyCutPieceVisibility.Hidden))
                        _draggingTargetMeshes.Add(m);
                }
            }
            else if (explicitSingleMesh != null && !explicitSingleMesh.IsEmpty)
            {
                _draggingTargetMeshes.Add(explicitSingleMesh);
            }
            else
            {
                _draggingTargetMeshes.AddRange(GetMeshGroup(_selectedMesh ?? ActiveMesh));
            }

            if (_draggingTargetMeshes.Count == 0)
                return;

            PushUndoState();
            _isDraggingMesh3D = true;
            _dragMoved = false;
            _activeAxisDragHandle = handle;
            _dragAxisWorldDir = axisDir;
            _lastDragMousePos = mousePos;
            _viewportPanel.Cursor = Cursors.SizeAll;

            string axisLabel = handle switch
            {
                TeddyAxisDragHandle.AxisX => "dọc trục X (Đỏ)",
                TeddyAxisDragHandle.AxisY => "dọc trục Y (Xanh lá)",
                TeddyAxisDragHandle.AxisZ => "dọc trục Z (Xanh dương)",
                _ => "tự do theo mặt phẳng màn hình"
            };
            UpdateStatus($"Đang kéo di chuyển khối 3D {axisLabel}...");
            _viewportPanel.Invalidate();
        }

        private void UpdateDraggingMesh3D(Point mousePos)
        {
            float dx = mousePos.X - _lastDragMousePos.X;
            float dy = mousePos.Y - _lastDragMousePos.Y;
            _lastDragMousePos = mousePos;

            if (MathF.Abs(dx) < 0.01f && MathF.Abs(dy) < 0.01f)
                return;

            Vector3 deltaWorld = Vector3.Zero;
            if (_activeAxisDragHandle == TeddyAxisDragHandle.FreeCenterO)
            {
                float dvx = dx / _camera.Zoom;
                float dvy = -dy / _camera.Zoom;
                deltaWorld = Vector3.Transform(new Vector3(dvx, dvy, 0f), Quaternion.Conjugate(_camera.Rotation));
            }
            else
            {
                Vector3 viewAxis = _camera.NormalToView(_dragAxisWorldDir);
                Vector2 screenAxis = new Vector2(viewAxis.X * _camera.Zoom, -viewAxis.Y * _camera.Zoom);
                float lenSq = screenAxis.LengthSquared();
                if (lenSq >= 1e-4f)
                {
                    float t = Vector2.Dot(new Vector2(dx, dy), screenAxis) / lenSq;
                    deltaWorld = _dragAxisWorldDir * t;
                }
            }

            if (deltaWorld.LengthSquared() > 1e-7f)
            {
                _dragMoved = true;
                foreach (var mesh in _draggingTargetMeshes)
                {
                    mesh.Translate(deltaWorld);
                }

                SyncCameraPivotToActiveShape();
                Vector3 p = _camera.PivotWorld;
                UpdateStatus($"Đang di chuyển khối 3D → Tọa độ mới: O({(int)MathF.Round(p.X)}, {(int)MathF.Round(p.Y)}, {(int)MathF.Round(p.Z)})");
                _viewportPanel.Invalidate();
            }
        }

        private void FinishDraggingMesh3D(Point mousePos)
        {
            bool didMove = _dragMoved;
            var handle = _activeAxisDragHandle;

            _isDraggingMesh3D = false;
            _dragMoved = false;
            _activeAxisDragHandle = TeddyAxisDragHandle.None;
            _draggingTargetMeshes.Clear();
            _viewportPanel.Cursor = GetDefaultModeCursor();

            if (!didMove)
            {
                if (_undoStack.Count > 0) _undoStack.Pop();
                if (handle == TeddyAxisDragHandle.FreeCenterO && !_hasMovedSignificantly && _sketchToolMode == Sketch2DToolMode.Move3DMesh)
                {
                    HandleViewportClick(mousePos);
                }
            }
            else
            {
                SyncCameraPivotToActiveShape();
                Vector3 p = _camera.PivotWorld;
                UpdateStatus($"Đã di chuyển khối 3D sang vị trí mới: O({(int)MathF.Round(p.X)}, {(int)MathF.Round(p.Y)}, {(int)MathF.Round(p.Z)}). Chuột Phải để xoay quanh tâm mới!");
            }

            _viewportPanel.Invalidate();
        }

        private void GetActiveShapeAxesAnchor(
            out Vector3 originWorld,
            out float referenceRadius,
            out (Vector3 Min, Vector3 Max)? meshBounds3D,
            TeddyMesh? overrideMesh = null)
        {
            GetActiveShapeAxesAnchor(out originWorld, out referenceRadius, out meshBounds3D, out _, overrideMesh);
        }

        private void GetActiveShapeAxesAnchor(
            out Vector3 originWorld,
            out float referenceRadius,
            out (Vector3 Min, Vector3 Max)? meshBounds3D,
            out Quaternion localRotation,
            TeddyMesh? overrideMesh = null)
        {
            // Ưu tiên 1: Nếu đang kéo vẽ nét 2D mới hoặc vừa vẽ/chọn nét 2D ở tọa độ mới -> bám ngay theo nét 2D mới!
            IReadOnlyList<PointF>? activeOrSelectedStroke2D = null;
            if (overrideMesh == null && !_isDraggingMesh3D && !_isRotatingObject3D)
            {
                if (_isDrawingStroke && _sketchToolMode == Sketch2DToolMode.DrawStroke && _activeStroke.Count >= 2)
                {
                    activeOrSelectedStroke2D = _activeStroke;
                }
                else if (_sketchLayer.SelectedStroke != null && _selectedMesh == null && _sketchLayer.SelectedStroke.Points.Count >= 2)
                {
                    activeOrSelectedStroke2D = _sketchLayer.SelectedStroke.Points;
                }
            }

            if (activeOrSelectedStroke2D != null && TryComputeStroke2DAxesAnchor(activeOrSelectedStroke2D, out originWorld, out referenceRadius))
            {
                meshBounds3D = null;
                localRotation = Quaternion.Identity;
                return;
            }

            // Ưu tiên 2: Bám theo tâm & hướng trục riêng của NHÓM hình 3D đang chọn (tuyệt đối không gộp 2 hình khác nhau!)
            List<TeddyMesh>? targetMeshes = null;
            if (overrideMesh != null && !overrideMesh.IsEmpty)
            {
                targetMeshes = new List<TeddyMesh> { overrideMesh };
            }
            else if (HasVisibleMeshes())
            {
                targetMeshes = GetMeshGroup(_selectedMesh ?? ActiveMesh);
            }

            if (targetMeshes != null && targetMeshes.Count > 0 && ComputeMeshesBounds(targetMeshes, out Vector3 mMin, out Vector3 mMax))
            {
                originWorld = (mMin + mMax) * 0.5f;
                Vector3 extents = (mMax - mMin) * 0.5f;
                referenceRadius = Math.Clamp(MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z)), 80f, 340f);
                meshBounds3D = (mMin, mMax);
                localRotation = targetMeshes[0].LocalRotation;
                return;
            }

            // Ưu tiên 3: Nét 2D bất kỳ còn lại trong lớp phác thảo
            if (_sketchLayer.HasStrokes)
            {
                var targetStroke = _sketchLayer.SelectedStroke ?? _sketchLayer.Strokes[_sketchLayer.Strokes.Count - 1];
                if (targetStroke.Points.Count >= 2 && TryComputeStroke2DAxesAnchor(targetStroke.Points, out originWorld, out referenceRadius))
                {
                    meshBounds3D = null;
                    localRotation = Quaternion.Identity;
                    return;
                }
            }

            // Mặc định không gian trống tại gốc (0, 0, 0)
            originWorld = Vector3.Zero;
            referenceRadius = 135f;
            meshBounds3D = null;
            localRotation = Quaternion.Identity;
        }

        private bool TryComputeStroke2DAxesAnchor(IReadOnlyList<PointF> stroke2D, out Vector3 originWorld, out float referenceRadius)
        {
            originWorld = Vector3.Zero;
            referenceRadius = 135f;
            if (stroke2D == null || stroke2D.Count < 2) return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in stroke2D)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            PointF center2D = new PointF((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            originWorld = _camera.ScreenToWorldPoint(center2D, _viewportPanel.ClientSize, 0f);
            float r2D = MathF.Max(maxX - minX, maxY - minY) * 0.5f / MathF.Max(0.35f, _camera.Zoom);
            referenceRadius = Math.Clamp(r2D, 85f, 320f);
            return true;
        }

        private void UpdateStyleButtonSelection()
        {
            UITheme.SetButtonSelected(_btnStylePenInk, _renderStyle == TeddyRenderStyle.PenAndInk);
            UITheme.SetButtonSelected(_btnStyleSmooth, _renderStyle == TeddyRenderStyle.SmoothShaded);
            UITheme.SetButtonSelected(_btnStyleWire, _renderStyle == TeddyRenderStyle.WireframeSpine);
        }

        private void Update2DToolSelection()
        {
            UITheme.SetButtonSelected(_btnToolDraw2D, _sketchToolMode == Sketch2DToolMode.DrawStroke);
            UITheme.SetButtonSelected(_btnToolErase2D, _sketchToolMode == Sketch2DToolMode.EraseStroke);
            if (_btnToolMove3D != null)
            {
                UITheme.SetButtonSelected(_btnToolMove3D, _sketchToolMode == Sketch2DToolMode.Move3DMesh);
            }
            if (_viewportPanel != null)
            {
                _viewportPanel.Cursor = GetDefaultModeCursor();
            }
        }

        private void UpdateCutPatternToolbarState()
        {
            foreach (var kvp in _cutPatternButtons)
            {
                UITheme.SetButtonSelected(kvp.Value, kvp.Key == _activeCutPattern);
            }

            if (_btnCutPieceVis != null)
            {
                if (_cutPieceVisibility == TeddyCutPieceVisibility.Visible)
                {
                    UITheme.StyleButton(_btnCutPieceVis, IconType.EyeOpen, "Phần cắt: Hiện", ButtonVariant.Accent, false,
                        _toolTip, "Đang GIỮ LẠI & HIỆN phần bị cắt. Bấm để ẨN tạm phần bị cắt khi muốn nhìn trực diện vào mặt cắt!");
                }
                else
                {
                    UITheme.StyleButton(_btnCutPieceVis, IconType.EyeClosed, "Phần cắt: Ẩn", ButtonVariant.Default, false,
                        _toolTip, "Đang ẨN tạm phần bị cắt. Bấm để HIỆN lại phần bị cắt!");
                }
            }
        }

        private void UpdateStatus(string statusMessage)
        {
            if (_sketchToolMode == Sketch2DToolMode.EraseStroke)
            {
                _lblModeBadge.Text = "2D ERASER MODE";
                _lblModeBadge.BackColor = UITheme.AccentDanger;
            }
            else if (_sketchToolMode == Sketch2DToolMode.Move3DMesh || _isDraggingMesh3D)
            {
                _lblModeBadge.Text = "3D MOVE MODE";
                _lblModeBadge.BackColor = UITheme.AccentSuccess;
            }
            else if (_activeCutPattern != TeddyCutPattern.Freehand)
            {
                _lblModeBadge.Text = $"2D CẮT: {_activeCutPattern.ToString().ToUpper()}";
                _lblModeBadge.BackColor = UITheme.AccentWarning;
            }
            else if (_sceneMeshes.Any(m => m.ActiveBaseRing != null))
            {
                _lblModeBadge.Text = "VÒNG ĐẾ ĐỎ + 2D";
                _lblModeBadge.BackColor = UITheme.AccentSecondary;
            }
            else
            {
                _lblModeBadge.Text = "2D SKETCH MODE";
                _lblModeBadge.BackColor = UITheme.AccentPrimary;
            }

            _lblStatusText.Text = statusMessage;
            RefreshStatsLabel();
        }

        private void RefreshStatsLabel()
        {
            int totalVerts = _sceneMeshes.Sum(m => m.Vertices.Count);
            int totalFaces = _sceneMeshes.Sum(m => m.Triangles.Count);
            _lblMeshStats.Text = $"Nét 2D: {_sketchLayer.Strokes.Count} | Khối: {_sceneMeshes.Count} | Mặt: {totalFaces} ({totalVerts}đ)";
        }

        private StudioHistoryState CaptureHistoryState()
        {
            var meshes = new List<TeddyMesh>(_sceneMeshes.Count);
            foreach (var m in _sceneMeshes) meshes.Add(m.Clone());
            return new StudioHistoryState
            {
                Meshes = meshes,
                SketchStrokes = _sketchLayer.CloneStrokes()
            };
        }

        private void ApplyHistoryState(StudioHistoryState state)
        {
            _sceneMeshes.Clear();
            foreach (var m in state.Meshes) _sceneMeshes.Add(m.Clone());
            _sketchLayer.RestoreStrokes(state.SketchStrokes);
            _selectedMesh = null;
            _selectedSurfaceLine = null;
            UpdateDeleteButtonState();
            RefreshStatsLabel();
        }

        private void PushUndoState()
        {
            _undoStack.Push(CaptureHistoryState());
            _redoStack.Clear();
        }

        private void RestoreFromUndoWithoutRedo()
        {
            if (_undoStack.Count == 0) return;
            ApplyHistoryState(_undoStack.Pop());
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Push(CaptureHistoryState());
            ApplyHistoryState(_undoStack.Pop());
            SyncCameraPivotToActiveShape();
            UpdateStatus("Đã hoàn tác thao tác trước đó.");
            _viewportPanel.Invalidate();
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Push(CaptureHistoryState());
            ApplyHistoryState(_redoStack.Pop());
            SyncCameraPivotToActiveShape();
            UpdateStatus("Đã làm lại thao tác vừa hoàn tác.");
            _viewportPanel.Invalidate();
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
                if (_sketchLayer.SelectedStroke != null || _selectedSurfaceLine != null || _selectedMesh != null)
                {
                    DeleteSelectedEntity();
                    return true;
                }
            }
            if (keyData == Keys.F)
            {
                ApplyFillRegionFromSketch();
                return true;
            }
            if (keyData == Keys.A)
            {
                Toggle3DAxes();
                return true;
            }
            if (keyData == Keys.M)
            {
                ToggleMove3DMode();
                return true;
            }
            if (keyData == Keys.Space)
            {
                ResetActiveRotationAndCamera();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (_sketchToolMode == Sketch2DToolMode.EraseStroke || _sketchToolMode == Sketch2DToolMode.Move3DMesh)
                {
                    _sketchToolMode = Sketch2DToolMode.DrawStroke;
                    Update2DToolSelection();
                    UpdateStatus("Đã quay về chế độ Vẽ 2D.");
                    _viewportPanel.Invalidate();
                    return true;
                }
                if (_sketchLayer.SelectedStroke != null || _selectedSurfaceLine != null || _selectedMesh != null)
                {
                    ClearSelection();
                    UpdateStatus("Đã hủy chọn.");
                    return true;
                }
                foreach (var m in _sceneMeshes) m.ActiveBaseRing = null;
                SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
                UpdateStatus("Đã hủy vòng chọn và quay về chế độ phác thảo 2D.");
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
            if (_btnDeleteSelected != null)
            {
                if (_sketchLayer.SelectedStroke != null)
                {
                    _btnDeleteSelected.Enabled = true;
                    UITheme.StyleButton(_btnDeleteSelected, IconType.Trash, "Xóa 2D", ButtonVariant.Danger, false, _toolTip, "Xóa nét phác thảo 2D đang chọn (Delete)");
                }
                else if (_selectedSurfaceLine != null)
                {
                    _btnDeleteSelected.Enabled = true;
                    UITheme.StyleButton(_btnDeleteSelected, IconType.Trash, "Xóa nét", ButtonVariant.Danger, false, _toolTip, "Xóa nét vẽ 3D đang chọn (Delete)");
                }
                else if (_selectedMesh != null)
                {
                    _btnDeleteSelected.Enabled = true;
                    UITheme.StyleButton(_btnDeleteSelected, IconType.Trash, "Xóa khối", ButtonVariant.Danger, false, _toolTip, "Xóa khối 3D đang chọn (Delete)");
                }
                else
                {
                    _btnDeleteSelected.Enabled = false;
                    UITheme.StyleButton(_btnDeleteSelected, IconType.Trash, "Xóa", ButtonVariant.Default, false, _toolTip, "Click chọn nét 2D, nét 3D hoặc khối 3D để xóa");
                }
                _btnDeleteSelected.Invalidate();
            }
            RefreshStatsLabel();
        }

        private void ClearSelection()
        {
            if (_sketchLayer.SelectedStroke != null || _selectedSurfaceLine != null || _selectedMesh != null)
            {
                _sketchLayer.SelectedStroke = null;
                _selectedSurfaceLine = null;
                _selectedMesh = null;
                UpdateDeleteButtonState();
                _viewportPanel.Invalidate();
            }
        }

        private void DeleteSelectedEntity()
        {
            if (_sketchLayer.SelectedStroke != null)
            {
                PushUndoState();
                _sketchLayer.RemoveSelectedOrLastStroke();
                UpdateDeleteButtonState();
                UpdateStatus("Đã xóa nét phác thảo 2D đang chọn. Bấm Ctrl+Z để hoàn tác.");
                _viewportPanel.Invalidate();
                return;
            }

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
                UpdateStatus("Đã xóa nét vẽ bề mặt 3D. Bấm Ctrl+Z để hoàn tác.");
                _viewportPanel.Invalidate();
                return;
            }

            if (_selectedMesh != null)
            {
                PushUndoState();
                _sceneMeshes.Remove(_selectedMesh);
                _selectedMesh = null;
                UpdateDeleteButtonState();
                UpdateStatus("Đã xóa khối 3D đang chọn. Bấm Ctrl+Z để hoàn tác.");
                _viewportPanel.Invalidate();
            }
        }

        private void HandleViewportClick(Point clickPt)
        {
            // Ưu tiên 1: Nét phác thảo 2D trong bộ đệm
            var hit2D = _sketchLayer.HitTest(clickPt);
            if (hit2D != null)
            {
                _sketchLayer.SelectedStroke = hit2D;
                _selectedSurfaceLine = null;
                _selectedMesh = null;
                SyncCameraPivotToActiveShape();
                UpdateDeleteButtonState();
                UpdateStatus($"Đã chọn {hit2D.Label} trên lớp 2D. Bấm Delete để xóa hoặc bấm nút chức năng 3D ở trên để áp dụng!");
                _viewportPanel.Invalidate();
                return;
            }

            if (_sceneMeshes.Count == 0)
            {
                ClearSelection();
                return;
            }

            // Ưu tiên 2: Nét vẽ trên bề mặt 3D (Surface Line)
            if (HitTestSurfaceLine(clickPt, _viewportPanel.ClientSize, out var hitLine, out _))
            {
                if (_selectedSurfaceLine == hitLine && HitTestMesh(clickPt, _viewportPanel.ClientSize, out var underMesh))
                {
                    _sketchLayer.SelectedStroke = null;
                    _selectedMesh = underMesh;
                    _selectedSurfaceLine = null;
                    SyncCameraPivotToActiveShape();
                    UpdateDeleteButtonState();
                    UpdateStatus($"Đã chọn Khối 3D ({underMesh!.Vertices.Count} đỉnh). Bấm Delete để xóa hoặc đổi màu khối.");
                    _viewportPanel.Invalidate();
                    return;
                }

                _sketchLayer.SelectedStroke = null;
                _selectedSurfaceLine = hitLine;
                _selectedMesh = null;
                UpdateDeleteButtonState();
                UpdateStatus("Đã chọn nét vẽ 3D trên bề mặt. Bấm Delete để xóa.");
                _viewportPanel.Invalidate();
                return;
            }

            // Ưu tiên 3: Khối 3D
            if (HitTestMesh(clickPt, _viewportPanel.ClientSize, out var hitMesh))
            {
                _sketchLayer.SelectedStroke = null;
                _selectedMesh = hitMesh;
                _selectedSurfaceLine = null;
                SyncCameraPivotToActiveShape();
                UpdateDeleteButtonState();
                UpdateStatus($"Đã chọn Khối 3D ({hitMesh!.Vertices.Count} đỉnh). Bấm Delete để xóa hoặc đổi màu khối.");
                _viewportPanel.Invalidate();
                return;
            }

            ClearSelection();
            UpdateStatus("Đã hủy chọn.");
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
            if (lenSq < 1e-5f) return StrokeProcessor.Distance(pt, a);
            float t = Math.Clamp(((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / lenSq, 0f, 1f);
            return StrokeProcessor.Distance(pt, new PointF(a.X + t * dx, a.Y + t * dy));
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
                screenPts.Add(screenPts[0]);

            if (screenPts.Count < 2) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen glowPen = new Pen(Color.FromArgb(120, 6, 182, 212), _selectedSurfaceLine.StrokeWidth + 6f))
            {
                glowPen.StartCap = LineCap.Round;
                glowPen.EndCap = LineCap.Round;
                glowPen.LineJoin = LineJoin.Round;
                g.DrawLines(glowPen, screenPts.ToArray());
            }

            float pad = 8f;
            RectangleF bbox = RectangleF.FromLTRB(minX - pad, minY - pad, maxX + pad, maxY + pad);
            using (Pen boxPen = new Pen(Color.FromArgb(6, 182, 212), 1.5f))
            {
                boxPen.DashStyle = DashStyle.Dash;
                boxPen.DashPattern = new float[] { 4f, 3f };
                g.DrawRectangle(boxPen, bbox.X, bbox.Y, bbox.Width, bbox.Height);
            }
        }

        private void DrawSelectedMesh3DBoundingBox(Graphics g, Size viewport, TeddyMesh mesh)
        {
            if (mesh == null || mesh.IsEmpty || mesh.Vertices.Count == 0) return;

            if (!ComputeMeshesBounds(new[] { mesh }, out Vector3 min, out Vector3 max)) return;
            float minX = min.X, minY = min.Y, minZ = min.Z;
            float maxX = max.X, maxY = max.Y, maxZ = max.Z;

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
                    _camera.Reset();
                    SetAlgorithmStepView(TeddyAlgorithmStepView.Final3DModel);
                    UpdateDeleteButtonState();
                    UpdateStatus($"Đã nạp file .OBJ thành công: {System.IO.Path.GetFileName(ofd.FileName)}");
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
                MessageBox.Show("Chưa có mô hình 3D nào để xuất! Hãy phác thảo 2D rồi bấm [Tạo Khối 3D] hoặc chọn Khối Cầu/Hộp.",
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
                    UpdateStatus($"Đã xuất file .OBJ thành công ({_sceneMeshes.Count(m => !m.IsEmpty)} vật thể): {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi lưu file .OBJ:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExportPng_Click(object? sender, EventArgs e)
        {
            if (_sceneMeshes.Count == 0 || _sceneMeshes.All(m => m.IsEmpty))
            {
                MessageBox.Show("Chưa có mô hình 3D nào để xuất ảnh PNG!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Title = "Xuất Ảnh Mô Hình 3D (.PNG Nền Trong Suốt)",
                Filter = "PNG Image (*.png)|*.png",
                FileName = "teddy_3d_render.png"
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    using Bitmap snapshot = TeddyRenderer.RenderTransparentSnapshot(
                        Math.Max(1, _viewportPanel.ClientSize.Width),
                        Math.Max(1, _viewportPanel.ClientSize.Height),
                        _sceneMeshes,
                        _camera,
                        _renderStyle,
                        _cutPieceVisibility,
                        _cutSurfaceViewMode,
                        _cutFillColor);
                    snapshot.Save(sfd.FileName, ImageFormat.Png);
                    UpdateStatus($"Đã xuất ảnh PNG nền trong suốt thành công: {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất ảnh PNG:\n{ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
