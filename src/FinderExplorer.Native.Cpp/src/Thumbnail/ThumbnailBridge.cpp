// Copyright (c) Finder Explorer. All rights reserved.
// ThumbnailBridge.cpp — IShellItemImageFactory thumbnail extractor.
// Migrated from ShellThumbnailExtractor.cs; runs fully in C++.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <objidl.h>
#include <gdi32.h>
#include <cstring>
#include <cstdlib>
#include "../../include/FinderExplorer.h"

// IShellItemImageFactory — available since Vista, always present on Win10/11.
// {bcc18b79-ba16-442f-80c4-8a59c30c463b}
static const IID IID_IShellItemImageFactory =
    { 0xbcc18b79, 0xba16, 0x442f, { 0x80, 0xc4, 0x8a, 0x59, 0xc3, 0x0c, 0x46, 0x3b } };

// ---------------------------------------------------------------------------
// FE_GetThumbnail
// ---------------------------------------------------------------------------
FINDER_API int FE_GetThumbnail(
    const wchar_t* path,
    int            size,
    uint8_t**      outPixels,
    int*           outWidth,
    int*           outHeight)
{
    if (!path || !outPixels || !outWidth || !outHeight)
        return 0;

    *outPixels = nullptr;
    *outWidth  = 0;
    *outHeight = 0;

    IShellItemImageFactory* pFactory = nullptr;
    HRESULT hr = ::SHCreateItemFromParsingName(
        path,
        nullptr,
        IID_IShellItemImageFactory,
        reinterpret_cast<void**>(&pFactory));
    if (FAILED(hr) || !pFactory)
        return 0;

    SIZE sz = { size, size };
    // SIIGBF_BIGGERSIZEOK (0x1): allow larger cached thumbnail
    HBITMAP hBmp = nullptr;
    hr = pFactory->GetImage(sz, static_cast<SIIGBF>(0x01), &hBmp);
    pFactory->Release();

    if (FAILED(hr) || !hBmp)
        return 0;

    // --- Extract BGRA pixels via GetDIBits ---
    BITMAP bmpInfo = {};
    if (!::GetObject(hBmp, sizeof(bmpInfo), &bmpInfo))
    {
        ::DeleteObject(hBmp);
        return 0;
    }

    int w      = bmpInfo.bmWidth;
    int h      = bmpInfo.bmHeight;
    int stride = w * 4; // 32-bit BGRA

    BITMAPINFOHEADER bih   = {};
    bih.biSize        = sizeof(bih);
    bih.biWidth       = w;
    bih.biHeight      = -h; // top-down
    bih.biPlanes      = 1;
    bih.biBitCount    = 32;
    bih.biCompression = BI_RGB;

    BITMAPINFO bi = {};
    bi.bmiHeader  = bih;

    auto* pixels = static_cast<uint8_t*>(std::malloc(static_cast<size_t>(stride) * h));
    if (!pixels)
    {
        ::DeleteObject(hBmp);
        return 0;
    }

    HDC hdc = ::CreateCompatibleDC(nullptr);
    int copied = ::GetDIBits(hdc, hBmp, 0, static_cast<UINT>(h), pixels, &bi, DIB_RGB_COLORS);
    ::DeleteDC(hdc);
    ::DeleteObject(hBmp);

    if (copied == 0)
    {
        std::free(pixels);
        return 0;
    }

    *outPixels = pixels;
    *outWidth  = w;
    *outHeight = h;
    return 1;
}

// ---------------------------------------------------------------------------
// FE_FreeThumbnail
// ---------------------------------------------------------------------------
FINDER_API void FE_FreeThumbnail(uint8_t* pixels)
{
    std::free(pixels);
}
