// Copyright (c) Finder Explorer. All rights reserved.
// ContextMenuBridge.cpp — IContextMenu COM implementation for native Windows Shell context menus.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <objbase.h>
#include "../../include/FinderExplorer.h"

namespace
{
    struct CoInitGuard
    {
        HRESULT hr;
        CoInitGuard() : hr(::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE)) {}
        ~CoInitGuard() { if (SUCCEEDED(hr)) ::CoUninitialize(); }
    };

    // Build an IShellFolder + pidl array from a list of paths.
    // All paths must share the same parent directory.
    HRESULT GetFolderAndItems(
        const wchar_t** paths,
        int             count,
        IShellFolder**  ppFolder,
        LPITEMIDLIST**  pppidls)
    {
        if (count <= 0) return E_INVALIDARG;

        // Get pidl for the first item's parent
        LPITEMIDLIST pidlParent = nullptr;
        HRESULT hr = ::SHParseDisplayName(paths[0], nullptr, &pidlParent, 0, nullptr);
        if (FAILED(hr)) return hr;

        // Walk up to parent pidl — remove last child
        // We need the parent folder of the items.  Use SHBindToParent on the first item.
        IShellFolder* pDesktop = nullptr;
        hr = ::SHGetDesktopFolder(&pDesktop);
        if (FAILED(hr)) { ::CoTaskMemFree(pidlParent); return hr; }

        IShellFolder* pParentFolder = nullptr;
        LPCITEMIDLIST pidlChild     = nullptr;
        hr = ::SHBindToParent(pidlParent, IID_PPV_ARGS(&pParentFolder), &pidlChild);
        ::CoTaskMemFree(pidlParent);
        pDesktop->Release();
        if (FAILED(hr)) return hr;

        // Now get child pidls for all items
        auto* pidls = static_cast<LPITEMIDLIST*>(::CoTaskMemAlloc(sizeof(LPITEMIDLIST) * count));
        if (!pidls) { pParentFolder->Release(); return E_OUTOFMEMORY; }

        for (int i = 0; i < count; ++i)
        {
            LPITEMIDLIST fullPidl = nullptr;
            if (SUCCEEDED(::SHParseDisplayName(paths[i], nullptr, &fullPidl, 0, nullptr)))
            {
                LPCITEMIDLIST pChild = nullptr;
                IShellFolder* pTmp  = nullptr;
                ::SHBindToParent(fullPidl, IID_PPV_ARGS(&pTmp), &pChild);
                // Duplicate the child pidl
                pidls[i] = ::ILClone(pChild);
                if (pTmp) pTmp->Release();
                ::CoTaskMemFree(fullPidl);
            }
            else
                pidls[i] = nullptr;
        }

        *ppFolder = pParentFolder;
        *pppidls  = pidls;
        return S_OK;
    }
} // namespace

// ---------------------------------------------------------------------------
// FE_Shell_ShowContextMenu
// ---------------------------------------------------------------------------
FINDER_API void FE_Shell_ShowContextMenu(
    const wchar_t** paths,
    int             count,
    HWND            hwnd,
    int             screenX,
    int             screenY)
{
    if (!paths || count <= 0) return;

    CoInitGuard coInit;

    IShellFolder*   pFolder = nullptr;
    LPITEMIDLIST*   pidls   = nullptr;
    if (FAILED(GetFolderAndItems(paths, count, &pFolder, &pidls)))
        return;

    IContextMenu* pCtxMenu = nullptr;
    HRESULT hr = pFolder->GetUIObjectOf(
        hwnd,
        static_cast<UINT>(count),
        const_cast<LPCITEMIDLIST*>(pidls),
        IID_IContextMenu,
        nullptr,
        reinterpret_cast<void**>(&pCtxMenu));

    // Free pidls
    for (int i = 0; i < count; ++i)
        if (pidls[i]) ::ILFree(pidls[i]);
    ::CoTaskMemFree(pidls);
    pFolder->Release();

    if (FAILED(hr) || !pCtxMenu) return;

    // Try IContextMenu3 first (supports extended menu messages), fall back to 2 or 1
    IContextMenu3* pCtx3 = nullptr;
    IContextMenu2* pCtx2 = nullptr;
    pCtxMenu->QueryInterface(IID_PPV_ARGS(&pCtx3));
    if (!pCtx3)
        pCtxMenu->QueryInterface(IID_PPV_ARGS(&pCtx2));

    HMENU hMenu = ::CreatePopupMenu();
    if (SUCCEEDED(pCtxMenu->QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXPLORE)))
    {
        // Track menu (blocking call)
        int cmd = ::TrackPopupMenuEx(
            hMenu,
            TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
            screenX, screenY,
            hwnd,
            nullptr);

        if (cmd > 0)
        {
            CMINVOKECOMMANDINFOEX ici = {};
            ici.cbSize       = sizeof(ici);
            ici.fMask        = CMIC_MASK_UNICODE | CMIC_MASK_ASYNCOK;
            ici.hwnd         = hwnd;
            ici.lpVerb       = MAKEINTRESOURCEA(cmd - 1);
            ici.lpVerbW      = MAKEINTRESOURCEW(cmd - 1);
            ici.nShow        = SW_SHOWNORMAL;
            pCtxMenu->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&ici));
        }
    }

    ::DestroyMenu(hMenu);
    if (pCtx3) pCtx3->Release();
    if (pCtx2) pCtx2->Release();
    pCtxMenu->Release();
}
