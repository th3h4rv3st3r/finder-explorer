// Copyright (c) Finder Explorer. All rights reserved.
// EverythingBridge.cpp — Everything SDK IPC via runtime-loaded Everything64.dll.
// Uses LoadLibrary so the app works even when Everything is not installed.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <algorithm>
#include "../../include/FinderExplorer.h"

// ---------------------------------------------------------------------------
// Everything SDK function pointer typedefs (Unicode versions)
// ---------------------------------------------------------------------------
using PFN_SetSearchW             = void   (__stdcall*)(const wchar_t*);
using PFN_SetMatchCase           = void   (__stdcall*)(BOOL);
using PFN_SetMax                 = void   (__stdcall*)(DWORD);
using PFN_SetRequestFlags        = void   (__stdcall*)(DWORD);
using PFN_QueryW                 = BOOL   (__stdcall*)(BOOL);
using PFN_GetNumResults          = DWORD  (__stdcall*)();
using PFN_GetResultFullPathNameW = DWORD  (__stdcall*)(DWORD dwIndex, wchar_t* buf, DWORD bufLen);
using PFN_GetResultDateModified  = BOOL   (__stdcall*)(DWORD dwIndex, FILETIME* ft);
using PFN_GetResultSize          = BOOL   (__stdcall*)(DWORD dwIndex, LARGE_INTEGER* size);
using PFN_IsFileResult           = BOOL   (__stdcall*)(DWORD dwIndex);
using PFN_IsDatabaseLoaded       = BOOL   (__stdcall*)();
using PFN_CleanUp                = void   (__stdcall*)();

// Everything request flags
static constexpr DWORD EVERYTHING_REQUEST_FILE_NAME       = 0x00000001;
static constexpr DWORD EVERYTHING_REQUEST_PATH            = 0x00000002;
static constexpr DWORD EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME = 0x00000004;
static constexpr DWORD EVERYTHING_REQUEST_EXTENSION       = 0x00000008;
static constexpr DWORD EVERYTHING_REQUEST_SIZE            = 0x00000010;
static constexpr DWORD EVERYTHING_REQUEST_DATE_MODIFIED   = 0x00000400;

// ---------------------------------------------------------------------------
// Singleton loader
// ---------------------------------------------------------------------------
namespace
{
    struct EverythingLib
    {
        HMODULE hMod = nullptr;

        PFN_SetSearchW             SetSearchW             = nullptr;
        PFN_SetMatchCase           SetMatchCase           = nullptr;
        PFN_SetMax                 SetMax                 = nullptr;
        PFN_SetRequestFlags        SetRequestFlags        = nullptr;
        PFN_QueryW                 QueryW                 = nullptr;
        PFN_GetNumResults          GetNumResults          = nullptr;
        PFN_GetResultFullPathNameW GetResultFullPathNameW = nullptr;
        PFN_GetResultDateModified  GetResultDateModified  = nullptr;
        PFN_GetResultSize          GetResultSize          = nullptr;
        PFN_IsFileResult           IsFileResult           = nullptr;
        PFN_IsDatabaseLoaded       IsDatabaseLoaded       = nullptr;
        PFN_CleanUp                CleanUp                = nullptr;

        bool loaded = false;

        void Load()
        {
            if (loaded) return;
            loaded = true;

            // Try to find Everything's own DLL directory via registry
            wchar_t dllPath[MAX_PATH] = {};
            DWORD   cbPath = sizeof(dllPath);
            LSTATUS status = ::RegGetValueW(
                HKEY_LOCAL_MACHINE,
                L"SOFTWARE\\voidtools\\Everything",
                L"Install_Dir",
                RRF_RT_REG_SZ, nullptr,
                dllPath, &cbPath);

            if (status == ERROR_SUCCESS)
                ::wcscat_s(dllPath, L"\\Everything64.dll");
            else
                ::wcscpy_s(dllPath, L"Everything64.dll"); // system PATH fallback

            hMod = ::LoadLibraryW(dllPath);
            if (!hMod) return;

#define LOAD_FN(name) name = reinterpret_cast<PFN_##name>(::GetProcAddress(hMod, "Everything_" #name))
            LOAD_FN(SetSearchW);
            LOAD_FN(SetMatchCase);
            LOAD_FN(SetMax);
            LOAD_FN(SetRequestFlags);
            LOAD_FN(QueryW);
            LOAD_FN(GetNumResults);
            LOAD_FN(GetResultFullPathNameW);
            LOAD_FN(GetResultDateModified);
            LOAD_FN(GetResultSize);
            LOAD_FN(IsFileResult);
            LOAD_FN(IsDatabaseLoaded);
            LOAD_FN(CleanUp);
#undef LOAD_FN
        }

