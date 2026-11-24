---
name: teddy-3d-algorithm
description: >-
  Complete algorithmic runbook, UX interaction rules (Standalone 3D Studio + Draw-2D-First then
  Click-to-Apply-3D workflow), mathematical formulas, codebase architecture, and Phase 2 roadmap
  for the Teddy 3D freeform sketch-based modeling engine (Igarashi et al., ACM SIGGRAPH '99) in
  PaintLike/Teddy/*.cs. Use this skill whenever the user asks to plan, implement, debug, refactor,
  visualize, or extend 3D sketching features—including Standalone 3D UI migration, 2D Sketch Layer,
  2D-to-3D Inflation, Constrained Delaunay Triangulation (CDT), Semicircle Pruning, Chordal Axis /
  Spine, Quarter-Oval elevation, Surface Painting, Extrusion, Cavity, Cutting / Biting, Bezier
  Smoothing, Bending, Pen-and-Ink NPR rendering, or .OBJ/.PNG export in PaintLike 3D.
---

# Teddy 3D Algorithm & Architecture Skill (`PaintLike/Teddy`)

Use this skill to maintain mathematical fidelity to **Takeo Igarashi, Satoshi Matsuoka, Hidehiko Tanaka — *"Teddy: A Sketching Interface for 3D Freeform Design"* (ACM SIGGRAPH '99)** AND enforce the project's **Standalone 3D + Staged 2D-to-3D Interaction Architecture**.

## Progressive Disclosure References

Load these files whenever touching math details or planning/implementing upgrades:
- **Exact SIGGRAPH '99 Mathematical Formulas & Figures (Sec. 3–5 & 8):** [siggraph99-math-spec.md](./references/siggraph99-math-spec.md)
- **Codebase Gap Audit, Standalone 3D Migration & Phase 2 Roadmap:** [codebase-audit-and-roadmap.md](./references/codebase-audit-and-roadmap.md)
- **Project Phase 2 Plan Document:** [Ke_Hoach_Nang_Cap_Thuat_Toan_Teddy_3D.md](../../../docs/Ke_Hoach_Nang_Cap_Thuat_Toan_Teddy_3D.md)

---

## 1. Core UX & Application Architecture Rules (NEVER FORGET)

1. **Standalone 3D Studio Only (Remove Legacy 2D `Form1`):**
   - Target architecture launches `TeddyStudioDialog` directly in `Program.cs` (`Application.Run(new TeddyStudioDialog())`) as a maximized standalone 3D modeling studio.
   - Legacy 2D raster Paint UI (`Form1.*`, `LayerPanel.*`, `Layer.cs`, `LayerItem.cs`, `ImageProcessingDialog.cs`, `ImageProcessor.cs`) is slated for removal; keep shared `UITheme.cs` and `WuLineDrawer.cs`. Replace `"Chèn vào Layer 2D"` with direct transparent **`.PNG` Export** alongside `.OBJ`.
2. **Two-Stage Interaction: "Draw 2D Sketch First $\to$ Click Function Button to Apply 3D Algorithm":**
   - **Stage A (2D Sketch-Only Mode — `TeddySketch2DLayer`):** Left-mouse dragging draws/edits 2D strokes in a persistent 2D overlay buffer (`List<SketchStroke2D>`) WITHOUT requiring `Shift` and **WITHOUT auto-executing any 3D algorithm on `MouseUp`**. Right-mouse rotates the 3D Trackball camera underneath; Mouse Wheel zooms; users can draw multiple strokes, erase/delete/close/smooth 2D strokes, and see live self-intersection validation before committing.
   - **Stage B (Explicit Click-to-Apply 3D Trigger):** 3D mesh algorithms execute **ONLY when the user clicks the corresponding 3D operation button**:

| Explicit 3D Action Button | Required Buffered 2D Input | Underlying SIGGRAPH '99 Algorithm Triggered |
|---|---|---|
| **`Tạo Khối 3D (Inflate)`** | 1+ non-self-intersecting 2D strokes (auto-closed) | **Sec. 5.1:** Uniform Resample $\to$ CDT (`T/S/J`) $\to$ Oriented Semicircle Pruning $\to$ Spine $\to$ Quarter-Oval $\pm Z$ |
| **`Dán Nét / Tô Vùng 3D`** | 2D stroke(s) overlapping front-facing 3D mesh | **Sec. 5.2:** Bounded Ray-Plane adjacency walk (`TeddySurfaceLine`) or closed-ring face color fill |
| **`Đặt Vòng Đế (Base Ring)`** | 1 closed 2D stroke on 3D object surface | **Sec. 4.4 / 5.3:** Project onto surface as red `ActiveBaseRing` & compute $\mathbf{n}_{ring}$ |
| **`Đùn Lồi / Khoét Lõm 3D`** | `ActiveBaseRing` + 1 2D profile stroke (outward or inward) | **Sec. 5.3:** Project onto plane containing $\mathbf{n}_{ring}$ $\to$ Two-Pointer Sweep + RMF + Barequet–Sharir sewing |
| **`Cắt / Cắn Khối 3D`** | 1 2D cut stroke (Freehand or Zigzag/Curved/Wave/Stepped/V) | **Sec. 5.4:** Bounded camera-stroke ruled slice + Steiner-CDT cap + optional split/keep piece |
| **`Làm Mượt Bezier 3D`** | `ActiveBaseRing` or 1 closed 2D region stroke on mesh | **Sec. 5.5:** Delete interior polygons $\to$ Local XY Steiner-CDT $\to$ Multi-Plane Tangent Cubic Bezier + Fairing |
| **`Uốn Khối 3D (Bend)`** | 2 2D strokes (`Reference`, `Target`) or 1 stroke (Auto-Spine) | **Sec. 4.7:** Screen-parallel curve-frame freeform warp preserving view depth $Z_{view}$ |

*(Note: Classic SIGGRAPH '99 auto-gesture invariants—such as $s_l / p_l > 1.5$ scribble ratio in Footnote 1—remain documented in [siggraph99-math-spec.md](./references/siggraph99-math-spec.md) if an optional "Auto-Gesture Demo Toggle" is requested.)*

---

## 2. Core Algorithmic Pipeline Summary (Section 5)

1. **Creation / Inflation (`Delaunay2D.cs`, `TeddyInflationEngine.cs`, `TeddyMeshOptimizer.cs` — Sec. 5.1, Figs. 13–15)**
   - Resample 2D stroke to uniform edge length; reject self-intersections; orient CCW.
   - CDT $\to$ classify triangles by external edge count: **Terminal (`T`, 2 ext)**, **Sleeve (`S`, 1 ext)**, **Junction (`J`, 0 ext)**.
   - **Oriented Semicircle Pruning (Fig. 14, Prasad [21]):** Advance from each `T` triangle across interior edge $e=(u,v)$ only while all region vertices lie inside the semicircle on $e$ **on the same half-plane side as the region**; fan from midpoint of $e$ (Fig. 14d) or centroid of `J` (Fig. 14f).
   - **Spine & Quarter-Oval Elevation (Fig. 15):** Elevate spine vertices by average distance to connected boundary vertices; sample ribs by angle $\theta_k = \frac{\pi k}{2K}$ ($\mathbf{xy} = \text{Lerp}(\mathbf{s}, \mathbf{b}, \sin\theta_k)$, $z = \pm H\cos\theta_k$); weld shared topology, refine manifold, and run Taubin fairing.
2. **Surface Painting (`TeddyRaycaster.cs`, `TeddySurfacePaintOp.cs` — Sec. 5.2, Fig. 16)**
   - Intersect bounded camera ray-planes by walking `tri.Neighbors`; abort/warn if crossing an occluding silhouette fold.
3. **Extrusion & Cavity (`TeddyExtrudeCutOp.cs` — Sec. 5.3, Figs. 17–19)**
   - Ring normal (Footnote 2, p. 6): $\mathbf{n}_{ring} = \text{normalize}(A_{yz}, A_{zx}, A_{xy})$. Project profile onto plane through $\mathbf{c}_{ring}$ parallel to $\mathbf{n}_{ring}$ maximally facing camera; advance Left/Right pointers maximizing $\angle(\overline{p_L p_R}, \mathbf{t}_{stroke}) \approx 90^\circ$; transport frames without twist (RMF) and sew rings.
4. **Cutting & Biting (`TeddyExtrudeCutOp.cs` — Sec. 5.4, Figs. 8, 9, 20)**
   - Bound the cut strictly to the stroke's intersection span (preserving local **Biting**, Fig. 8) and triangulate cross-section loops with Steiner-refined CDT [22].
5. **Smoothing (`TeddySmoothBendOp.cs` — Sec. 5.5, Figs. 10, 21)**
   - Delete interior triangles inside the ring, build local XY Steiner-CDT, elevate interior vertices via averaged multi-plane tangent-matched Cubic Bezier curves (Fig. 21c), and apply Taubin fairing [24].

---

## 3. Team Ownership Boundaries (Zero Git Conflict Rule)

- **TV1 (Geometry & Inflation):** `TeddyMesh.cs`, `Delaunay2D.cs`, `TeddyInflationEngine.cs`, `TeddyMeshOptimizer.cs`, `TeddyAlgorithmDebugInfo.cs`
- **TV2 (3D Modeling Operations):** `TeddyRaycaster.cs`, `TeddySurfacePaintOp.cs`, `TeddyExtrudeCutOp.cs`, `TeddySmoothBendOp.cs`
- **TV3 (Standalone 3D UI, 2D Sketch Layer, Camera & NPR Render):** `Program.cs`, `TeddySketch2DLayer.cs`, `StrokeProcessor.cs`, `VirtualTrackballCamera.cs`, `TeddyRenderer.cs`, `ObjExporter.cs`, `TeddyStudioDialog.cs`, `UITheme.cs`

---

## 4. Mandatory "Never Forget" Checklist Before Completing Any Code Task

1. **Respect User No-Edit Directives:** When the user asks for planning/design only, never touch `.cs`/`.csproj` files until asked to implement.
2. **Manifold & Normal Integrity:** Every closed solid mutation must maintain outward CCW winding and call `CompactMeshVertices` (when removing tris) $\to$ `BuildConnectivity()` $\to$ `RecalculateNormals()`.
3. **Undo / Redo Safety:** Push undo state before both 2D sketch mutations and 3D mesh executions.
4. **Design System Compliance (`design-system/paintlike/MASTER.md`):** Keep **Bright Studio Light** styling—use Navy-800 `#1E3A5F` (`RGB(30, 58, 95)`) for primary text and Pen-and-Ink silhouettes, never pure `#000000` black on UI toolbars.
5. **Build Verification (when editing code):**
   ```powershell
   powershell -ExecutionPolicy Bypass -File ".agents/skills/teddy-3d-algorithm/scripts/verify-teddy-build.ps1"
   ```
