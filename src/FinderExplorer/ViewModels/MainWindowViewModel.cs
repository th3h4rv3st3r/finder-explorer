// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinderExplorer.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private CancellationTokenSource? _loadCts;

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

    [ObservableProperty]
    private bool _isLoading;

    public MainWindowViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        InitializeSidebar();
        _ = NavigateToAsync(CurrentPath);
    }

    private void InitializeSidebar()
    {
        var sidebarItems = _fileSystem.GetSidebarItems();

        SidebarFavorites = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Favorites)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIcon(i.IconKey))));

        SidebarVolumes = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Volumes)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIcon(i.IconKey))));
    }

    [RelayCommand]
    private async Task NavigateToAsync(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
            return;

        CurrentPath = path;
        WindowTitle = $"Finder Explorer — {Path.GetFileName(path)}";
        UpdateBreadcrumbs();
        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task NavigateUpAsync()
    {
        var parent = _fileSystem.GetParentPath(CurrentPath);
        if (parent is not null)
            await NavigateToAsync(parent);
    }

    [RelayCommand]
    private async Task OpenItemAsync(FileItemViewModel? item)
    {
        if (item is null) return;

        if (item.IsDirectory)
            await NavigateToAsync(item.FullPath);
        // TODO: open files with default app via Process.Start
    }

    [RelayCommand]
    private async Task SidebarNavigateAsync(SidebarItemViewModel? item)
    {
        if (item is not null)
        {
            // Update selection state
            foreach (var fav in SidebarFavorites) fav.IsSelected = false;
            foreach (var vol in SidebarVolumes) vol.IsSelected = false;
            item.IsSelected = true;

            await NavigateToAsync(item.Path);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task DeleteItemAsync(FileItemViewModel? item)
    {
        if (item is null) return;
        await _fileSystem.DeleteAsync(item.FullPath);
        Items.Remove(item);
    }

    private void UpdateBreadcrumbs()
    {
        BreadcrumbSegments.Clear();
        var parts = CurrentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
            BreadcrumbSegments.Add(part);
    }

    private async Task LoadDirectoryAsync()
    {
        // Cancel any in-flight load
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;

        try
        {
            var fsItems = await _fileSystem.GetItemsAsync(CurrentPath, ct);
            ct.ThrowIfCancellationRequested();

            Items.Clear();
            foreach (var fsItem in fsItems)
            {
                Items.Add(new FileItemViewModel
                {
                    Name = fsItem.Name,
                    FullPath = fsItem.FullPath,
                    IsDirectory = fsItem.IsDirectory,
                    Size = fsItem.Size,
                    Modified = fsItem.LastModified,
                    Icon = fsItem.IsDirectory ? "📁" : GetFileIcon(fsItem.Extension)
                });
            }
        }
        catch (OperationCanceledException) { /* Expected on rapid navigation */ }
        catch { /* Access denied or other FS error */ }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetSidebarIcon(string iconKey) => iconKey switch
    {
        "desktop" => "🖥",
        "documents" => "📄",
        "downloads" => "⬇",
        "pictures" => "🖼",
        "music" => "🎵",
        "videos" => "🎬",
        "drive" => "💾",
        _ => "📁"
    };

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
