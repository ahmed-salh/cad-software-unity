using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Full CADUI — includes File menu (New, Open .cadproj, Save, Save As)
/// AND a separate "Import Mesh" button that opens a Windows file dialog
/// to import .obj / .stl files directly into the scene.
/// </summary>
public class CADUI : MonoBehaviour
{
    // ── File menu ──────────────────────────────────────────────────────────────
    [Header("File operations")]
    public Button btnNew;
    public Button btnOpen;       // opens .cadproj project file
    public Button btnSave;
    public Button btnSaveAs;

    [Header("Import (OBJ / STL)")]
    [Tooltip("Clicking this opens the Windows file dialog to import a mesh file.")]
    public Button btnImportMesh;

    [Header("Dialogs")]
    public FileDialogPanel fileDialog;
    public UnsavedChangesDialog unsavedDialog;

    [Header("Title bar")]
    public TMP_Text titleLabel;

    // ── Toolbar ────────────────────────────────────────────────────────────────
    [Header("Mode buttons")]
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

    [Header("Properties panel")]
    public GameObject propertiesPanel;
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
        // ── File ──────────────────────────────────────────────────────────────
        btnNew?.onClick.AddListener(OnNewClicked);
        btnOpen?.onClick.AddListener(OnOpenClicked);
        btnSave?.onClick.AddListener(OnSaveClicked);
        btnSaveAs?.onClick.AddListener(OnSaveAsClicked);

        // ── Import Mesh ───────────────────────────────────────────────────────
        btnImportMesh?.onClick.AddListener(OnImportMeshClicked);

        if (MeshImportManager.Instance != null)
        {
            MeshImportManager.Instance.OnImportComplete += objs =>
                SetStatus($"Imported {objs.Count} object(s) — " +
                          (objs.Count > 0 ? objs[0].objectName : ""));

            MeshImportManager.Instance.OnImportError += msg =>
                SetStatus($"Import error: {msg}");
        }

        // ── Shapes ────────────────────────────────────────────────────────────
        btnSelect?.onClick.AddListener(() => SetMode(CADMode.Select));

        btnBox?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateBox(DropPoint(), Vector3.one);
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnCylinder?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateCylinder(DropPoint());
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnSphere?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateSphere(DropPoint());
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnMeasure?.onClick.AddListener(() => SetMode(CADMode.Measure));

        // ── Boolean ───────────────────────────────────────────────────────────
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

        // ── Export ────────────────────────────────────────────────────────────
        btnExportSTL?.onClick.AddListener(ExportSTL);
        btnExportOBJ?.onClick.AddListener(ExportOBJ);

        // ── Properties ────────────────────────────────────────────────────────
        inputW?.onEndEdit.AddListener(v => ApplyDimension(0, v));
        inputH?.onEndEdit.AddListener(v => ApplyDimension(1, v));
        inputD?.onEndEdit.AddListener(v => ApplyDimension(2, v));

        // ── Events ────────────────────────────────────────────────────────────
        CADManager.Instance.OnSelectionChanged += RefreshProperties;
        CADManager.Instance.OnModeChanged += m => SetStatus($"Mode: {m}");

        if (CADFileManager.Instance != null)
        {
            CADFileManager.Instance.OnSceneNameChanged += UpdateTitleBar;
            CADFileManager.Instance.OnDirtyChanged += _ => UpdateTitleBar(CADFileManager.Instance.CurrentSceneName);
            CADFileManager.Instance.OnStatusMessage += SetStatus;
        }

        propertiesPanel?.SetActive(false);
        UpdateTitleBar("Untitled");
    }

    void OnDestroy()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged -= RefreshProperties;
    }

    // ── Import mesh ────────────────────────────────────────────────────────────

    private void OnImportMeshClicked()
    {
        SetStatus("Opening file dialog…");
        MeshImportManager.Instance.OpenAndImport();
    }

    // ── File operations ────────────────────────────────────────────────────────

    private void OnNewClicked()
    {
        if (CADFileManager.Instance.IsDirty)
            ShowUnsavedDialog("New Scene",
                saveAction: () => { CADFileManager.Instance.Save(); CADFileManager.Instance.NewScene(); },
                discardAction: () => CADFileManager.Instance.NewScene());
        else
            CADFileManager.Instance.NewScene();
    }

    private void OnOpenClicked()
    {
        if (CADFileManager.Instance.IsDirty)
            ShowUnsavedDialog("Open File",
                saveAction: () => { CADFileManager.Instance.Save(); ShowOpenDialog(); },
                discardAction: ShowOpenDialog);
        else
            ShowOpenDialog();
    }

    private void OnSaveClicked()
    {
        if (string.IsNullOrEmpty(CADFileManager.Instance.CurrentFilePath))
            OnSaveAsClicked();
        else
            CADFileManager.Instance.Save();
    }

    private void OnSaveAsClicked()
    {
        if (fileDialog == null)
        {
            string fallback = Path.Combine(
                Application.persistentDataPath,
                CADFileManager.Instance.CurrentSceneName + "_" +
                System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + CADFileManager.FILE_EXT);
            CADFileManager.Instance.SaveAs(fallback);
            return;
        }
        fileDialog.ShowSave(
            CADFileManager.Instance.CurrentSceneName,
            path => CADFileManager.Instance.SaveAs(path));
    }

    private void ShowOpenDialog()
    {
        if (fileDialog == null) { SetStatus("FileDialogPanel not assigned."); return; }
        fileDialog.ShowOpen(path => CADFileManager.Instance.Open(path));
    }

    private void ShowUnsavedDialog(string context,
        System.Action saveAction, System.Action discardAction)
    {
        if (unsavedDialog == null) { discardAction?.Invoke(); return; }
        unsavedDialog.Show($"You have unsaved changes.\nSave before {context}?",
            saveAction, discardAction);
    }

    // ── Title bar ──────────────────────────────────────────────────────────────

    private void UpdateTitleBar(string sceneName)
    {
        if (titleLabel == null) return;
        bool dirty = CADFileManager.Instance != null && CADFileManager.Instance.IsDirty;
        titleLabel.text = $"{sceneName}{(dirty ? " *" : "")}  —  Unity CAD";
    }

    // ── Mode ───────────────────────────────────────────────────────────────────

    private void SetMode(CADMode mode) => CADManager.Instance.SetMode(mode);

    private Vector3 DropPoint()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.position + cam.transform.forward * 5f : Vector3.zero;
    }

    // ── Properties panel ───────────────────────────────────────────────────────

    private void RefreshProperties(CADObject obj)
    {
        inspectedObject = obj;
        propertiesPanel?.SetActive(obj != null);
        if (obj == null) return;

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
        SetStatus($"STL → {path}");
    }

    private void ExportOBJ()
    {
        string path = Path.Combine(Application.persistentDataPath, "export.obj");
        OBJExporter.ExportAll(CADManager.Instance.AllObjects, path);
        SetStatus($"OBJ → {path}");
    }

    // ── Status ─────────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
        Debug.Log("[CADUI] " + msg);
    }
}