// Copyright (c) Finder Explorer. All rights reserved.
// PreviewBridge.cpp - Native Windows IPreviewHandler integration.
// Renders PDF/Office/media previews into a provided HWND.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE

#include <windows.h>
#include <objbase.h>
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
    RECT             rcBounds = { 0, 0, 0, 0 };
};

namespace
{
    constexpr wchar_t kPreviewHandlerGuid[] = L"{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    static bool IsDirectoryPath(const wchar_t* path)
    {
        if (!path || !path[0])
            return false;

        const DWORD attributes = ::GetFileAttributesW(path);
        return attributes != INVALID_FILE_ATTRIBUTES &&
               (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    static HRESULT GetPreviewHandlerCLSIDFromRegistry(const wchar_t* ext, CLSID* pClsid)
    {
        if (!ext || !ext[0] || !pClsid)
            return E_INVALIDARG;

        wchar_t clsidStr[64] = {};
        DWORD size = sizeof(clsidStr);

        // 1. HKCR\.ext\shellex\{preview-guid}
        wchar_t subKey[256] = {};
        ::wsprintfW(subKey, L"%s\\shellex\\%s", ext, kPreviewHandlerGuid);
        if (::RegGetValueW(HKEY_CLASSES_ROOT, subKey, nullptr, RRF_RT_REG_SZ, nullptr, clsidStr, &size) == ERROR_SUCCESS)
            return ::CLSIDFromString(clsidStr, pClsid);

        // 2. HKCR\SystemFileAssociations\.ext\shellex\{preview-guid}
        ::wsprintfW(subKey, L"SystemFileAssociations\\%s\\shellex\\%s", ext, kPreviewHandlerGuid);
        size = sizeof(clsidStr);
        if (::RegGetValueW(HKEY_CLASSES_ROOT, subKey, nullptr, RRF_RT_REG_SZ, nullptr, clsidStr, &size) == ERROR_SUCCESS)
            return ::CLSIDFromString(clsidStr, pClsid);

        return E_FAIL;
    }

    static HRESULT GetPreviewHandlerCLSID(const wchar_t* path, CLSID* pClsid)
    {
        if (!path || !path[0] || !pClsid)
            return E_INVALIDARG;

        const wchar_t* ext = ::PathFindExtensionW(path);
        if (!ext || !ext[0])
            return E_FAIL;

        // Preferred: ask Shell association API (handles per-user overrides/progid mapping).
        wchar_t clsidStr[64] = {};
        DWORD cch = ARRAYSIZE(clsidStr);
        const HRESULT assocHr = ::AssocQueryStringW(
            ASSOCF_INIT_IGNOREUNKNOWN,
            ASSOCSTR_SHELLEXTENSION,
            ext,
            kPreviewHandlerGuid,
            clsidStr,
            &cch);

        if (SUCCEEDED(assocHr) && clsidStr[0] != L'\0')
        {
            const HRESULT clsidHr = ::CLSIDFromString(clsidStr, pClsid);
            if (SUCCEEDED(clsidHr))
                return S_OK;
        }

        return GetPreviewHandlerCLSIDFromRegistry(ext, pClsid);
    }

    static HRESULT TryBindPreviewHandlerFromShellItem(
        const wchar_t* path,
        IShellItem** outItem,
        IPreviewHandler** outHandler)
    {
        if (!path || !outItem || !outHandler)
            return E_INVALIDARG;

        *outItem = nullptr;
        *outHandler = nullptr;

        IShellItem* item = nullptr;
        HRESULT hr = ::SHCreateItemFromParsingName(path, nullptr, IID_PPV_ARGS(&item));
        if (FAILED(hr) || !item)
            return hr;

        IPreviewHandler* handler = nullptr;
        hr = item->BindToHandler(nullptr, BHID_PreviewHandler, IID_PPV_ARGS(&handler));
        if (FAILED(hr) || !handler)
        {
            item->Release();
            return FAILED(hr) ? hr : E_NOINTERFACE;
        }

        *outItem = item;
        *outHandler = handler;
        return S_OK;
    }

    static HRESULT CreatePreviewHandlerForPath(
        const wchar_t* path,
        IPreviewHandler** outHandler,
        IShellItem** outBoundItem)
    {
        if (!path || !outHandler)
            return E_INVALIDARG;

        *outHandler = nullptr;
        if (outBoundItem)
            *outBoundItem = nullptr;

        // Preferred: let shell resolve and bind directly for this exact item.
        IShellItem* shellItem = nullptr;
        IPreviewHandler* shellBoundHandler = nullptr;
        const HRESULT bindHr = TryBindPreviewHandlerFromShellItem(path, &shellItem, &shellBoundHandler);
        if (SUCCEEDED(bindHr) && shellBoundHandler)
        {
            *outHandler = shellBoundHandler;
            if (outBoundItem)
                *outBoundItem = shellItem;
            else
                shellItem->Release();
            return S_OK;
        }

        CLSID clsid = {};
        const HRESULT clsidHr = GetPreviewHandlerCLSID(path, &clsid);
        if (FAILED(clsidHr))
            return clsidHr;

        IPreviewHandler* handler = nullptr;
        const HRESULT createHr = ::CoCreateInstance(
            clsid,
            nullptr,
            CLSCTX_LOCAL_SERVER | CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&handler));

        if (FAILED(createHr) || !handler)
            return createHr;

        *outHandler = handler;
        return S_OK;
    }

    static bool TryInitializePreviewHandler(
        IPreviewHandler* handler,
        const wchar_t* path,
        IShellItem* shellItem)
    {
        if (!handler || !path)
            return false;

        // Many shell-bound handlers already arrive ready. Keep this initializer chain for
        // manually created handlers and for handlers that still expect explicit init.
        if (shellItem)
        {
            IInitializeWithItem* initItem = nullptr;
            if (SUCCEEDED(handler->QueryInterface(IID_PPV_ARGS(&initItem))))
            {
                const HRESULT hr = initItem->Initialize(shellItem, STGM_READ);
                initItem->Release();
                if (SUCCEEDED(hr) || hr == HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED) || hr == E_UNEXPECTED)
                    return true;
            }
        }

        IInitializeWithFile* initFile = nullptr;
        if (SUCCEEDED(handler->QueryInterface(IID_PPV_ARGS(&initFile))))
        {
            const HRESULT hr = initFile->Initialize(path, STGM_READ);
            initFile->Release();
            if (SUCCEEDED(hr) || hr == HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED) || hr == E_UNEXPECTED)
                return true;
        }

