// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FinderExplorer.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private long? _size;
    [ObservableProperty] private DateTime _modified;
    [ObservableProperty] private string _icon = "📄";

    public string SizeDisplay => IsDirectory ? "--" : FormatSize(Size ?? 0);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
