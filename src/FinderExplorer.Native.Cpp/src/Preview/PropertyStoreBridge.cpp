// Copyright (c) Finder Explorer. All rights reserved.
// PropertyStoreBridge.cpp — IShellItem2::GetPropertyStore
// Fetches native system metadata (Exif, resolution, duration, author, etc).

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shobjidl.h>
#include <propkey.h>
#include <propvarutil.h>
#include "../../include/FinderExplorer.h"

#pragma comment(lib, "propsys.lib")

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static bool CopyPropString(IPropertyStore* store, const PROPERTYKEY& key, wchar_t* outBuf, int bufLen)
{
    PROPVARIANT prop;
    ::PropVariantInit(&prop);
    
    bool ok = false;
    if (SUCCEEDED(store->GetValue(key, &prop)))
    {
        wchar_t* strval = nullptr;
        if (SUCCEEDED(::PropVariantToStringAlloc(prop, &strval)))
        {
            ::wcsncpy_s(outBuf, bufLen, strval, _TRUNCATE);
            ::CoTaskMemFree(strval);
            ok = true;
        }
        ::PropVariantClear(&prop);
    }
    return ok;
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

FINDER_API int FE_Property_GetDetails(
    const wchar_t* path,
    wchar_t* outType,          int typeLen,
    wchar_t* outDimensions,    int dimLen,
    wchar_t* outDateTaken,     int dateLen,
    wchar_t* outAuthors,       int authorLen)
{
    if (!path) return 0;

    IShellItem2* pItem = nullptr;
    if (FAILED(::SHCreateItemFromParsingName(path, nullptr, IID_PPV_ARGS(&pItem))))
        return 0;

    IPropertyStore* store = nullptr;
    if (FAILED(pItem->GetPropertyStore(GPS_DEFAULT, IID_PPV_ARGS(&store))))
    {
        pItem->Release();
        return 0;
    }

    // System.ItemType (e.g., "JPEG image", "PDF Document")
    if (outType && typeLen > 0)
        CopyPropString(store, PKEY_ItemType, outType, typeLen);

    // System.Image.Dimensions (e.g., "1920 x 1080")
    if (outDimensions && dimLen > 0)
    {
        if (!CopyPropString(store, PKEY_Image_Dimensions, outDimensions, dimLen))
        {
            // Fallback to Video dimensions if not an image
            uint32_t w = 0, h = 0;
            PROPVARIANT pv;
            ::PropVariantInit(&pv);
            if (SUCCEEDED(store->GetValue(PKEY_Video_FrameWidth, &pv)) && pv.vt == VT_UI4) w = pv.ulVal;
            ::PropVariantClear(&pv);
            
            if (SUCCEEDED(store->GetValue(PKEY_Video_FrameHeight, &pv)) && pv.vt == VT_UI4) h = pv.ulVal;
            ::PropVariantClear(&pv);

            if (w > 0 && h > 0)
                ::wsprintfW(outDimensions, L"? x ?", w, h); // wsprintf doesn't support %u easily without crt, using %d is fine for size
        }
    }

    // System.ItemDate (Date Taken or Created)
    if (outDateTaken && dateLen > 0)
    {
        if (!CopyPropString(store, PKEY_Photo_DateTaken, outDateTaken, dateLen))
            CopyPropString(store, PKEY_Media_DateReleased, outDateTaken, dateLen); // Video release
    }

    // System.Author or System.Music.Artist
    if (outAuthors && authorLen > 0)
    {
        if (!CopyPropString(store, PKEY_Author, outAuthors, authorLen))
            CopyPropString(store, PKEY_Music_Artist, outAuthors, authorLen);
    }

    store->Release();
    pItem->Release();

    return 1;
}
