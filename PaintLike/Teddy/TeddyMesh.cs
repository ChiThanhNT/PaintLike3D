using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Phân loại tam giác trong thuật toán Chordal Axis (Mục 5.1 - Hình 13b):
    /// - Terminal (T): Có 2 cạnh thuộc biên ngoài (External), 1 cạnh trong (Internal).
    /// - Sleeve (S): Có 1 cạnh thuộc biên ngoài, 2 cạnh trong.
    /// - Junction (J): Không có cạnh nào thuộc biên ngoài (cả 3 đều là cạnh trong).
    /// </summary>
    public enum TriangleCategory
    {
        Junction = 0,
        Sleeve = 1,
        Terminal = 2
    }

    /// <summary>
    /// Mặt tam giác 3D lưu chỉ số 3 đỉnh, pháp tuyến mặt và thông tin tam giác kề.
    /// </summary>
    public class TeddyTriangle
    {
        public int V0;
        public int V1;
        public int V2;
        public Vector3 FaceNormal;
        public TriangleCategory Category;
        public Color? CustomColor = null;

        /// <summary>
        /// Chỉ số 3 tam giác kề qua cạnh (V0-V1), (V1-V2), (V2-V0). Giá trị -1 nếu là cạnh biên hở.
        /// </summary>
        public int[] Neighbors = new int[] { -1, -1, -1 };

        public TeddyTriangle(int v0, int v1, int v2, TriangleCategory category = TriangleCategory.Junction, Color? customColor = null)
        {
            V0 = v0;
            V1 = v1;
            V2 = v2;
            Category = category;
            CustomColor = customColor;
            FaceNormal = Vector3.UnitZ;
        }

        public TeddyTriangle Clone()
        {
            var copy = new TeddyTriangle(V0, V1, V2, Category, CustomColor)
            {
                FaceNormal = this.FaceNormal
            };
            Array.Copy(this.Neighbors, copy.Neighbors, 3);
            return copy;
        }
    }

    /// <summary>
    /// Cạnh của lưới đa giác, dùng để tra cứu kề (Connectivity) và phát hiện đường viền Silhouette (Pen-and-Ink NPR).
    /// </summary>
    public struct TeddyEdge
    {
        public int A;
        public int B;
        public int TriLeft;
        public int TriRight;

        public TeddyEdge(int a, int b, int triLeft, int triRight = -1)
        {
            if (a < b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
            TriLeft = triLeft;
            TriRight = triRight;
        }
    }

    /// <summary>
    /// Nét vẽ 3D bám trên bề mặt mô hình (Mục 4.3 & 5.2 - Surface Lines) hoặc vòng cơ sở đùn khối (Base Ring).
    /// </summary>
    public class TeddySurfaceLine
    {
        public List<Vector3> Points { get; set; } = new List<Vector3>();
        public Color StrokeColor { get; set; } = Color.FromArgb(30, 41, 59);
        public float StrokeWidth { get; set; } = 2.6f;
        public bool IsClosedRing { get; set; } = false;

        public TeddySurfaceLine Clone()
        {
            return new TeddySurfaceLine
            {
                Points = new List<Vector3>(this.Points),
                StrokeColor = this.StrokeColor,
                StrokeWidth = this.StrokeWidth,
                IsClosedRing = this.IsClosedRing
            };
        }
    }

    /// <summary>
    /// [TV1-01] Cấu trúc dữ liệu Lưới 3D trung tâm (Polygonal Mesh - Mục 5 bài báo Teddy SIGGRAPH '99).
    /// Dùng chung cho cả 3 thành viên: Tạo khối (TV1), Biến đổi 3D (TV2), và Kết xuất/Camera (TV3).
    /// </summary>
    public class TeddyMesh
    {
        public List<Vector3> Vertices { get; private set; } = new List<Vector3>();
        public List<Vector3> VertexNormals { get; private set; } = new List<Vector3>();
        public List<TeddyTriangle> Triangles { get; private set; } = new List<TeddyTriangle>();
        public List<TeddyEdge> Edges { get; private set; } = new List<TeddyEdge>();

        // Dữ liệu trục xương sống (Chordal Axis / Spine) để hiển thị minh họa thuật toán Mục 5.1
        public List<Vector3> SpineVertices { get; private set; } = new List<Vector3>();
        public List<(int A, int B)> SpineEdges { get; private set; } = new List<(int A, int B)>();

        // Các nét vẽ trên bề mặt (Mục 4.3 & 5.2)
        public List<TeddySurfaceLine> SurfaceLines { get; private set; } = new List<TeddySurfaceLine>();

        // Vòng chọn màu đỏ khi vào chế độ Extrusion Mode (Mục 4.4 & 5.3)
        public TeddySurfaceLine? ActiveBaseRing { get; set; } = null;

        // Dữ liệu trực quan hóa từng bước thuật toán Mục 5.1 (Hình 13, 14, 15)
        public TeddyAlgorithmDebugInfo? DebugInfo { get; set; } = null;

        // Màu sắc chủ đạo của mô hình
        public Color BaseColor { get; set; } = Color.FromArgb(244, 162, 97); // Warm clay / plush orange

        public bool IsEmpty => Vertices.Count == 0 || Triangles.Count == 0;

        public void Clear()
        {
            Vertices.Clear();
            VertexNormals.Clear();
            Triangles.Clear();
            Edges.Clear();
            SpineVertices.Clear();
            SpineEdges.Clear();
            SurfaceLines.Clear();
            ActiveBaseRing = null;
            DebugInfo = null;
        }

        public TeddyMesh Clone()
        {
            TeddyMesh copy = new TeddyMesh
            {
                BaseColor = this.BaseColor
            };
            copy.Vertices.AddRange(this.Vertices);
            copy.VertexNormals.AddRange(this.VertexNormals);
            foreach (var tri in this.Triangles)
                copy.Triangles.Add(tri.Clone());
            copy.Edges.AddRange(this.Edges);
            copy.SpineVertices.AddRange(this.SpineVertices);
            copy.SpineEdges.AddRange(this.SpineEdges);
            foreach (var sl in this.SurfaceLines)
                copy.SurfaceLines.Add(sl.Clone());
            if (this.ActiveBaseRing != null)
                copy.ActiveBaseRing = this.ActiveBaseRing.Clone();
            if (this.DebugInfo != null)
                copy.DebugInfo = this.DebugInfo.Clone();
            return copy;
        }

        /// <summary>
        /// Tính toán lại pháp tuyến mặt (FaceNormals) và pháp tuyến đỉnh (VertexNormals) mượt.
        /// </summary>
        public void RecalculateNormals()
        {
            VertexNormals.Clear();
            for (int i = 0; i < Vertices.Count; i++)
            {
                VertexNormals.Add(Vector3.Zero);
            }

            foreach (var tri in Triangles)
            {
                if (tri.V0 < 0 || tri.V0 >= Vertices.Count ||
                    tri.V1 < 0 || tri.V1 >= Vertices.Count ||
                    tri.V2 < 0 || tri.V2 >= Vertices.Count)
                    continue;

                Vector3 p0 = Vertices[tri.V0];
                Vector3 p1 = Vertices[tri.V1];
                Vector3 p2 = Vertices[tri.V2];

                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                float len = cross.Length();
                if (len > 1e-7f)
                {
                    tri.FaceNormal = cross / len;
                }
                else
                {
                    tri.FaceNormal = Vector3.UnitZ;
                }

                // Cộng dồn pháp tuyến có trọng số diện tích cho các đỉnh
                VertexNormals[tri.V0] += cross;
                VertexNormals[tri.V1] += cross;
                VertexNormals[tri.V2] += cross;
            }

            for (int i = 0; i < VertexNormals.Count; i++)
            {
                float len = VertexNormals[i].Length();
                if (len > 1e-7f)
                    VertexNormals[i] /= len;
                else
                    VertexNormals[i] = Vector3.UnitZ;
            }
        }

        /// <summary>
        /// Xây dựng bảng tra cứu cạnh (Edges) và danh sách tam giác kề (Neighbors) cho toàn bộ lưới.
        /// </summary>
        public void BuildConnectivity()
        {
            Edges.Clear();
            var edgeMap = new Dictionary<(int, int), int>();

            for (int tIdx = 0; tIdx < Triangles.Count; tIdx++)
            {
                var tri = Triangles[tIdx];
                tri.Neighbors[0] = -1;
                tri.Neighbors[1] = -1;
                tri.Neighbors[2] = -1;

                int[] verts = new int[] { tri.V0, tri.V1, tri.V2 };
                for (int e = 0; e < 3; e++)
                {
                    int a = verts[e];
                    int b = verts[(e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);

                    if (!edgeMap.TryGetValue(key, out int edgeIndex))
                    {
                        edgeIndex = Edges.Count;
                        Edges.Add(new TeddyEdge(key.Item1, key.Item2, tIdx, -1));
                        edgeMap[key] = edgeIndex;
                    }
                    else
                    {
                        TeddyEdge existing = Edges[edgeIndex];
                        if (existing.TriRight == -1 && existing.TriLeft != tIdx)
                        {
                            existing.TriRight = tIdx;
                            Edges[edgeIndex] = existing;

                            // Cập nhật liên kết kề 2 chiều giữa 2 tam giác
                            int otherTriIdx = existing.TriLeft;
                            Triangles[tIdx].Neighbors[e] = otherTriIdx;

                            var otherTri = Triangles[otherTriIdx];
                            int[] otherVerts = new int[] { otherTri.V0, otherTri.V1, otherTri.V2 };
                            for (int oe = 0; oe < 3; oe++)
                            {
                                int oa = otherVerts[oe];
                                int ob = otherVerts[(oe + 1) % 3];
                                var oKey = oa < ob ? (oa, ob) : (ob, oa);
                                if (oKey == key)
                                {
                                    otherTri.Neighbors[oe] = tIdx;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Làm mịn lưới Taubin / Laplacian (không làm co rút thể tích mô hình - Mục 5.1 & 5.5).
        /// </summary>
        public void SmoothMesh(int iterations = 2, float lambda = 0.42f, float mu = -0.44f, HashSet<int>? lockedVertices = null)
        {
            if (Vertices.Count == 0 || Triangles.Count == 0) return;

            var neighbors = new List<int>[Vertices.Count];
            for (int i = 0; i < Vertices.Count; i++)
                neighbors[i] = new List<int>();

            foreach (var tri in Triangles)
            {
                AddUniqueNeighbor(neighbors[tri.V0], tri.V1);
                AddUniqueNeighbor(neighbors[tri.V0], tri.V2);
                AddUniqueNeighbor(neighbors[tri.V1], tri.V0);
                AddUniqueNeighbor(neighbors[tri.V1], tri.V2);
                AddUniqueNeighbor(neighbors[tri.V2], tri.V0);
                AddUniqueNeighbor(neighbors[tri.V2], tri.V1);
            }

            Vector3[] temp = new Vector3[Vertices.Count];
            for (int iter = 0; iter < iterations; iter++)
            {
                // Bước 1: Co nhẹ (Lambda > 0)
                ApplyLaplacianStep(neighbors, temp, lambda, lockedVertices);
                // Bước 2: Giãn nhẹ (Mu < 0) để giữ nguyên độ phồng của thú bông
                ApplyLaplacianStep(neighbors, temp, mu, lockedVertices);
            }

            RecalculateNormals();
        }

        private void ApplyLaplacianStep(List<int>[] neighbors, Vector3[] temp, float factor, HashSet<int>? lockedVertices)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (lockedVertices != null && lockedVertices.Contains(i))
                {
                    temp[i] = Vertices[i];
                    continue;
                }

                var nList = neighbors[i];
                if (nList.Count == 0)
                {
                    temp[i] = Vertices[i];
                    continue;
                }

                Vector3 avg = Vector3.Zero;
                for (int j = 0; j < nList.Count; j++)
                    avg += Vertices[nList[j]];
                avg /= nList.Count;

                temp[i] = Vertices[i] + factor * (avg - Vertices[i]);
            }

            for (int i = 0; i < Vertices.Count; i++)
                Vertices[i] = temp[i];
        }

        private static void AddUniqueNeighbor(List<int> list, int v)
        {
            if (!list.Contains(v)) list.Add(v);
        }

        // =========================================================================
        // CÁC KHỐI CƠ BẢN DÙNG CHO GIAI ĐOẠN 1 (MỤC 4.2 BÀI BÁO: SPHERE & CUBE)
        // =========================================================================

        /// <summary>
        /// Tạo khối Cầu (Sphere) chuẩn với lưới tam giác đều (Mục 4.2).
        /// </summary>
        public static TeddyMesh CreateSphere(float radius = 140f, int slices = 28, int stacks = 18)
        {
            TeddyMesh mesh = new TeddyMesh
            {
                BaseColor = Color.FromArgb(244, 162, 97)
            };

            // Đỉnh cực Bắc
            mesh.Vertices.Add(new Vector3(0, radius, 0));

            for (int i = 1; i < stacks; i++)
            {
                float phi = MathF.PI * i / stacks;
                float y = radius * MathF.Cos(phi);
                float r = radius * MathF.Sin(phi);

                for (int j = 0; j < slices; j++)
                {
                    float theta = 2f * MathF.PI * j / slices;
                    float x = r * MathF.Cos(theta);
                    float z = r * MathF.Sin(theta);
                    mesh.Vertices.Add(new Vector3(x, y, z));
                }
            }

            // Đỉnh cực Nam
            mesh.Vertices.Add(new Vector3(0, -radius, 0));
            int southPoleIdx = mesh.Vertices.Count - 1;

            // Nắp cực Bắc
            for (int j = 0; j < slices; j++)
            {
                int next = (j + 1) % slices;
                mesh.Triangles.Add(new TeddyTriangle(0, 1 + next, 1 + j));
            }

            // Các dải giữa
            for (int i = 0; i < stacks - 2; i++)
            {
                int rowStart = 1 + i * slices;
                int nextRowStart = 1 + (i + 1) * slices;
                for (int j = 0; j < slices; j++)
                {
                    int next = (j + 1) % slices;
                    int a = rowStart + j;
                    int b = rowStart + next;
                    int c = nextRowStart + next;
                    int d = nextRowStart + j;

                    mesh.Triangles.Add(new TeddyTriangle(a, b, c));
                    mesh.Triangles.Add(new TeddyTriangle(a, c, d));
                }
            }

            // Nắp cực Nam
            int lastRingStart = 1 + (stacks - 2) * slices;
            for (int j = 0; j < slices; j++)
            {
                int next = (j + 1) % slices;
                mesh.Triangles.Add(new TeddyTriangle(southPoleIdx, lastRingStart + j, lastRingStart + next));
            }

            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>
        /// Tạo khối Lập phương chia lưới mịn bo nhẹ (Subdivided Cube - Mục 4.2).
        /// </summary>
        public static TeddyMesh CreateCube(float halfSize = 110f, int subdivisions = 6)
        {
            TeddyMesh mesh = new TeddyMesh
            {
                BaseColor = Color.FromArgb(99, 179, 237)
            };

            var vertMap = new Dictionary<(int, int, int), int>();

            int GetOrAddVertex(int gx, int gy, int gz)
            {
                var key = (gx, gy, gz);
                if (vertMap.TryGetValue(key, out int idx))
                    return idx;

                float fx = -halfSize + (2f * halfSize * gx / subdivisions);
                float fy = -halfSize + (2f * halfSize * gy / subdivisions);
                float fz = -halfSize + (2f * halfSize * gz / subdivisions);

                idx = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vector3(fx, fy, fz));
                vertMap[key] = idx;
                return idx;
            }

            void AddFaceGrid(Func<int, int, (int, int, int)> coordFunc, bool flipWinding)
            {
                for (int u = 0; u < subdivisions; u++)
                {
                    for (int v = 0; v < subdivisions; v++)
                    {
                        var c00 = coordFunc(u, v);
                        var c10 = coordFunc(u + 1, v);
                        var c11 = coordFunc(u + 1, v + 1);
                        var c01 = coordFunc(u, v + 1);

                        int i00 = GetOrAddVertex(c00.Item1, c00.Item2, c00.Item3);
                        int i10 = GetOrAddVertex(c10.Item1, c10.Item2, c10.Item3);
                        int i11 = GetOrAddVertex(c11.Item1, c11.Item2, c11.Item3);
                        int i01 = GetOrAddVertex(c01.Item1, c01.Item2, c01.Item3);

                        if (!flipWinding)
                        {
                            mesh.Triangles.Add(new TeddyTriangle(i00, i10, i11));
                            mesh.Triangles.Add(new TeddyTriangle(i00, i11, i01));
                        }
                        else
                        {
                            mesh.Triangles.Add(new TeddyTriangle(i00, i11, i10));
                            mesh.Triangles.Add(new TeddyTriangle(i00, i01, i11));
                        }
                    }
                }
            }

            int N = subdivisions;
            AddFaceGrid((u, v) => (u, v, N), false); // Mặt trước (+Z)
            AddFaceGrid((u, v) => (u, v, 0), true);  // Mặt sau (-Z)
            AddFaceGrid((u, v) => (N, u, v), false); // Mặt phải (+X)
            AddFaceGrid((u, v) => (0, u, v), true);  // Mặt trái (-X)
            AddFaceGrid((u, v) => (u, N, v), true);  // Mặt trên (+Y)
            AddFaceGrid((u, v) => (u, 0, v), false); // Mặt dưới (-Y)

            mesh.BuildConnectivity();
            mesh.SmoothMesh(1, 0.25f, -0.26f); // Bo nhẹ cạnh để hiển thị Pen-and-Ink đẹp mắt
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
