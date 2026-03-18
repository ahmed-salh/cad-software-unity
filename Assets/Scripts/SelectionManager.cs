using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// Click-to-select with full multi-select workflow support.
///
/// Single select  : LMB click on object
/// Secondary pick : In Boolean mode, LMB on a second object adds it as secondary
/// Deselect       : LMB click on empty space  (NOT while a handle is being dragged)
/// Delete         : Delete key
/// Escape         : Deselect all
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

        // A handle drag started this frame — don't also process a selection event
        if (TransformHandles.IsDraggingHandle) return;

        // UI click — don't raycast into the scene
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var mode = CADManager.Instance.CurrentMode;

        // Creation modes: toolbar handles placement, not this script
        if (mode == CADMode.CreateBox || mode == CADMode.CreateCylinder ||
            mode == CADMode.CreateSphere || mode == CADMode.Extrude) return;

        Vector2 mpos = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mpos);

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, cadLayer))
        {
            var obj = hit.collider.GetComponent<CADObject>();
            if (obj != null)
            {
                CADManager.Instance.Select(obj);
                return;
            }
        }

        // Missed everything → deselect (only in Select mode so Boolean 2nd-pick isn't lost)
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