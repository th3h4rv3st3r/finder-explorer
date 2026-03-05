// Copyright (c) Finder Explorer. All rights reserved.
// FinderExplorer.h — Public C API, shared between C++ implementation and C# bridge.
// All functions use blittable types only (int, wchar_t*, int64_t, bool, HWND).
// Include this header in every .cpp that exports symbols declared here.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdbool.h>

// ---------------------------------------------------------------------------
// Export macro
// ---------------------------------------------------------------------------
#ifdef FINDEREXPLORER_EXPORTS
#  define FINDER_API extern "C" __declspec(dllexport)
#else
#  define FINDER_API extern "C" __declspec(dllimport)
#endif

// ---------------------------------------------------------------------------
// Thumbnail
// ---------------------------------------------------------------------------

/// Extracts a Shell thumbnail for `path` at the requested `size` (px, square).
/// On success returns 1 and writes top-down BGRA pixels into `*outPixels`.
/// Caller must free with FE_FreeThumbnail.
FINDER_API int  FE_GetThumbnail(
    const wchar_t* path,
    int            size,
    uint8_t**      outPixels,
    int*           outWidth,
    int*           outHeight);

/// Frees a pixel buffer returned by FE_GetThumbnail.
FINDER_API void FE_FreeThumbnail(uint8_t* pixels);

// ---------------------------------------------------------------------------
// Everything Search
// Everything64.dll is loaded at runtime via LoadLibrary — not linked.
// ---------------------------------------------------------------------------

/// Returns 1 if Everything64.dll is loaded and the database is ready.
FINDER_API int  FE_ES_IsAvailable(void);

/// Runs a search.  `scope` may be NULL (search everywhere).
/// Returns number of results (capped at maxResults), or -1 on error.
FINDER_API int  FE_ES_Search(
    const wchar_t* query,
    const wchar_t* scope,
    uint32_t       maxResults);

/// Returns the result count from the last FE_ES_Search call.
FINDER_API int  FE_ES_GetResultCount(void);

/// Writes result data into caller-supplied buffers.
/// `pathBuf` receives the full path (NUL-terminated, truncated to bufLen).
/// `modifiedFt` receives FILETIME as a 64-bit integer (0 if unknown).
/// Returns 1 on success, 0 if index is out of range.
FINDER_API int  FE_ES_GetResult(
    int       index,
    wchar_t*  pathBuf,
    int       bufLen,
    int64_t*  outSize,
    int64_t*  outModifiedFiletime,
    bool*     outIsDir);

/// Releases internal Everything result set memory.
FINDER_API void FE_ES_Reset(void);

// ---------------------------------------------------------------------------
// Shell Operations (IFileOperation COM — native Explorer progress dialogs)
// ---------------------------------------------------------------------------

/// Copies files/folders to `dest` folder.  `hwnd` is the owner window (may be NULL).
FINDER_API HRESULT FE_Shell_CopyItems(
    const wchar_t** sources,
    int             count,
    const wchar_t*  dest,
    HWND            hwnd);

/// Moves files/folders to `dest` folder.
FINDER_API HRESULT FE_Shell_MoveItems(
    const wchar_t** sources,
    int             count,
    const wchar_t*  dest,
    HWND            hwnd);

/// Deletes files/folders.  `recycle` = TRUE sends to Recycle Bin.
FINDER_API HRESULT FE_Shell_DeleteItems(
    const wchar_t** paths,
    int             count,
    HWND            hwnd,
    bool            recycle);

/// Renames a single item.
FINDER_API HRESULT FE_Shell_RenameItem(
    const wchar_t* path,
    const wchar_t* newName,
    HWND           hwnd);

/// Creates a new folder inside `parentPath`.
FINDER_API HRESULT FE_Shell_CreateFolder(
    const wchar_t* parentPath,
    const wchar_t* folderName,
    HWND           hwnd);

// ---------------------------------------------------------------------------
// Shell Context Menu (IContextMenu COM)
// ---------------------------------------------------------------------------

