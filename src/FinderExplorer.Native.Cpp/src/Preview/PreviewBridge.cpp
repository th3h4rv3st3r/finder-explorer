// Copyright (c) Finder Explorer. All rights reserved.
// PreviewBridge.cpp — Native Windows IPreviewHandler integration.
// Renders PDFs, Office docs, Images, Videos natively into a provided HWND.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include "../../include/FinderExplorer.h"

#pragma comment(lib, "shlwapi.lib")

// ---------------------------------------------------------------------------
// Struct
// ---------------------------------------------------------------------------
struct FE_PreviewContext
{
    IPreviewHandler* pHandler = nullptr;
    HWND             hwndParent = nullptr;
    RECT             rcBounds = {0, 0, 0, 0};
};

// ---------------------------------------------------------------------------
// Helper: Find Preview Handler CLSID for a given extension
// ---------------------------------------------------------------------------
static HRESULT GetPreviewHandlerCLSID(const wchar_t* path, CLSID* pClsid)
{
    const wchar_t* ext = ::PathFindExtensionW(path);
    if (!ext || !ext[0]) return E_FAIL;

    // 1. Check HKCR\.ext
    wchar_t clsidStr[64] = {};
    DWORD size = sizeof(clsidStr);
    
    // Look for shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}
    wchar_t subKey[256];
    ::wsprintfW(subKey, L"%s\\shellex\\{8895b1c6-b41f-4c1c-a562-0d564250836f}", ext);
    if (::RegGetValueW(HKEY_CLASSES_ROOT, subKey, nullptr, RRF_RT_REG_SZ, nullptr, clsidStr, &size) == ERROR_SUCCESS)
    {
        return ::CLSIDFromString(clsidStr, pClsid);
    }

    // 2. Check HKCR\SystemFileAssociations\.ext
    ::wsprintfW(subKey, L"SystemFileAssociations\\%s\\shellex\\{8895b1c6-b41f-4c1c-a562-0d564250836f}", ext);
    size = sizeof(clsidStr);
    if (::RegGetValueW(HKEY_CLASSES_ROOT, subKey, nullptr, RRF_RT_REG_SZ, nullptr, clsidStr, &size) == ERROR_SUCCESS)
    {
        return ::CLSIDFromString(clsidStr, pClsid);
    }

    // 3. Check AppUserModelID / ProgID if not found directly
    // This could be expanded, but usually the first two catch PDF/Word/etc.

    return E_FAIL;
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

FINDER_API void* FE_Preview_Create(const wchar_t* path, HWND hwndParent, const RECT* bounds)
{
    if (!path || !hwndParent || !bounds) return nullptr;

    CLSID clsid;
    if (FAILED(GetPreviewHandlerCLSID(path, &clsid)))
        return nullptr; // No preview handler for this type

    IPreviewHandler* pHandler = nullptr;
    if (FAILED(::CoCreateInstance(clsid, nullptr, CLSCTX_LOCAL_SERVER | CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&pHandler))))
        return nullptr;

    // Initialize with file
    bool initialized = false;
    IInitializeWithFile* pInitFile = nullptr;
    if (SUCCEEDED(pHandler->QueryInterface(IID_PPV_ARGS(&pInitFile))))
    {
        if (SUCCEEDED(pInitFile->Initialize(path, STGM_READ)))
            initialized = true;
        pInitFile->Release();
    }

    if (!initialized)
    {
        IInitializeWithItem* pInitItem = nullptr;
        if (SUCCEEDED(pHandler->QueryInterface(IID_PPV_ARGS(&pInitItem))))
        {
            IShellItem* pItem = nullptr;
            if (SUCCEEDED(::SHCreateItemFromParsingName(path, nullptr, IID_PPV_ARGS(&pItem))))
            {
                if (SUCCEEDED(pInitItem->Initialize(pItem, STGM_READ)))
                    initialized = true;
                pItem->Release();
            }
            pInitItem->Release();
        }
    }

    if (!initialized)
    {
        IInitializeWithStream* pInitStream = nullptr;
        if (SUCCEEDED(pHandler->QueryInterface(IID_PPV_ARGS(&pInitStream))))
        {
            IStream* pStream = nullptr;
            if (SUCCEEDED(::SHCreateStreamOnFileEx(path, STGM_READ, FILE_ATTRIBUTE_NORMAL, FALSE, nullptr, &pStream)))
            {
                if (SUCCEEDED(pInitStream->Initialize(pStream, STGM_READ)))
                    initialized = true;
                pStream->Release();
            }
            pInitStream->Release();
        }
    }

    if (!initialized)
    {
        pHandler->Release();
        return nullptr;
    }

    // Set Window and Rect
    if (FAILED(pHandler->SetWindow(hwndParent, bounds)))
    {
        pHandler->Release();
        return nullptr;
    }

    if (FAILED(pHandler->DoPreview()))
    {
        pHandler->Unload();
        pHandler->Release();
        return nullptr;
    }

    FE_PreviewContext* ctx = new FE_PreviewContext();
    ctx->pHandler = pHandler;
    ctx->hwndParent = hwndParent;
    ctx->rcBounds = *bounds;

    return ctx;
}

FINDER_API void FE_Preview_Resize(void* context, const RECT* newBounds)
{
    auto ctx = static_cast<FE_PreviewContext*>(context);
    if (ctx && ctx->pHandler && newBounds)
    {
        ctx->rcBounds = *newBounds;
        ctx->pHandler->SetRect(newBounds);
    }
}

FINDER_API void FE_Preview_Destroy(void* context)
{
    auto ctx = static_cast<FE_PreviewContext*>(context);
    if (ctx)
    {
        if (ctx->pHandler)
        {
            ctx->pHandler->Unload();
            ctx->pHandler->Release();
        }
        delete ctx;
    }
}
