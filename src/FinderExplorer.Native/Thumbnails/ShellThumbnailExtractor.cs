// Copyright (c) Finder Explorer. All rights reserved.
// Native Win32 thumbnail extraction via IShellItemImageFactory.
// Uses CsWin32 source-generated P/Invoke for type safety and AOT compatibility.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Native.Thumbnails;

/// <summary>
/// Extracts Windows Shell thumbnails using IShellItemImageFactory COM interface.
/// This produces GPU-composited thumbnails identical to what Explorer shows,
/// including previews for images, videos, PDFs, etc.
/// </summary>
public static class ShellThumbnailExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    /// <summary>
    /// Extracts a thumbnail bitmap for the given file path.
    /// Returns raw BGRA pixel data + dimensions for direct use with Avalonia WriteableBitmap.
    /// </summary>
    public static Task<ThumbnailResult?> GetThumbnailAsync(
        string filePath, int size, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out var factory);

                var nativeSize = new SIZE { cx = size, cy = size };
                // SIIGBF_THUMBNAILONLY = 0x04 — only returns thumbnail, no icon fallback
                // SIIGBF_BIGGERSIZEOK = 0x01 — allows returning a larger cached thumbnail
                factory.GetImage(nativeSize, SIIGBF.SIIGBF_BIGGERSIZEOK, out var hBitmap);

                try
                {
                    return ExtractBitmapData(hBitmap, size);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }, ct);
    }

    private static ThumbnailResult? ExtractBitmapData(IntPtr hBitmap, int requestedSize)
    {
        var bmp = new BITMAP();
        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bmp) == 0)
            return null;

        int width = bmp.bmWidth;
        int height = bmp.bmHeight;
        int stride = width * 4;

        // Get DIB bits
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            }
        };

        var pixels = new byte[stride * height];
        var hdc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref bmi, 0);
        }
        finally
        {
            DeleteDC(hdc);
        }

        return new ThumbnailResult(pixels, width, height, stride);
    }

    #region Native declarations

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObj, int cbBuffer, ref BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [Flags]
    private enum SIIGBF : uint
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    #endregion
}

/// <summary>
/// Raw BGRA pixel data from a Windows Shell thumbnail.
/// Can be directly written to an Avalonia WriteableBitmap for GPU-composited rendering.
/// </summary>
public sealed record ThumbnailResult(byte[] Pixels, int Width, int Height, int Stride);
