using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Full CADUI — now includes three gizmo-mode buttons (Move / Scale / Rotate)
/// that control which gizmo is shown when an object is selected.
///
/// New Inspector slots:
///   btnGizmoMove   — activates Move gizmo   (W shortcut)
///   btnGizmoScale  — activates Scale gizmo  (R shortcut)
///   btnGizmoRotate — activates Rotate gizmo (E shortcut)
///
/// The active button is visually highlighted using the activeButtonColor.
/// </summary>
public class CADUI : MonoBehaviour
{
    // ── Gizmo mode buttons ─────────────────────────────────────────────────────
    [Header("Gizmo mode")]
    public Button btnGizmoMove;
    public Button btnGizmoScale;
    public Button btnGizmoRotate;
    [Tooltip("Background colour applied to the active gizmo button")]
    public Color activeButtonColor = new(0.25f, 0.55f, 1.00f);
    [Tooltip("Background colour for inactive gizmo buttons")]
    public Color inactiveButtonColor = new(0.22f, 0.22f, 0.22f);

    // ── File ──────────────────────────────────────────────────────────────────
    [Header("File operations")]
    public Button btnNew;
    public Button btnOpen;
    public Button btnSave;
    public Button btnSaveAs;

    [Header("Import (OBJ / STL)")]
    public Button btnImportMesh;

    [Header("Dialogs")]
    public FileDialogPanel fileDialog;
    public UnsavedChangesDialog unsavedDialog;

    [Header("Title bar")]
    public TMP_Text titleLabel;

    // ── Toolbar ───────────────────────────────────────────────────────────────
    [Header("Mode buttons")]
    public Button btnSelect;
    public Button btnBox;
    public Button btnCylinder;
    public Button btnSphere;
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

    // ── Internal ──────────────────────────────────────────────────────────────
    private CADObject inspectedObject;
    private bool updatingFromCode;

    private Button[] gizmoBtns;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        // ── Gizmo mode ────────────────────────────────────────────────────────
        gizmoBtns = new[] { btnGizmoMove, btnGizmoScale, btnGizmoRotate };

        btnGizmoMove?.onClick.AddListener(() => SetGizmoMode(GizmoMode.Move));
        btnGizmoScale?.onClick.AddListener(() => SetGizmoMode(GizmoMode.Scale));
        btnGizmoRotate?.onClick.AddListener(() => SetGizmoMode(GizmoMode.Rotate));

        if (GizmoController.Instance != null)
            GizmoController.Instance.OnModeChanged += RefreshGizmoButtons;

        RefreshGizmoButtons(GizmoController.Instance?.ActiveMode ?? GizmoMode.Move);

        // ── File ──────────────────────────────────────────────────────────────
        btnNew?.onClick.AddListener(OnNewClicked);
        btnOpen?.onClick.AddListener(OnOpenClicked);
        btnSave?.onClick.AddListener(OnSaveClicked);
        btnSaveAs?.onClick.AddListener(OnSaveAsClicked);
        btnImportMesh?.onClick.AddListener(OnImportMeshClicked);

        if (MeshImportManager.Instance != null)
        {
            MeshImportManager.Instance.OnImportComplete += objs =>
                SetStatus($"Imported {objs.Count} object(s): {(objs.Count > 0 ? objs[0].objectName : "")}");
            MeshImportManager.Instance.OnImportError += msg =>
                SetStatus($"Import error: {msg}");
        }

        // ── Shapes ────────────────────────────────────────────────────────────
        btnSelect?.onClick.AddListener(() => SetMode(CADMode.Select));

