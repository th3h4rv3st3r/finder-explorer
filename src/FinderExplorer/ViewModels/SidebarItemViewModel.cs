// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;

namespace FinderExplorer.ViewModels;

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _icon;
    [ObservableProperty] private bool _isSelected;

    public SidebarItemViewModel(string label, string path, string icon)
    {
        _label = label;
        _path = path;
        _icon = icon;
    }
}
