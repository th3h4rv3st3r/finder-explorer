// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System;

namespace FinderExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private string _windowTitle = "Finder Explorer";

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = [];

    [ObservableProperty]
    private ObservableCollection<string> _breadcrumbSegments = [];

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarFavorites = [];

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarVolumes = [];

    public MainWindowViewModel()
    {
        InitializeSidebar();
        NavigateTo(CurrentPath);
    }

    private void InitializeSidebar()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        SidebarFavorites =
        [
            new("Desktop", Path.Combine(userProfile, "Desktop"), "🖥"),
            new("Documents", Path.Combine(userProfile, "Documents"), "📄"),
            new("Downloads", Path.Combine(userProfile, "Downloads"), "⬇"),
            new("Pictures", Path.Combine(userProfile, "Pictures"), "🖼"),
            new("Music", Path.Combine(userProfile, "Music"), "🎵"),
            new("Videos", Path.Combine(userProfile, "Videos"), "🎬"),
        ];

        SidebarVolumes = [];
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
                SidebarVolumes.Add(new(label, drive.Name, "💾"));
            }
        }
    }

    [RelayCommand]
    private void NavigateTo(string path)
    {
        if (!Directory.Exists(path))
            return;

        CurrentPath = path;
        UpdateBreadcrumbs();
        LoadDirectory();
    }

    [RelayCommand]
    private void NavigateUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
            NavigateTo(parent.FullName);
    }

    [RelayCommand]
    private void OpenItem(FileItemViewModel? item)
    {
        if (item is null) return;

        if (item.IsDirectory)
            NavigateTo(item.FullPath);
    }

    [RelayCommand]
    private void SidebarNavigate(SidebarItemViewModel? item)
    {
        if (item is not null)
            NavigateTo(item.Path);
    }

    private void UpdateBreadcrumbs()
    {
        BreadcrumbSegments.Clear();
        var parts = CurrentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            BreadcrumbSegments.Add(part);
    }

    private void LoadDirectory()
    {
        Items.Clear();

        try
        {
            var dirInfo = new DirectoryInfo(CurrentPath);

            // Directories first
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                try
                {
                    Items.Add(new FileItemViewModel
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = null,
                        Modified = dir.LastWriteTime,
                        Icon = "📁"
                    });
                }
                catch { /* Skip inaccessible */ }
            }

            // Then files
            foreach (var file in dirInfo.EnumerateFiles())
            {
                try
                {
                    Items.Add(new FileItemViewModel
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        Modified = file.LastWriteTime,
                        Icon = GetFileIcon(file.Extension)
                    });
                }
                catch { /* Skip inaccessible */ }
            }
        }
        catch { /* Access denied to directory */ }
    }

    private static string GetFileIcon(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "🖼",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "🎬",
        ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" => "🎵",
        ".pdf" => "📕",
        ".doc" or ".docx" or ".odt" => "📝",
        ".xls" or ".xlsx" or ".csv" => "📊",
        ".ppt" or ".pptx" => "📈",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "📦",
        ".exe" or ".msi" => "⚙",
        ".txt" or ".md" or ".log" => "📄",
        ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" => "💻",
        _ => "📄"
    };
}

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

public partial class SidebarItemViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _icon;

    public SidebarItemViewModel(string label, string path, string icon)
    {
        _label = label;
        _path = path;
        _icon = icon;
    }
}
