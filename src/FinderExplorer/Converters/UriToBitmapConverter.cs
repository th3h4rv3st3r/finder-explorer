// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace FinderExplorer.Converters;

/// <summary>
/// Converts an avares:// URI string to an Avalonia Bitmap for Image.Source binding.
/// </summary>
public class UriToBitmapConverter : IValueConverter
{
    public static readonly UriToBitmapConverter Instance = new();
    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            if (TryGetCachedBitmap(path, out var cachedBitmap))
                return cachedBitmap;

            try
            {
                Bitmap? bitmap = null;

                // App bundled asset.
                if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(path);
                    using var stream = AssetLoader.Open(uri);
                    bitmap = new Bitmap(stream);
                }

                // Absolute file path or file:// URI.
                if (bitmap is null && Uri.TryCreate(path, UriKind.Absolute, out var absUri) && absUri.IsFile)
                {
                    using var stream = File.OpenRead(absUri.LocalPath);
                    bitmap = new Bitmap(stream);
                }

                if (bitmap is null && File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    bitmap = new Bitmap(stream);
                }

                if (bitmap is not null)
                {
                    BitmapCache[path] = new WeakReference<Bitmap>(bitmap);
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryGetCachedBitmap(string path, out Bitmap bitmap)
    {
        bitmap = null!;
        if (!BitmapCache.TryGetValue(path, out var weakReference))
            return false;

        if (weakReference.TryGetTarget(out var cached) && cached is not null)
        {
            bitmap = cached;
            return true;
        }

        BitmapCache.TryRemove(path, out _);
        return false;
    }
}
