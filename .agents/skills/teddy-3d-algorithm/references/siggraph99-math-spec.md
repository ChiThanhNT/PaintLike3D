# Teddy (ACM SIGGRAPH '99) — Complete Mathematical & Algorithmic Specification

Reference distilled from:
> Takeo Igarashi, Satoshi Matsuoka, Hidehiko Tanaka, *"Teddy: A Sketching Interface for 3D Freeform Design"*, ACM SIGGRAPH '99, pp. 409–416, 1999.

---

## 1. Input Preprocessing & Gesture Recognition (Sections 4 & 5)

### 1.1 Uniform Polyline Resampling
- Raw mouse/tablet points contain jitter and uneven speed. Every freeform stroke is resampled to a polyline with **predefined uniform edge length** $L_{edge}$ before geometric construction (Sec. 5, citing Cohen et al. [4]).
- In Creation mode, start and end points are connected automatically to form a closed planar polygon.
- **Self-Intersection Guard:** Test all non-adjacent edge pairs $(e_i, e_j)$ for proper 2D segment intersection. If self-intersecting, reject the stroke and prompt the user for an alternative stroke (Sec. 4.2 & 5.1).

### 1.2 Scribbling Detection (Footnote 1, p. 4)
Let $s_l$ be total arc length of the stroke and $p_l$ be the perimeter of its 2D Convex Hull:
$$\text{IsScribble} \iff \frac{s_l}{p_l} > 1.5$$
- In `PAINTING` mode $\to$ **Erases** painted surface lines covered by the scribble.
- In `EXTRUSION` mode $\to$ **Smoothes** the 3D surface inside the active red ring (Sec. 4.6 & 5.5).

---

## 2. Creating a New 3D Object — Chordal Axis Inflation (Section 5.1, Figs. 13–15)

### Step 2.1: Constrained Delaunay Triangulation & Triangle Classification (Fig. 13a–b)
Given uniform closed CCW boundary polygon $P = \{v_0, v_1, \dots, v_{n-1}\}$:
- Edges of $P$ are **external edges**; diagonals added inside $P$ by CDT are **internal edges**.
- Classify every CDT triangle $T$ by how many external edges it has:
  - **Terminal triangle (`T`)**: 2 external edges, 1 internal edge.
  - **Sleeve triangle (`S`)**: 1 external edge, 2 internal edges.
  - **Junction triangle (`J`)**: 0 external edges, 3 internal edges.

### Step 2.2: Oriented Semicircle Branch Pruning (Fig. 14, Prasad [21])
 Unpruned chordal axes suffer from noisy twigs caused by small boundary bumps (Fig. 13c). For each unvisited **Terminal triangle** $X$:
1. Let $e = (u, v)$ be the current single interior edge of merged region $X$, with midpoint $\mathbf{m} = \frac{1}{2}(\mathbf{p}_u + \mathbf{p}_v)$ and radius $R = \frac{1}{2}\|\mathbf{p}_v - \mathbf{p}_u\|$.
2. Erect the **semicircle** on diameter $e$ lying on **the same side of $e$ as region $X$**:
   - Every boundary vertex $\mathbf{q}$ of $X$ must satisfy $\|\mathbf{q} - \mathbf{m}\| \le R$ AND lie in the same half-plane of line $(u, v)$ as $X$.
3. If all vertices of $X$ lie on or inside this semicircle:
   - Remove interior edge $e$ and merge $X$ with adjacent triangle $Y$ across $e$:
     - If $Y$ is a **Sleeve (`S`)** triangle, $X \cup Y$ now has 3+ exterior edges and 1 new interior edge $e'$; repeat step 2 on $e'$ (Fig. 14a–b).
     - If $Y$ is a **Junction (`J`)** triangle, **stop** advancing and triangulate $X \cup Y$ as a **fan** radiating from the centroid/midpoint of the `J` triangle (Fig. 14e–f).
