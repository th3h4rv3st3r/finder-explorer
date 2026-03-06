using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace FinderExplorer.Converters;

/// <summary>
/// Conversor automático de cores para contraste ideal sobre o Mica.
/// Avalia se o sistema está em modo claro ou escuro (via ThemeVariant ou cor)
/// e retorna uma cor adequada para o Grupo 1 (mais frosted/branca) ou Grupo 2 (mais escura/sólida).
/// </summary>
public sealed class MicaContrastColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isDarkTheme = true;

        if (value is Color systemColor)
        {
            // Calcula a luminância aproximada da cor (0.0=escuro, 1.0=claro)
            double luminance = (0.299 * systemColor.R + 0.587 * systemColor.G + 0.114 * systemColor.B) / 255.0;
            isDarkTheme = luminance < 0.5;
        }
        else if (value is Avalonia.Styling.ThemeVariant theme)
        {
            isDarkTheme = theme == Avalonia.Styling.ThemeVariant.Dark;
        }
        else if (Avalonia.Application.Current != null)
        {
            isDarkTheme = Avalonia.Application.Current.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        }

        string group = parameter?.ToString()?.Trim() ?? "1";

        if (group == "2" || group.Contains("2"))
        {
            // GRUPO 2: Fundo Interno (Caixotão de Arquivos) + Toolbar
            if (isDarkTheme)
            {
                // Um pouco mais branco
                return Color.Parse("#1AFFFFFF"); // 10% white
            }
            else
            {
                return Color.Parse("#66FFFFFF"); // 40% white
            }
        }
        else
        {
            // GRUPO 1: Aba Ativa + Sidebar + NavigationBar (Fundo atrás do caixote)
            if (isDarkTheme)
            {
                // 5% mais escuro
                return Color.Parse("#33000000"); // 20% black
            }
            else
            {
                return Color.Parse("#D9F3F3F3"); // 85% opacity
            }
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
