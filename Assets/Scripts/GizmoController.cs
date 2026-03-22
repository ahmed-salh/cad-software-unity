using UnityEngine;

/// <summary>
/// Owns the active GizmoMode and acts as the bridge between
/// the toolbar buttons and the gizmo renderers.
///
/// Add to a persistent GameObject in CADRoot (e.g. "GizmoController").
/// CADUI reads and writes ActiveMode via this singleton.
/// </summary>
public class GizmoController : MonoBehaviour
{
    public static GizmoController Instance { get; private set; }

    public GizmoMode ActiveMode { get; private set; } = GizmoMode.Move;

    public event System.Action<GizmoMode> OnModeChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetMode(GizmoMode mode)
    {
        if (ActiveMode == mode) return;
        ActiveMode = mode;
        OnModeChanged?.Invoke(mode);
    }
}