using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV2-01 & TV2-02] Hệ thống chiếu tia (Raycasting), vẽ nét trên bề mặt 3D (Surface Painting - Mục 5.2),
    /// xóa nét vẽ bề mặt (Erasing - Mục 4.3) và tính pháp tuyến vòng đùn (Base Ring Normal - Mục 5.3).
    /// </summary>
    public static class TeddyRaycaster
    {
        /// <summary>
        /// Thuật toán Möller–Trumbore kiểm tra giao điểm tia (Origin, Dir) với tam giác (V0, V1, V2).
        /// </summary>
        public static bool RayIntersectsTriangle(
            Vector3 rayOrigin,
            Vector3 rayDir,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float t,
            out Vector3 hitPoint)
        {
            t = 0f;
            hitPoint = Vector3.Zero;

            const float EPSILON = 1e-6f;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(rayDir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -EPSILON && a < EPSILON)
                return false; // Tia song song mặt phẳng tam giác

            float f = 1.0f / a;
            Vector3 s = rayOrigin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(rayDir, q);
            if (v < 0.0f || u + v > 1.0f)
                return false;

            t = f * Vector3.Dot(edge2, q);
            if (t > EPSILON)
            {
                hitPoint = rayOrigin + rayDir * t;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Bắn một tia từ tọa độ màn hình vào TeddyMesh, trả về điểm giao trên mặt tam giác gần Camera nhất.
        /// </summary>
        public static bool RaycastMesh(
            TeddyMesh mesh,
            PointF screenPt,
            VirtualTrackballCamera camera,
            Size viewport,
            out int hitTriIndex,
            out Vector3 hitWorldPoint,
            out Vector3 hitNormal)
        {
            hitTriIndex = -1;
            hitWorldPoint = Vector3.Zero;
            hitNormal = Vector3.UnitZ;

            if (mesh == null || mesh.IsEmpty) return false;

            var (rayOrigin, rayDir) = camera.ScreenPointToRay(screenPt, viewport);
            Vector3 camForward = camera.GetCameraForwardInWorld();

            float closestT = float.MaxValue;
            bool found = false;

            for (int i = 0; i < mesh.Triangles.Count; i++)
            {
                var tri = mesh.Triangles[i];
                // Chỉ xét các mặt quay về phía Camera (Front-facing polygons - Mục 5.2)
                if (Vector3.Dot(tri.FaceNormal, camForward) < -0.05f)
                    continue;

                Vector3 p0 = mesh.Vertices[tri.V0];
                Vector3 p1 = mesh.Vertices[tri.V1];
                Vector3 p2 = mesh.Vertices[tri.V2];

                if (RayIntersectsTriangle(rayOrigin, rayDir, p0, p1, p2, out float t, out Vector3 hp))
                {
                    if (t < closestT)
                    {
                        closestT = t;
                        hitTriIndex = i;
                        hitWorldPoint = hp;
                        hitNormal = tri.FaceNormal;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// [Mục 5.2 - Hình 16] Chiếu một nét vẽ 2D trên màn hình lên bề mặt 3D của mô hình để tạo SurfaceLine.
        /// Trả về danh sách các điểm 3D nằm trên bề mặt và cờ cho biết toàn bộ nét vẽ có nằm trong bóng vật thể không.
        /// </summary>
        public static List<Vector3> ProjectStrokeToSurface(
            TeddyMesh mesh,
            IReadOnlyList<PointF> screenStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            out int hitCount)
        {
            hitCount = 0;
            var surfacePoints = new List<Vector3>();
            if (mesh == null || mesh.IsEmpty || screenStroke.Count == 0)
                return surfacePoints;

            for (int i = 0; i < screenStroke.Count; i++)
            {
                if (RaycastMesh(mesh, screenStroke[i], camera, viewport, out _, out Vector3 hitPt, out Vector3 hitNormal))
                {
                    // Nâng nhẹ điểm vẽ lên theo pháp tuyến 1.2 đơn vị để nét vẽ nổi rõ trên mặt lưới
                    surfacePoints.Add(hitPt + hitNormal * 1.2f);
                    hitCount++;
                }
            }

            return surfacePoints;
        }

        /// <summary>
        /// [Mục 4.3 - Hình 3u-w] Xóa các nét vẽ bề mặt (SurfaceLines) bị gạch đè bởi cử chỉ Scribbling.
        /// </summary>
        public static int EraseSurfaceLines(
            TeddyMesh mesh,
            IReadOnlyList<PointF> scribbleStroke,
            VirtualTrackballCamera camera,
            Size viewport,
            float eraseRadiusPx = 22f)
        {
            if (mesh == null || mesh.SurfaceLines.Count == 0 || scribbleStroke.Count == 0)
                return 0;

            float r2 = eraseRadiusPx * eraseRadiusPx;
            int removedCount = 0;

            for (int i = mesh.SurfaceLines.Count - 1; i >= 0; i--)
            {
                var line = mesh.SurfaceLines[i];
                bool hit = false;

                foreach (Vector3 pt3D in line.Points)
                {
                    // Chỉ xóa nét nằm ở mặt nhìn thấy phía trước
                    Vector3 screenProj = camera.ProjectToScreen(pt3D, viewport);
                    for (int j = 0; j < scribbleStroke.Count; j++)
                    {
                        float dx = screenProj.X - scribbleStroke[j].X;
                        float dy = screenProj.Y - scribbleStroke[j].Y;
                        if (dx * dx + dy * dy <= r2)
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (hit) break;
                }

                if (hit)
                {
                    mesh.SurfaceLines.RemoveAt(i);
                    removedCount++;
                }
            }

            return removedCount;
        }

        /// <summary>
        /// [Mục 5.3 - Chú thích 2 trang 6] Tính vector pháp tuyến của vòng khép kín 3D (Base Ring)
        /// bằng công thức diện tích có hướng (Signed Area) chiếu lên 3 mặt phẳng YZ, ZX, XY:
        /// v = Normalize(Ayz, Azx, Axy).
        /// </summary>
        public static Vector3 ComputeRingNormal(IReadOnlyList<Vector3> ring)
        {
            if (ring == null || ring.Count < 3)
                return Vector3.UnitZ;

            float axy = 0f;
            float ayz = 0f;
            float azx = 0f;
            int n = ring.Count;

            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = ring[i];
                Vector3 p1 = ring[(i + 1) % n];

                axy += (p0.X * p1.Y - p1.X * p0.Y);
                ayz += (p0.Y * p1.Z - p1.Y * p0.Z);
                azx += (p0.Z * p1.X - p1.Z * p0.X);
            }

            Vector3 v = new Vector3(0.5f * ayz, 0.5f * azx, 0.5f * axy);
            float len = v.Length();
            return len > 1e-6f ? v / len : Vector3.UnitZ;
        }
    }
}
