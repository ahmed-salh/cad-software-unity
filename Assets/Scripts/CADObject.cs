using UnityEngine;

public enum CADPrimitive { Box, Cylinder, Sphere, Custom }

/// <summary>
/// Component attached to every piece of geometry in the scene.
/// Owns metadata, highlight state, and the cached bounds.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class CADObject : MonoBehaviour
{
    [Header("Metadata")]
    public CADPrimitive primitiveType = CADPrimitive.Custom;
    public string objectName = "Part";
    public Vector3 dimensions = Vector3.one;   // logical size (world-space)

    // ── Materials ─────────────────────────────────────────────────────────────

    private MeshRenderer mr;
    private Material matDefault;
    private Material matHighlight;
    private Material matSecondary;

    public static Color HighlightColor = new(0.25f, 0.55f, 1.00f, 1f);
    public static Color SecondaryColor = new(1.00f, 0.60f, 0.20f, 1f);

    void Awake()
    {
        mr = GetComponent<MeshRenderer>();
        matDefault = mr.sharedMaterial != null
            ? new Material(mr.sharedMaterial)
            : new Material(Shader.Find("Standard"));

        matHighlight = new Material(matDefault) { color = HighlightColor };
        matSecondary = new Material(matDefault) { color = SecondaryColor };

        mr.material = matDefault;
        CADManager.Instance.RegisterObject(this);
    }

    void OnDestroy() => CADManager.Instance?.UnregisterObject(this);

    // ── Highlight ─────────────────────────────────────────────────────────────

    public void SetHighlight(bool on, bool secondary = false)
    {
        mr.material = on ? (secondary ? matSecondary : matHighlight) : matDefault;
    }

    // ── Bounds ────────────────────────────────────────────────────────────────

    public Bounds GetWorldBounds() => mr.bounds;

    // ── Dimension sync ────────────────────────────────────────────────────────

    /// <summary>
    /// Resize by directly setting world-space dimensions.
    /// Keeps position centred on the object's current pivot.
    /// </summary>
    public void ApplyDimensions(Vector3 newDims)
    {
        dimensions = newDims;
        transform.localScale = newDims;
    }
}