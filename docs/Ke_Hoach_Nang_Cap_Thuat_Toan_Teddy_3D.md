# Kế Hoạch Tái Cấu Trúc Giao Diện 3D Độc Lập & Nâng Cấp Chuyên Sâu Thuật Toán Teddy 3D

> **Tài liệu kỹ thuật dự án PaintLike 3D (Bản cập nhật Kiến trúc Standalone 3D + Vẽ 2D Trước / Click Chạy 3D)**
> - **Nguyên tắc hiện tại:** Tài liệu đặc tả kế hoạch thiết kế chi tiết (chưa chỉnh sửa mã nguồn `.cs`).
> - **Skill đi kèm cho AI Agent:** `.agents/skills/teddy-3d-algorithm/SKILL.md`

---

## 1. Hai Thay Đổi Kiến Trúc Cốt Lõi Về Giao Diện & Tương Tác

### 1.1. Xóa bỏ Giao diện Paint 2D Ban đầu (`Form1`) — Chỉ giữ Giao diện 3D Duy nhất
- **Điểm khởi chạy (`Program.cs`):** Đổi từ `Application.Run(new Form1())` sang khởi chạy trực tiếp `Application.Run(new TeddyStudioDialog())` ở chế độ cửa sổ chính toàn màn hình (`WindowState = FormWindowState.Maximized`).
- **Dọn dẹp các tệp UI 2D cũ không còn dùng:** Gỡ bỏ `Form1.cs`, `Form1.Designer.cs`, `Form1.resx`, `LayerPanel.cs`, `LayerPanel.Designer.cs`, `Layer.cs`, `LayerItem.cs`, `ImageProcessingDialog.cs`, `ImageProcessor.cs` (cùng gói OpenCvSharp nếu không còn dùng). Chỉ giữ lại `UITheme.cs` và `WuLineDrawer.cs` phục vụ giao diện 3D Studio và vẽ nét phác thảo 2D.
- **Thay thế đầu ra 2D Layer:** Thay nút `"Chèn vào Layer 2D"` trên thanh Ribbon 3D bằng nút **`"Xuất Ảnh PNG"`** (lưu ảnh chụp mô hình 3D nền trong suốt `RGBA` trực tiếp ra đĩa cứng bên cạnh chức năng Xuất/Nhập `.OBJ`).

### 1.2. Cơ chế "Chỉ Vẽ 2D Phác Thảo Trước $\to$ Click Nút Chức Năng Mới Áp Dụng Thuật Toán 3D"
Thay vì tự động biến đổi lưới 3D ngay tại sự kiện `MouseUp`, toàn bộ quy trình dựng hình chuyển sang mô hình **2 giai đoạn tường minh (Staged 2D Sketch $\to$ Explicit 3D Algorithm Trigger)**:

1. **Giai đoạn 1 — Chế độ Chỉ Vẽ 2D (`TeddySketch2DLayer`)**:
   - Người dùng dùng **Chuột Trái** vẽ thoải mái một hoặc nhiều nét 2D lên lớp phác thảo trong suốt đè trên khung nhìn 3D (không cần giữ phím `Shift`; **Chuột Phải** vẫn dùng xoay Camera Trackball 3D và **Con lăn** để Zoom nhằm căn góc khối 3D bên dưới nét 2D).
   - Khi nhả chuột (`MouseUp`), nét vẽ **chỉ lưu dưới dạng đối tượng 2D (`SketchStroke2D`)** trong bộ đệm phác thảo, hoàn toàn chưa gọi thuật toán 3D.
   - Hỗ trợ đầy đủ thao tác nháp 2D: vẽ thêm nét, chọn/xóa nét nháp, tẩy nét 2D, nối khép kín tự động đầu–cuối, làm mượt nét 2D, và hiển thị cảnh báo trực quan nếu nét tự cắt nhau (`IsSelfIntersecting`).
2. **Giai đoạn 2 — Click Chuột Vào Nút Chức Năng Mới Chạy Thuật Toán 3D**:
   - **`[Tạo Khối 3D (Inflate)]`**: Lấy nét 2D khép kín trong bộ đệm $\to$ chạy thuật toán Phồng 3D (Mục 5.1: *CDT $\to$ Pruning $\to$ Spine $\to$ Quarter-Oval Elevation*).
   - **`[Dán Nét / Tô Vùng 3D]`**: Chiếu các nét 2D đang nằm trên bóng khối xuống bề mặt lưới 3D (Mục 5.2: *Bounded Ray-Plane Connectivity Splicing*).
   - **`[Đặt Vòng Đế (Base Ring)]` $\to$ Xoay 3D $\to$ Vẽ nét biên 2D $\to$ `[Đùn Lồi / Khoét Lõm 3D]`**: Thực thi quy trình đùn 2 bước tường minh theo Mục 5.3 (*Optimal Ring-Normal Plane + Two-Pointer Sweep*).
   - **`[Cắt / Cắn Khối 3D]`**: Áp dụng đường cắt 2D (tự do hoặc mẫu Thẳng / Zíc-zắc / Cong / Sóng / Bậc thang / Chữ V) để cắt khối theo Mục 5.4 (*Bounded Ruled Surface + Steiner-CDT Cap*).
   - **`[Làm Mượt Bezier 3D]`**: Lấy vòng chọn hiện tại (hoặc nét 2D khoanh vùng trên khối) $\to$ chạy giải thuật làm mượt xóa lỗ & đắp mặt cong Bezier chuẩn Mục 5.5 (*Delete Interior + Steiner-CDT + Multi-Plane Tangent Cubic Bezier + Taubin Fairing*).
   - **`[Uốn Khối 3D (Bend)]`**: Lấy cặp 2 nét 2D (`Reference` & `Target`) hoặc 1 nét 2D kết hợp trục `Spine` tự động $\to$ biến dạng khối theo Mục 4.7.

