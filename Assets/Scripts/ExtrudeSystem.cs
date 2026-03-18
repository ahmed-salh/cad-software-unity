using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Extrudes a 2D polygon profile along +Y to produce a CADObject.
///
/// For CONVEX profiles the built-in fan triangulation works correctly.
/// For CONCAVE profiles, swap the cap triangulator with an ear-clipping
/// implementation (e.g. Clipper2 or LibTessDotNet — both available on NuGet).
/// </summary>
public class ExtrudeSystem : MonoBehaviour
{
    public static ExtrudeSystem Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Extrudes a closed 2D polygon given in the XZ plane.
    /// </summary>
    /// <param name="profile">Ordered vertices in XZ (wound CCW when viewed from +Y).</param>
    /// <param name="height">Extrusion distance along +Y.</param>
    /// <param name="position">World-space pivot.</param>
    public CADObject Extrude(List<Vector2> profile, float height, Vector3 position)
    {
        if (profile == null || profile.Count < 3)
        {
            Debug.LogError("ExtrudeSystem: profile needs at least 3 points.");
            return null;
        }

        var mesh = BuildMesh(profile, Mathf.Max(0.001f, height));
        return SpawnObject(mesh, position);
    }

    // ── Mesh builder ───────────────────────────────────────────────────────────

    private Mesh BuildMesh(List<Vector2> profile, float height)
    {
        int n = profile.Count;

        // Vertices: bottom cap [0..n-1], top cap [n..2n-1]
        var verts = new List<Vector3>(n * 2 + 4);
        for (int i = 0; i < n; i++) verts.Add(new Vector3(profile[i].x, 0, profile[i].y));
        for (int i = 0; i < n; i++) verts.Add(new Vector3(profile[i].x, height, profile[i].y));

        var tris = new List<int>();

        // Side quads
        for (int i = 0; i < n; i++)
        {
            int i1 = (i + 1) % n;
            int b0 = i, b1 = i1;
            int t0 = i + n, t1 = i1 + n;
            tris.AddRange(new[] { b0, t0, b1, b1, t0, t1 });
        }

        // Bottom cap — fan from vertex 0 (works for convex; use ear-clip for concave)
        for (int i = 1; i < n - 1; i++)
            tris.AddRange(new[] { 0, i + 1, i }); // reversed winding for bottom face

        // Top cap
        for (int i = 1; i < n - 1; i++)
            tris.AddRange(new[] { n, n + i, n + i + 1 });

        var mesh = new Mesh { name = "ExtrudedProfile" };
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();
        return mesh;
    }

    // ── Object spawning ────────────────────────────────────────────────────────

    private CADObject SpawnObject(Mesh mesh, Vector3 position)
    {
        var go = new GameObject("Extrude_" + Random.Range(1000, 9999));
        go.transform.position = position;
        go.layer = LayerMask.NameToLayer("CADObject");

        go.AddComponent<MeshFilter>().mesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ShapeFactory.Instance != null
            ? ShapeFactory.Instance.defaultMaterial
            : new Material(Shader.Find("Standard"));

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        var obj = go.AddComponent<CADObject>();
        obj.primitiveType = CADPrimitive.Custom;
        obj.objectName = go.name;
        obj.dimensions = mesh.bounds.size;

        CADManager.Instance.NotifySceneChanged();
        return obj;
    }

    // ── Convenience: rectangle profile ────────────────────────────────────────

    public CADObject ExtrudeRect(Vector2 size, float height, Vector3 position)
    {
        float hw = size.x * 0.5f, hd = size.y * 0.5f;
        var profile = new List<Vector2>
        {
            new(-hw, -hd), new( hw, -hd),
            new( hw,  hd), new(-hw,  hd)
        };
        return Extrude(profile, height, position);
    }

    // ── Convenience: regular polygon ──────────────────────────────────────────

    public CADObject ExtrudeRegularPolygon(int sides, float radius, float height, Vector3 position)
    {
        sides = Mathf.Max(3, sides);
        var profile = new List<Vector2>(sides);
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.PI * 2f * i / sides;
            profile.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
        }
        return Extrude(profile, height, position);
    }
}