        [[nodiscard]] bool Ready() const
        {
            return hMod
                && SetSearchW && QueryW && GetNumResults
                && GetResultFullPathNameW && IsDatabaseLoaded;
        }
    };

    EverythingLib& GetLib()
    {
        static EverythingLib lib;
        lib.Load();
        return lib;
    }
} // namespace

// ---------------------------------------------------------------------------
// API implementation
// ---------------------------------------------------------------------------

FINDER_API int FE_ES_IsAvailable()
{
    auto& lib = GetLib();
    return (lib.Ready() && lib.IsDatabaseLoaded()) ? 1 : 0;
}

FINDER_API int FE_ES_Search(
    const wchar_t* query,
    const wchar_t* scope,
    uint32_t       maxResults)
{
    auto& lib = GetLib();
    if (!lib.Ready() || !lib.IsDatabaseLoaded())
        return -1;

    // Build scoped query: "path:<scope>\ <query>" if scope given
    wchar_t scopedQuery[2048] = {};
    if (scope && scope[0] != L'\0')
        ::_snwprintf_s(scopedQuery, _countof(scopedQuery), _TRUNCATE, L"path:\"%s\" %s", scope, query ? query : L"");
    else
        ::wcscpy_s(scopedQuery, query ? query : L"");

    lib.SetSearchW(scopedQuery);
    if (lib.SetMatchCase)  lib.SetMatchCase(FALSE);
    if (lib.SetMax)        lib.SetMax(maxResults);
    if (lib.SetRequestFlags)
        lib.SetRequestFlags(
            EVERYTHING_REQUEST_FULL_PATH_AND_FILE_NAME |
            EVERYTHING_REQUEST_SIZE |
            EVERYTHING_REQUEST_DATE_MODIFIED);

    if (!lib.QueryW(TRUE))
        return -1;

    return static_cast<int>(lib.GetNumResults());
}

FINDER_API int FE_ES_GetResultCount()
{
    auto& lib = GetLib();
    if (!lib.Ready()) return 0;
    return static_cast<int>(lib.GetNumResults());
}

FINDER_API int FE_ES_GetResult(
    int       index,
    wchar_t*  pathBuf,
    int       bufLen,
    int64_t*  outSize,
    int64_t*  outModifiedFiletime,
    bool*     outIsDir)
{
    auto& lib = GetLib();
    if (!lib.Ready() || index < 0 || index >= static_cast<int>(lib.GetNumResults()))
        return 0;

    auto dwIdx = static_cast<DWORD>(index);

    // Path
    lib.GetResultFullPathNameW(dwIdx, pathBuf, static_cast<DWORD>(bufLen));

    // Is directory (not IsFileResult)
    if (outIsDir && lib.IsFileResult)
        *outIsDir = !lib.IsFileResult(dwIdx);

    // Size
    if (outSize)
    {
        LARGE_INTEGER li = {};
        if (lib.GetResultSize && lib.GetResultSize(dwIdx, &li))
            *outSize = li.QuadPart;
        else
            *outSize = -1;
    }

    // Modified FILETIME as int64
    if (outModifiedFiletime)
    {
        FILETIME ft = {};
        if (lib.GetResultDateModified && lib.GetResultDateModified(dwIdx, &ft))
        {
            ULARGE_INTEGER ui;
            ui.LowPart  = ft.dwLowDateTime;
            ui.HighPart = ft.dwHighDateTime;
            *outModifiedFiletime = static_cast<int64_t>(ui.QuadPart);
        }
        else
            *outModifiedFiletime = 0;
    }

    return 1;
}

FINDER_API void FE_ES_Reset()
{
    auto& lib = GetLib();
    if (lib.CleanUp) lib.CleanUp();
}
