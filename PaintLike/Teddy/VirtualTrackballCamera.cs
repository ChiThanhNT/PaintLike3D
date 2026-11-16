using System;
using System.Drawing;
using System.Numerics;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV3-02] Điều khiển Camera 3D theo mô hình Virtual Trackball (Hultquist 1990 - Mục 3 bài báo Teddy).
    /// - Chuột phải kéo: Xoay tự do trên mặt cầu ảo bằng Quaternion.
    /// - Con lăn chuột: Thu phóng (Zoom).
    /// - Chuột giữa kéo: Dịch chuyển tâm nhìn (Pan).
    /// </summary>
    public class VirtualTrackballCamera
    {
        public Quaternion Rotation { get; set; } = Quaternion.Identity;
        public float Zoom { get; set; } = 1.0f;
        public Vector2 PanOffset { get; set; } = Vector2.Zero;

        private Point _lastMousePos;
        private bool _isRotating = false;
        private bool _isPanning = false;

        public bool IsRotating => _isRotating;
        public bool IsPanning => _isPanning;
        public bool IsInteracting => _isRotating || _isPanning;

        public void Reset()
        {
            Rotation = Quaternion.Identity;
            Zoom = 1.0f;
            PanOffset = Vector2.Zero;
        }

        public void StartRotate(Point mousePos)
        {
            _isRotating = true;
            _lastMousePos = mousePos;
        }

        public void StartPan(Point mousePos)
        {
            _isPanning = true;
            _lastMousePos = mousePos;
        }

        public void StopInteraction()
        {
            _isRotating = false;
            _isPanning = false;
        }

        public void OnMouseMove(Point currentPos, Size viewportSize)
        {
            if (_isRotating)
            {
                if (currentPos == _lastMousePos) return;

                Vector3 v1 = MapToTrackballSphere(_lastMousePos, viewportSize);
                Vector3 v2 = MapToTrackballSphere(currentPos, viewportSize);

                Vector3 axis = Vector3.Cross(v1, v2);
                float axisLen = axis.Length();

                if (axisLen > 1e-5f)
                {
                    axis /= axisLen;
                    float dot = Math.Clamp(Vector3.Dot(v1, v2), -1.0f, 1.0f);
                    float angle = MathF.Acos(dot) * 1.85f; // Độ nhạy xoay mượt

                    Quaternion deltaRot = Quaternion.CreateFromAxisAngle(axis, angle);
                    Rotation = Quaternion.Normalize(deltaRot * Rotation);
                }

                _lastMousePos = currentPos;
            }
            else if (_isPanning)
            {
                float dx = currentPos.X - _lastMousePos.X;
                float dy = currentPos.Y - _lastMousePos.Y;
                PanOffset += new Vector2(dx, dy);
                _lastMousePos = currentPos;
            }
        }

        public void OnMouseWheel(int delta)
        {
            float factor = delta > 0 ? 1.1f : 0.9f;
            Zoom = Math.Clamp(Zoom * factor, 0.35f, 4.0f);
        }

        /// <summary>
        /// Ánh xạ điểm màn hình 2D lên mặt cầu ảo bán kính R = 0.85 * min(W, H)/2 (Hultquist Trackball).
        /// </summary>
        private static Vector3 MapToTrackballSphere(Point pt, Size viewport)
        {
            float cx = viewport.Width * 0.5f;
            float cy = viewport.Height * 0.5f;
            float radius = MathF.Min(cx, cy) * 0.85f;
            if (radius < 1f) radius = 1f;

            // Trục Y màn hình hướng xuống, trục Y 3D hướng lên
            float x = (pt.X - cx) / radius;
            float y = (cy - pt.Y) / radius;

            float d2 = x * x + y * y;
            float z;
            if (d2 <= 0.5f)
            {
                z = MathF.Sqrt(1.0f - d2);
            }
            else
            {
                // Hyperbolic sheet nối tiếp mặt cầu để không bị giật ở rìa màn hình
                z = 0.5f / MathF.Sqrt(d2);
            }

            return Vector3.Normalize(new Vector3(x, y, z));
        }

        /// <summary>
        /// Biến đổi một đỉnh 3D từ không gian vật thể (Model Space) sang không gian Camera (View Space).
        /// </summary>
        public Vector3 WorldToView(Vector3 worldPos)
        {
            return Vector3.Transform(worldPos, Rotation);
        }

        /// <summary>
        /// Biến đổi một vector pháp tuyến từ không gian vật thể sang không gian Camera.
        /// </summary>
        public Vector3 NormalToView(Vector3 normal)
        {
            return Vector3.Normalize(Vector3.Transform(normal, Rotation));
        }

        /// <summary>
        /// Chiếu một điểm 3D trong Model Space lên tọa độ màn hình 2D (Screen Space) kèm giá trị độ sâu Z.
        /// </summary>
        public Vector3 ProjectToScreen(Vector3 worldPos, Size viewport)
        {
            Vector3 viewPos = WorldToView(worldPos);
            float cx = viewport.Width * 0.5f + PanOffset.X;
            float cy = viewport.Height * 0.5f + PanOffset.Y;

            // Phép chiếu trực giao có tỉ lệ Zoom (giúp nét vẽ 2D khớp chính xác 1:1 với biên 3D khi tạo vật thể)
            float sx = cx + viewPos.X * Zoom;
            float sy = cy - viewPos.Y * Zoom;

            return new Vector3(sx, sy, viewPos.Z);
        }

        /// <summary>
        /// Chuyển đổi một điểm màn hình 2D thành điểm trên mặt phẳng đi qua tâm vật thể (Z_view = depth).
        /// Dùng khi khởi tạo vật thể 3D từ nét vẽ 2D hoặc chiếu nét đùn.
        /// </summary>
        public Vector3 ScreenToWorldPoint(PointF screenPt, Size viewport, float viewDepth = 0f)
        {
            float cx = viewport.Width * 0.5f + PanOffset.X;
            float cy = viewport.Height * 0.5f + PanOffset.Y;

            float vx = (screenPt.X - cx) / Zoom;
            float vy = (cy - screenPt.Y) / Zoom;
            Vector3 viewPt = new Vector3(vx, vy, viewDepth);

            Quaternion invRot = Quaternion.Conjugate(Rotation);
            return Vector3.Transform(viewPt, invRot);
        }

        /// <summary>
        /// Tạo tia chiếu (RayOrigin, RayDirection) trong không gian Model Space từ tọa độ chuột trên màn hình.
        /// Dùng cho thuật toán Raycasting vẽ trên bề mặt (Mục 5.2) và Cắt khối (Mục 5.4).
        /// </summary>
        public (Vector3 Origin, Vector3 Direction) ScreenPointToRay(PointF screenPt, Size viewport)
        {
            Vector3 nearWorld = ScreenToWorldPoint(screenPt, viewport, 1500f);
            Vector3 farWorld = ScreenToWorldPoint(screenPt, viewport, -1500f);
            Vector3 dir = Vector3.Normalize(farWorld - nearWorld);
            return (nearWorld, dir);
        }

        /// <summary>
        /// Hướng nhìn của Camera trong không gian vật thể (từ vật thể hướng ra mắt người nhìn: +Z trong View Space).
        /// </summary>
        public Vector3 GetCameraForwardInWorld()
        {
            Quaternion invRot = Quaternion.Conjugate(Rotation);
            return Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, invRot));
        }
    }
}
