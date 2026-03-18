using UnityEngine;
using TMPro;

/// <summary>
/// Shows W/H/D dimension labels in world space next to the selected object.
/// Fixed: null-guards on CADManager.Instance in OnEnable/OnDisable,
///        subscription moved to Start so CADManager is guaranteed to exist.
/// </summary>
public class MeasurementSystem : MonoBehaviour
{
    [Header("Prefab — world-space TextMeshPro (NOT TextMeshProUGUI)")]
    public GameObject labelPrefab;

    [Header("Units")]
    public float unitScale = 1f;
    public string unitLabel = "u";

    [Header("Style")]
    public Color lineColor = new(1f, 0.85f, 0f);
    public float lineOffset = 0.25f;
    public float fontSize = 0.25f;

    // ── Internals ──────────────────────────────────────────────────────────────
    private const int AXES = 3;
    private TextMeshPro[] labels = new TextMeshPro[AXES];
    private LineRenderer[] lines = new LineRenderer[AXES];
    private CADObject target;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Awake()
    {
        BuildUI();
        SetVisible(false);
    }

    void OnEnable()
    {
        // Guard: CADManager may not be initialized yet at OnEnable time
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged += OnSelect;
    }

    void OnDisable()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged -= OnSelect;
    }

    // Start is called after all Awake() calls, so CADManager is guaranteed ready
    void Start()
    {
        CADManager.Instance.OnSelectionChanged -= OnSelect; // safe no-op if not subscribed
        CADManager.Instance.OnSelectionChanged += OnSelect;
    }

    void LateUpdate()
    {
        if (target == null) return;
        UpdateDimensions();
        FaceCameraLabels();
    }

    // ── Selection callback ─────────────────────────────────────────────────────

    private void OnSelect(CADObject obj)
    {
        target = obj;
        SetVisible(obj != null);
    }

    // ── Build ──────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var lineMat = new Material(Shader.Find("Sprites/Default")) { color = lineColor };

        for (int i = 0; i < AXES; i++)
        {
            // Label
            GameObject go = labelPrefab != null
                ? Instantiate(labelPrefab, transform)
                : new GameObject("DimLabel_" + "XYZ"[i]);

            if (labelPrefab == null) go.transform.SetParent(transform, false);

            var tmp = go.GetComponent<TextMeshPro>() ?? go.AddComponent<TextMeshPro>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = lineColor;
            labels[i] = tmp;

            // Line
            var lineGo = new GameObject("DimLine_" + "XYZ"[i]);
            lineGo.transform.SetParent(transform, false);
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = 0.015f;
            lr.material = lineMat;
            lr.startColor = lr.endColor = lineColor;
            lr.useWorldSpace = true;
            lines[i] = lr;
        }
    }

    // ── Dimension update ───────────────────────────────────────────────────────

    private void UpdateDimensions()
    {
        Bounds b = target.GetWorldBounds();
        Vector3 c = b.center;
        Vector3 e = b.extents;
        float o = lineOffset;

        // Width (X)
        lines[0].SetPositions(new[]
        {
            c + new Vector3(-e.x, -e.y - o, 0),
            c + new Vector3( e.x, -e.y - o, 0)
        });
        labels[0].transform.position = c + new Vector3(0, -e.y - o - 0.18f, 0);
        labels[0].text = $"W {b.size.x * unitScale:F2} {unitLabel}";

        // Height (Y)
        lines[1].SetPositions(new[]
        {
            c + new Vector3(e.x + o, -e.y, 0),
            c + new Vector3(e.x + o,  e.y, 0)
        });
        labels[1].transform.position = c + new Vector3(e.x + o + 0.25f, 0, 0);
        labels[1].text = $"H {b.size.y * unitScale:F2} {unitLabel}";

        // Depth (Z)
        lines[2].SetPositions(new[]
        {
            c + new Vector3(0, -e.y - o, -e.z),
            c + new Vector3(0, -e.y - o,  e.z)
        });
        labels[2].transform.position = c + new Vector3(0, -e.y - o - 0.18f, e.z + 0.15f);
        labels[2].text = $"D {b.size.z * unitScale:F2} {unitLabel}";
    }

    private void FaceCameraLabels()
    {
        Vector3 camFwd = Camera.main.transform.forward;
        foreach (var l in labels)
            if (l != null) l.transform.rotation = Quaternion.LookRotation(camFwd);
    }

    private void SetVisible(bool v)
    {
        foreach (var l in labels) if (l != null) l.gameObject.SetActive(v);
        foreach (var r in lines) if (r != null) r.gameObject.SetActive(v);
    }
}