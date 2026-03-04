// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace FinderExplorer.ViewModels;

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _icon; // emoji fallback
    [ObservableProperty] private string _iconKey; // resource key e.g. "Icon.Home"
    [ObservableProperty] private bool _isSelected;

    public SidebarItemViewModel(string label, string path, string icon, string iconKey = "Icon.Files.App.ThemedIcons.Folder")
    {
        _label = label;
        _path = path;
        _icon = icon;
        _iconKey = iconKey;
    }
}
