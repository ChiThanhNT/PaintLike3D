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

            // 1. Lấy mẫu nét vẽ và bắn tia kiểm tra điểm đầu, điểm cuối và các điểm giữa có chạm vật thể không
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

            // 2. Nếu điểm đầu VÀ điểm cuối đều nằm ngoài bóng vật thể, nhưng phần giữa cắt ngang qua vật thể -> CUTTING (Mục 4.5)
            // Kiểm tra trước IsScribbling để đường cắt tự do hình Zíc-zắc / Lượn sóng xuyên qua vật thể luôn được nhận diện là Cắt!
            float chordSpan = StrokeProcessor.Distance(rawStroke[0], rawStroke[rawStroke.Count - 1]);
            if (!startHits && !endHits && hitCount >= 2 && chordSpan >= 32f)
            {
                return PaintingStrokeIntent.CutObject;
            }

            // 3. Kiểm tra cử chỉ gạch xóa (Scribbling: sl / pl > 1.5 - Chú thích 1 trang 4)
            if (StrokeProcessor.IsScribbling(rawStroke, out scribbleRatio))
            {
                return PaintingStrokeIntent.ErasePaintedStrokes;
            }

            // 4. Nếu nét vẽ nằm trên bề mặt vật thể -> sử dụng TV2-06 Bounded Ray-Plane Adjacency Splicing + Fold Check (Hình 16)
            if (projectedSurfacePoints.Count >= 3)
            {
                bool isMostlyOnSurface = hitCount >= (int)(resampled.Count * 0.75f);
                bool wantClosedRing = StrokeProcessor.IsClosedSurfaceStroke(rawStroke) && isMostlyOnSurface && projectedSurfacePoints.Count >= 6;
                if (isMostlyOnSurface)
                {
                    var projStatus = TeddyRaycaster.TryProjectStrokeWithRayPlaneSplicing(
                        mesh,
                        rawStroke,
                        camera,
                        viewport,
                        wantClosedRing,
                        out List<Vector3> splicedPts,
                        out _,
                        out _);

                    if (projStatus == SurfaceProjectionStatus.FoldDetected)
                    {
                        projectedSurfacePoints.Clear();
                        return PaintingStrokeIntent.None;
                    }

                    if (projStatus == SurfaceProjectionStatus.Success && splicedPts.Count >= 2)
                    {
                        projectedSurfacePoints = splicedPts;
                    }

                    if (wantClosedRing && projectedSurfacePoints.Count >= 6)
                    {
                        return PaintingStrokeIntent.SelectExtrusionRing;
                    }
                    return PaintingStrokeIntent.PaintOnSurface;
                }
            }

            return PaintingStrokeIntent.None;
        }

        /// <summary>
        /// [TV2-06] Thực thi dán nét 2D lên bề mặt 3D (Ray-Plane Adjacency Splicing + Fold Detection)
        /// hoặc tự động xóa nét mặt trước có kiểm tra độ sâu nếu phát hiện cử chỉ gạch xóa (Scribble: sl/pl > 1.5).
        /// </summary>
        public static bool TryPaintOrDepthEraseOnSurface(
            TeddyMesh mesh,
            IReadOnlyList<PointF> rawStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            Color paintColor,
            float paintWidth,
            out bool wasEraseOperation,
            out string statusMessage)
        {
            wasEraseOperation = false;
            statusMessage = "";

            if (mesh == null || mesh.IsEmpty || rawStroke == null || rawStroke.Count < 2)
            {
                statusMessage = "Không đủ dữ liệu nét 2D để dán lên bề mặt khối 3D.";
                return false;
            }

            // Nếu trên khối đã có nét vẽ bề mặt và người dùng vẽ nét gạch xóa zíc-zắc (Scribble: sl/pl > 1.5) -> Xóa nét mặt trước có Depth Check!
            if (mesh.SurfaceLines.Count > 0 && StrokeProcessor.IsScribbling(rawStroke, out float scribbleRatio))
            {
                int erased = TeddyRaycaster.EraseSurfaceLines(
                    mesh,
                    rawStroke,
                    camera,
                    viewport,
                    eraseRadiusPx: 22f,
                    checkFrontVisibilityDepth: true);

                if (erased > 0)
                {
                    wasEraseOperation = true;
                    statusMessage = $"Đã xóa {erased} nét vẽ bề mặt ở mặt trước (Scribble sl/pl = {scribbleRatio:F2}, giữ nguyên nét mặt sau khuất - TV2-06).";
                    return true;
                }
            }

            // Chiếu dán nét 3D bám sát cạnh tam giác kề nhau + kiểm tra nếp gấp khuất (Fold Check - Hình 16)
            var status = TeddyRaycaster.TryProjectStrokeWithRayPlaneSplicing(
                mesh,
                rawStroke,
                camera,
                viewport,
                closeLoop: false,
                out List<Vector3> splicedPts,
                out int crossedEdges,
                out string diag);

            if (status == SurfaceProjectionStatus.FoldDetected)
            {
                statusMessage = diag;
                return false;
            }

            if (status != SurfaceProjectionStatus.Success || splicedPts.Count < 2)
            {
                statusMessage = string.IsNullOrEmpty(diag)
                    ? "Nét vẽ 2D chưa nằm trên bề mặt khối 3D! Hãy xoay khối 3D bằng Chuột Phải hoặc vẽ lại nét 2D lên mặt khối."
                    : diag;
                return false;
            }

            AddSurfaceLine(mesh, splicedPts, paintColor, paintWidth);
            statusMessage = $"Đã dán nét 3D bám sát bề mặt qua {crossedEdges} cạnh tam giác ({splicedPts.Count} điểm - TV2-06, Hình 16).";
            return true;
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
