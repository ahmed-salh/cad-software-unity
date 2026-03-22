using UnityEngine;

/// <summary>
/// Draws the three gizmo types using GL immediate-mode lines.
/// Rendered in OnRenderObject so it always appears on top of the scene
/// (uses ZTest Always).
///
/// Gizmo sizes are kept constant in SCREEN SPACE — they don't shrink as
/// you zoom out, matching Blender / Maya behaviour.
///
/// Attach to the Main Camera GameObject.
/// </summary>
[RequireComponent(typeof(Camera))]
public class GizmoRenderer : MonoBehaviour
{
    // ── Colours ───────────────────────────────────────────────────────────────
    public Color colorX = new(0.86f, 0.18f, 0.18f); // red
    public Color colorY = new(0.32f, 0.78f, 0.18f); // green
    public Color colorZ = new(0.18f, 0.42f, 0.86f); // blue
    public Color colorHover = new(1.00f, 0.85f, 0.10f); // yellow highlight
    public Color colorCenter = new(1.00f, 1.00f, 1.00f); // white center dot

    // ── Sizing (screen-space units) ───────────────────────────────────────────
    [Tooltip("Length of each axis arm in screen pixels")]
    public float armLengthPx = 90f;
    [Tooltip("Arrowhead / cube / ring radius in screen pixels")]
    public float tipSizePx = 10f;
    [Tooltip("Rotation ring radius in screen pixels")]
    public float ringRadiusPx = 80f;
    [Tooltip("Number of segments in the rotation ring")]
    public int ringSegments = 48;

    // ── Internal ──────────────────────────────────────────────────────────────
    private Material lineMat;
    private Camera cam;

    // Exposed to GizmoInteractor so it can use the same pixel sizes
    public float ArmLengthPx => armLengthPx;
    public float TipSizePx => tipSizePx;
    public float RingRadiusPx => ringRadiusPx;

