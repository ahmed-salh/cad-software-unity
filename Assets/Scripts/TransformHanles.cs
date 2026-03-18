using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Renders 6 handle cubes (±X ±Y ±Z) around the selected object.
///
/// Fixes applied:
///  1. Handles are children that hide/show individually — the parent GameObject
///     stays ALWAYS ACTIVE so OnEnable/OnDisable subscription is stable.
///  2. A static flag (IsDraggingHandle) lets SelectionManager skip its raycast
///     on the same frame a handle drag begins, preventing accidental deselect.
/// </summary>
public class TransformHandles : MonoBehaviour
{
    [Header("Visual")]
    public float handleScreenRadius = 18f;   // px hit area — increase if hard to click
    public float handleWorldSize = 0.13f;

    public Color colorIdle = new(1.00f, 0.85f, 0.10f);
    public Color colorActive = new(1.00f, 0.30f, 0.10f);

    /// <summary>True while a handle drag is in progress. SelectionManager reads this.</summary>
    public static bool IsDraggingHandle { get; private set; }

    // ── Internals ──────────────────────────────────────────────────────────────
    private const int N = 6;

    private GameObject[] handleGOs;
    private MeshRenderer[] handleMRs;
    private Material[] handleMats;

    private Camera cam;
    private CADObject target;

    private bool isDragging;
    private int activeHandle = -1;

    private Vector3 startScale;
    private Vector3 startPos;
    private Vector3 startMouseWorld;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Awake()
    {
        cam = Camera.main;
        BuildHandles();
        ShowHandles(false);   // hide the child cubes, NOT this GameObject
    }

    void Start()
    {
        // Start() runs after all Awake() calls → CADManager is guaranteed ready
        CADManager.Instance.OnSelectionChanged += OnSelect;
    }

    void OnDestroy()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged -= OnSelect;
    }

    void Update()
    {
        if (target == null) return;
        PositionHandles();
        ProcessInput();
    }

    // ── Selection callback ─────────────────────────────────────────────────────

    private void OnSelect(CADObject obj)
    {
        target = obj;
        ShowHandles(obj != null);
        if (obj != null) PositionHandles();
    }

    // ── Handle creation ────────────────────────────────────────────────────────

    private void BuildHandles()
    {
        handleGOs = new GameObject[N];
        handleMRs = new MeshRenderer[N];
        handleMats = new Material[N];

        var proto = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var sharedMesh = proto.GetComponent<MeshFilter>().sharedMesh;
        Destroy(proto);

        for (int i = 0; i < N; i++)
        {
            var go = new GameObject("Handle_" + i);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * handleWorldSize;

            go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;

            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default")) { color = colorIdle };
            mr.material = mat;

            handleGOs[i] = go;
            handleMRs[i] = mr;
            handleMats[i] = mat;
        }
    }

    private void ShowHandles(bool show)
    {
        if (handleGOs == null) return;
        foreach (var go in handleGOs) go.SetActive(show);
    }

    // ── Handle positioning ─────────────────────────────────────────────────────

    private void PositionHandles()
    {
        Bounds b = target.GetWorldBounds();
        Vector3 c = b.center;
        Vector3 e = b.extents;

        handleGOs[0].transform.position = c + new Vector3(e.x, 0, 0);
        handleGOs[1].transform.position = c + new Vector3(-e.x, 0, 0);
        handleGOs[2].transform.position = c + new Vector3(0, e.y, 0);
        handleGOs[3].transform.position = c + new Vector3(0, -e.y, 0);
        handleGOs[4].transform.position = c + new Vector3(0, 0, e.z);
        handleGOs[5].transform.position = c + new Vector3(0, 0, -e.z);
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    private void ProcessInput()
    {
        var mouse = Mouse.current;

        if (mouse.leftButton.wasPressedThisFrame && !isDragging)
        {
            int h = ClosestHandleToMouse();
            if (h >= 0)
            {
                BeginDrag(h);
                // Tell SelectionManager not to process this click
                IsDraggingHandle = true;
                return;
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame && isDragging)
        {
            EndDrag();
            IsDraggingHandle = false;
            return;
        }

        if (isDragging) ContinueDrag();
    }

    private void BeginDrag(int h)
    {
        isDragging = true;
        activeHandle = h;
        startScale = target.transform.localScale;
        startPos = target.transform.position;
        startMouseWorld = ProjectMouseOnDragPlane();
        handleMats[h].color = colorActive;
    }

    private void EndDrag()
    {
        isDragging = false;
        if (activeHandle >= 0) handleMats[activeHandle].color = colorIdle;
        activeHandle = -1;
        target.dimensions = target.transform.localScale;
        CADManager.Instance.NotifySceneChanged();
    }

    private void ContinueDrag()
    {
        Vector3 curr = ProjectMouseOnDragPlane();
        Vector3 delta3 = curr - startMouseWorld;

        int axis = activeHandle / 2;
        int sign = (activeHandle % 2 == 0) ? 1 : -1;
        Vector3 axisV = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        float delta = Vector3.Dot(delta3, axisV) * sign;

        Vector3 newScale = startScale;
        newScale[axis] = Mathf.Max(0.01f, startScale[axis] + delta);

        Vector3 newPos = startPos;
        newPos[axis] = startPos[axis] + delta * 0.5f * sign;

        target.transform.localScale = newScale;
        target.transform.position = newPos;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private int ClosestHandleToMouse()
    {
        int best = -1;
        float bestDist = handleScreenRadius;
        Vector2 mpos = Mouse.current.position.ReadValue();

        for (int i = 0; i < N; i++)
        {
            if (!handleGOs[i].activeSelf) continue;
            Vector3 sc = cam.WorldToScreenPoint(handleGOs[i].transform.position);
            if (sc.z < 0) continue;
            float d = Vector2.Distance(mpos, sc);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private Vector3 ProjectMouseOnDragPlane()
    {
        Plane plane = new(cam.transform.forward, target.transform.position);
        Vector2 mpos = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(mpos);
        if (plane.Raycast(ray, out float t)) return ray.GetPoint(t);
        return startMouseWorld;
    }
}