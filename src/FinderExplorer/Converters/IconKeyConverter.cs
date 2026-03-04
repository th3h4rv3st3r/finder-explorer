// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FinderExplorer.Converters;

/// <summary>
/// Converts a string resource key (e.g. "Icon.Home") to the corresponding
/// StreamGeometry from the application's resource dictionary.
/// </summary>
public class IconKeyConverter : IValueConverter
{
    public static readonly IconKeyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
            return null;

        var normalizedKey = NormalizeIconResourceKey(key);

        if (Application.Current?.TryFindResource(normalizedKey, out var resource) == true
            && resource is StreamGeometry geometry)
        {
            return geometry;
        }

        // Fallback: use official Files folder geometry.
        if (Application.Current?.TryFindResource("Icon.Files.App.ThemedIcons.Folder", out var fallback) == true)
            return fallback as StreamGeometry;

        if (Application.Current?.TryFindResource("Icon.Folder", out fallback) == true)
            return fallback as StreamGeometry;

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static string NormalizeIconResourceKey(string key)
    {
        if (key.StartsWith("App.ThemedIcons.", StringComparison.Ordinal))
            return $"Icon.Files.{key}";

        return key;
    }
}

/// <summary>
/// Converts IsDirectory (bool) to a folder or file StreamGeometry icon.
/// </summary>
public class FolderOrFileIconConverter : IValueConverter
{
    public static readonly FolderOrFileIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isDir = value is true;
        string key = isDir
            ? "Icon.Files.App.ThemedIcons.Folder"
            : "Icon.Files.App.ThemedIcons.File";

        if (Application.Current?.TryFindResource(key, out var resource) == true
            && resource is StreamGeometry geometry)
        {
            return geometry;
        }

        var legacyKey = isDir ? "Icon.Folder" : "Icon.File";
        if (Application.Current?.TryFindResource(legacyKey, out var legacyResource) == true
            && legacyResource is StreamGeometry legacyGeometry)
        {
            return legacyGeometry;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
