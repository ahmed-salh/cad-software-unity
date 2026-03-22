using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Handles all mouse interaction with the gizmo — hover, begin drag, continue drag.
///
/// Attach to the same GameObject as GizmoRenderer (Main Camera).
///
/// Axis indices:
///   0 = X   1 = Y   2 = Z   3 = center (uniform scale)
///  -1 = nothing hovered
/// </summary>
[RequireComponent(typeof(Camera))]
public class GizmoInteractor : MonoBehaviour
{
    public static GizmoInteractor Instance { get; private set; }

    /// <summary>Highlighted axis index — read by GizmoRenderer each frame.</summary>
    public int HoveredAxis { get; private set; } = -1;

    /// <summary>
    /// True while any gizmo drag is active.
    /// SelectionManager checks this flag so a drag-start click never also deselects.
    /// </summary>
    public static bool IsDragging { get; private set; }

    [Tooltip("Screen-pixel tolerance for axis arm / ring hover detection")]
    public float hitTolerancePx = 12f;

    // ── Drag state ─────────────────────────────────────────────────────────────

    private int activeAxis;
    private GizmoMode activeMode;

    // Move
    private Vector3 moveStartPos;
    private Vector3 moveStartMouseWorld;

    // Scale
    private Vector3 scaleStartScale;
    private Vector3 scaleStartMouseWorld;
    private Vector2 scaleStartMouseScreen;

    // Rotate — store start rotation + start mouse screen pos; compute total angle each frame
    private Quaternion rotateStartRotation;
    private Vector2 rotateStartMouseScreen;
    private Vector3 rotateWorldAxis;

    // ── References ─────────────────────────────────────────────────────────────

    private Camera cam;
    private GizmoRenderer gizmoRenderer;

