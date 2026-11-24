# `PaintLike/Teddy` Codebase Architecture, Gap Audit & Phase 2 Roadmap

## 1. Standalone 3D Migration & Staged 2D-to-3D Interaction Target

- **Current State (Phase 1 MVP):**
  - `Program.cs` launches `Form1` (a 2D raster paint app with layers & OpenCV filters), which opens `TeddyStudioDialog` as a secondary dialog.
  - Inside `TeddyStudioDialog.cs`, `ViewportPanel_MouseUp` immediately calls `ProcessCompletedUserStroke(_activeStroke)`, auto-running 3D inflation/cutting/extrusion on mouse release and requiring `Shift + Left Drag` to draw.
- **Target Architecture (Planned for Stage 5 — Do Not Forget):**
  1. **Remove Legacy 2D `Form1` UI:** `Program.cs` starts `Application.Run(new TeddyStudioDialog())` directly; remove `Form1.*`, `LayerPanel.*`, `Layer.cs`, `LayerItem.cs`, `ImageProcessingDialog.cs`, `ImageProcessor.cs`; replace `"Chèn vào Layer 2D"` with direct transparent `.PNG` export.
  2. **Staged `TeddySketch2DLayer` (Draw 2D First $\to$ Click Button to Apply 3D):**
     - Left-click dragging in **2D Sketch Mode** records `SketchStroke2D` curves onto a persistent 2D canvas overlay without running any 3D operation on `MouseUp`.
     - Right-click rotates the 3D Trackball camera underneath the 2D sketch overlay; Mouse Wheel zooms.
     - Users can draw multiple 2D strokes, select/erase/close/smooth 2D strokes, view live self-intersection checks, and **only trigger 3D algorithms by clicking explicit action buttons** (`Tạo Khối 3D`, `Dán Nét / Tô Vùng`, `Đặt Vòng Đế`, `Đùn / Khoét 3D`, `Cắt / Cắn 3D`, `Làm Mượt Bezier`, `Uốn 3D`).

---

## 2. File Map & Responsibilities (`PaintLike/Teddy/`)

| File | Owner | Paper Section | Role |
|---|---|---|---|
| `TeddyMesh.cs` | TV1 | Sec. 4.2, 5 | Core `TeddyMesh`, `TeddyTriangle`, `TeddyEdge`, `TeddySurfaceLine`, `BuildConnectivity()`, `RecalculateNormals()`, Taubin `SmoothMesh()`, `CreateSphere()`, `CreateCube()` |
| `Delaunay2D.cs` | TV1 | Sec. 5.1 (Fig. 13b) | Ear-clipping + Lawson circumcircle edge-flipping CDT, `T`/`S`/`J` classification (+ planned Steiner-CDT) |
| `TeddyInflationEngine.cs` | TV1 | Sec. 5.1 (Figs. 13–15) | Semicircle pruning, pruned Spine extraction, spine Z-elevation, symmetric quarter-oval mesh construction |
| `TeddyMeshOptimizer.cs` | TV1 | Sec. 5.1 & Fig. 1, 2, 5 | Short-edge collapse (`RefineMesh`) + paper sample presets (`TeddySilhouette`, `Cherry`, `Snake`) |
| `TeddyAlgorithmDebugInfo.cs` | TV1/TV3 | Figs. 13–15 | Intermediate 2D/3D stages for interactive step-by-step visualization |
| `TeddyRaycaster.cs` | TV2 | Sec. 5.2, 5.3 | Möller–Trumbore raycast, surface stroke projection, scribble erasing, `ComputeRingNormal()` |
| `TeddySurfacePaintOp.cs` | TV2 | Sec. 4.3, 5.2 | Surface line creation, extrusion base ring setup & stroke validation |
| `TeddyExtrudeCutOp.cs` | TV2 | Sec. 5.3, 5.4 (Figs. 6–9, 17–20) | Two-pointer extrusion, cavity digging, 7 cut patterns (`Freehand`, `Straight`, `Zigzag`, `Curved`, `Wave`, `Stepped`, `ChevronV`), split/keep cut piece |
| `TeddySmoothBendOp.cs` | TV2 | Sec. 4.6, 4.7, 5.5 (Figs. 10, 11, 21) | Bezier region smoothing, face color region fill, 2-stroke screen-parallel Field Warp bending |
| `TeddySketch2DLayer.cs` *(planned)* | TV3 | UX Stage A | Persistent 2D sketch buffer (`SketchStroke2D`), 2D selection/eraser/auto-close, live self-intersection indicator |
| `StrokeProcessor.cs` | TV3 | Sec. 4.1, 5 | `ResampleUniform()`, `IsScribbling()` ($s_l/p_l > 1.5$), `IsSelfIntersecting()`, Monotone Chain Convex Hull |
| `VirtualTrackballCamera.cs` | TV3 | Sec. 3 | Hultquist Quaternion virtual trackball, orthographic zoom/pan, `ProjectToScreen()`, `ScreenPointToRay()` |
| `TeddyRenderer.cs` | TV3 | Sec. 1, 6 (Fig. 1) | `PenAndInk` silhouette/crease NPR, `SmoothShaded`, `WireframeSpine`, 2D sketch overlay, PNG snapshot |
| `ObjExporter.cs` | TV3 | Sec. 6 | Multi-object Wavefront `.OBJ` export & normalized `.OBJ` import |
| `TeddyStudioDialog.cs` | TV3 | Sec. 3 | Standalone main WinForms 3D Studio window, 2D tools + explicit 3D algorithm trigger buttons, Undo/Redo |