    void Awake()
    {
        cam = GetComponent<Camera>();
        // Unlit line shader — works in all render pipelines
        lineMat = new Material(Shader.Find("Hidden/Internal-Colored"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        lineMat.SetInt("_ZWrite", 0);
        lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void OnRenderObject()
    {
        if (cam == null) return;
        var target = CADManager.Instance?.SelectedObject;
        if (target == null) return;
        if (GizmoController.Instance == null) return;

        // Get the hovered axis from the interactor (may be -1)
        int hovered = GizmoInteractor.Instance != null
            ? GizmoInteractor.Instance.HoveredAxis : -1;

        lineMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(cam.projectionMatrix);
        GL.modelview = cam.worldToCameraMatrix;

        Vector3 origin = target.transform.position;

        switch (GizmoController.Instance.ActiveMode)
        {
            case GizmoMode.Move: DrawMoveGizmo(origin, hovered); break;
            case GizmoMode.Scale: DrawScaleGizmo(origin, hovered); break;
            case GizmoMode.Rotate: DrawRotateGizmo(origin, hovered); break;
        }

        GL.PopMatrix();
    }

    // ── Move gizmo ────────────────────────────────────────────────────────────
    // Three arrows along world X Y Z, each ending in a cone (triangle in 2D)

    private void DrawMoveGizmo(Vector3 origin, int hovered)
    {
        DrawArrow(origin, Vector3.right, colorX, hovered == 0, cone: true);
        DrawArrow(origin, Vector3.up, colorY, hovered == 1, cone: true);
        DrawArrow(origin, Vector3.forward, colorZ, hovered == 2, cone: true);

        // Center dot
        DrawDot(origin, colorCenter, 5f);
    }

    // ── Scale gizmo ───────────────────────────────────────────────────────────
    // Three arrows ending in cubes (square cap) along world X Y Z
    // plus a white center cube for uniform scale

    private void DrawScaleGizmo(Vector3 origin, int hovered)
    {
        DrawArrow(origin, Vector3.right, colorX, hovered == 0, cone: false);
        DrawArrow(origin, Vector3.up, colorY, hovered == 1, cone: false);
        DrawArrow(origin, Vector3.forward, colorZ, hovered == 2, cone: false);

        // Center cube — uniform scale handle (axis 3)
        DrawScreenSquare(origin, colorCenter, tipSizePx * 0.9f);
    }

    // ── Rotate gizmo ─────────────────────────────────────────────────────────
    // Three circles (rings) around world X Y Z axes

    private void DrawRotateGizmo(Vector3 origin, int hovered)
    {
        DrawRing(origin, Vector3.right, colorX, hovered == 0);
        DrawRing(origin, Vector3.up, colorY, hovered == 1);
        DrawRing(origin, Vector3.forward, colorZ, hovered == 2);
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws an axis arm from origin in worldDir direction.
    /// Length and tip size are constant in screen pixels.
    /// </summary>
    private void DrawArrow(Vector3 origin, Vector3 worldDir, Color col, bool highlight, bool cone)
    {
        Color c = highlight ? colorHover : col;

        // Convert a screen-pixel offset to a world-space offset at the origin depth
        float pixToWorld = PixelToWorldScale(origin);
        float armWorld = armLengthPx * pixToWorld;
        float tipWorld = tipSizePx * pixToWorld;

        Vector3 tip = origin + worldDir * armWorld;

        GL.Begin(GL.LINES);
        GL.Color(c);
        GL.Vertex(origin);
        GL.Vertex(tip);
        GL.End();

        if (cone)
            DrawScreenCone(tip, worldDir, c, tipWorld);
        else
            DrawScreenCube(tip, c, tipWorld);
    }

    /// <summary>
    /// Draws an approximate cone arrowhead (billboard triangle in world space).
    /// </summary>
    private void DrawScreenCone(Vector3 tip, Vector3 dir, Color col, float sizeWorld)
    {
        // Two perpendicular vectors in the plane perpendicular to dir
        Vector3 perp1 = Vector3.Cross(dir, cam.transform.up);
        if (perp1.sqrMagnitude < 0.001f) perp1 = Vector3.Cross(dir, cam.transform.right);
        perp1.Normalize();
        Vector3 perp2 = Vector3.Cross(dir, perp1).normalized;

        Vector3 base1 = tip - dir * sizeWorld * 2f + perp1 * sizeWorld;
        Vector3 base2 = tip - dir * sizeWorld * 2f - perp1 * sizeWorld;
        Vector3 base3 = tip - dir * sizeWorld * 2f + perp2 * sizeWorld;
        Vector3 base4 = tip - dir * sizeWorld * 2f - perp2 * sizeWorld;

        GL.Begin(GL.LINES);
        GL.Color(col);
        GL.Vertex(tip); GL.Vertex(base1);
        GL.Vertex(tip); GL.Vertex(base2);
        GL.Vertex(tip); GL.Vertex(base3);
        GL.Vertex(tip); GL.Vertex(base4);
        GL.Vertex(base1); GL.Vertex(base2);
        GL.Vertex(base3); GL.Vertex(base4);
        GL.End();
    }

    /// <summary>
    /// Draws a small cube cap (scale handle tip) as a screen-facing square.
    /// </summary>
    private void DrawScreenCube(Vector3 center, Color col, float halfSizeWorld)
    {
        DrawScreenSquare(center, col, halfSizeWorld);
    }

    private void DrawScreenSquare(Vector3 center, Color col, float halfSizeWorld)
    {
        Vector3 r = cam.transform.right * halfSizeWorld;
        Vector3 u = cam.transform.up * halfSizeWorld;

        Vector3 tl = center - r + u;
        Vector3 tr = center + r + u;
        Vector3 br = center + r - u;
        Vector3 bl = center - r - u;

        GL.Begin(GL.LINES);
        GL.Color(col);
        GL.Vertex(tl); GL.Vertex(tr);
        GL.Vertex(tr); GL.Vertex(br);
        GL.Vertex(br); GL.Vertex(bl);
        GL.Vertex(bl); GL.Vertex(tl);
        GL.End();
    }

    private void DrawDot(Vector3 center, Color col, float halfSizePx)
    {
        float h = halfSizePx * PixelToWorldScale(center);
        DrawScreenSquare(center, col, h);
    }

    /// <summary>
    /// Draws a ring (circle) around origin in the plane whose normal is worldAxis.
    /// </summary>
    private void DrawRing(Vector3 origin, Vector3 worldAxis, Color col, bool highlight)
    {
        Color c = highlight ? colorHover : col;
        float pixToWorld = PixelToWorldScale(origin);
        float radiusWorld = ringRadiusPx * pixToWorld;

        // Build two perpendicular vectors in the ring plane
        Vector3 perp1 = Vector3.Cross(worldAxis, cam.transform.forward);
        if (perp1.sqrMagnitude < 0.001f) perp1 = Vector3.Cross(worldAxis, Vector3.up);
        perp1.Normalize();
        Vector3 perp2 = Vector3.Cross(worldAxis, perp1).normalized;

        GL.Begin(GL.LINES);
        GL.Color(c);

        for (int i = 0; i < ringSegments; i++)
        {
            float a1 = Mathf.PI * 2f * i / ringSegments;
            float a2 = Mathf.PI * 2f * (i + 1) / ringSegments;

            Vector3 p1 = origin + (perp1 * Mathf.Cos(a1) + perp2 * Mathf.Sin(a1)) * radiusWorld;
            Vector3 p2 = origin + (perp1 * Mathf.Cos(a2) + perp2 * Mathf.Sin(a2)) * radiusWorld;

            GL.Vertex(p1);
            GL.Vertex(p2);
        }

        GL.End();
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns how many world units correspond to 1 screen pixel at the depth of worldPos.
    /// This keeps gizmo arms a fixed pixel length regardless of zoom.
    /// </summary>
    public float PixelToWorldScale(Vector3 worldPos)
    {
        float dist = Vector3.Distance(cam.transform.position, worldPos);

        if (cam.orthographic)
            return cam.orthographicSize * 2f / cam.pixelHeight;

        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float heightAtDist = 2f * dist * Mathf.Tan(fovRad * 0.5f);
        return heightAtDist / cam.pixelHeight;
    }
}