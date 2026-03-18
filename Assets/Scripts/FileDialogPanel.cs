using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Self-contained in-game file browser panel.
/// Works on ALL platforms with no third-party dependency.
///
/// Supports two modes: Save and Open.
///
/// Setup (Inspector):
///   • panelRoot      — the root Panel GameObject (starts inactive)
///   • fileListContent — the ScrollView's Content transform
///   • fileItemPrefab — a prefab with a Button + TMP_Text child named "Label"
///   • inputFilename  — TMP_InputField for the filename
///   • labelTitle     — TMP_Text showing "Save As" or "Open"
///   • btnConfirm     — confirm button
///   • btnCancel      — cancel button
///   • labelCurrentDir — TMP_Text showing current directory path
///   • btnDirUp       — Button to navigate up one directory
/// </summary>
public class FileDialogPanel : MonoBehaviour
{
    [Header("Panel references")]
    public GameObject panelRoot;
    public Transform fileListContent;
    public GameObject fileItemPrefab;
    public TMP_InputField inputFilename;
    public TMP_Text labelTitle;
    public TMP_Text labelCurrentDir;
    public Button btnConfirm;
    public Button btnCancel;
    public Button btnDirUp;

    // ── State ──────────────────────────────────────────────────────────────────

    public enum DialogMode { Save, Open }

    private DialogMode mode;
    private string currentDir;
    private System.Action<string> onConfirm;  // called with full path on confirm
    private List<GameObject> listItems = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        btnConfirm?.onClick.AddListener(OnConfirmClicked);
        btnCancel?.onClick.AddListener(Close);
        btnDirUp?.onClick.AddListener(NavigateUp);
        panelRoot?.SetActive(false);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void ShowSave(string defaultName, System.Action<string> callback)
    {
        mode = DialogMode.Save;
        onConfirm = callback;
        if (labelTitle != null) labelTitle.text = "Save As";
        if (inputFilename != null) inputFilename.text = defaultName;
        Open(GetStartDirectory());
    }

    public void ShowOpen(System.Action<string> callback)
    {
        mode = DialogMode.Open;
        onConfirm = callback;
        if (labelTitle != null) labelTitle.text = "Open";
        if (inputFilename != null) inputFilename.text = "";
        Open(GetStartDirectory());
    }

    // ── Directory navigation ───────────────────────────────────────────────────

    private void Open(string dir)
    {
        if (!Directory.Exists(dir)) dir = GetStartDirectory();
        currentDir = dir;
        panelRoot?.SetActive(true);
        Refresh();
    }

    public void Close() => panelRoot?.SetActive(false);

    private void NavigateUp()
    {
        var parent = Directory.GetParent(currentDir);
        if (parent != null) { currentDir = parent.FullName; Refresh(); }
    }

    private void Refresh()
    {
        if (labelCurrentDir != null)
            labelCurrentDir.text = ShortenPath(currentDir);

        // Clear existing list items
        foreach (var go in listItems) Destroy(go);
        listItems.Clear();

        // Directories first
        try
        {
            foreach (var d in Directory.GetDirectories(currentDir))
                AddListItem("[DIR]  " + Path.GetFileName(d), () =>
                {
                    currentDir = d;
                    Refresh();
                });
        }
        catch { /* permission denied — skip */ }

        // .cadproj files
        try
        {
            foreach (var f in Directory.GetFiles(currentDir, "*." + CADFileManager.FILE_EXT))
            {
                string captured = f;
                AddListItem(Path.GetFileName(f), () =>
                {
                    if (inputFilename != null)
                        inputFilename.text = Path.GetFileNameWithoutExtension(captured);
                });
            }
        }
        catch { /* skip */ }
    }

    private void AddListItem(string label, System.Action onClick)
    {
        if (fileItemPrefab == null || fileListContent == null) return;

        var go = Instantiate(fileItemPrefab, fileListContent);
        var btn = go.GetComponent<Button>();
        var txt = go.GetComponentInChildren<TMP_Text>();

        if (txt != null) txt.text = label;
        btn?.onClick.AddListener(() => onClick());
        listItems.Add(go);
    }

    // ── Confirm ────────────────────────────────────────────────────────────────

    private void OnConfirmClicked()
    {
        string filename = inputFilename != null ? inputFilename.text.Trim() : "Untitled";
        if (string.IsNullOrEmpty(filename)) filename = "Untitled";

        // Append extension if missing
        if (!filename.EndsWith("." + CADFileManager.FILE_EXT))
            filename += "." + CADFileManager.FILE_EXT;

        string fullPath = Path.Combine(currentDir, filename);

        if (mode == DialogMode.Open && !File.Exists(fullPath))
        {
            Debug.LogWarning($"[FileDialog] File does not exist: {fullPath}");
            return;
        }

        Close();
        onConfirm?.Invoke(fullPath);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string GetStartDirectory()
    {
        // Default to persistent data path — always exists and is writable
        return Application.persistentDataPath;
    }

    private static string ShortenPath(string path)
    {
        const int MAX = 50;
        if (path.Length <= MAX) return path;
        return "…" + path.Substring(path.Length - MAX);
    }
}