using System;
using System.Collections.Generic;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Các bước trực quan hóa thuật toán bài báo Teddy (Hình 13, 14, 15 - Mục 5.1).
    /// Giúp thuyết trình và kiểm thử trực quan từng bước biến đổi từ nét vẽ 2D sang lưới 3D.
    /// </summary>
    public enum TeddyAlgorithmStepView
    {
        Final3DModel = 0,           // Mô hình 3D hoàn chỉnh (Pen & Ink / Shaded / Wireframe)
        Step1_InitialPolygon2D = 1, // Hình 13a: Đa giác phẳng 2D đã chuẩn hóa độ dài cạnh đều nhau
        Step2_CDT_T_S_J = 2,        // Hình 13b: Lưới Constrained Delaunay & Phân loại tam giác T / S / J
        Step3_RawChordalAxis = 3,   // Hình 13c: Trục Chordal Axis thô trước khi cắt tỉa nhánh phụ
        Step4_PruningAndSpine = 4,  // Hình 13d-f & 14: Cắt tỉa nửa đường tròn, tạo quạt Fan & Spine chính
        Step5_QuarterOvalRibs3D = 5 // Hình 15a-c: Nâng cao độ Z cho Spine & các cung sườn Quarter-Oval 3D
    }

    /// <summary>
    /// Lưu trữ dữ liệu trung gian của từng bước trong thuật toán Inflation (Mục 5.1 - Hình 13, 14, 15).
    /// </summary>
    public class TeddyAlgorithmDebugInfo
    {
        public Quaternion CreationViewRotation { get; set; } = Quaternion.Identity;

        // Hình 13a: Đa giác 2D ban đầu
        public List<Vector2> BoundaryPolygon2D { get; set; } = new List<Vector2>();

        // Hình 13b: Các tam giác CDT kèm nhãn Terminal (T), Sleeve (S), Junction (J)
        public List<Tri2D> CdtTriangles { get; set; } = new List<Tri2D>();

        // Hình 13c: Trục Chordal Axis thô (nối trung điểm mọi cạnh trong trước khi cắt tỉa)
        public List<(Vector2 A, Vector2 B)> RawChordalAxisEdges { get; set; } = new List<(Vector2 A, Vector2 B)>();

        // Hình 14: Các vòng tròn đường kính cạnh trong được kiểm tra khi cắt tỉa (Pruning Semicircles)
        public List<(Vector2 Center, float Radius, Vector2 EdgeA, Vector2 EdgeB)> PruningCircles { get; set; }
            = new List<(Vector2 Center, float Radius, Vector2 EdgeA, Vector2 EdgeB)>();

        // Hình 13e: Trục xương sống (Pruned Spine) sau khi đã cắt tỉa nhánh phụ
        public List<(Vector2 A, Vector2 B)> PrunedSpineEdges2D { get; set; } = new List<(Vector2 A, Vector2 B)>();
        public List<Vector2> PrunedSpineNodes2D { get; set; } = new List<Vector2>();

        // Hình 13f: Lưới tam giác 2D đã chia nhỏ giữa Spine và đường biên
        public List<(Vector2 A, Vector2 B, Vector2 C)> SubdividedMesh2D { get; set; }
            = new List<(Vector2 A, Vector2 B, Vector2 C)>();

        // Hình 15b-c: Các cung 1/4 elip (Quarter-Oval 3D Ribs) nối từ đỉnh Spine đã nâng Z ra đỉnh biên
        public List<Vector3[]> QuarterOvalRibs3D { get; set; } = new List<Vector3[]>();
        public List<(Vector3 A, Vector3 B)> ElevatedSpineTop3D { get; set; } = new List<(Vector3 A, Vector3 B)>();
        public List<(Vector3 A, Vector3 B)> ElevatedSpineBottom3D { get; set; } = new List<(Vector3 A, Vector3 B)>();

        public TeddyAlgorithmDebugInfo Clone()
        {
            var copy = new TeddyAlgorithmDebugInfo
            {
                CreationViewRotation = this.CreationViewRotation,
                BoundaryPolygon2D = new List<Vector2>(this.BoundaryPolygon2D),
                RawChordalAxisEdges = new List<(Vector2 A, Vector2 B)>(this.RawChordalAxisEdges),
                PruningCircles = new List<(Vector2 Center, float Radius, Vector2 EdgeA, Vector2 EdgeB)>(this.PruningCircles),
                PrunedSpineEdges2D = new List<(Vector2 A, Vector2 B)>(this.PrunedSpineEdges2D),
                PrunedSpineNodes2D = new List<Vector2>(this.PrunedSpineNodes2D),
                SubdividedMesh2D = new List<(Vector2 A, Vector2 B, Vector2 C)>(this.SubdividedMesh2D),
                QuarterOvalRibs3D = new List<Vector3[]>(this.QuarterOvalRibs3D),
                ElevatedSpineTop3D = new List<(Vector3 A, Vector3 B)>(this.ElevatedSpineTop3D),
                ElevatedSpineBottom3D = new List<(Vector3 A, Vector3 B)>(this.ElevatedSpineBottom3D)
            };

            foreach (var t in this.CdtTriangles)
            {
                var tc = new Tri2D(t.V0, t.V1, t.V2) { Category = t.Category };
                Array.Copy(t.Neighbors, tc.Neighbors, 3);
                copy.CdtTriangles.Add(tc);
            }

            return copy;
        }
    }
}
