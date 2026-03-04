// Copyright (c) Finder Explorer. All rights reserved.
// NativeBridge.cs — Thin DllImport bridge to FinderExplorer.Native.Cpp.dll.
// Contains ZERO logic — only P/Invoke declarations with blittable types.
// All heavy lifting is in C++.

using System;
using System.Runtime.InteropServices;

namespace FinderExplorer.Native.Bridge;

/// <summary>
/// Raw P/Invoke declarations for FinderExplorer.Native.Cpp.dll.
/// Use the higher-level service wrappers in FinderExplorer.Core instead of calling these directly.
/// </summary>
internal static partial class NativeBridge
{
    private const string DllName = "FinderExplorer.Native.Cpp.dll";

    // -----------------------------------------------------------------------
    // Thumbnail
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extracts a shell thumbnail. Returns 1 on success; outPixels must be freed with FE_FreeThumbnail.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "FE_GetThumbnail", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetThumbnail(
        string         path,
        int            size,
        out IntPtr     outPixels,
        out int        outWidth,
        out int        outHeight);

    /// <summary>Frees a pixel buffer returned by GetThumbnail.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_FreeThumbnail")]
    internal static partial void FreeThumbnail(IntPtr pixels);

    // -----------------------------------------------------------------------
    // Everything Search
    // -----------------------------------------------------------------------

    /// <summary>Returns 1 if Everything is installed and its database is ready.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_ES_IsAvailable")]
    internal static partial int ES_IsAvailable();

    /// <summary>
    /// Executes a search.  scope may be null.
    /// Returns result count (&lt;= maxResults), or -1 on error.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "FE_ES_Search", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ES_Search(string query, string? scope, uint maxResults);

    /// <summary>Returns the result count from the last search.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_ES_GetResultCount")]
    internal static partial int ES_GetResultCount();

    /// <summary>
    /// Retrieves a single result by index.
    /// pathBuf must be pre-allocated (recommend MAX_PATH = 260 wchar_t).
    /// modifiedFiletime is a FILETIME packed in int64; 0 if unknown.
    /// Returns 1 on success, 0 if out of range.
    /// </summary>
    [LibraryImport(DllName, EntryPoint = "FE_ES_GetResult", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ES_GetResult(
        int      index,
        Span<char> pathBuf,   // wchar_t buffer — Span<char> is blittable to wchar_t*
        int      bufLen,
        out long outSize,
        out long outModifiedFiletime,
        [MarshalAs(UnmanagedType.U1)] out bool outIsDir);

    /// <summary>Releases the internal Everything result set.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_ES_Reset")]
    internal static partial void ES_Reset();

    // -----------------------------------------------------------------------
    // Shell Operations
    // -----------------------------------------------------------------------

    /// <summary>Copies items to dest via IFileOperation (shows native Explorer dialog).</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_CopyItems", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Shell_CopyItems(
        string[] sources, int count, string dest, IntPtr hwnd);

    /// <summary>Moves items to dest via IFileOperation.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_MoveItems", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Shell_MoveItems(
        string[] sources, int count, string dest, IntPtr hwnd);

    /// <summary>Deletes items via IFileOperation.  recycle = send to Recycle Bin.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_DeleteItems", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Shell_DeleteItems(
        string[] paths, int count, IntPtr hwnd,
        [MarshalAs(UnmanagedType.U1)] bool recycle);

    /// <summary>Renames a single item via IFileOperation.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_RenameItem", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Shell_RenameItem(string path, string newName, IntPtr hwnd);

    /// <summary>Creates a new folder via IFileOperation.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_CreateFolder", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int Shell_CreateFolder(string parentPath, string folderName, IntPtr hwnd);

    // -----------------------------------------------------------------------
    // Context Menu
    // -----------------------------------------------------------------------

    /// <summary>Shows the native Windows Shell context menu for the given paths.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_Shell_ShowContextMenu", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial void Shell_ShowContextMenu(
        string[] paths, int count, IntPtr hwnd, int screenX, int screenY);

    // -----------------------------------------------------------------------
    // Drive Watcher
    // -----------------------------------------------------------------------

    /// <summary>Delegate called on a thread-pool thread when a drive arrives or leaves.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal delegate void DriveCallback(
        [MarshalAs(UnmanagedType.U1)] bool arrived,
        string driveLetter);

    /// <summary>Starts drive-change monitoring.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_DriveWatcher_Start")]
    internal static partial void DriveWatcher_Start(DriveCallback callback);

    /// <summary>Stops drive-change monitoring.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_DriveWatcher_Stop")]
    internal static partial void DriveWatcher_Stop();

    // -----------------------------------------------------------------------
    // NanaZip
    // -----------------------------------------------------------------------

    /// <summary>Delegate called periodically with a progress fraction (0.0 – 1.0).</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate void ProgressCallback(double fraction);

    /// <summary>Lists archive entries as JSON into outJson.  Returns 0 on success.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_NanaZip_List", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int NanaZip_List(
        string archivePath, Span<char> outJson, int bufLen);

    /// <summary>Extracts archive to dest.  cb may be null.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_NanaZip_Extract", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int NanaZip_Extract(
        string archivePath, string dest, ProgressCallback? cb);

    /// <summary>Compresses sources into destArchive.  cb may be null.</summary>
    [LibraryImport(DllName, EntryPoint = "FE_NanaZip_Compress", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int NanaZip_Compress(
        string[] sources, int count, string destArchive, ProgressCallback? cb);
}
