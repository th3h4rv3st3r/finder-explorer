// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace FinderExplorer.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private long? _size;
    [ObservableProperty] private DateTime _modified;
    [ObservableProperty] private DateTime _created;
    [ObservableProperty] private string _icon = "📄";
    [ObservableProperty] private string _iconResourceKey = "Icon.Files.App.ThemedIcons.File";
    [ObservableProperty] private string? _iconImagePath;
    [ObservableProperty] private bool _isNextcloudItem;
    [ObservableProperty] private bool _showFileExtension = true;

    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);
    public string DisplayName => !IsDirectory && !ShowFileExtension
        ? Path.GetFileNameWithoutExtension(Name)
        : Name;
    public string SizeDisplay => IsDirectory ? "--" : FormatSize(Size ?? 0);
    public bool HasImageIcon => !string.IsNullOrWhiteSpace(IconImagePath);

    /// <summary>
    /// Extracts the WebDAV-relative remote path from a <c>nc://</c> FullPath.
    /// Returns empty when this is not a Nextcloud item.
    /// </summary>
    public string NextcloudRemotePath
    {
        get
        {
            if (!IsNextcloudItem || string.IsNullOrWhiteSpace(FullPath))
                return string.Empty;
            // nc:///Documents/file.txt → /Documents/file.txt
            return FullPath.StartsWith("nc://", StringComparison.OrdinalIgnoreCase)
                ? FullPath[5..] // strip "nc://"
                : FullPath;
        }
    }

    partial void OnIconImagePathChanged(string? value) => OnPropertyChanged(nameof(HasImageIcon));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnShowFileExtensionChanged(bool value) => OnPropertyChanged(nameof(DisplayName));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
