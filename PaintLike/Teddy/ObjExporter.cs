using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace PaintLike.Teddy
{
    /// <summary>
    /// [TV3-04] Xuất và Nhập mô hình 3D theo định dạng chuẩn Wavefront .OBJ (Mục 6 bài báo Teddy SIGGRAPH '99).
    /// </summary>
    public static class ObjExporter
    {
        public static void SaveToFile(TeddyMesh mesh, string filePath)
        {
            if (mesh == null || mesh.IsEmpty)
                throw new InvalidOperationException("Không có dữ liệu lưới 3D để xuất ra file .OBJ.");

            mesh.RecalculateNormals();
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            sb.AppendLine("# Exported from PaintLike Studio - Teddy 3D Freeform Modeler (ACM SIGGRAPH '99)");
            sb.AppendLine($"# Vertices: {mesh.Vertices.Count}, Faces: {mesh.Triangles.Count}");
            sb.AppendLine("o TeddyModel");

            foreach (Vector3 v in mesh.Vertices)
            {
                sb.AppendLine(string.Format(inv, "v {0:F4} {1:F4} {2:F4}", v.X, v.Y, v.Z));
            }

            foreach (Vector3 vn in mesh.VertexNormals)
            {
                sb.AppendLine(string.Format(inv, "vn {0:F4} {1:F4} {2:F4}", vn.X, vn.Y, vn.Z));
            }

            foreach (var tri in mesh.Triangles)
            {
                int i0 = tri.V0 + 1;
                int i1 = tri.V1 + 1;
                int i2 = tri.V2 + 1;
                sb.AppendLine($"f {i0}//{i0} {i1}//{i1} {i2}//{i2}");
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public static TeddyMesh LoadFromFile(string filePath)
        {
            var mesh = new TeddyMesh
            {
                BaseColor = Color.FromArgb(244, 162, 97)
            };

            var inv = CultureInfo.InvariantCulture;
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                if (parts[0] == "v" && parts.Length >= 4)
                {
                    if (float.TryParse(parts[1], NumberStyles.Float, inv, out float x) &&
                        float.TryParse(parts[2], NumberStyles.Float, inv, out float y) &&
                        float.TryParse(parts[3], NumberStyles.Float, inv, out float z))
                    {
                        mesh.Vertices.Add(new Vector3(x, y, z));
                    }
                }
                else if (parts[0] == "f" && parts.Length >= 4)
                {
                    var faceIndices = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string token = parts[i].Split('/')[0];
                        if (int.TryParse(token, NumberStyles.Integer, inv, out int objIdx))
                        {
                            int zeroIdx = objIdx > 0 ? objIdx - 1 : mesh.Vertices.Count + objIdx;
                            if (zeroIdx >= 0 && zeroIdx < mesh.Vertices.Count)
                                faceIndices.Add(zeroIdx);
                        }
                    }

                    // Chia quạt tam giác nếu mặt có >= 3 đỉnh
                    for (int i = 1; i < faceIndices.Count - 1; i++)
                    {
                        mesh.Triangles.Add(new TeddyTriangle(faceIndices[0], faceIndices[i], faceIndices[i + 1]));
                    }
                }
            }

            // Tự động căn tâm và chuẩn hóa kích thước nếu mô hình bên ngoài quá nhỏ hoặc quá lớn
            NormalizeLoadedMesh(mesh, 150f);
            mesh.BuildConnectivity();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static void NormalizeLoadedMesh(TeddyMesh mesh, float targetRadius)
        {
            if (mesh.Vertices.Count == 0) return;

            Vector3 min = mesh.Vertices[0];
            Vector3 max = mesh.Vertices[0];
            foreach (var v in mesh.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            Vector3 center = 0.5f * (min + max);
            float maxExtent = MathF.Max(max.X - min.X, MathF.Max(max.Y - min.Y, max.Z - min.Z)) * 0.5f;
            float scale = maxExtent > 1e-4f ? targetRadius / maxExtent : 1f;

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                mesh.Vertices[i] = (mesh.Vertices[i] - center) * scale;
            }
        }
    }
}
