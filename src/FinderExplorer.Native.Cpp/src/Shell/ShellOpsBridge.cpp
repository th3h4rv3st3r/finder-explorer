// Copyright (c) Finder Explorer. All rights reserved.
// ShellOpsBridge.cpp — IFileOperation COM wrapper for native Explorer copy/move/delete/rename/create.
// IFileOperation gives progress dialogs, collision resolution UI, and proper Recycle Bin support.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <objbase.h>
#include "../../include/FinderExplorer.h"

// ---------------------------------------------------------------------------
// RAII COM guard
// ---------------------------------------------------------------------------
namespace
{
    struct CoInitGuard
    {
        HRESULT hr;
        CoInitGuard() : hr(::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)) {}
        ~CoInitGuard() { if (SUCCEEDED(hr)) ::CoUninitialize(); }
    };

    // Creates an IShellItem from a file system path.
    HRESULT ShellItemFromPath(const wchar_t* path, IShellItem** ppItem)
    {
        return ::SHCreateItemFromParsingName(path, nullptr, IID_PPV_ARGS(ppItem));
    }

    // Creates an IFileOperation instance with standard flags.
    HRESULT CreateFileOp(HWND hwnd, IFileOperation** ppOp)
    {
        HRESULT hr = ::CoCreateInstance(
            CLSID_FileOperation, nullptr,
            CLSCTX_ALL, IID_PPV_ARGS(ppOp));
        if (FAILED(hr)) return hr;

        DWORD flags = FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD;
        if (hwnd == nullptr)
            flags |= FOF_NOERRORUI | FOF_SILENT;

        (*ppOp)->SetOperationFlags(flags);
        if (hwnd) (*ppOp)->SetOwnerWindow(hwnd);
        return S_OK;
    }
} // namespace

// ---------------------------------------------------------------------------
// FE_Shell_CopyItems
// ---------------------------------------------------------------------------
FINDER_API HRESULT FE_Shell_CopyItems(
    const wchar_t** sources,
    int             count,
    const wchar_t*  dest,
    HWND            hwnd)
{
    CoInitGuard coInit;

    IFileOperation* pOp = nullptr;
    HRESULT hr = CreateFileOp(hwnd, &pOp);
    if (FAILED(hr)) return hr;

    IShellItem* pDestItem = nullptr;
    hr = ShellItemFromPath(dest, &pDestItem);
    if (FAILED(hr)) { pOp->Release(); return hr; }

    for (int i = 0; i < count; ++i)
    {
        IShellItem* pSrc = nullptr;
        if (SUCCEEDED(ShellItemFromPath(sources[i], &pSrc)))
        {
            pOp->CopyItem(pSrc, pDestItem, nullptr, nullptr);
            pSrc->Release();
        }
    }

    hr = pOp->PerformOperations();
    pDestItem->Release();
    pOp->Release();
    return hr;
}

// ---------------------------------------------------------------------------
// FE_Shell_MoveItems
// ---------------------------------------------------------------------------
FINDER_API HRESULT FE_Shell_MoveItems(
    const wchar_t** sources,
    int             count,
    const wchar_t*  dest,
    HWND            hwnd)
{
    CoInitGuard coInit;

    IFileOperation* pOp = nullptr;
    HRESULT hr = CreateFileOp(hwnd, &pOp);
    if (FAILED(hr)) return hr;

    IShellItem* pDestItem = nullptr;
    hr = ShellItemFromPath(dest, &pDestItem);
    if (FAILED(hr)) { pOp->Release(); return hr; }

    for (int i = 0; i < count; ++i)
    {
        IShellItem* pSrc = nullptr;
        if (SUCCEEDED(ShellItemFromPath(sources[i], &pSrc)))
        {
            pOp->MoveItem(pSrc, pDestItem, nullptr, nullptr);
            pSrc->Release();
        }
    }

    hr = pOp->PerformOperations();
    pDestItem->Release();
    pOp->Release();
    return hr;
}

// ---------------------------------------------------------------------------
// FE_Shell_DeleteItems
// ---------------------------------------------------------------------------
FINDER_API HRESULT FE_Shell_DeleteItems(
    const wchar_t** paths,
    int             count,
    HWND            hwnd,
    bool            recycle)
{
    CoInitGuard coInit;

    IFileOperation* pOp = nullptr;
    HRESULT hr = CreateFileOp(hwnd, &pOp);
    if (FAILED(hr)) return hr;

    // Override recycle flag
    DWORD flags = FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD;
    if (!recycle) flags |= FOF_ALLOWUNDO; // counter-intuitively, clear ALLOWUNDO disables recycle
    if (recycle)  flags |= FOF_ALLOWUNDO;
    else          flags |= FOFX_RECYCLEONDELETE;

    // Re-apply
    pOp->SetOperationFlags(recycle ? (FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD)
                                   : (FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD));

    for (int i = 0; i < count; ++i)
    {
        IShellItem* pItem = nullptr;
        if (SUCCEEDED(ShellItemFromPath(paths[i], &pItem)))
        {
            pOp->DeleteItem(pItem, nullptr);
            pItem->Release();
        }
    }

    hr = pOp->PerformOperations();
    pOp->Release();
    return hr;
}

// ---------------------------------------------------------------------------
// FE_Shell_RenameItem
// ---------------------------------------------------------------------------
FINDER_API HRESULT FE_Shell_RenameItem(
    const wchar_t* path,
    const wchar_t* newName,
    HWND           hwnd)
{
    CoInitGuard coInit;

    IFileOperation* pOp = nullptr;
    HRESULT hr = CreateFileOp(hwnd, &pOp);
    if (FAILED(hr)) return hr;

    IShellItem* pItem = nullptr;
    hr = ShellItemFromPath(path, &pItem);
    if (FAILED(hr)) { pOp->Release(); return hr; }

    pOp->RenameItem(pItem, newName, nullptr);
    hr = pOp->PerformOperations();

    pItem->Release();
    pOp->Release();
    return hr;
}

// ---------------------------------------------------------------------------
// FE_Shell_CreateFolder
// ---------------------------------------------------------------------------
FINDER_API HRESULT FE_Shell_CreateFolder(
    const wchar_t* parentPath,
    const wchar_t* folderName,
    HWND           hwnd)
{
    CoInitGuard coInit;

    IFileOperation* pOp = nullptr;
    HRESULT hr = CreateFileOp(hwnd, &pOp);
    if (FAILED(hr)) return hr;

    IShellItem* pParent = nullptr;
    hr = ShellItemFromPath(parentPath, &pParent);
    if (FAILED(hr)) { pOp->Release(); return hr; }

    hr = pOp->NewItem(pParent, FILE_ATTRIBUTE_DIRECTORY, folderName, nullptr, nullptr);
    if (SUCCEEDED(hr))
        hr = pOp->PerformOperations();

    pParent->Release();
    pOp->Release();
    return hr;
}
