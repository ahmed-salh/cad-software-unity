using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Handles New / Save / Save As / Open for .cadproj files.
///
/// .cadproj is plain JSON — open in any text editor.
///
/// Primitives (Box, Cylinder, Sphere) are reconstructed via ShapeFactory
/// on load so they stay lightweight. Custom / extruded meshes serialize
/// every vertex + triangle so nothing is lost.
///
/// Hook up:
///   CADFileManager.Instance.NewScene();
///   CADFileManager.Instance.Save();
///   CADFileManager.Instance.SaveAs(path);
///   CADFileManager.Instance.Open(path);
///
/// The class fires events so CADUI can update the title bar etc.
/// </summary>
public class CADFileManager : MonoBehaviour
{
    public static CADFileManager Instance { get; private set; }

    public const string FILE_EXT = "cadproj";
    public const string FILE_FILTER = "CAD Project (*.cadproj)|*.cadproj";

    // ── State ──────────────────────────────────────────────────────────────────

    public string CurrentFilePath { get; private set; } = string.Empty;
    public string CurrentSceneName { get; private set; } = "Untitled";
    public bool IsDirty { get; private set; }          // unsaved changes flag

    // ── Events ─────────────────────────────────────────────────────────────────

    public event Action<string> OnSceneNameChanged;   // arg = new name
    public event Action<bool> OnDirtyChanged;        // arg = isDirty
    public event Action<string> OnStatusMessage;       // arg = message for status bar

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        CADManager.Instance.OnSceneChanged += MarkDirty;
    }

    void OnDestroy()
    {
        if (CADManager.Instance != null)
            CADManager.Instance.OnSceneChanged -= MarkDirty;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Clears the scene. Prompts nothing — caller handles "unsaved changes" dialog.</summary>
    public void NewScene()
    {
        ClearScene();
        CurrentFilePath = string.Empty;
        CurrentSceneName = "Untitled";
        IsDirty = false;
        OnSceneNameChanged?.Invoke(CurrentSceneName);
        OnDirtyChanged?.Invoke(false);
        OnStatusMessage?.Invoke("New scene created.");
        Debug.Log("[CADFile] New scene.");
    }

    /// <summary>Saves to the current path, or behaves like SaveAs if no path yet.</summary>
    public void Save()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            // No path yet — fall back to a default location so Save always works
            // without needing a file dialog. CADUI can override this with SaveAs.
            string defaultDir = Application.persistentDataPath;
            CurrentFilePath = Path.Combine(defaultDir, CurrentSceneName + "." + FILE_EXT);
        }
        SaveToPath(CurrentFilePath);
    }

    /// <summary>Saves to an explicit path chosen by the caller (e.g. from a file dialog).</summary>
    public void SaveAs(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        CurrentFilePath = path;
        CurrentSceneName = Path.GetFileNameWithoutExtension(path);
        SaveToPath(path);
        OnSceneNameChanged?.Invoke(CurrentSceneName);
    }

    /// <summary>Loads a .cadproj file, replacing the current scene.</summary>
    public void Open(string path)
    {
        if (!File.Exists(path))
        {
            OnStatusMessage?.Invoke($"File not found: {path}");
            Debug.LogWarning($"[CADFile] File not found: {path}");
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            var data = JsonUtility.FromJson<CADSceneData>(json);
            LoadScene(data);

            CurrentFilePath = path;
            CurrentSceneName = data.sceneName;
            IsDirty = false;

            OnSceneNameChanged?.Invoke(CurrentSceneName);
            OnDirtyChanged?.Invoke(false);
            OnStatusMessage?.Invoke($"Opened: {path}");
            Debug.Log($"[CADFile] Opened {path}  ({data.objects.Count} objects)");
        }
        catch (Exception ex)
        {
            OnStatusMessage?.Invoke($"Open failed: {ex.Message}");
            Debug.LogError($"[CADFile] Open failed: {ex}");
        }
    }

    // ── Dirty tracking ─────────────────────────────────────────────────────────

    public void MarkDirty()
    {
        if (IsDirty) return;
        IsDirty = true;
        OnDirtyChanged?.Invoke(true);
    }

    // ── Serialize ──────────────────────────────────────────────────────────────

    private void SaveToPath(string path)
    {
        try
        {
            var data = BuildSceneData();
            string json = JsonUtility.ToJson(data, prettyPrint: true);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);

            IsDirty = false;
            OnDirtyChanged?.Invoke(false);
            OnStatusMessage?.Invoke($"Saved → {path}");
            Debug.Log($"[CADFile] Saved {path}");
        }
        catch (Exception ex)
        {
            OnStatusMessage?.Invoke($"Save failed: {ex.Message}");
            Debug.LogError($"[CADFile] Save failed: {ex}");
        }
    }

    private CADSceneData BuildSceneData()
    {
        var data = new CADSceneData
        {
            version = "1.0",
            sceneName = CurrentSceneName,
            savedAt = DateTime.UtcNow.ToString("o"),
            objects = new List<CADObjectData>()
        };

        foreach (var obj in CADManager.Instance.AllObjects)
        {
            var t = obj.transform;
            var mf = obj.GetComponent<MeshFilter>();
            var entry = new CADObjectData
            {
                name = obj.objectName,
                primitiveType = obj.primitiveType.ToString(),
                position = SerializedVector3.From(t.position),
                rotation = SerializedVector3.From(t.eulerAngles),
                scale = SerializedVector3.From(t.localScale),
            };

            // Custom meshes (extruded, boolean results) need full vertex data
            bool isCustom = obj.primitiveType == CADPrimitive.Custom;
            if (isCustom && mf?.sharedMesh != null)
            {
                var mesh = mf.sharedMesh;
                entry.hasMeshData = true;

                var verts = mesh.vertices;
                var norms = mesh.normals;
                entry.meshVertices = new SerializedVector3[verts.Length];
                entry.meshNormals = new SerializedVector3[norms.Length];
                for (int i = 0; i < verts.Length; i++)
                    entry.meshVertices[i] = SerializedVector3.From(verts[i]);
                for (int i = 0; i < norms.Length; i++)
                    entry.meshNormals[i] = SerializedVector3.From(norms[i]);

                entry.meshTriangles = mesh.triangles;
            }

            data.objects.Add(entry);
        }

        return data;
    }

    // ── Deserialize ────────────────────────────────────────────────────────────

    private void LoadScene(CADSceneData data)
    {
        ClearScene();

        foreach (var entry in data.objects)
        {
            if (!Enum.TryParse<CADPrimitive>(entry.primitiveType, out var primType))
                primType = CADPrimitive.Custom;

            CADObject obj = null;

            switch (primType)
            {
                case CADPrimitive.Box:
                    obj = ShapeFactory.Instance.CreateBox(
                        entry.position.ToVector3(), entry.scale.ToVector3());
                    break;

                case CADPrimitive.Cylinder:
                    obj = ShapeFactory.Instance.CreateCylinder(
                        entry.position.ToVector3(),
                        radius: entry.scale.ToVector3().x * 0.5f,
                        height: entry.scale.ToVector3().y * 2f);
                    break;

                case CADPrimitive.Sphere:
                    obj = ShapeFactory.Instance.CreateSphere(
                        entry.position.ToVector3(),
                        diameter: entry.scale.ToVector3().x);
                    break;

                case CADPrimitive.Custom:
                    obj = RebuildCustomMesh(entry);
                    break;
            }

            if (obj == null) continue;

            // Restore exact transform (ShapeFactory may offset slightly)
            obj.transform.position = entry.position.ToVector3();
            obj.transform.eulerAngles = entry.rotation.ToVector3();
            obj.transform.localScale = entry.scale.ToVector3();
            obj.objectName = entry.name;
            obj.dimensions = entry.scale.ToVector3();
        }

        CADManager.Instance.NotifySceneChanged();
    }

    private CADObject RebuildCustomMesh(CADObjectData entry)
    {
        if (!entry.hasMeshData || entry.meshVertices == null || entry.meshTriangles == null)
        {
            Debug.LogWarning($"[CADFile] Custom object '{entry.name}' has no mesh data — skipped.");
            return null;
        }

        var verts = new Vector3[entry.meshVertices.Length];
        var norms = new Vector3[entry.meshNormals?.Length ?? 0];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = entry.meshVertices[i].ToVector3();
        for (int i = 0; i < norms.Length; i++)
            norms[i] = entry.meshNormals[i].ToVector3();

        var mesh = new Mesh { name = entry.name };
        mesh.vertices = verts;
        mesh.triangles = entry.meshTriangles;
        if (norms.Length == verts.Length)
            mesh.normals = norms;
        else
            mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(entry.name);
        go.layer = LayerMask.NameToLayer("CADObject");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = ShapeFactory.Instance != null
            ? ShapeFactory.Instance.defaultMaterial
            : new Material(Shader.Find("Standard"));

        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        var cadObj = go.AddComponent<CADObject>();
        cadObj.primitiveType = CADPrimitive.Custom;
        cadObj.objectName = entry.name;
        cadObj.dimensions = entry.scale.ToVector3();

        return cadObj;
    }

    // ── Scene clear ────────────────────────────────────────────────────────────

    private void ClearScene()
    {
        CADManager.Instance.Deselect();

        // Take a snapshot because Destroy unregisters from the list mid-loop
        var all = new List<CADObject>(CADManager.Instance.AllObjects);
        foreach (var obj in all)
        {
            CADManager.Instance.UnregisterObject(obj);
            Destroy(obj.gameObject);
        }
    }
}