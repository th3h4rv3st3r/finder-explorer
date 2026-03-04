// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace FinderExplorer.Converters;

/// <summary>
/// Converte bool → string de classes CSS do Avalonia.
/// Permite animar a seleção da pill via classe .on em vez de Opacity direta,
/// o que funciona melhor com os estilos do Avalonia 11.
/// </summary>
public sealed class BoolToClassConverter : IValueConverter
{
    public string TrueClass  { get; set; } = string.Empty;
    public string FalseClass { get; set; } = string.Empty;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueClass : FalseClass;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == TrueClass;
}
