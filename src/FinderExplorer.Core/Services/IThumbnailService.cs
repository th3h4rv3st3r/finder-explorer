// Copyright (c) Finder Explorer. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Asynchronous thumbnail extraction with LRU memory cache and optional disk cache.
/// </summary>
public interface IThumbnailService
{
    /// <summary>
    /// Extracts (or returns cached) thumbnail for <paramref name="path"/> at <paramref name="sizePx"/> squared.
    /// Returns raw BGRA pixel data ready for Avalonia <c>WriteableBitmap</c>,
    /// or <c>null</c> when no thumbnail is available.
    /// </summary>
    Task<ThumbnailData?> GetThumbnailAsync(
        string            path,
        int               sizePx,
        CancellationToken ct = default);

    /// <summary>Evicts a single path from the in-memory cache (e.g. after file rename/delete).</summary>
    void Invalidate(string path);

    /// <summary>Clears the entire in-memory cache.</summary>
    void InvalidateAll();
}

/// <summary>Raw BGRA pixel data for a single thumbnail.</summary>
public sealed record ThumbnailData(byte[] Pixels, int Width, int Height)
{
    public int Stride => Width * 4;
}