---

## 2. Bảy Hạng Mục Chuẩn Hóa Toán Học Thuật Toán Teddy (SIGGRAPH '99)

1. **Mục 5.1 — Oriented Semicircle Pruning & Junction Coordination (`TeddyInflationEngine.cs`, Hình 13–14)**: Kiểm tra cả bán kính lẫn dấu nửa mặt phẳng cùng phía vùng gộp $X$ (Prasad [21]); tránh trùng quạt khi nhiều nhánh Terminal hội tụ tại 1 tam giác Junction; chọn đường chéo tứ giác Sleeve tối ưu Delaunay.
2. **Mục 5.1 — Angular Quarter-Oval Elevation & Topological Sewing (`TeddyInflationEngine.cs`, Hình 15c–d)**: Tham số hóa cung 1/4 elip theo góc $\theta_k = \frac{\pi k}{2K}$ ($\mathbf{xy} = \text{Lerp}(\mathbf{s}, \mathbf{b}, \sin\theta_k)$, $z = \pm H\cos\theta_k$) để pháp tuyến tại biên $z=0$ tròn mượt; khâu đỉnh bằng bảng chỉ số cạnh Topo thay vì làm tròn tọa độ `Round(xy * 10f)`.
3. **Mục 5.1 — Isotropic Manifold Remesher (`TeddyMeshOptimizer.cs`, Hoppe [12])**: Thay Union-Find gộp mù 1 lượt bằng 4 bước bảo toàn đa tạp: *Edge Split $\to$ Link-Condition Safe Collapse $\to$ Valence-6 Flip $\to$ Tangential Taubin Fairing*.
4. **Mục 5.2 — Bounded Ray-Plane Adjacency Splicing (`TeddyRaycaster.cs`, Hình 16)**: Tìm giao tuyến mặt phẳng tia bằng cách bước qua `tri.Neighbors`, tự động phát hiện và chặn lỗi nhảy qua nếp gấp khuất (**Fold Check**); xóa đoạn nét có kiểm tra độ sâu mặt trước.
5. **Mục 5.3 — Optimal Extrusion Plane, RMF Frame & Manifold Sewing (`TeddyExtrudeCutOp.cs`, Hình 17–19)**: Chiếu nét đùn 2D lên mặt phẳng chứa `ringNormal` hướng tối đa về Camera; dùng **Rotation-Minimizing Frame** chống xoắn lật lát quét; bổ sung chế độ **Khâu liền khối Barequet–Sharir [1]** để làm mượt được ngấn chân đùn (Hình 10b).
6. **Mục 5.4 — Bounded Biting Cut & Steiner-CDT Section Cap (`TeddyExtrudeCutOp.cs`, Hình 8, 9, 20)**: Giới hạn vết cắt trong phạm vi nét 2D giao với vật thể để cắn khuyết cục bộ (Biting) không chém nhầm nhánh xa; chia lưới nắp mặt cắt bằng **2D Quality Steiner-CDT** (Shewchuk [22]).
7. **Mục 5.5 & Mục 1 — True Hole-Deleting Bezier Smoothing & Software Z-Buffer NPR (`TeddySmoothBendOp.cs`, `TeddyRenderer.cs`, Hình 1, 10, 21)**: Xóa sạch đa giác cũ trong vùng khoanh trước khi chia lưới Steiner-CDT và nâng cao độ Cubic Bezier khớp tiếp tuyến mặt ngoài; bổ sung **Software Z-Buffer** khử nét khuất Pen-and-Ink terwijl vẫn vẽ nổi lớp phác thảo 2D bên trên.

---

## 3. Lộ Trình 5 Chặng (Chặng 5 – Chặng 9) & Phân Công 3 Thành Viên

