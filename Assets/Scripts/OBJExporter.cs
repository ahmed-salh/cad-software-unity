using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports all CADObjects to an ASCII Wavefront OBJ file.
/// Vertices, normals and UV coordinates are all baked into world space.
/// Each CADObject becomes a named group ('g') in the output file.
/// </summary>
public static class OBJExporter
{
    // ── Public API ─────────────────────────────────────────────────────────────

    public static void ExportAll(List<CADObject> objects, string filePath)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine("# Unity CAD – OBJ Export");
        sb.AppendLine($"# Objects: {objects.Count}");
        sb.AppendLine($"# Generated: {System.DateTime.UtcNow:u}");
        sb.AppendLine();

        int vOffset = 1;   // OBJ indices are 1-based

        foreach (var obj in objects)
        {
            var mf = obj.GetComponent<MeshFilter>();
            if (mf?.sharedMesh == null) continue;
            vOffset = WriteMesh(sb, mf.sharedMesh, obj.transform, obj.objectName, vOffset);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[OBJExporter] Wrote {objects.Count} objects → {filePath}");
    }

    public static void ExportSelected(CADObject obj, string filePath)
    {
        if (obj == null) return;
        ExportAll(new List<CADObject> { obj }, filePath);
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    /// <returns>New vertex offset for the next mesh.</returns>
    private static int WriteMesh(StringBuilder sb, Mesh mesh, Transform t, string groupName, int vOffset)
    {
        sb.AppendLine($"g {SanitiseName(groupName)}");

        // Vertices (world space)
        foreach (var v in mesh.vertices)
        {
            var w = t.TransformPoint(v);
            sb.AppendLine($"v {w.x:F6} {w.y:F6} {w.z:F6}");
        }

        // Normals (world space)
        foreach (var n in mesh.normals)
        {
            var wn = t.TransformDirection(n).normalized;
            sb.AppendLine($"vn {wn.x:F6} {wn.y:F6} {wn.z:F6}");
        }

        // UVs
        var uvs = mesh.uv;
        bool hasUV = uvs != null && uvs.Length == mesh.vertexCount;
        if (hasUV)
            foreach (var uv in uvs)
                sb.AppendLine($"vt {uv.x:F6} {uv.y:F6}");

        // Faces — Unity uses CW winding; OBJ expects CCW, so reverse vertex order
        int[] tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i] + vOffset;
            int b = tris[i + 1] + vOffset;
            int c = tris[i + 2] + vOffset;

            if (hasUV)
                sb.AppendLine($"f {a}/{a}/{a} {c}/{c}/{c} {b}/{b}/{b}");
            else
                sb.AppendLine($"f {a}//{a} {c}//{c} {b}//{b}");
        }

        sb.AppendLine();
        return vOffset + mesh.vertexCount;
    }

    private static string SanitiseName(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, @"[^\w\-]", "_");
}