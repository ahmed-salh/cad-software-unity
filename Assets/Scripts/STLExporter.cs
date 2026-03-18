using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Exports CADObjects to binary STL (the compact, industry-standard format).
/// All meshes are written in world space so transforms are baked in.
/// </summary>
public static class STLExporter
{
    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Export a single selected object.</summary>
    public static void ExportSelected(CADObject obj, string filePath)
    {
        if (obj == null) { Debug.LogWarning("STLExporter: no object provided."); return; }
        var mf = obj.GetComponent<MeshFilter>();
        if (mf?.sharedMesh == null) { Debug.LogWarning("STLExporter: object has no mesh."); return; }
        WriteBinarySTL(new[] { (mf.sharedMesh, obj.transform) }, obj.objectName, filePath);
    }

    /// <summary>Export every object in the scene as a single STL.</summary>
    public static void ExportAll(List<CADObject> objects, string filePath)
    {
        var pairs = new List<(Mesh, Transform)>();
        foreach (var obj in objects)
        {
            var mf = obj.GetComponent<MeshFilter>();
            if (mf?.sharedMesh != null) pairs.Add((mf.sharedMesh, obj.transform));
        }
        WriteBinarySTL(pairs, "CAD_Export", filePath);
    }

    // ── Binary STL writer ──────────────────────────────────────────────────────

    private static void WriteBinarySTL(
        IEnumerable<(Mesh mesh, Transform t)> meshes,
        string name,
        string path)
    {
        // Count total triangles first (required in STL header)
        uint totalTris = 0;
        foreach (var (m, _) in meshes) totalTris += (uint)(m.triangles.Length / 3);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // 80-byte ASCII header
        byte[] header = new byte[80];
        Encoding.ASCII.GetBytes(name.PadRight(80).Substring(0, 80)).CopyTo(header, 0);
        bw.Write(header);

        // Triangle count (uint32)
        bw.Write(totalTris);

        foreach (var (mesh, t) in meshes)
        {
            int[] tris = mesh.triangles;
            Vector3[] verts = mesh.vertices;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v0 = t.TransformPoint(verts[tris[i]]);
                Vector3 v1 = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 v2 = t.TransformPoint(verts[tris[i + 2]]);

                // STL uses right-hand coords; Unity is left-handed → flip winding
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                WriteVec3(bw, normal);
                WriteVec3(bw, v0);
                WriteVec3(bw, v1);
                WriteVec3(bw, v2);
                bw.Write((ushort)0); // attribute byte count (unused)
            }
        }

        Debug.Log($"[STLExporter] Wrote {totalTris} triangles → {path}");
    }

    private static void WriteVec3(BinaryWriter bw, Vector3 v)
    {
        bw.Write(v.x);
        bw.Write(v.y);
        bw.Write(v.z);
    }
}