    private static readonly Vector3[] WorldAxes =
        { Vector3.right, Vector3.up, Vector3.forward };

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        cam = GetComponent<Camera>();
        gizmoRenderer = GetComponent<GizmoRenderer>();
    }

    void Update()
    {
        var target = CADManager.Instance?.SelectedObject;
        if (target == null)
        {
            HoveredAxis = -1;
            IsDragging = false;
            return;
        }

        if (GizmoController.Instance == null) return;

        Vector2 mpos = Mouse.current.position.ReadValue();

        // ── Release ────────────────────────────────────────────────────────────
        if (IsDragging && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            IsDragging = false;
            activeAxis = -1;
            target.dimensions = target.transform.localScale;
            CADManager.Instance.NotifySceneChanged();
            return;
        }

        // ── Continue drag ──────────────────────────────────────────────────────
        if (IsDragging)
        {
            switch (activeMode)
            {
                case GizmoMode.Move: ContinueMove(target, mpos); break;
                case GizmoMode.Scale: ContinueScale(target, mpos); break;
                case GizmoMode.Rotate: ContinueRotate(target, mpos); break;
            }
            return;
        }

        // ── Hover ──────────────────────────────────────────────────────────────
        bool overUI = EventSystem.current != null &&
                      EventSystem.current.IsPointerOverGameObject();
        HoveredAxis = overUI ? -1 : HitTest(target.transform.position, mpos);

        // ── Begin drag ─────────────────────────────────────────────────────────
        if (HoveredAxis >= 0 && Mouse.current.leftButton.wasPressedThisFrame)
            BeginDrag(target, HoveredAxis, mpos);
    }

    // ── Begin drag ─────────────────────────────────────────────────────────────

    private void BeginDrag(CADObject target, int axis, Vector2 mpos)
    {
        IsDragging = true;
        activeAxis = axis;
        activeMode = GizmoController.Instance.ActiveMode;

        switch (activeMode)
        {
            case GizmoMode.Move:
                moveStartPos = target.transform.position;
                moveStartMouseWorld = RaycastDragPlane(moveStartPos, axis, mpos);
                break;

            case GizmoMode.Scale:
                scaleStartScale = target.transform.localScale;
                scaleStartMouseScreen = mpos;
                scaleStartMouseWorld = RaycastDragPlane(target.transform.position, axis, mpos);
                break;

            case GizmoMode.Rotate:
                rotateStartRotation = target.transform.rotation;
                rotateStartMouseScreen = mpos;
                rotateWorldAxis = axis < 3 ? WorldAxes[axis] : cam.transform.forward;
                break;
        }
    }

    // ── Move ───────────────────────────────────────────────────────────────────

    private void ContinueMove(CADObject target, Vector2 mpos)
    {
        Vector3 nowWorld = RaycastDragPlane(moveStartPos, activeAxis, mpos);
        Vector3 delta = nowWorld - moveStartMouseWorld;

        if (activeAxis < 3)
        {
            Vector3 ax = WorldAxes[activeAxis];
            delta = ax * Vector3.Dot(delta, ax);
        }

        target.transform.position = moveStartPos + delta;
    }

    // ── Scale ──────────────────────────────────────────────────────────────────

    private void ContinueScale(CADObject target, Vector2 mpos)
    {
        if (activeAxis == 3)
        {
            // Uniform scale — horizontal drag
            float dx = mpos.x - scaleStartMouseScreen.x;
            float factor = Mathf.Max(0.001f, 1f + dx * 0.005f);
            target.transform.localScale = scaleStartScale * factor;
        }
        else
        {
            Vector3 nowWorld = RaycastDragPlane(moveStartPos, activeAxis, mpos);
            Vector3 ax = WorldAxes[activeAxis];
            float axDelta = Vector3.Dot(nowWorld - scaleStartMouseWorld, ax);

            Vector3 newScale = scaleStartScale;
            newScale[activeAxis] = Mathf.Max(0.001f, scaleStartScale[activeAxis] + axDelta);
            target.transform.localScale = newScale;
        }

        target.dimensions = target.transform.localScale;
    }

    // ── Rotate ─────────────────────────────────────────────────────────────────
    // Drift-free: compute the TOTAL angle from drag start each frame,
    // then apply it to the start rotation. No per-frame accumulation.

    private void ContinueRotate(CADObject target, Vector2 mpos)
    {
        Vector2 center = cam.WorldToScreenPoint(target.transform.position);
        Vector2 toStart = rotateStartMouseScreen - center;
        Vector2 toCurrent = mpos - center;

        // Need at least a few pixels from centre to compute a reliable angle
        if (toStart.sqrMagnitude < 9f || toCurrent.sqrMagnitude < 9f) return;

        // SignedAngle: CCW positive in screen space
        float totalAngle = Vector2.SignedAngle(toStart, toCurrent);

        // Flip sign on X axis so dragging up rotates in the expected direction
        if (activeAxis == 0) totalAngle = -totalAngle;

        // Apply total angle FROM start rotation — no drift
        target.transform.rotation =
            Quaternion.AngleAxis(totalAngle, rotateWorldAxis) * rotateStartRotation;
    }

    // ── Hit testing ────────────────────────────────────────────────────────────

    private int HitTest(Vector3 origin, Vector2 mpos)
    {
        return GizmoController.Instance.ActiveMode == GizmoMode.Rotate
            ? HitTestRings(origin, mpos)
            : HitTestArms(origin, mpos);
    }

    private int HitTestArms(Vector3 origin, Vector2 mpos)
    {
        float pxToWorld = gizmoRenderer.PixelToWorldScale(origin);
        float armWorld = gizmoRenderer.ArmLengthPx * pxToWorld;

        int best = -1;
        float bestDist = hitTolerancePx;

        for (int i = 0; i < 3; i++)
        {
            Vector3 tip = origin + WorldAxes[i] * armWorld;
            Vector2 aScreen = cam.WorldToScreenPoint(origin);
            Vector2 bScreen = cam.WorldToScreenPoint(tip);
            float d = DistPointToSegment(mpos, aScreen, bScreen);
            if (d < bestDist) { bestDist = d; best = i; }
        }

        // Center cube — uniform scale only
        if (GizmoController.Instance.ActiveMode == GizmoMode.Scale)
        {
            float dc = Vector2.Distance(mpos, cam.WorldToScreenPoint(origin));
            if (dc < gizmoRenderer.TipSizePx * 2f && dc < bestDist)
                best = 3;
        }

        return best;
    }

    private int HitTestRings(Vector3 origin, Vector2 mpos)
    {
        float pxToWorld = gizmoRenderer.PixelToWorldScale(origin);
        float ringWorld = gizmoRenderer.RingRadiusPx * pxToWorld;

        int best = -1;
        float bestDist = hitTolerancePx;

        for (int i = 0; i < 3; i++)
        {
            Vector3 ax = WorldAxes[i];

            Vector3 perp1 = Vector3.Cross(ax, cam.transform.forward);
            if (perp1.sqrMagnitude < 0.001f) perp1 = Vector3.Cross(ax, Vector3.up);
            perp1.Normalize();
            Vector3 perp2 = Vector3.Cross(ax, perp1).normalized;

            float minD = float.MaxValue;
            for (int s = 0; s < 64; s++)
            {
                float a = Mathf.PI * 2f * s / 64;
                Vector3 p = origin + (perp1 * Mathf.Cos(a) + perp2 * Mathf.Sin(a)) * ringWorld;
                float d = Vector2.Distance(mpos, cam.WorldToScreenPoint(p));
                if (d < minD) minD = d;
            }

            if (minD < bestDist) { bestDist = minD; best = i; }
        }

        return best;
    }

    // ── Projection ─────────────────────────────────────────────────────────────

    private Vector3 RaycastDragPlane(Vector3 origin, int axis, Vector2 mpos)
    {
        Vector3 normal = DragPlaneNormal(axis);
        Plane plane = new(normal, origin);
        Ray ray = cam.ScreenPointToRay(mpos);

        if (plane.Raycast(ray, out float t)) return ray.GetPoint(t);

        // Fallback
        Plane vp = new(cam.transform.forward, origin);
        if (vp.Raycast(ray, out float t2)) return ray.GetPoint(t2);

        return origin;
    }

    private Vector3 DragPlaneNormal(int axis)
    {
        if (axis >= 3) return cam.transform.forward;

        Vector3 a1 = WorldAxes[(axis + 1) % 3];
        Vector3 a2 = WorldAxes[(axis + 2) % 3];
        float d1 = Mathf.Abs(Vector3.Dot(a1, cam.transform.forward));
        float d2 = Mathf.Abs(Vector3.Dot(a2, cam.transform.forward));
        return d1 > d2 ? a1 : a2;
    }

    // ── Math ───────────────────────────────────────────────────────────────────

    private static float DistPointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 0.0001f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return Vector2.Distance(p, a + t * ab);
    }
}