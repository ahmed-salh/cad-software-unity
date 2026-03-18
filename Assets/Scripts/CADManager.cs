using System.Collections.Generic;
using UnityEngine;

public enum CADMode { Select, CreateBox, CreateCylinder, CreateSphere, Extrude, Measure, Boolean }

/// <summary>
/// Central singleton. Owns mode state, selection, and the object registry.
/// All other systems talk through this rather than to each other directly.
/// </summary>
public class CADManager : MonoBehaviour
{
    public static CADManager Instance { get; private set; }

    public CADMode CurrentMode { get; private set; } = CADMode.Select;
    public List<CADObject> AllObjects { get; } = new();
    public CADObject SelectedObject { get; private set; }
    public CADObject SecondarySelection { get; private set; } // used for boolean ops

    public event System.Action<CADObject> OnSelectionChanged;
    public event System.Action<CADMode> OnModeChanged;
    public event System.Action OnSceneChanged;   // fire after add/delete

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Mode ─────────────────────────────────────────────────────────────────

    public void SetMode(CADMode mode)
    {
        CurrentMode = mode;
        // Clear secondary when leaving boolean mode
        if (mode != CADMode.Boolean) ClearSecondary();
        OnModeChanged?.Invoke(mode);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void Select(CADObject obj)
    {
        if (CurrentMode == CADMode.Boolean && SelectedObject != null && obj != SelectedObject)
        {
            // Second pick in boolean mode
            SecondarySelection?.SetHighlight(false);
            SecondarySelection = obj;
            obj.SetHighlight(true, secondary: true);
            OnSelectionChanged?.Invoke(obj);
            return;
        }

        SelectedObject?.SetHighlight(false);
        SecondarySelection?.SetHighlight(false);
        SecondarySelection = null;
        SelectedObject = obj;
        SelectedObject?.SetHighlight(true);
        OnSelectionChanged?.Invoke(obj);
    }

    public void Deselect()
    {
        SelectedObject?.SetHighlight(false);
        SecondarySelection?.SetHighlight(false);
        SelectedObject = null;
        SecondarySelection = null;
        OnSelectionChanged?.Invoke(null);
    }

    private void ClearSecondary()
    {
        SecondarySelection?.SetHighlight(false);
        SecondarySelection = null;
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    public void RegisterObject(CADObject obj)
    {
        if (!AllObjects.Contains(obj)) AllObjects.Add(obj);
    }

    public void UnregisterObject(CADObject obj)
    {
        AllObjects.Remove(obj);
    }

    public void DeleteSelected()
    {
        if (SelectedObject == null) return;
        UnregisterObject(SelectedObject);
        Destroy(SelectedObject.gameObject);
        SelectedObject = null;
        SecondarySelection = null;
        OnSelectionChanged?.Invoke(null);
        OnSceneChanged?.Invoke();
    }

    /// <summary>
    /// Call this after any geometry add so listeners (UI, undo stack) stay in sync.
    /// </summary>
    public void NotifySceneChanged() => OnSceneChanged?.Invoke();
}