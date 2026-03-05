// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Collections;
using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Native.Services;

/// <summary>
/// <see cref="IThumbnailService"/> backed by the C++ <c>FE_GetThumbnail</c>
/// (IShellItemImageFactory GPU path) with a two-level cache:
/// 1. LRU memory cache  — instant hit, bounded to <see cref="MemoryCacheCapacity"/> entries.
/// 2. Disk cache        — BGRA raw files in %LOCALAPPDATA%\FinderExplorer\thumbs\
/// </summary>
public sealed class ThumbnailService : IThumbnailService, IDisposable
{
    // Memory cache: 512 thumbnails @ 128×128×4 bytes ≈ 32 MB max
    private const int MemoryCacheCapacity = 512;
    private const string ThumbnailCacheVersion = "v2";

    private readonly LruCache<string, ThumbnailData> _memCache = new(MemoryCacheCapacity);
    private readonly string _diskCacheDir;
    private readonly SemaphoreSlim _throttle = new(Environment.ProcessorCount * 2);
    private readonly ISettingsService _settings;

    public ThumbnailService(ISettingsService settings)
    {
        _settings = settings;
        _diskCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinderExplorer", "thumbs-v2");
        Directory.CreateDirectory(_diskCacheDir);
    }

    // -----------------------------------------------------------------------
    // GetThumbnailAsync
    // -----------------------------------------------------------------------

    public async Task<ThumbnailData?> GetThumbnailAsync(
        string path, int sizePx, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_settings.Current.UseGpuThumbnails)
            return null;

        // 1. Memory cache hit
        string cacheKey = $"{ThumbnailCacheVersion}|{path}|{sizePx}";
        if (_memCache.TryGet(cacheKey, out var cached))
            return cached;

        // 2. Disk cache hit
        string diskPath = GetDiskCachePath(path, sizePx);
        if (File.Exists(diskPath))
        {
            var data = await TryReadDiskCacheAsync(diskPath, ct);
            if (data is not null)
            {
                _memCache.Set(cacheKey, data);
                return data;
            }
        }

        // 3. Extract via C++ (GPU path)
        await _throttle.WaitAsync(ct);
        try
        {
            var result = await Task.Run(() => ExtractNative(path, sizePx), ct);
            if (result is null) return null;

            _memCache.Set(cacheKey, result);
            // Persist to disk (fire-and-forget — don't block the caller)
            _ = WriteDiskCacheAsync(diskPath, result, CancellationToken.None);
            return result;
        }
        finally
        {
            _throttle.Release();
        }
    }

    // -----------------------------------------------------------------------
    // Cache management
    // -----------------------------------------------------------------------

    public void Invalidate(string path)
    {
        // Evict all sizes for this path (iterate common sizes)
        foreach (int sz in new[] { 64, 96, 128, 256 })
            _memCache.Remove($"{ThumbnailCacheVersion}|{path}|{sz}");
    }

    public void InvalidateAll() => _memCache.Clear();

    // -----------------------------------------------------------------------
    // Native extraction
    // -----------------------------------------------------------------------

    private static ThumbnailData? ExtractNative(string path, int sizePx)
    {
        int ok = NativeBridge.GetThumbnail(path, sizePx,
            out IntPtr pixelPtr, out int width, out int height);

        if (ok != 1 || pixelPtr == IntPtr.Zero)
            return null;

        try
        {
            int   stride = width * 4;
            var   pixels = new byte[stride * height];
            Marshal.Copy(pixelPtr, pixels, 0, pixels.Length);
            return new ThumbnailData(pixels, width, height);
        }
        finally
        {
            NativeBridge.FreeThumbnail(pixelPtr);
        }
    }

    // -----------------------------------------------------------------------
    // Disk cache helpers
    // -----------------------------------------------------------------------

    private string GetDiskCachePath(string path, int sizePx)
    {
        // Key: SHA-ish hash of path + modification time + size
        long mtime = 0;
        try { mtime = File.GetLastWriteTimeUtc(path).Ticks; } catch { }

        int hash = HashCode.Combine(ThumbnailCacheVersion.GetHashCode(), path.GetHashCode(), mtime, sizePx);
        return Path.Combine(_diskCacheDir, $"{(uint)hash:x8}_{sizePx}.bgra");
    }

    private static async Task<ThumbnailData?> TryReadDiskCacheAsync(
        string diskPath, CancellationToken ct)
    {
        try
        {
            // Format: 4-byte width, 4-byte height, then raw BGRA pixels
            await using var fs = File.OpenRead(diskPath);
            var header = new byte[8];
            if (await fs.ReadAsync(header, ct) < 8) return null;

            int width  = BitConverter.ToInt32(header, 0);
            int height = BitConverter.ToInt32(header, 4);
            var pixels = new byte[width * height * 4];
            await fs.ReadExactlyAsync(pixels, ct);
            return new ThumbnailData(pixels, width, height);
        }
        catch { return null; }
    }

    private static async Task WriteDiskCacheAsync(
        string diskPath, ThumbnailData data, CancellationToken ct)
    {
        try
        {
            await using var fs = File.Create(diskPath);
            await fs.WriteAsync(BitConverter.GetBytes(data.Width),  ct);
            await fs.WriteAsync(BitConverter.GetBytes(data.Height), ct);
            await fs.WriteAsync(data.Pixels, ct);
        }
        catch { /* Non-critical — disk full or access denied */ }
    }

    public void Dispose() => _throttle.Dispose();
}
