using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Toolbar + properties panel.
/// Fixed: null-checks on every optional Inspector reference so missing
/// assignments don't throw at Start() or RefreshProperties().
/// </summary>
public class CADUI : MonoBehaviour
{
    [Header("Mode buttons (all optional — assign what you have)")]
    public Button btnSelect;
    public Button btnBox;
    public Button btnCylinder;
    public Button btnSphere;
    public Button btnExtrude;
    public Button btnMeasure;
    public Button btnBoolUnion;
    public Button btnBoolSub;
    public Button btnBoolIntersect;

    [Header("Export")]
    public Button btnExportSTL;
    public Button btnExportOBJ;

    [Header("Properties panel (assign in Inspector)")]
    public GameObject propertiesPanel;   // root panel — hides when nothing is selected
    public TMP_InputField inputW;
    public TMP_InputField inputH;
    public TMP_InputField inputD;
    public TMP_Text objectNameLabel;

    [Header("Status bar")]
    public TMP_Text statusLabel;

    // ── Internal ───────────────────────────────────────────────────────────────
    private CADObject inspectedObject;
    private bool updatingFromCode;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    void Start()
    {
        // --- Mode buttons ---
        btnSelect?.onClick.AddListener(() => SetMode(CADMode.Select));

        btnBox?.onClick.AddListener(() =>
        {
            SetMode(CADMode.CreateBox);
            var obj = ShapeFactory.Instance.CreateBox(DropPoint(), Vector3.one);
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnCylinder?.onClick.AddListener(() =>
        {
            SetMode(CADMode.CreateCylinder);
            var obj = ShapeFactory.Instance.CreateCylinder(DropPoint());
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnSphere?.onClick.AddListener(() =>
        {
            SetMode(CADMode.CreateSphere);
            var obj = ShapeFactory.Instance.CreateSphere(DropPoint());
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnMeasure?.onClick.AddListener(() => SetMode(CADMode.Measure));

        // --- Boolean ops ---
        btnBoolUnion?.onClick.AddListener(() =>
            BooleanOperations.Instance.Union(
                CADManager.Instance.SelectedObject,
                CADManager.Instance.SecondarySelection));

        btnBoolSub?.onClick.AddListener(() =>
            BooleanOperations.Instance.Subtract(
                CADManager.Instance.SelectedObject,
                CADManager.Instance.SecondarySelection));

        btnBoolIntersect?.onClick.AddListener(() =>
            BooleanOperations.Instance.Intersect(
                CADManager.Instance.SelectedObject,
                CADManager.Instance.SecondarySelection));

        // --- Export ---
        btnExportSTL?.onClick.AddListener(ExportSTL);
        btnExportOBJ?.onClick.AddListener(ExportOBJ);

        // --- Properties input fields ---
        inputW?.onEndEdit.AddListener(v => ApplyDimension(0, v));
        inputH?.onEndEdit.AddListener(v => ApplyDimension(1, v));
        inputD?.onEndEdit.AddListener(v => ApplyDimension(2, v));

        // --- Events ---
        CADManager.Instance.OnSelectionChanged += RefreshProperties;
        CADManager.Instance.OnModeChanged += m => SetStatus($"Mode: {m}");

        // Hide properties panel initially — safely
        if (propertiesPanel != null)
            propertiesPanel.SetActive(false);
        else
            Debug.LogWarning("CADUI: propertiesPanel is not assigned in the Inspector. " +
                             "Assign a UI panel GameObject to hide it when nothing is selected.");
    }

    void OnDestroy()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged -= RefreshProperties;
    }

    // ── Mode ───────────────────────────────────────────────────────────────────

    private void SetMode(CADMode mode) => CADManager.Instance.SetMode(mode);

    // ── Shape placement ────────────────────────────────────────────────────────

    private Vector3 DropPoint()
    {
        var cam = Camera.main;
        return cam != null
            ? cam.transform.position + cam.transform.forward * 5f
            : Vector3.zero;
    }

    // ── Properties panel ───────────────────────────────────────────────────────

    private void RefreshProperties(CADObject obj)
    {
        inspectedObject = obj;
        bool hasObj = obj != null;

        // Null-check — panel may not be assigned yet
        if (propertiesPanel != null)
            propertiesPanel.SetActive(hasObj);

        if (!hasObj) return;

        updatingFromCode = true;
        var s = obj.transform.localScale;
        if (inputW != null) inputW.text = s.x.ToString("F3");
        if (inputH != null) inputH.text = s.y.ToString("F3");
        if (inputD != null) inputD.text = s.z.ToString("F3");
        if (objectNameLabel != null) objectNameLabel.text = obj.objectName;
        updatingFromCode = false;
    }

    private void ApplyDimension(int axis, string raw)
    {
        if (updatingFromCode || inspectedObject == null) return;
        if (!float.TryParse(raw, out float v) || v <= 0f) return;

        var s = inspectedObject.transform.localScale;
        s[axis] = v;
        inspectedObject.ApplyDimensions(s);
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    private void ExportSTL()
    {
        string path = Path.Combine(Application.persistentDataPath, "export.stl");
        STLExporter.ExportAll(CADManager.Instance.AllObjects, path);
        SetStatus($"STL saved → {path}");
    }

    private void ExportOBJ()
    {
        string path = Path.Combine(Application.persistentDataPath, "export.obj");
        OBJExporter.ExportAll(CADManager.Instance.AllObjects, path);
        SetStatus($"OBJ saved → {path}");
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
        Debug.Log("[CADUI] " + msg);
    }
}