| Mã CV | Phụ trách | Chặng | Tên công việc | File `.cs` thực hiện | Đầu ra nghiệm thu |
|---|---|---|---|---|---|
| **TV3-06A** | Thành viên 3 | Chặng 5 | Gỡ giao diện `Form1` 2D cũ & Khởi chạy thẳng cửa sổ 3D độc lập | `Program.cs`, `TeddyStudioDialog.cs`, xóa `Form1.*`, `Layer*.*`, `ImageProcess*.*` | App mở thẳng vào **Teddy 3D Studio**; xuất ảnh PNG nền trong suốt trực tiếp. |
| **TV3-06B** | Thành viên 3 | Chặng 5 | Lớp Phác Thảo `TeddySketch2DLayer` & Hệ thống nút Click-to-Apply 3D | `Teddy/TeddySketch2DLayer.cs`, `StrokeProcessor.cs`, `TeddyStudioDialog.cs` | Vẽ 2D tự do bằng Chuột Trái không tự chạy 3D; chỉ khi click nút chức năng mới áp dụng thuật toán 3D tương ứng. |
| **TV1-06** | Thành viên 1 | Chặng 6 | Oriented Semicircle Pruning & Topological Vertex Sewing | `Delaunay2D.cs`, `TeddyInflationEngine.cs` | Cắt tỉa có hướng chuẩn Hình 14; khâu đỉnh Topo bảo toàn cổ/cuống hẹp; hỗ trợ phồng nhiều nét 2D cùng lúc. |
| **TV1-07** | Thành viên 1 | Chặng 6 | Angular Quarter-Oval Elevation ($\sin\theta, \cos\theta$) | `TeddyInflationEngine.cs` | Thiết diện elip tròn đều tại biên $Z=0$, xoay 90° mượt không lộ gờ nhọn. |
| **TV2-06** | Thành viên 2 | Chặng 6 | Ray-Plane Connectivity Splicing, Fold Detection & Depth Erase | `TeddyRaycaster.cs`, `TeddySurfacePaintOp.cs` | Nút *Dán Nét 3D* đi sát từng cạnh tam giác kề nhau (Hình 16) và chặn lỗi vắt qua nếp gấp khuất. |
| **TV1-08** | Thành viên 1 | Chặng 7 | Bộ chia lưới 2D Steiner-CDT chất lượng cao (Shewchuk [22]) | `Delaunay2D.cs` | Chèn điểm Steiner tâm ngoại tiếp cho đa giác lõm bất kỳ, góc nhỏ nhất $> 18^\circ$. |
| **TV2-07** | Thành viên 2 | Chặng 7 | Optimal Extrusion Plane, RMF Sweep & Manifold Sewn Extrusion | `TeddyExtrudeCutOp.cs` | Nút *Đùn / Khoét 3D* chiếu chuẩn theo `ringNormal`, chống xoắn Hình 19, hỗ trợ khâu liền khối. |
| **TV2-08** | Thành viên 2 | Chặng 7 | Bounded Biting Cut (Hình 8) & Steiner-CDT Cross-Section Cap | `TeddyExtrudeCutOp.cs` | Nút *Cắt 3D* cắn khuyết cục bộ chuẩn xác và đóng nắp mặt cắt bằng lưới Steiner-CDT đều mịn. |
| **TV3-07** | Thành viên 3 | Chặng 7 | Software Z-Buffer & Hidden-Line Pen-and-Ink Renderer | `TeddyRenderer.cs` | Khử nét Silhouette bị che khuất sau thân vật thể, hiển thị mượt lớp phác thảo 2D bên trên. |
| **TV1-09** | Thành viên 1 | Chặng 8 | Isotropic Manifold Remesher & Kiểm định Euler $V - E + F = 2$ | `TeddyMeshOptimizer.cs`, `TeddyMesh.cs` | Split / Link-Safe Collapse / Valence Flip + hàm kiểm tra tính kín của khối 3D. |
| **TV2-09** | Thành viên 2 | Chặng 8 | True Hole-Deleting Multi-Plane Bezier Smoothing (Hình 10, 21) | `TeddySmoothBendOp.cs` | Nút *Làm Mượt Bezier* xóa lưới nhăn cũ, đắp lưới mới nội suy Cubic Bezier khớp độ dốc biên + Fairing. |
| **TV3-08** | Thành viên 3 | Chặng 8 | Minh họa trực quan từng bước giải thuật Mục 5.1–5.5 (Hình 13–21) | `TeddyAlgorithmDebugInfo.cs`, `TeddyRenderer.cs`, `TeddyStudioDialog.cs` | Xem trực quan từng bước sau khi bấm chạy Inflate, Extrude (2 con trỏ) và Smooth (vòm Bezier). |
| **TV2-10** | Thành viên 2 | Chặng 9 | Auto-Spine Bending (Mục 4.7), Crease Stroke & Twisting (Mục 8) | `TeddySmoothBendOp.cs` | Nút *Uốn 3D* hỗ trợ 1 nét (tự bắt `Spine`) hoặc 2 nét nháp 2D, giữ thể tích khúc cua + tạo gờ sắc. |
| **TV3-09** | Thành viên 3 | Chặng 9 | Xuất/Nhập `.OBJ + .MTL` đa màu & Bảng Benchmark Hình học | `ObjExporter.cs`, `TeddyStudioDialog.cs` | Bảo toàn màu từng vùng (Hình 2) khi xuất `.obj/.mtl`; thống kê thời gian chạy & chất lượng tam giác. |
