# Báo Cáo Nghiệm Thu Dự Án PaintLike 3D (Chặng 5 – Chặng 9)

**Hệ thống:** PaintLike — **Teddy 3D Freeform Modeler** (ACM SIGGRAPH '99: Takeo Igarashi, Satoshi Matsuoka, Hidehiko Tanaka)  
**Giải pháp:** `PaintLike.sln` (.NET 8 Windows Forms)  
**Trạng thái biên dịch cuối cùng:** `Build succeeded. 0 Warning(s), 0 Error(s)` (kiểm định qua `.agents/skills/teddy-3d-algorithm/scripts/verify-teddy-build.ps1`)  
**Phạm vi nghiệm thu:** Toàn bộ **14 hạng mục** thuộc **Chặng 5 → Chặng 9** (`TV3-06A`, `TV3-06B`, `TV1-06`, `TV1-07`, `TV2-06`, `TV1-08`, `TV2-07`, `TV2-08`, `TV3-07`, `TV1-09`, `TV2-09`, `TV3-08`, `TV2-10`, `TV3-09`) cùng các cải tiến UX thực tế.

---

## 1. Tổng Quan Kiến Trúc & Quy Trình Tương Tác Đạt Chuẩn

1. **Ứng dụng 3D Độc lập (`Standalone 3D Studio`):**
   - Khởi chạy trực tiếp vào cửa sổ phóng đại `TeddyStudioDialog` từ `PaintLike/Program.cs`, gỡ bỏ hoàn toàn giao diện Paint 2D cũ (`Form1`, `LayerPanel`, `ImageProcessor`...).
   - Giữ lại `UITheme.cs` (phong cách Bright Studio Light) và `WuLineDrawer.cs`.
2. **Mô hình 2 Giai đoạn Tường minh (`Draw-2D-First → Click-to-Apply 3D`):**
   - **Giai đoạn 1 (`TeddySketch2DLayer`):** Người dùng kéo Chuột Trái vẽ tự do (hoặc triển khai mẫu `Khối Cầu`, `Khối Hộp`, `Gấu Teddy`, `Quả Cherry`, `Con Rắn`) dưới dạng nét vẽ 2D trên lớp overlay trong suốt. Chuột Phải xoay 3D, Con lăn Zoom, Chuột Giữa Pan.
   - **Giai đoạn 2 (Thực thi thuật toán 3D):** Chỉ khi bấm nút chức năng trên thanh Action Bar (`[Tạo Khối 3D]`, `[Dán Nét 3D]`, `[Tô Vùng 3D]`, `[Đặt Vòng Đế]`, `[Đùn / Khoét 3D]`, `[Cắt Khối 3D]`, `[Mượt Bezier 3D]`, `[Uốn Khối 3D]`), thuật toán 3D tương ứng mới chạy trên nét 2D.

---

## 2. Bảng Tổng Hợp Nghiệm Thu Chi Tiết (Chặng 5 – Chặng 9)

| Chặng | Mã CV | Phụ trách | Tên công việc & Thuật toán đã cài đặt | Tệp mã nguồn `.cs` | Kết quả nghiệm thu |
|---|---|---|---|---|---|
| **Chặng 5** | **TV3-06A** | Thành viên 3 | Gỡ giao diện Paint 2D cũ (`Form1`), khởi chạy thẳng `TeddyStudioDialog` phóng đại, xuất ảnh `.PNG` nền trong suốt RGBA (`RenderTransparentSnapshot`). | `Program.cs`, `TeddyStudioDialog.cs`, `TeddyRenderer.cs` | **ĐẠT** |
| **Chặng 5** | **TV3-06B** | Thành viên 3 | Lớp phác thảo 2D độc lập `TeddySketch2DLayer` (`SketchStroke2D`, tẩy/xóa/khép kín 2D, cảnh báo tự cắt) & hệ thống nút Click-to-Apply 3D. | `TeddySketch2DLayer.cs`, `StrokeProcessor.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 6** | **TV1-06** | Thành viên 1 | Oriented Semicircle Pruning (`AreAllVerticesInOrientedSemicircle`, chống trùng quạt Junction `fanDedup`, chọn đường chéo Delaunay `PreferDiagonalAC`), Topological Vertex Sewing và thổi phồng đồng loạt nhiều nét 2D. | `Delaunay2D.cs`, `TeddyInflationEngine.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 6** | **TV1-07** | Thành viên 1 | Angular Quarter-Oval Elevation (`QuarterOvalSteps = 6`, `EvaluateAngularQuarterOval` với $\sin\theta, \cos\theta$), khóa biên hình bóng $Z=0$ qua `SmoothMesh` & `RefineMesh`. | `TeddyInflationEngine.cs`, `TeddyMeshOptimizer.cs`, `TeddyRenderer.cs` | **ĐẠT** |
| **Chặng 6** | **TV2-06** | Thành viên 2 | Bounded Ray-Plane Adjacency Splicing (`TryProjectStrokeWithRayPlaneSplicing`), phát hiện nếp gấp khuất (`SurfaceProjectionStatus.FoldDetected`) và xóa nét có kiểm tra độ sâu mặt trước (`EraseSurfaceLines`). | `TeddyRaycaster.cs`, `TeddySurfacePaintOp.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 7** | **TV1-08** | Thành viên 1 | Bộ chia lưới 2D chất lượng cao Shewchuk Steiner-CDT (`TriangulateQualitySteinerCDT`, `SteinerCdtResult`, chèn điểm Steiner tâm ngoại tiếp / Off-center, bảo toàn chỉ số biên gốc & làm mượt góc). | `Delaunay2D.cs` | **ĐẠT** |
| **Chặng 7** | **TV2-07** | Thành viên 2 | Optimal Ring-Normal Projection Plane $\Pi_{\text{ext}}$ (Hình 17a), khung vận chuyển song song **Rotation-Minimizing Frame (RMF)** chống xoắn (Hình 19) & chế độ **Khâu liền khối đa tạp** (`sewToParent` mặc định; giữ `Shift` để đùn khối rời). | `TeddyExtrudeCutOp.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 7** | **TV2-08** | Thành viên 2 | Bounded Biting Cut (`DetectBoundedBitingCut`, Hình 8) tránh cắt nhầm nhánh xa & đóng nắp mặt cắt bằng **2D Quality Steiner-CDT** trên không gian $(s, z)$ (Hình 20). | `TeddyExtrudeCutOp.cs` | **ĐẠT** |
| **Chặng 7** | **TV3-07** | Thành viên 3 | `SoftwareDepthBuffer` (lưới `2x2 px`, dùng chung toàn cảnh qua `BuildSceneDepthBuffer`) khử nét Silhouette & nét vẽ bề mặt bị che khuất ở chế độ `PenAndInk` (Hình 1), giữ lớp 2D nổi rõ bên trên. | `TeddyRenderer.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 8** | **TV1-09** | Thành viên 1 | Bộ tái chia lưới đẳng hướng 4 pha `IsotropicManifoldRemesh` (*Split → Link-Condition Collapse → Valence-6 Flip → Tangential Taubin Fairing*) & hàm kiểm định `ValidateManifoldAndEuler()` ($\chi = V - E + F = 2$). | `TeddyMesh.cs`, `TeddyMeshOptimizer.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 8** | **TV2-09** | Thành viên 2 | True Hole-Deleting Bezier Smoothing (`TrySmoothRegionInBaseRing`): tách vòng biên lỗ đơn lớn nhất, xóa tam giác nhăn cũ, chia lưới Steiner-CDT, nâng $Z$ bằng **Cubic Bezier đa mặt phẳng khớp tiếp tuyến $C^1$** + Taubin Fairing. | `TeddySmoothBendOp.cs` | **ĐẠT** |
| **Chặng 8** | **TV3-08** | Thành viên 3 | Mở rộng `TeddyAlgorithmDebugInfo` & `TeddyAlgorithmStepView`: thêm **`6. Đùn 17-18`** (phím `6`) và **`7. Bezier 21`** (phím `7`) có thể xoay 3D bằng Chuột Phải. | `TeddyAlgorithmDebugInfo.cs`, `TeddyMesh.cs`, `TeddyRenderer.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 9** | **TV2-10** | Thành viên 2 | Auto-Spine 1-Stroke Bending & Two-Stroke Bending (`TryAutoSpineOrTwoStrokeBend`), tạo Gờ Sắc 3D (`TryApplyCreaseStroke` khi giữ `Shift + Dán Nét 3D`) & Xoắn khối 3D (`TryTwistMeshAlongStroke` khi giữ `Shift + Uốn Khối 3D`). | `TeddySmoothBendOp.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |
| **Chặng 9** | **TV3-09** | Thành viên 3 | Xuất/Nhập `.OBJ + .MTL` đa màu bảo toàn từng màu vùng mặt tam giác (`ObjExporter`), đo thời gian thực thi thuật toán (`Stopwatch`) & Thẻ Benchmark Hình học 3D (`DrawBenchmarkOverlayCard`, phím `B`). | `ObjExporter.cs`, `TeddyRenderer.cs`, `TeddyStudioDialog.cs` | **ĐẠT** |

---

## 3. Các Hạng Mục Bổ Sung & Hoàn Thiện Trải Nghiệm Người Dùng

Song song với lộ trình Chặng 5 – Chặng 9, các yêu cầu trực tiếp trong quá trình sử dụng đã được tích hợp đầy đủ:
1. **Triển khai mọi hình mẫu ở dạng 2D trước khi dựng 3D:** Các nút `Khối Cầu`, `Khối Hộp`, `Gấu Teddy`, `Quả Cherry`, `Con Rắn` chỉ triển khai viền 2D (tự dịch ngang chống chồng lấp); chỉ khi bấm **`[Tạo Khối 3D]`** mới chuyển thành khối 3D chuẩn (giữ nguyên độ tròn đều cho Khối Cầu, tỉ lệ lập phương cho Khối Hộp và tô màu đa vùng cho các mẫu bài báo).
2. **Hệ trục tọa độ 3D (`Oxyz`) & La bàn góc (Gizmo):** Nút `[Trục 3D]` (phím `A`), vẽ trục `+X/+Y/+Z` bám theo tâm từng khối 3D hoặc hình vẽ 2D mới, hiển thị tọa độ `O(x, y, z)` trực tiếp.
3. **Di chuyển & Xoay độc lập từng hình 3D trên trục riêng:**
   - Mỗi khối mang `GroupId` và hướng trục riêng `LocalRotation` (`RotateAroundPivot`, `SetLocalRotationAroundPivot`).
   - Kéo Chuột Phải trên một hình 3D hoặc bấm La bàn Gizmo chỉ xoay hình đó quanh tâm `O` của chính nó mà **không làm xoay các hình khác**.
   - Bật `[Di Chuyển 3D]` (phím `M`) hoặc kéo trực tiếp tâm `O` / mũi tên trục `+X, +Y, +Z` để tịnh tiến khối 3D sang vị trí mới.
4. **Cắt khối 3D trực quan theo 7 mẫu:** Hỗ trợ 7 mẫu (`Tự do`, `Thẳng`, `Zíc-zắc`, `Cong`, `Sóng`, `Bậc thang`, `Chữ V`), giữ lại phần bị cắt (`Hiện / Ẩn`), biểu diễn vòng cắt nét đứt (`IsDashed`) và tô sẫm màu phía trong thiết diện mặt cắt.

---

## 4. Kết Quả Kiểm Định Tự Động & Số Liệu Thực Nghiệm

Sau bản build cuối cùng (`0 Warning(s), 0 Error(s)`), bộ kiểm thử tự động trong dự án kiểm tra `TestSteiner` ghi nhận các kết quả thực tế sau:

### 4.1. Kiểm định bộ chia lưới `TV1-08` (Stewchuk Steiner-CDT)
| Đa giác kiểm thử | Chế độ biên (`allowBoundarySplit`) | Tổng đỉnh (Steiner) | Tam giác | Góc nhỏ nhất | Tam giác lật (`Inv`) |
|---|---:|---:|---:|---:|---:|
| **L-Shape** | `False` | `6 (0)` | `4` | **`26.57°`** ($> 18^\circ$) | `0` |
| **L-Shape** | `True` | `8 (2)` | `6` | **`33.69°`** ($> 18^\circ$) | `0` |
| **U-Shape** | `False` / `True` | `8 (0)` / `11 (3)` | `6` / `9` | **`21.80°`** / **`33.69°`** | `0` |
| **Concave-Star** | `False` / `True` | `16 (0)` | `14` | **`22.50°`** ($> 18^\circ$) | `0` |
| **Teddy-2D Silhouette** | `False` / `True` | `242 (154)` | `394` | **`34.58°`** ($> 18^\circ$) | `0` |

### 4.2. Kiểm định Đa tạp & Euler `TV1-09` (`IsotropicManifoldRemesh`)
- **Mô hình Gấu Teddy (`TeddySilhouette`):**
  - Trước Remesh: `V = 7874`, `E = 23616`, `F = 15744`, $\chi = V - E + F = \mathbf{2}$, `Boundary = 0`, `NonManifold = 0`.
  - Sau Remesh (`2787 ops`): `V = 6703`, `E = 20103`, `F = 13402`, $\chi = \mathbf{2}$, `Boundary = 0`, `NonManifold = 0`, **`Watertight = True`**, góc nhỏ nhất tăng từ `0.75°` lên `1.47°` (`Avg = 16.43°`).
- **Mô hình Khối Cầu (`CreateSphere`):**
  - Sau Remesh: `V = 438`, `E = 1308`, `F = 872`, $\chi = \mathbf{2}$, `Boundary = 0`, `NonManifold = 0`, **`Watertight = True`**.

### 4.3. Kiểm định Xóa Lỗ Bezier `TV2-09`
- Trên mô hình Gấu Teddy khoanh vòng chọn mặt trước:
  - Kết quả: `ok = True`, tạo `172` tam giác mới (`55` điểm Steiner), bảo toàn hoàn toàn đa tạp kín sau khi vá lỗ: $\chi = \mathbf{2}$, `Boundary = 0`, `NonManifold = 0`.

### 4.4. Kiểm định Xuất / Nhập `.OBJ + .MTL` Đa Màu `TV3-09`
- Xuất Gấu Teddy ra `.obj`: tự động sinh tệp vật liệu `.mtl` đi kèm (`mtlExists = True`).
- Nạp lại từ `.obj`: khôi phục chính xác **`13442 / 13442`** mặt tam giác mang màu riêng (`DistinctColors = 4`).

---

## 5. Hướng Dẫn Kiểm Tra Nhanh Trên Giao Diện Teddy 3D Studio

| Thao tác kiểm thử | Bước thực hiện trên ứng dụng | Kết quả mong đợi |
|---|---|---|
| **Phác thảo 2D → 3D & Đa nét (`TV1-06`, `TV1-07`)** | Bấm `Gấu Teddy` / `Khối Cầu` hoặc vẽ tay nhiều vòng 2D kín → bấm **`[Tạo Khối 3D]`** | Ban đầu chỉ hiện nét 2D; sau khi bấm `[Tạo Khối 3D]`, tất cả nét 2D hợp lệ phồng thành khối 3D tròn mượt (bấm phím `1` → `5` để xem Hình 13–15). |
| **Dán nét, Chặn Fold & Tạo Gờ (`TV2-06`, `TV2-10`)** | Vẽ nét 2D trên mặt khối → bấm **`[Dán Nét 3D]`**; giữ **`Shift + Dán Nét 3D`** để tạo Gờ Sắc | Nét bám sát từng cạnh tam giác; nét vắt qua mép khuất bị chặn (`Fold Check`); gạch zíc-zắc xóa đúng nét mặt trước. |
| **Đùn liền khối RMF & Xóa lỗ Bezier (`TV2-07`, `TV2-09`, `TV3-08`)** | Khoanh vòng trên khối → **`[Đặt Vòng Đế]`** → xoay nghiêng → vẽ vòm 2D → **`[Đùn / Khoét 3D]`** → khoanh quanh ngấn → **`[Mượt Bezier 3D]`** | Đùn liền khối theo hướng `ringNormal` không xoắn; bấm phím `6` và `7` để xem minh họa Hình 17–21. |
| **Cắn khuyết cục bộ & Nắp Steiner-CDT (`TV2-08`)** | Vẽ nét cắt cong cục bộ ở một bên thân khối → bấm **`[Cắt Khối 3D]`** | Chỉ cắn khuyết vùng cục bộ, đóng nắp mặt cắt mịn bằng lưới Steiner-CDT, giữ nguyên các nhánh khác. |
| **Tái lưới, Benchmark & Xuất đa màu (`TV1-09`, `TV3-09`)** | Bấm **`[Tái lưới]`**, nhấn phím **`B`** để xem Benchmark, bấm **`Xuất .OBJ`** | Thẻ Benchmark hiển thị `χ = V - E + F = 2`, `Kín Đa Tạp`, `ms` thực thi; xuất đồng thời `.obj` & `.mtl` giữ nguyên màu đa vùng. |
