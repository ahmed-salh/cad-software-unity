using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Imports a Wavefront .obj file at runtime and produces one CADObject per group/object.
///
/// Supported:
///   v   — vertices
///   vn  — normals
///   vt  — UV coordinates
///   f   — faces (triangles and quads, all three index formats:
///             v   /   v/vt   /   v/vt/vn   /   v//vn)
///   o/g — named sub-objects / groups (each becomes its own CADObject)
///   #   — comments (ignored)
///   mtl — material names parsed but not applied (materials use the default CAD material)
///
/// Usage:
///   var objects = OBJImporter.Import(path);
/// </summary>
public static class OBJImporter
{
    public static List<CADObject> Import(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[OBJImporter] File not found: {path}");
            return null;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            return Parse(lines, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OBJImporter] Failed to parse {path}: {ex.Message}");
            return null;
        }
    }

    // ── Parser ────────────────────────────────────────────────────────────────

    private static List<CADObject> Parse(string[] lines, string baseName)
    {
        // Global pools
        var gVerts = new List<Vector3>();
        var gNormals = new List<Vector3>();
        var gUVs = new List<Vector2>();

        // Current group state
        var groupName = baseName;
        var groupVerts = new List<Vector3>();
        var groupNormals = new List<Vector3>();
        var groupUVs = new List<Vector2>();
        var groupTris = new List<int>();

        // (vertIdx, normIdx, uvIdx) → local index cache to avoid duplicates
        var indexCache = new Dictionary<(int, int, int), int>();

        var results = new List<CADObject>();

        void FlushGroup()
        {
            if (groupTris.Count == 0) return;
            var obj = BuildCADObject(groupName, groupVerts, groupNormals, groupUVs, groupTris);
            if (obj != null) results.Add(obj);

            groupVerts.Clear();
            groupNormals.Clear();
            groupUVs.Clear();
            groupTris.Clear();
            indexCache.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            // Split on whitespace
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length == 0) continue;

            switch (parts[0].ToLowerInvariant())
            {
                case "v":
                    gVerts.Add(ParseVec3(parts));
                    break;

                case "vn":
                    gNormals.Add(ParseVec3(parts));
                    break;

                case "vt":
                    gUVs.Add(ParseVec2(parts));
                    break;

                case "o":
                case "g":
                    FlushGroup();
                    groupName = parts.Length > 1 ? parts[1] : baseName;
                    break;

                case "f":
                    // A face can be a triangle (3 verts) or quad (4 verts)
                    // Each vert token: v  OR  v/vt  OR  v/vt/vn  OR  v//vn
                    var faceLocalIdx = new List<int>();

                    for (int i = 1; i < parts.Length; i++)
                    {
                        var (vi, vti, vni) = ParseFaceToken(parts[i]);

                        // OBJ indices are 1-based; negative = relative to end
                        int vAbs = ResolveIndex(vi, gVerts.Count);
                        int vnAbs = ResolveIndex(vni, gNormals.Count);
                        int vtAbs = ResolveIndex(vti, gUVs.Count);

                        var key = (vAbs, vnAbs, vtAbs);
                        if (!indexCache.TryGetValue(key, out int localIdx))
                        {
                            localIdx = groupVerts.Count;
                            indexCache[key] = localIdx;

                            groupVerts.Add(vAbs >= 0 && vAbs < gVerts.Count
                                ? gVerts[vAbs] : Vector3.zero);

                            groupNormals.Add(vnAbs >= 0 && vnAbs < gNormals.Count
                                ? gNormals[vnAbs] : Vector3.up);

                            groupUVs.Add(vtAbs >= 0 && vtAbs < gUVs.Count
                                ? gUVs[vtAbs] : Vector2.zero);
                        }

                        faceLocalIdx.Add(localIdx);
                    }

                    // Fan triangulation for tri and quad faces
                    for (int i = 1; i < faceLocalIdx.Count - 1; i++)
                    {
                        // OBJ is right-handed, Unity left-handed → reverse winding
                        groupTris.Add(faceLocalIdx[0]);
                        groupTris.Add(faceLocalIdx[i + 1]);
                        groupTris.Add(faceLocalIdx[i]);
                    }
                    break;
            }
        }

        FlushGroup(); // flush the last group
        return results;
    }

    // ── Mesh builder ──────────────────────────────────────────────────────────

    private static CADObject BuildCADObject(
        string name,
        List<Vector3> verts,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> tris)
    {
        if (verts.Count == 0 || tris.Count == 0) return null;

        var mesh = new Mesh { name = name };
        mesh.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();

        if (normals.Count == verts.Count)
            mesh.normals = normals.ToArray();
        else
            mesh.RecalculateNormals();

        if (uvs.Count == verts.Count)
            mesh.uv = uvs.ToArray();

        mesh.RecalculateBounds();
        mesh.Optimize();

        return SpawnObject(mesh, name);
    }

    private static CADObject SpawnObject(Mesh mesh, string name)
    {
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

    private static Vector3 ParseVec3(string[] p)
    {
        float x = F(p, 1), y = F(p, 2), z = F(p, 3);
        // OBJ right-hand → Unity left-hand: negate Z
        return new Vector3(x, y, -z);
    }

    private static Vector2 ParseVec2(string[] p)
        => new(F(p, 1), F(p, 2));

    private static float F(string[] p, int i)
        => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    // Token formats: v | v/vt | v/vt/vn | v//vn
    // Returns (vIdx, vtIdx, vnIdx) as 1-based (0 = absent)
    private static (int v, int vt, int vn) ParseFaceToken(string token)
    {
        var parts = token.Split('/');
        int v = parts.Length > 0 ? ParseInt(parts[0]) : 0;
        int vt = parts.Length > 1 ? ParseInt(parts[1]) : 0;
        int vn = parts.Length > 2 ? ParseInt(parts[2]) : 0;
        return (v, vt, vn);
    }

    private static int ParseInt(string s)
        => int.TryParse(s, out int v) ? v : 0;

    // Converts 1-based OBJ index (or negative relative) to 0-based array index.
    // Returns -1 if absent (0 input).
    private static int ResolveIndex(int objIdx, int count)
    {
        if (objIdx == 0) return -1;
        return objIdx > 0 ? objIdx - 1 : count + objIdx;
    }
}