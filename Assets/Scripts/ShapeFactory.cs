using UnityEngine;

/// <summary>
/// Creates primitives, assigns materials, configures colliders,
/// and places the resulting CADObject into the scene at world position.
/// </summary>
public class ShapeFactory : MonoBehaviour
{
    public static ShapeFactory Instance { get; private set; }

    [Header("Default material assigned to every new shape")]
    public Material defaultMaterial;

    // Layer that the SelectionManager raycasts against
    private int cadLayer;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        cadLayer = LayerMask.NameToLayer("CADObject");
        if (cadLayer == -1) Debug.LogWarning("ShapeFactory: layer 'CADObject' not found. Create it in Project Settings.");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public CADObject CreateBox(Vector3 position, Vector3 size)
    {
        var go = MakePrimitive(PrimitiveType.Cube, position, size, CADPrimitive.Box);
        return go;
    }

    /// <param name="radius">Half the diameter in X/Z</param>
    /// <param name="height">Full height in Y</param>
    public CADObject CreateCylinder(Vector3 position, float radius = 0.5f, float height = 1f)
    {
        // Unity's built-in cylinder has half-height scale, hence height/2 for Y
        var scale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        var go = MakePrimitive(PrimitiveType.Cylinder, position, scale, CADPrimitive.Cylinder);
        return go;
    }

    public CADObject CreateSphere(Vector3 position, float diameter = 1f)
    {
        var go = MakePrimitive(PrimitiveType.Sphere, position, Vector3.one * diameter, CADPrimitive.Sphere);
        return go;
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private CADObject MakePrimitive(PrimitiveType type, Vector3 pos, Vector3 scale, CADPrimitive cadType)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.layer = cadLayer;
        go.name = cadType + "_" + Random.Range(1000, 9999);

        // Replace Unity's auto-collider with a MeshCollider so CSG works correctly
        RemoveDefaultCollider(go, type);
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;

        if (defaultMaterial != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = defaultMaterial;

        var cadObj = go.AddComponent<CADObject>();
        cadObj.primitiveType = cadType;
        cadObj.objectName = go.name;
        cadObj.dimensions = scale;

        CADManager.Instance.NotifySceneChanged();
        return cadObj;
    }

    private static void RemoveDefaultCollider(GameObject go, PrimitiveType type)
    {
        switch (type)
        {
            case PrimitiveType.Cube: Destroy(go.GetComponent<BoxCollider>()); break;
            case PrimitiveType.Sphere: Destroy(go.GetComponent<SphereCollider>()); break;
            case PrimitiveType.Cylinder:
            case PrimitiveType.Capsule: Destroy(go.GetComponent<CapsuleCollider>()); break;
        }
    }
}