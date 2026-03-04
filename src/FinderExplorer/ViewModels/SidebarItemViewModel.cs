// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace FinderExplorer.ViewModels;

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private string? _iconImage;
    [ObservableProperty] private bool _isSelected;

    public SidebarItemViewModel(string label, string path, string icon, string? iconImage = null)
    {
        _label = label;
        _path = path;
        _icon = icon;
        _iconImage = iconImage;
    }

    /// <summary>
    /// Whether this item has a PNG icon image (vs emoji fallback).
    /// </summary>
    public bool HasIconImage => IconImage is not null;
}
