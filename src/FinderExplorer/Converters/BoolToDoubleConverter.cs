// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace FinderExplorer.Converters;

/// <summary>
/// Converts a bool to a double opacity value.
/// true  → 1.0 (visible)
/// false → 0.0 (invisible)
/// Matches Files Community's SelectionIndicator Opacity animation.
/// </summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d && d > 0.5;
}
