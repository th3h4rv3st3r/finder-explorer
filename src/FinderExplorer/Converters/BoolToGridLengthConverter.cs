// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace FinderExplorer.Converters;

/// <summary>
/// Converts bool to GridLength values.
/// </summary>
public sealed class BoolToGridLengthConverter : IValueConverter
{
    public GridLength TrueLength { get; set; } = new(1, GridUnitType.Star);
    public GridLength FalseLength { get; set; } = new(0);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueLength : FalseLength;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not GridLength gridLength)
            return false;

        return gridLength != FalseLength;
    }
}