        IInitializeWithStream* initStream = nullptr;
        if (SUCCEEDED(handler->QueryInterface(IID_PPV_ARGS(&initStream))))
        {
            IStream* stream = nullptr;
            const HRESULT openHr = ::SHCreateStreamOnFileEx(
                path,
                STGM_READ,
                FILE_ATTRIBUTE_NORMAL,
                FALSE,
                nullptr,
                &stream);

            if (SUCCEEDED(openHr) && stream)
            {
                const HRESULT initHr = initStream->Initialize(stream, STGM_READ);
                stream->Release();
                initStream->Release();
                if (SUCCEEDED(initHr) || initHr == HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED) || initHr == E_UNEXPECTED)
                    return true;
            }
            else
            {
                initStream->Release();
            }
        }

        return false;
    }
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

FINDER_API int FE_Preview_CanHandle(const wchar_t* path)
{
    if (!path || !path[0] || IsDirectoryPath(path))
        return 0;

    IPreviewHandler* pHandler = nullptr;
    IShellItem* pShellItem = nullptr;
    if (FAILED(CreatePreviewHandlerForPath(path, &pHandler, &pShellItem)) || !pHandler)
        return 0;

    const bool shellBound = pShellItem != nullptr;
    const bool initialized = TryInitializePreviewHandler(pHandler, path, pShellItem);

    if (pShellItem)
        pShellItem->Release();

    pHandler->Release();
    return (initialized || shellBound) ? 1 : 0;
}

FINDER_API void* FE_Preview_Create(const wchar_t* path, HWND hwndParent, const RECT* bounds)
{
    if (!path || !path[0] || !hwndParent || !bounds || IsDirectoryPath(path))
        return nullptr;

    IPreviewHandler* pHandler = nullptr;
    IShellItem* pShellItem = nullptr;
    if (FAILED(CreatePreviewHandlerForPath(path, &pHandler, &pShellItem)) || !pHandler)
        return nullptr;

    const bool shellBound = pShellItem != nullptr;
    const bool initialized = TryInitializePreviewHandler(pHandler, path, pShellItem);

    if (pShellItem)
        pShellItem->Release();

    if (!initialized && !shellBound)
    {
        pHandler->Release();
        return nullptr;
    }

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
    auto* ctx = static_cast<FE_PreviewContext*>(context);
    if (ctx && ctx->pHandler && newBounds)
    {
        ctx->rcBounds = *newBounds;
        ctx->pHandler->SetRect(newBounds);
    }
}

FINDER_API void FE_Preview_Destroy(void* context)
{
    auto* ctx = static_cast<FE_PreviewContext*>(context);
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

