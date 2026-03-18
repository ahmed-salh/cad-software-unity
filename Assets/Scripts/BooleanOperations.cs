using UnityEngine;

/// <summary>
/// Thin wrapper around the free Parabox CSG library (pb_CSG).
///
/// SETUP
/// ─────
/// 1. Clone or download pb_CSG:
///    https://github.com/karl-/pb_CSG
/// 2. Drop the /Parabox folder into your Assets.
/// 3. The CSGModel component will become available.
///
/// ALTERNATIVE: Runtime-CSG by Sabresaurus (Asset Store, paid) offers
/// better concave support and keeps UVs intact.
///
/// USAGE
/// ─────
///   BooleanOperations.Instance.Union(selected, secondary);
///   BooleanOperations.Instance.Subtract(selected, secondary);
///   BooleanOperations.Instance.Intersect(selected, secondary);
/// </summary>
public class BooleanOperations : MonoBehaviour
{
    public static BooleanOperations Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void Union(CADObject a, CADObject b) => Apply(a, b, Parabox.CSG.BooleanOp.Union);
    public void Subtract(CADObject a, CADObject b) => Apply(a, b, Parabox.CSG.BooleanOp.Subtract);
    public void Intersect(CADObject a, CADObject b) => Apply(a, b, Parabox.CSG.BooleanOp.Intersect);

    // ── Internal ───────────────────────────────────────────────────────────────

    private void Apply(CADObject a, CADObject b, Parabox.CSG.BooleanOp op)
    {
        if (a == null || b == null)
        {
            Debug.LogWarning("BooleanOperations: two objects must be selected.");
            return;
        }

        // pb_CSG works with MeshFilter components directly
        var mfA = a.GetComponent<MeshFilter>();
        var mfB = b.GetComponent<MeshFilter>();

        if (mfA == null || mfA.sharedMesh == null ||
            mfB == null || mfB.sharedMesh == null)
        {
            Debug.LogError("BooleanOperations: operands must have valid MeshFilters.");
            return;
        }

        // Parabox.CSG.CSG.Perform returns a new Mesh + Material[]
        var result = Parabox.CSG.CSG.Perform(
            mfA.sharedMesh, a.transform,
            mfB.sharedMesh, b.transform,
            op);

        if (result == null || result.mesh == null)
        {
            Debug.LogError("BooleanOperations: CSG returned null. Check for degenerate geometry.");
            return;
        }

        SpawnResult(result, a);

        // Remove operands
        CADManager.Instance.UnregisterObject(a);
        CADManager.Instance.UnregisterObject(b);
        Destroy(a.gameObject);
        Destroy(b.gameObject);
        CADManager.Instance.NotifySceneChanged();
    }

    private void SpawnResult(Parabox.CSG.CSGResult result, CADObject source)
    {
        var go = new GameObject("Bool_" + Random.Range(1000, 9999));
        go.transform.position = source.transform.position;
        go.layer = LayerMask.NameToLayer("CADObject");

        go.AddComponent<MeshFilter>().sharedMesh = result.mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = result.materials;

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = result.mesh;

        var cadObj = go.AddComponent<CADObject>();
        cadObj.primitiveType = CADPrimitive.Custom;
        cadObj.objectName = go.name;
        cadObj.dimensions = result.mesh.bounds.size;

        CADManager.Instance.Select(cadObj);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// STUB — keeps the project compiling before pb_CSG is imported.
// Delete this entire namespace block once the real library is in Assets/.
// ─────────────────────────────────────────────────────────────────────────────
#if !PARABOX_CSG
namespace Parabox.CSG
{
    public enum BooleanOp { Union, Subtract, Intersect }
    public class CSGResult { public UnityEngine.Mesh mesh; public UnityEngine.Material[] materials; }
    public static class CSG
    {
        public static CSGResult Perform(
            UnityEngine.Mesh mA, UnityEngine.Transform tA,
            UnityEngine.Mesh mB, UnityEngine.Transform tB,
            BooleanOp op)
        {
            UnityEngine.Debug.LogError("CSG stub — import pb_CSG from github.com/karl-/pb_CSG");
            return null;
        }
    }
}
#endif