using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Orchestrates the full import flow:
///   1. Opens the Windows native file dialog (OBJ / STL filter)
///   2. Dispatches to OBJImporter or STLImporter based on extension
///   3. Centres the imported mesh(es) at the world origin
///   4. Frames the camera on the result
///   5. Fires OnImportComplete so CADUI can show a status message
///
/// Attach to a persistent GameObject (e.g. inside CADRoot).
/// Call:  MeshImportManager.Instance.OpenAndImport();
/// </summary>
public class MeshImportManager : MonoBehaviour
{
    public static MeshImportManager Instance { get; private set; }

    /// <summary>Fired after a successful import. Arg = list of spawned CADObjects.</summary>
    public event System.Action<List<CADObject>> OnImportComplete;

    /// <summary>Fired on any import error. Arg = error message.</summary>
    public event System.Action<string> OnImportError;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the native Windows file dialog then imports the chosen file.
    /// Safe to call from a UI button click.
    /// </summary>
    public void OpenAndImport()
    {
        string path = WindowsFileDialog.OpenFile(
            title: "Import OBJ or STL",
            filter: "Mesh files\0*.obj;*.stl\0" +
                    "OBJ files\0*.obj\0" +
                    "STL files\0*.stl\0" +
                    "All files\0*.*\0",
            initialDir: "");

        if (string.IsNullOrEmpty(path)) return; // user cancelled

        StartCoroutine(ImportCoroutine(path));
    }

    // ── Import coroutine ───────────────────────────────────────────────────────
    // Using a coroutine so we can yield after the heavy parse and let Unity
    // update the UI / prevent a hard freeze on very large meshes.

    private IEnumerator ImportCoroutine(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        // Give Unity one frame to render the "Importing…" status if CADUI updates it
        yield return null;

        List<CADObject> imported = null;

        if (ext == ".obj")
        {
            imported = OBJImporter.Import(path);
        }
        else if (ext == ".stl")
        {
            var obj = STLImporter.Import(path);
            if (obj != null) imported = new List<CADObject> { obj };
        }
        else
        {
            string msg = $"Unsupported format '{ext}'. Only .obj and .stl are supported.";
            Debug.LogWarning($"[MeshImportManager] {msg}");
            OnImportError?.Invoke(msg);
            yield break;
        }

        if (imported == null || imported.Count == 0)
        {
            string msg = $"Import failed or file contained no geometry: {Path.GetFileName(path)}";
            Debug.LogWarning($"[MeshImportManager] {msg}");
            OnImportError?.Invoke(msg);
            yield break;
        }

        // ── Post-process ───────────────────────────────────────────────────────

        CentreObjects(imported);
        yield return null; // let transform changes propagate

        FrameCamera(imported);

        // Select the first imported object
        if (imported.Count > 0)
            CADManager.Instance.Select(imported[0]);

        string success = imported.Count == 1
            ? $"Imported: {imported[0].objectName}"
            : $"Imported {imported.Count} objects from {Path.GetFileName(path)}";

        Debug.Log($"[MeshImportManager] {success}");
        OnImportComplete?.Invoke(imported);
    }

    // ── Post-process helpers ───────────────────────────────────────────────────

    /// <summary>Moves all imported objects so their combined centre is at the world origin.</summary>
    private static void CentreObjects(List<CADObject> objects)
    {
        // Compute combined bounds
        var combined = new Bounds(objects[0].GetWorldBounds().center, Vector3.zero);
        foreach (var obj in objects)
            combined.Encapsulate(obj.GetWorldBounds());

        Vector3 offset = -combined.center;
        foreach (var obj in objects)
            obj.transform.position += offset;
    }

    /// <summary>Moves and orients the main camera to frame all imported objects.</summary>
    private static void FrameCamera(List<CADObject> objects)
    {
        var cam = Camera.main;
        if (cam == null) return;

        var orbit = cam.GetComponent<OrbitCamera>();
        if (orbit == null) return;

        // Compute combined world bounds after centring
        var combined = new Bounds(objects[0].GetWorldBounds().center, Vector3.zero);
        foreach (var obj in objects)
            combined.Encapsulate(obj.GetWorldBounds());

        orbit.FocusOn(combined);
    }
}