        btnBox?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateBox(Vector3.zero, Vector3.one);
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnCylinder?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateCylinder(Vector3.zero);
            if (obj != null) { CADManager.Instance.Select(obj); SetMode(CADMode.Select); }
        });

        btnSphere?.onClick.AddListener(() =>
        {
            var obj = ShapeFactory.Instance.CreateSphere(Vector3.zero);
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
            CADFileManager.Instance.OnDirtyChanged +=
                _ => UpdateTitleBar(CADFileManager.Instance.CurrentSceneName);
            CADFileManager.Instance.OnStatusMessage += SetStatus;
        }

        propertiesPanel?.SetActive(false);
        UpdateTitleBar("Untitled");
    }

    void Update()
    {
        // ── Gizmo keyboard shortcuts (W / E / R — standard 3D software convention) ──
        if (Keyboard.current == null) return;
        if (Keyboard.current.wKey.wasPressedThisFrame) SetGizmoMode(GizmoMode.Move);
        if (Keyboard.current.eKey.wasPressedThisFrame) SetGizmoMode(GizmoMode.Rotate);
        if (Keyboard.current.rKey.wasPressedThisFrame) SetGizmoMode(GizmoMode.Scale);

        // ── File shortcuts ────────────────────────────────────────────────────
        if (Keyboard.current.ctrlKey.isPressed)
        {
            if (Keyboard.current.sKey.wasPressedThisFrame)
            {
                if (Keyboard.current.shiftKey.isPressed) OnSaveAsClicked();
                else OnSaveClicked();
            }
            if (Keyboard.current.nKey.wasPressedThisFrame) OnNewClicked();
            if (Keyboard.current.oKey.wasPressedThisFrame) OnOpenClicked();
        }
    }

    void OnDestroy()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSelectionChanged -= RefreshProperties;
        if (GizmoController.Instance != null)
            GizmoController.Instance.OnModeChanged -= RefreshGizmoButtons;
    }

    // ── Gizmo mode ─────────────────────────────────────────────────────────────

    private void SetGizmoMode(GizmoMode mode)
    {
        GizmoController.Instance?.SetMode(mode);
        SetStatus($"Gizmo: {mode}");
    }

    private void RefreshGizmoButtons(GizmoMode active)
    {
        Button[] btns = { btnGizmoMove, btnGizmoScale, btnGizmoRotate };
        GizmoMode[] modes = { GizmoMode.Move, GizmoMode.Scale, GizmoMode.Rotate };

        for (int i = 0; i < btns.Length; i++)
        {
            if (btns[i] == null) continue;
            var img = btns[i].GetComponent<Image>();
            if (img != null)
                img.color = (modes[i] == active) ? activeButtonColor : inactiveButtonColor;
        }
    }

    // ── Import mesh ────────────────────────────────────────────────────────────

    private void OnImportMeshClicked()
    {
        SetStatus("Opening file dialog…");
        MeshImportManager.Instance?.OpenAndImport();
    }

    // ── File ───────────────────────────────────────────────────────────────────

    private void OnNewClicked()
    {
        if (CADFileManager.Instance.IsDirty)
            ShowUnsavedDialog("New Scene",
                () => { CADFileManager.Instance.Save(); CADFileManager.Instance.NewScene(); },
                () => CADFileManager.Instance.NewScene());
        else
            CADFileManager.Instance.NewScene();
    }

    private void OnOpenClicked()
    {
        if (CADFileManager.Instance.IsDirty)
            ShowUnsavedDialog("Open File",
                () => { CADFileManager.Instance.Save(); ShowOpenDialog(); },
                ShowOpenDialog);
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
                System.DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                "." + CADFileManager.FILE_EXT);
            CADFileManager.Instance.SaveAs(fallback);
            return;
        }
        fileDialog.ShowSave(CADFileManager.Instance.CurrentSceneName,
            path => CADFileManager.Instance.SaveAs(path));
    }

    private void ShowOpenDialog()
    {
        if (fileDialog == null) { SetStatus("FileDialogPanel not assigned."); return; }
        fileDialog.ShowOpen(path => CADFileManager.Instance.Open(path));
    }

    private void ShowUnsavedDialog(string ctx,
        System.Action save, System.Action discard)
    {
        if (unsavedDialog == null) { discard?.Invoke(); return; }
        unsavedDialog.Show($"You have unsaved changes.\nSave before {ctx}?", save, discard);
    }

    // ── Title bar ──────────────────────────────────────────────────────────────

    private void UpdateTitleBar(string name)
    {
        if (titleLabel == null) return;
        bool dirty = CADFileManager.Instance != null && CADFileManager.Instance.IsDirty;
        titleLabel.text = $"{name}{(dirty ? " *" : "")}  —  Unity CAD";
    }

    // ── Mode ───────────────────────────────────────────────────────────────────

    private void SetMode(CADMode mode) => CADManager.Instance.SetMode(mode);

    private Vector3 DropPoint()
    {
        var c = Camera.main;
        return c != null ? c.transform.position + c.transform.forward * 5f : Vector3.zero;
    }

    // ── Properties ─────────────────────────────────────────────────────────────

    private void RefreshProperties(CADObject obj)
    {
        inspectedObject = obj;
        propertiesPanel?.SetActive(obj != null);
        if (obj == null) return;

        updatingFromCode = true;
        var s = obj.transform.localScale;
        if (inputW) inputW.text = s.x.ToString("F3");
        if (inputH) inputH.text = s.y.ToString("F3");
        if (inputD) inputD.text = s.z.ToString("F3");
        if (objectNameLabel) objectNameLabel.text = obj.objectName;
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
        if (statusLabel) statusLabel.text = msg;
        Debug.Log("[CADUI] " + msg);
    }
}