/// Shows the native Windows Shell context menu for the given paths
/// at screen coordinates (x, y).  `hwnd` is the owner window.
FINDER_API void FE_Shell_ShowContextMenu(
    const wchar_t** paths,
    int             count,
    HWND            hwnd,
    int             screenX,
    int             screenY);

// ---------------------------------------------------------------------------
// Drive Watcher (WM_DEVICECHANGE on a hidden window thread)
// ---------------------------------------------------------------------------

/// Callback signature: `arrived` = true when inserted, false when removed.
/// `driveLetter` is e.g. L"D:\\"
typedef void(__stdcall* FE_DriveCallback)(bool arrived, const wchar_t* driveLetter);

/// Starts drive-change monitoring.  Calls `callback` on a thread-pool thread.
FINDER_API void FE_DriveWatcher_Start(FE_DriveCallback callback);

/// Stops drive-change monitoring and cleans up the hidden window.
FINDER_API void FE_DriveWatcher_Stop(void);

// ---------------------------------------------------------------------------
// NanaZip / 7z CLI wrapper
// ---------------------------------------------------------------------------

/// Progress callback: 0.0 – 1.0
typedef void(__stdcall* FE_ProgressCallback)(double fraction);

/// Lists archive entries as a JSON array written into `outJson`.
/// Returns number of entries, or -1 on error.
FINDER_API int  FE_NanaZip_List(
    const wchar_t* archivePath,
    wchar_t*       outJson,
    int            bufLen);

/// Extracts archive to `dest`.  Progress reported via `cb` (may be NULL).
/// Returns 0 on success, non-zero on error.
FINDER_API int  FE_NanaZip_Extract(
    const wchar_t*      archivePath,
    const wchar_t*      dest,
    FE_ProgressCallback cb);

/// Compresses `sources` into `destArchive`.  Progress reported via `cb`.
/// Returns 0 on success, non-zero on error.
FINDER_API int  FE_NanaZip_Compress(
    const wchar_t**     sources,
    int                 count,
    const wchar_t*      destArchive,
    FE_ProgressCallback cb);

// ---------------------------------------------------------------------------
// Preview Handler (IPreviewHandler COM)
// ---------------------------------------------------------------------------

/// Instantiates the native Windows preview handler for `path` and renders it
/// into the provided `hwndParent` window within `bounds`.
/// Returns an opaque context pointer, or NULL on failure.
FINDER_API void* FE_Preview_Create(
    const wchar_t* path,
    HWND           hwndParent,
    const RECT*    bounds);

/// Returns 1 when Windows exposes a native preview handler for the file path.
/// Returns 0 when no native preview handler can be created.
FINDER_API int FE_Preview_CanHandle(const wchar_t* path);

/// Resizes the preview handler within the parent window.
FINDER_API void FE_Preview_Resize(void* context, const RECT* newBounds);

/// Destroys the preview handler and frees resources.
FINDER_API void FE_Preview_Destroy(void* context);

// ---------------------------------------------------------------------------
// Native Metadata Properties (IShellItem2::GetPropertyStore)
// ---------------------------------------------------------------------------

/// Gets extended metadata for `path`.
/// Returns 1 on success, 0 on failure.
FINDER_API int FE_Property_GetDetails(
    const wchar_t* path,
    wchar_t*       outType,       int typeLen,
    wchar_t*       outDimensions, int dimLen,
    wchar_t*       outDateTaken,  int dateLen,
    wchar_t*       outAuthors,    int authorLen);

// ---------------------------------------------------------------------------
// Tray & Lifecycle
// ---------------------------------------------------------------------------

/// Callback action code:
/// 0 = open/activate, 1 = double-click, 2 = exit selected.
typedef void(__stdcall* FE_TrayCallback)(int actionCode);

FINDER_API void FE_Tray_Create(HWND ownerHwnd, const wchar_t* tooltip, FE_TrayCallback callback);
FINDER_API void FE_Tray_Destroy(void);
FINDER_API void FE_Tray_ShowBalloon(const wchar_t* title, const wchar_t* msg, uint32_t timeoutMs);


