// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace FinderExplorer.ViewModels;

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _iconPath; // avares:// path
    [ObservableProperty] private bool _canNavigate;
    [ObservableProperty] private bool _isSelected;

    public SidebarItemViewModel(string label, string path, string iconPath, bool canNavigate = true)
    {
        _label = label;
        _path = path;
        _iconPath = iconPath;
        _canNavigate = canNavigate;
    }
}
