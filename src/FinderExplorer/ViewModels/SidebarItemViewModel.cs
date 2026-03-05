// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace FinderExplorer.ViewModels;

public enum SidebarItemType
{
    Local,
    Network,
    Nextcloud
}

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _iconPath; // avares:// path
    [ObservableProperty] private bool _canNavigate;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private SidebarItemType _itemType;

    public SidebarItemViewModel(string label, string path, string iconPath, bool canNavigate = true, SidebarItemType itemType = SidebarItemType.Local)
    {
        _label = label;
        _path = path;
        _iconPath = iconPath;
        _canNavigate = canNavigate;
        _itemType = itemType;
    }
}
