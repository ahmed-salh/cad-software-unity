using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Opens the native Windows "Open File" dialog via P/Invoke (comdlg32.dll).
/// Works in the Unity Editor and in Windows standalone builds.
/// Does NOT work on Mac / Linux / Android / iOS — guard with #if UNITY_STANDALONE_WIN.
///
/// Usage:
///   string path = WindowsFileDialog.OpenFile(
///       title:  "Import mesh",
///       filter: "Mesh files\0*.obj;*.stl\0OBJ files\0*.obj\0STL files\0*.stl\0All files\0*.*\0",
///       initialDir: "");
///   if (!string.IsNullOrEmpty(path)) { /* use path */ }
/// </summary>
public static class WindowsFileDialog
{
    // ── Win32 struct ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    // OFN flags
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_EXPLORER = 0x00080000;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the Windows Open File dialog.
    /// </summary>
    /// <param name="title">Dialog title bar text.</param>
    /// <param name="filter">
    /// Null-character delimited pairs: "Description\0*.ext\0".
    /// Example: "Mesh files\0*.obj;*.stl\0All files\0*.*\0"
    /// </param>
    /// <param name="initialDir">Starting directory, or "" for last used.</param>
    /// <returns>Full path to the selected file, or empty string if cancelled.</returns>
    public static string OpenFile(
        string title = "Open file",
        string filter = "All files\0*.*\0",
        string initialDir = "")
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Buffer must be large enough for MAX_PATH
        var fileBuffer = new StringBuilder(512);

        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = IntPtr.Zero,
            lpstrFilter = filter,
            lpstrFile = fileBuffer.ToString().PadRight(512, '\0'),
            nMaxFile = fileBuffer.Capacity,
            lpstrTitle = title,
            lpstrInitialDir = initialDir,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST |
                             OFN_NOCHANGEDIR | OFN_HIDEREADONLY |
                             OFN_EXPLORER,
            lpstrDefExt = "",
        };

        // We need to allocate a managed string buffer that P/Invoke can write into
        // Using a char array approach for reliable marshalling
        char[] buf = new char[512];
        ofn.lpstrFile = new string(buf);
        ofn.nMaxFile = buf.Length;

        if (GetOpenFileName(ref ofn))
            return ofn.lpstrFile.TrimEnd('\0');

        int err = Marshal.GetLastWin32Error();
        if (err != 0) // 0 = user cancelled (not an error)
            Debug.LogWarning($"[WindowsFileDialog] GetOpenFileName error code: {err}");

        return string.Empty;
#else
        Debug.LogWarning("[WindowsFileDialog] Native dialog only supported on Windows. Returning empty path.");
        return string.Empty;
#endif
    }
}