4. If any vertex of $X$ falls **outside** the semicircle (Fig. 14c):
   - **Stop** before merging $Y$, and triangulate $X$ as a **fan** of triangles radiating from the midpoint $\mathbf{m}$ of interior edge $e$ to all exterior edges of $X$ (Fig. 14d).

### Step 2.3: Pruned Spine & Base 2D Mesh Subdivision (Fig. 13e–f)
- Connect midpoints of remaining internal edges of unpruned `S` triangles and centroids of `J` triangles to obtain the **pruned Spine** (Fig. 13e).
- Subdivide remaining `S` and `J` triangles along the spine segments and triangulate resulting quads (choosing well-shaped diagonals) to produce a complete 2D mesh spanning between Spine vertices and Boundary vertices (Fig. 13f).

### Step 2.4: Spine Elevation & Symmetric Quarter-Oval Sewing (Fig. 15a–d)
1. **Spine Z-Elevation (Fig. 15b):** For each spine vertex $s$, let $B(s)$ be the set of external (boundary) vertices directly connected to $s$ in the subdivided 2D mesh:
   $$H(s) = \frac{1}{|B(s)|} \sum_{b \in B(s)} \|\mathbf{p}_s - \mathbf{p}_b\|$$
   *(Wide regions get fat; narrow regions become thin.)*
2. **Quarter-Oval Rib Conversion (Fig. 15c):** Every non-spine internal edge connecting spine vertex $(\mathbf{p}_s, H(s))$ to boundary vertex $(\mathbf{p}_b, 0)$ becomes a quarter-oval discretized by angle $\theta_k = \frac{\pi k}{2K}$ ($k = 0 \dots K$):
   $$\mathbf{xy}_k = (1 - \sin\theta_k)\mathbf{p}_s + \sin\theta_k\,\mathbf{p}_b, \qquad z_k = H(s)\cos\theta_k$$
3. **Sewing & Symmetry (Fig. 15d):** Sew neighboring elevated ribs into triangles for $+Z$, mirror to $-Z$ sharing the $z=0$ silhouette boundary vertices to form a closed spherical-topology mesh, and apply Hoppe mesh refinement [12] to collapse tiny/degenerate edges.

---

## 3. Painting on the Surface (Section 5.2, Fig. 16)

- For each 2D segment $\overline{a b}$ of the stroke on screen, form the bounded plane $\Pi$ of all camera rays passing through $\overline{a b}$.
- Intersect $\Pi$ with front-facing mesh triangles by walking triangle-to-triangle connectivity (`Neighbors`) rather than brute-force disconnected point sampling.
- If a camera ray hits multiple layers of the mesh, keep only the **nearest** polygon to the camera.
- **Fold Failure Condition:** If consecutive 3D segments cannot be topologically spliced (e.g., the stroke crosses an occluding silhouette "fold" jumping between non-adjacent surface sheets), **abort the stroke**.

---

## 4. Extrusion & Digging Cavities (Section 5.3, Figs. 6, 7, 17–19)

### 4.1 Ring Normal via Projected Signed Areas (Footnote 2, p. 6)
Given closed 3D base ring $\{\mathbf{p}_0, \dots, \mathbf{p}_{n-1}\}$ (indices mod $n$):
$$A_{xy} = \frac{1}{2}\sum_{i=0}^{n-1} (x_i y_{i+1} - x_{i+1} y_i), \quad A_{yz} = \frac{1}{2}\sum_{i=0}^{n-1} (y_i z_{i+1} - y_{i+1} z_i), \quad A_{zx} = \frac{1}{2}\sum_{i=0}^{n-1} (z_i x_{i+1} - x_{i+1} z_i)$$
$$\mathbf{n}_{ring} = \text{normalize}(A_{yz}, A_{zx}, A_{xy})$$
Orient $\mathbf{n}_{ring}$ outward to match underlying mesh face normals.

