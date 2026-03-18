using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// CAD viewport camera — rewritten for the New Input System.
///   RMB drag  → orbit
///   MMB drag  → pan
///   Scroll    → zoom
///   F key     → frame selected object
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Orbit")]
    public float orbitSpeed = 4f;
    public float minPitch = -85f;
    public float maxPitch = 85f;

    [Header("Zoom")]
    public float zoomSpeed = 5f;
    public float minDistance = 0.5f;
    public float maxDistance = 100f;
    public float distance = 8f;

    [Header("Pan")]
    public float panSpeed = 0.008f;

    // ── State ──────────────────────────────────────────────────────────────────
    private float yaw, pitch;
    private Vector3 pivot;

    void Start()
    {
        pivot = Vector3.zero;
        var eu = transform.eulerAngles;
        yaw = eu.y;
        pitch = eu.x;
        ApplyTransform();
    }

    void Update()
    {
        if (IsPointerOverUI()) return;

        HandleOrbit();
        HandlePan();
        HandleZoom();
        HandleFrameKey();

        ApplyTransform();
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    private void HandleOrbit()
    {
        if (!Mouse.current.rightButton.isPressed) return;
        var delta = Mouse.current.delta.ReadValue();
        yaw += delta.x * orbitSpeed * Time.deltaTime * 10f;
        pitch -= delta.y * orbitSpeed * Time.deltaTime * 10f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void HandlePan()
    {
        if (!Mouse.current.middleButton.isPressed) return;
        var delta = Mouse.current.delta.ReadValue();
        float scale = distance * panSpeed;
        pivot -= transform.right * (delta.x * scale);
        pivot -= transform.up * (delta.y * scale);
    }

    private void HandleZoom()
    {
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;
        distance -= scroll * zoomSpeed * Time.deltaTime;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    private void HandleFrameKey()
    {
        if (Keyboard.current.fKey.wasPressedThisFrame)
        {
            var sel = CADManager.Instance?.SelectedObject;
            if (sel != null) FocusOn(sel.GetWorldBounds());
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void FocusOn(Bounds b)
    {
        pivot = b.center;
        distance = Mathf.Clamp(b.size.magnitude * 1.6f, minDistance, maxDistance);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ApplyTransform()
    {
        var rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = pivot - rot * new Vector3(0, 0, distance);
        transform.rotation = rot;
    }

    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}