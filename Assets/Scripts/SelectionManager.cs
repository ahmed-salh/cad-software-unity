using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Click-to-select. Checks GizmoInteractor.IsDragging so a gizmo drag-start
/// click never simultaneously deselects the object.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    [Tooltip("Must match the layer used in ShapeFactory")]
    public LayerMask cadLayer;

    private Camera cam;

    void Start() => cam = Camera.main;

    void Update()
    {
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        // A gizmo drag started this frame — don't also process a selection event
        if (GizmoInteractor.IsDragging) return;

        // UI click
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var mode = CADManager.Instance.CurrentMode;

        if (mode == CADMode.CreateBox || mode == CADMode.CreateCylinder ||
            mode == CADMode.CreateSphere || mode == CADMode.Extrude) return;

        Vector2 mpos = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mpos);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, cadLayer))
        {
            var obj = hit.collider.GetComponent<CADObject>();
            if (obj != null) { CADManager.Instance.Select(obj); return; }
        }

        if (mode == CADMode.Select)
            CADManager.Instance.Deselect();
    }

    void LateUpdate()
    {
        if (Keyboard.current.deleteKey.wasPressedThisFrame)
            CADManager.Instance.DeleteSelected();

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            CADManager.Instance.Deselect();
    }
}