### 4.2 Projection Plane Selection (Fig. 17a)
Find plane $\Pi_{ext}$ passing through ring centroid $\mathbf{c}_{ring}$ and parallel to $\mathbf{n}_{ring}$ ($\mathbf{n}_{\Pi} \perp \mathbf{n}_{ring}$) facing the camera direction $\mathbf{v}_{cam}$ as much as possible:
$$\mathbf{b} = \text{normalize}(\mathbf{n}_{ring} \times \mathbf{v}_{cam}), \qquad \mathbf{n}_{\Pi} = \text{normalize}(\mathbf{b} \times \mathbf{n}_{ring})$$
Project the 2D extruding stroke rays onto $\Pi_{ext}$ to obtain 3D stroke $S = \{\mathbf{s}_0, \dots, \mathbf{s}_{m-1}\}$.

### 4.3 Two-Pointer Sweep Algorithm (Fig. 18a–b)
Initialize pointers $L = 0$ and $R = m - 1$ at opposite ends of $S$. At each step, evaluate three candidate moves: $(L+1, R)$, $(L, R-1)$, or $(L+1, R-1)$:
- **Goodness Metric:** Higher when the angle between chord $\overline{\mathbf{s}_L \mathbf{s}_R}$ and stroke tangents $\mathbf{t}_L, \mathbf{t}_R$ at both pointers is closest to $90^\circ$:
  $$G(L, R) = \big(1 - |\hat{\mathbf{c}}_{LR} \cdot \mathbf{t}_L|\big) + \big(1 - |\hat{\mathbf{c}}_{LR} \cdot \mathbf{t}_R|\big), \qquad \hat{\mathbf{c}}_{LR} = \frac{\mathbf{s}_R - \mathbf{s}_L}{\|\mathbf{s}_R - \mathbf{s}_L\|}$$
- Advance until $L$ and $R$ meet. At each step, place a copy of the Base Ring centered at $\frac{1}{2}(\mathbf{s}_L + \mathbf{s}_R)$, oriented perpendicular to the sweep tangent, scaled by $\|\mathbf{s}_R - \mathbf{s}_L\| / \|\mathbf{s}_{m-1} - \mathbf{s}_0\|$.
- Delete original polygons enclosed by Base Ring and sew ring copies sequentially (Barequet & Sharir [1]). Same algorithm digs a **cavity** when stroke goes inward (Fig. 7a–c).

---

## 5. Cutting & Smoothing (Sections 5.4 & 5.5, Figs. 8–10, 20–21)

### 5.1 Cutting (Sec. 5.4, Fig. 20)
- Project each cutting stroke segment onto both **front-facing and back-facing** polygons; connect corresponding front/back endpoints to form planar section polygons, splice them into closed loops, triangulate via Shewchuk CDT [22], and remove mesh geometry to the **left** of the directed stroke.
- Highlight section boundary red (`EXTRUSION` mode): click finishes cut (Fig. 3q); drawing extrudes smoothly from cut section without root creases (Fig. 9).

### 5.2 Smoothing (Sec. 5.5, Fig. 21a–d)
1. Transform object to local frame where $+Z \parallel \mathbf{n}_{ring}$; delete all polygons inside the closed red ring.
2. Project ring onto local $XY$-plane and generate a quality refined triangulation (CDT + edge splitting/flipping [22], Fig. 21b). *(Fails gracefully if projected ring self-folds.)*
3. **Multi-Plane Bezier Z Elevation (Fig. 21c):** For each interior vertex $\mathbf{v}$ and each ring edge $e$ with midpoint $\mathbf{m}_e$, construct the vertical plane parallel to $Z$ passing through $\mathbf{v}$ and $\mathbf{m}_e$ intersecting the opposite side of the ring at $\mathbf{q}_e$. Interpolate $z_e(\mathbf{v})$ on the 2D Bezier curve smoothly matching boundary heights and surface tangents at $\mathbf{m}_e$ and $\mathbf{q}_e$. Average $z_e(\mathbf{v})$ across all ring edges, then apply Taubin surface fairing [24] (Fig. 21d).
