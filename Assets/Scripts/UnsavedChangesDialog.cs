using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple modal dialog: "You have unsaved changes. Save, Discard or Cancel?"
///
/// Setup (Inspector):
///   • panelRoot  — root Panel (starts inactive)
///   • labelMsg   — TMP_Text showing the message
///   • btnSave    — "Save" button
///   • btnDiscard — "Discard" button
///   • btnCancel  — "Cancel" button
/// </summary>
public class UnsavedChangesDialog : MonoBehaviour
{
    public GameObject panelRoot;
    public TMP_Text labelMsg;
    public Button btnSave;
    public Button btnDiscard;
    public Button btnCancel;

    private Action onSave;
    private Action onDiscard;

    void Awake()
    {
        btnSave?.onClick.AddListener(() => Respond(onSave));
        btnDiscard?.onClick.AddListener(() => Respond(onDiscard));
        btnCancel?.onClick.AddListener(() => Close());
        panelRoot?.SetActive(false);
    }

    /// <summary>
    /// Show the dialog.
    /// <param name="message">Body text shown to the user.</param>
    /// <param name="saveAction">Called when user clicks Save (should save then proceed).</param>
    /// <param name="discardAction">Called when user clicks Discard (proceed without saving).</param>
    /// </summary>
    public void Show(string message, Action saveAction, Action discardAction)
    {
        if (labelMsg != null) labelMsg.text = message;
        onSave = saveAction;
        onDiscard = discardAction;
        panelRoot?.SetActive(true);
    }

    private void Respond(Action action)
    {
        Close();
        action?.Invoke();
    }

    private void Close() => panelRoot?.SetActive(false);
}