using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Imports both binary and ASCII .stl files at runtime.
///
/// Binary STL  : 80-byte header + uint32 triangle count + (normal + 3×vertex + attr) per triangle
/// ASCII STL   : "solid name … facet normal … vertex … endsolid"
///
/// The importer welds shared vertices (same position + normal within tolerance)
/// to produce clean mesh topology, which improves rendering and CSG results.
///
/// Usage:
///   CADObject obj = STLImporter.Import(path);
/// </summary>
public static class STLImporter
{
    private const float WELD_TOLERANCE = 1e-6f;

    public static CADObject Import(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[STLImporter] File not found: {path}");
            return null;
        }

        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            bool isAscii = IsAsciiSTL(path);

            List<Vector3> verts, normals;
            List<int> tris;

            if (isAscii)
                ParseAscii(path, out verts, out normals, out tris);
            else
                ParseBinary(path, out verts, out normals, out tris);

            Debug.Log($"[STLImporter] {(isAscii ? "ASCII" : "Binary")} STL: " +
                      $"{tris.Count / 3} triangles before weld.");

            WeldVertices(ref verts, ref normals, ref tris);

            Debug.Log($"[STLImporter] After weld: {verts.Count} vertices, {tris.Count / 3} triangles.");

            return BuildCADObject(name, verts, normals, tris);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[STLImporter] Failed to parse {path}: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    // ── Format detection ──────────────────────────────────────────────────────

    private static bool IsAsciiSTL(string path)
    {
        // Binary STL starts with an 80-byte header (never "solid" unless it's ASCII)
        // Fastest heuristic: read first 256 bytes and check for "solid"
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        byte[] header = new byte[256];
        int read = fs.Read(header, 0, header.Length);
        string text = Encoding.ASCII.GetString(header, 0, read).TrimStart();
        return text.StartsWith("solid", StringComparison.OrdinalIgnoreCase);
    }

    // ── ASCII parser ──────────────────────────────────────────────────────────

    private static void ParseAscii(
        string path,
        out List<Vector3> verts,
        out List<Vector3> normals,
        out List<int> tris)
    {
        verts = new List<Vector3>();
        normals = new List<Vector3>();
        tris = new List<int>();

        var lines = File.ReadAllLines(path);
        Vector3 currentNormal = Vector3.up;
        int vertInFacet = 0;
        int baseIndex = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
            {
                currentNormal = ParseVec3(line, 2);
                vertInFacet = 0;
                baseIndex = verts.Count;
            }
            else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                // STL right-hand → Unity left-hand: negate Z
                var v = ParseVec3(line, 1);
                v.z = -v.z;
                verts.Add(v);
                normals.Add(new Vector3(currentNormal.x, currentNormal.y, -currentNormal.z));
                vertInFacet++;
            }
            else if (line.StartsWith("endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (vertInFacet == 3)
                {
                    // Reverse winding for Unity left-hand coordinate system
                    tris.Add(baseIndex);
                    tris.Add(baseIndex + 2);
                    tris.Add(baseIndex + 1);
                }
            }
        }
    }

    // ── Binary parser ─────────────────────────────────────────────────────────

    private static void ParseBinary(
        string path,
        out List<Vector3> verts,
        out List<Vector3> normals,
        out List<int> tris)
    {
        verts = new List<Vector3>();
        normals = new List<Vector3>();
        tris = new List<int>();

        using var br = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read));

        // Skip 80-byte header
        br.ReadBytes(80);
        uint triangleCount = br.ReadUInt32();

        for (uint i = 0; i < triangleCount; i++)
        {
            // Normal (stored per-face in STL)
            var n = ReadVec3(br);
            n.z = -n.z; // handedness fix

            // 3 vertices
            int baseIdx = verts.Count;
            for (int v = 0; v < 3; v++)
            {
                var vert = ReadVec3(br);
                vert.z = -vert.z; // handedness fix
                verts.Add(vert);
                normals.Add(n);
            }

            // Reverse winding
            tris.Add(baseIdx);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);

            // Attribute byte count (unused, skip 2 bytes)
            br.ReadUInt16();
        }
    }

    // ── Vertex welding ────────────────────────────────────────────────────────
    // STL stores 3 independent vertices per triangle with no sharing.
    // Welding merges vertices that are at the same position and have
    // the same normal, producing a proper indexed mesh.

    private static void WeldVertices(
        ref List<Vector3> verts,
        ref List<Vector3> normals,
        ref List<int> tris)
    {
        var weldedVerts = new List<Vector3>();
        var weldedNormals = new List<Vector3>();
        var remap = new int[verts.Count];

        for (int i = 0; i < verts.Count; i++)
        {
            int found = -1;
            for (int j = 0; j < weldedVerts.Count; j++)
            {
                if ((verts[i] - weldedVerts[j]).sqrMagnitude < WELD_TOLERANCE * WELD_TOLERANCE &&
                    (normals[i] - weldedNormals[j]).sqrMagnitude < WELD_TOLERANCE * WELD_TOLERANCE)
                {
                    found = j;
                    break;
                }
            }

            if (found >= 0)
            {
                remap[i] = found;
            }
            else
            {
                remap[i] = weldedVerts.Count;
                weldedVerts.Add(verts[i]);
                weldedNormals.Add(normals[i]);
            }
        }

        // Remap triangle indices
        for (int i = 0; i < tris.Count; i++)
            tris[i] = remap[tris[i]];

        verts = weldedVerts;
        normals = weldedNormals;
    }

    // ── Mesh builder ──────────────────────────────────────────────────────────

    private static CADObject BuildCADObject(
        string name,
        List<Vector3> verts,
        List<Vector3> normals,
        List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.normals = normals.ToArray();
        mesh.RecalculateBounds();
        mesh.Optimize();

        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("CADObject");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ShapeFactory.Instance?.defaultMaterial
            ?? new Material(Shader.Find("Standard"));

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        var cadObj = go.AddComponent<CADObject>();
        cadObj.primitiveType = CADPrimitive.Custom;
        cadObj.objectName = name;
        cadObj.dimensions = mesh.bounds.size;

        CADManager.Instance.NotifySceneChanged();
        return cadObj;
    }

    // ── Parse helpers ─────────────────────────────────────────────────────────

    private static Vector3 ReadVec3(BinaryReader br)
        => new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

    // Parse a vec3 starting at word index `startWord` in a space-separated line
    private static Vector3 ParseVec3(string line, int startWord)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        float x = P(parts, startWord);
        float y = P(parts, startWord + 1);
        float z = P(parts, startWord + 2);
        return new Vector3(x, y, z);
    }

    private static float P(string[] parts, int i)
        => i < parts.Length && float.TryParse(
            parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
}