---

## 3. Seven Mathematical Approximations to Upgrade (SIGGRAPH '99 Fidelity)

1. **`TeddyInflationEngine.cs` — Full-Circle vs. Oriented Semicircle & Quantized Welding**
   - `AreAllVerticesInSemicircle` checks radial distance $\le 1.02 R$ without verifying half-plane orientation relative to the interior edge, and multiple `T` branches hitting one `J` triangle need coordinated fan deduplication.
   - `GetOrAdd3DVertex` welds vertices via `(int)Round(xy * 10f)` instead of exact topological edge/vertex indices, risking accidental welding on thin stems, and uses uniform $(x,y)$ steps instead of angular $\theta_k = \frac{\pi k}{2K}$ (`Sin`/`Cos`), causing steep normal distortion near $z=0$.
2. **`TeddyMeshOptimizer.cs` — Union-Find Collapse Without Link Condition**
   - `RefineMesh` collapses short edges in one Union-Find pass without checking the manifold **Link Condition** ($\text{Lk}(u) \cap \text{Lk}(v) = \text{Lk}(uv)$) and lacks long-edge splitting and valence-6 edge flips.
3. **`TeddyRaycaster.cs` — Discrete Ray Sampling vs. Bounded Ray-Plane Adjacency Walk**
   - `ProjectStrokeToSurface` samples isolated rays every `8px` and offsets `+1.2f * normal` instead of walking `tri.Neighbors` along the ray-plane intersection; misses **fold/occlusion jumps** (Sec. 5.2).
4. **`TeddyExtrudeCutOp.cs` — Constant-Z Projection Plane & Disconnected Extrusion**
   - Extruding stroke unprojects to flat `viewCenter.Z` instead of intersecting the optimal plane containing `ringNormal` (Sec. 5.3, Fig. 17a).
   - Positive extrusion creates a detached mesh (useful for part deletion), but lacks a **Manifold Sewn Extrusion** option needed for **Smoothing extrusion root creases (Fig. 10b)**, and needs Rotation-Minimizing Frames (RMF) to prevent Fig. 19 twisting.
5. **`TeddyExtrudeCutOp.cs` — Infinite Cut Extension vs. Bounded Biting & Steiner-CDT Cap**
   - Extending cut endpoints by `3200px` slices remote limbs during local **Biting** strokes (Fig. 8); section caps use a 1D spine strip instead of Steiner-refined 2D CDT.
6. **`TeddySmoothBendOp.cs` — In-Place Vertex Shift vs. Hole-Deleting Bezier Remeshing**
   - `TrySmoothRegionInBaseRing` shifts existing vertices without deleting crumpled interior triangles or matching exterior boundary surface tangents—upgrade to **Delete Interior + Steiner CDT + Tangent-Matched Multi-Plane Cubic Bezier + Fairing** (Sec. 5.5, Fig. 21).
7. **`TeddyRenderer.cs` — Painter's Algorithm vs. Software Z-Buffer**
   - Sorting triangles by `AvgZ` causes overlap artifacts between limbs/cut pieces, and `DrawPenAndInkSilhouettes` draws back-limb silhouettes through front surfaces without depth testing.

---

## 4. Roadmap Stages 5–9 Summary

- **Stage 5 (Standalone 3D UI & 2D Sketch Buffer):** `TV3-06A` (Remove `Form1`, boot `TeddyStudioDialog` standalone + `.PNG` export), `TV3-06B` (`TeddySketch2DLayer` draw-2D-only mode + explicit click buttons to run each 3D algorithm).
- **Stage 6 (Core Inflation & Surface Projection Precision):** `TV1-06` (Oriented Semicircle Pruning + Topological sewing), `TV1-07` (Angular Quarter-Oval elevation), `TV2-06` (Bounded Ray-Plane adjacency walk + Fold check).
- **Stage 7 (Robust Extrusion, Biting Cut & Z-Buffer):** `TV1-08` (2D Steiner-CDT), `TV2-07` (`ringNormal` plane + RMF sweep + Manifold Sewn Extrusion), `TV2-08` (Bounded Biting cut + Steiner-CDT cap), `TV3-07` (Software Z-Buffer + Hidden-line Pen-and-Ink).
- **Stage 8 (True Bezier Hole-Smoothing & Remeshing):** `TV1-09` (Isotropic Manifold Remesher + Euler $V-E+F=2$ check), `TV2-09` (Delete-hole + Steiner-CDT + Tangent Cubic Bezier Smoothing), `TV3-08` (Step-by-step visualizers for Extrude & Smooth).
- **Stage 9 (Section 8 Extensions & Multi-Material Export):** `TV2-10` (Auto-Spine 1-stroke Bending + Crease/Twist), `TV3-09` (`.OBJ + .MTL` region color export + Geometry Benchmark panel).
