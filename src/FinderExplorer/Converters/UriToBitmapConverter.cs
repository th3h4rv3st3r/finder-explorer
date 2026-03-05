// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;
using System.IO;

namespace FinderExplorer.Converters;

/// <summary>
/// Converts an avares:// URI string to an Avalonia Bitmap for Image.Source binding.
/// </summary>
public class UriToBitmapConverter : IValueConverter
{
    public static readonly UriToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try
            {
                // App bundled asset.
                if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(path);
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }

                // Absolute file path or file:// URI.
                if (Uri.TryCreate(path, UriKind.Absolute, out var absUri) && absUri.IsFile)
                {
                    using var stream = File.OpenRead(absUri.LocalPath);
                    return new Bitmap(stream);
                }

                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    return new Bitmap(stream);
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
}
