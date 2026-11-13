using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// Phân loại ý định cử chỉ trong chế độ PAINTING theo đúng Sơ đồ Hình 4 (trang 3 bài báo Teddy).
    /// </summary>
    public enum PaintingStrokeIntent
    {
        None,
        PaintOnSurface,         // Nằm trên bề mặt vật thể, không khép kín -> Vẽ nét màu 3D
        SelectExtrusionRing,    // Nằm trên bề mặt vật thể, khép kín -> Tạo vòng đỏ Base Ring (vào Extrusion Mode)
        CutObject,              // Bắt đầu và kết thúc bên ngoài bóng vật thể, cắt ngang qua vật thể -> Cắt khối
        ErasePaintedStrokes     // Nét gạch xóa zíc-zắc (Scribbling: sl/pl > 1.5) -> Xóa nét vẽ bề mặt
    }

    /// <summary>
    /// [TV2-02] Xử lý phân loại cử chỉ trên bề mặt mô hình 3D, vẽ nét 3D (Surface Painting - Mục 4.3 & 5.2)
    /// và xóa nét vẽ bằng cử chỉ Scribbling.
    /// </summary>
    public static class TeddySurfacePaintOp
    {
        /// <summary>
        /// Phân tích nét vẽ của người dùng khi đang ở chế độ PAINTING để chọn đúng thao tác theo Hình 4 bài báo.
        /// </summary>
        public static PaintingStrokeIntent AnalyzeStrokeIntent(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out List<Vector3> projectedSurfacePoints,
            out float scribbleRatio)
        {
            projectedSurfacePoints = new List<Vector3>();
            scribbleRatio = 0f;

            if (mesh == null || mesh.IsEmpty || rawStroke == null || rawStroke.Count < 2)
                return PaintingStrokeIntent.None;

            float totalLen = StrokeProcessor.ComputePolylineLength(rawStroke, false);
            if (totalLen < 12f)
                return PaintingStrokeIntent.None;

            // 1. Kiểm tra cử chỉ gạch xóa (Scribbling: sl / pl > 1.5 - Chú thích 1 trang 4)
            if (StrokeProcessor.IsScribbling(rawStroke, out scribbleRatio))
            {
                return PaintingStrokeIntent.ErasePaintedStrokes;
            }

            // 2. Lấy mẫu nét vẽ và bắn tia kiểm tra điểm đầu, điểm cuối và các điểm giữa có chạm vật thể không
            List<PointF> resampled = StrokeProcessor.ResampleUniform(rawStroke, 8f, false);
            if (resampled.Count < 3)
                return PaintingStrokeIntent.None;

            bool startHits = TeddyRaycaster.RaycastMesh(mesh, resampled[0], camera, viewport, out _, out _, out _);
            bool endHits = TeddyRaycaster.RaycastMesh(mesh, resampled[resampled.Count - 1], camera, viewport, out _, out _, out _);

            int hitCount = 0;
            for (int i = 0; i < resampled.Count; i++)
            {
                if (TeddyRaycaster.RaycastMesh(mesh, resampled[i], camera, viewport, out _, out Vector3 hp, out Vector3 hn))
                {
                    projectedSurfacePoints.Add(hp + hn * 1.2f);
                    hitCount++;
                }
            }

            // 3. Nếu điểm đầu VÀ điểm cuối đều nằm ngoài bóng vật thể, nhưng phần giữa cắt ngang qua vật thể -> CUTTING (Mục 4.5)
            if (!startHits && !endHits && hitCount >= 2)
            {
                return PaintingStrokeIntent.CutObject;
            }

            // 4. Nếu nét vẽ nằm trên bề mặt vật thể
            if (projectedSurfacePoints.Count >= 3)
            {
                if (StrokeProcessor.IsClosedSurfaceStroke(rawStroke) && projectedSurfacePoints.Count >= 6)
                {
                    return PaintingStrokeIntent.SelectExtrusionRing;
                }
                return PaintingStrokeIntent.PaintOnSurface;
            }

            return PaintingStrokeIntent.None;
        }

        /// <summary>
        /// Thực hiện thêm nét vẽ 3D bám trên bề mặt mô hình (Mục 4.3 & 5.2 - Hình 16).
        /// </summary>
        public static void AddSurfaceLine(TeddyMesh mesh, List<Vector3> surfacePoints, Color color, float width = 2.8f)
        {
            if (mesh == null || surfacePoints == null || surfacePoints.Count < 2)
                return;

            mesh.SurfaceLines.Add(new TeddySurfaceLine
            {
                Points = new List<Vector3>(surfacePoints),
                StrokeColor = color,
                StrokeWidth = width,
                IsClosedRing = false
            });
        }

        /// <summary>
        /// Tạo vòng chọn màu đỏ (Base Ring) trên bề mặt để bắt đầu chế độ Extrusion Mode (Mục 4.4 - Hình 3i).
        /// </summary>
        public static Vector3 SetExtrusionBaseRing(TeddyMesh mesh, List<Vector3> ringPoints)
        {
            mesh.ActiveBaseRing = new TeddySurfaceLine
            {
                Points = new List<Vector3>(ringPoints),
                StrokeColor = Color.FromArgb(239, 68, 68),
                StrokeWidth = 3.6f,
                IsClosedRing = true
            };

            return TeddyRaycaster.ComputeRingNormal(ringPoints);
        }
    }
}
