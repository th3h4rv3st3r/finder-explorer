using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using FinderExplorer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FinderExplorer.Views.Controls;

public partial class NavigationBar : UserControl
{
    private OmnibarMode _currentMode = OmnibarMode.Path;

    public NavigationBar()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SetMode(OmnibarMode.Path);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
            topLevel.KeyDown += OnTopLevelKeyDown;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
            topLevel.KeyDown -= OnTopLevelKeyDown;
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SetMode(OmnibarMode.Search);
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.P &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SetMode(OmnibarMode.CommandPalette);
            CommandPaletteBox.Focus();
            CommandPaletteBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SetMode(OmnibarMode.Path);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _currentMode != OmnibarMode.Path)
        {
            SetMode(OmnibarMode.Path);
            e.Handled = true;
        }
    }

    private void SearchModeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetMode(OmnibarMode.Search);
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CommandPaletteModeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetMode(OmnibarMode.CommandPalette);
        CommandPaletteBox.Focus();
        CommandPaletteBox.SelectAll();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        if (e.Key == Key.Enter)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SetMode(OmnibarMode.Path);
            e.Handled = true;
        }
    }

    private void CommandPaletteBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteCommandPaletteQuery(CommandPaletteBox.Text ?? string.Empty);
            SetMode(OmnibarMode.Path);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            SetMode(OmnibarMode.Path);
            e.Handled = true;
        }
    }

    private void ExecuteCommandPaletteQuery(string query)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var key = query.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (Matches(key, "refresh", "reload", "atualizar", "recarregar"))
            vm.RefreshCommand.Execute(null);
        else if (Matches(key, "new tab", "nova aba", "abrir aba"))
            vm.NewTabCommand.Execute(null);
        else if (Matches(key, "close tab", "fechar aba"))
            vm.CloseCurrentTabCommand.Execute(null);
        else if (Matches(key, "close other tabs", "fechar outras abas"))
            vm.CloseOtherTabsCommand.Execute(null);
        else if (Matches(key, "toggle details", "details pane", "painel de detalhes", "detalhes"))
            vm.ToggleDetailsPaneCommand.Execute(null);
        else if (Matches(key, "up", "go up", "parent", "subir", "pasta pai"))
            vm.NavigateUpCommand.Execute(null);
        else if (Matches(key, "back", "voltar"))
            vm.NavigateBackCommand.Execute(null);
        else if (Matches(key, "forward", "avancar", "avançar"))
            vm.NavigateForwardCommand.Execute(null);
        else if (Matches(key, "open", "abrir"))
            vm.OpenSelectedItemCommand.Execute(null);

        CommandPaletteBox.Text = string.Empty;
    }

    private static bool Matches(string source, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (source.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void BreadcrumbChevronButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not BreadcrumbSegmentViewModel segment)
            return;

        var menu = new ContextMenu
        {
            Placement = PlacementMode.Bottom,
            ItemsSource = BuildBreadcrumbChildMenuItems(segment.FullPath)
        };

        menu.Open(control);
        e.Handled = true;
    }

    private List<MenuItem> BuildBreadcrumbChildMenuItems(string basePath)
    {
        var items = new List<MenuItem>();

        try
        {
            var children = Directory
                .EnumerateDirectories(basePath)
                .Select(path => new { Path = path, Label = GetPathLabel(path) })
                .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
                .Take(50)
                .ToList();

            foreach (var child in children)
            {
                var menuItem = new MenuItem { Header = child.Label };
                ToolTip.SetTip(menuItem, child.Path);
                menuItem.Click += (_, _) => NavigateToBreadcrumbPath(child.Path);
                items.Add(menuItem);
            }

            if (items.Count == 0)
                items.Add(new MenuItem { Header = "No folders", IsEnabled = false });
        }
        catch
        {
            items.Add(new MenuItem { Header = "Cannot access", IsEnabled = false });
        }

        return items;
    }

    private void NavigateToBreadcrumbPath(string path)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(path))
            return;

        vm.BreadcrumbNavigateCommand.Execute(
            new BreadcrumbSegmentViewModel(
                GetPathLabel(path),
                path,
                showSeparator: false,
                showChevronButton: false));
    }

    private static string GetPathLabel(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root))
        {
            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmedPath, trimmedRoot, StringComparison.OrdinalIgnoreCase))
                return root.TrimEnd(Path.DirectorySeparatorChar);
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void SetMode(OmnibarMode mode)
    {
        _currentMode = mode;

        PathModeHost.IsVisible = mode == OmnibarMode.Path;
        CommandPaletteModeHost.IsVisible = mode == OmnibarMode.CommandPalette;
        SearchModeHost.IsVisible = mode == OmnibarMode.Search;

        SetClass(CommandPaletteModeButton, "active", mode == OmnibarMode.CommandPalette);
        SetClass(SearchModeButton, "active", mode == OmnibarMode.Search);

        SetIconResource(
            CommandPaletteToggleIcon,
            mode == OmnibarMode.CommandPalette
                ? "Icon.Files.App.ThemedIcons.Omnibar.Commands.Filled"
                : "Icon.Files.App.ThemedIcons.Omnibar.Commands");

        SetIconResource(
            SearchToggleIcon,
            mode == OmnibarMode.Search
                ? "Icon.Files.App.ThemedIcons.Omnibar.Search.Filled"
                : "Icon.Files.App.ThemedIcons.Omnibar.Search");

        SetIconResource(
            CommandPaletteModeGlyph,
            "Icon.Files.App.ThemedIcons.Omnibar.Commands");

        SetIconResource(
            SearchModeGlyph,
            "Icon.Files.App.ThemedIcons.Omnibar.Search");
    }

    private static void SetClass(Control control, string className, bool enabled)
    {
        if (enabled)
        {
            if (!control.Classes.Contains(className))
                control.Classes.Add(className);
        }
        else
        {
            control.Classes.Remove(className);
        }
    }

    private enum OmnibarMode
    {
        Path,
        CommandPalette,
        Search
    }

    private void SetIconResource(PathIcon icon, string key)
    {
        if (this.TryFindResource(key, out var localValue) && localValue is Geometry localGeometry)
        {
            icon.Data = localGeometry;
            return;
        }

        if (Application.Current?.TryFindResource(key, out var globalValue) == true &&
            globalValue is Geometry globalGeometry)
        {
            icon.Data = globalGeometry;
            return;
        }

        if (Application.Current?.TryFindResource("Icon.CommandPalette", out var commandFallback) == true &&
            key.Contains("Commands", StringComparison.OrdinalIgnoreCase) &&
            commandFallback is Geometry commandGeometry)
        {
            icon.Data = commandGeometry;
            return;
        }

        if (Application.Current?.TryFindResource("Icon.Search", out var searchFallback) == true &&
            key.Contains("Search", StringComparison.OrdinalIgnoreCase) &&
            searchFallback is Geometry searchGeometry)
        {
            icon.Data = searchGeometry;
            return;
        }

        if (Application.Current?.TryFindResource("Icon.StatusCenter", out var statusFallback) == true &&
            key.Contains("StatusCenter", StringComparison.OrdinalIgnoreCase) &&
            statusFallback is Geometry statusGeometry)
            icon.Data = statusGeometry;
    }

    private void StatusCenterFlyout_Opened(object? sender, EventArgs e)
    {
        SetIconResource(StatusCenterIcon, "Icon.Files.App.ThemedIcons.StatusCenter.Filled");
    }

    private void StatusCenterFlyout_Closed(object? sender, EventArgs e)
    {
        SetIconResource(StatusCenterIcon, "Icon.Files.App.ThemedIcons.StatusCenter");
    }
}
