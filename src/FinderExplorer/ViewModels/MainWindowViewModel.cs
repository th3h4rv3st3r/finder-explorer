// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinderExplorer.Core.Services;
using System;
using System.Collections.Generic;
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
    private ObservableCollection<BreadcrumbSegmentViewModel> _breadcrumbSegments = [];

    [ObservableProperty]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarFavorites = [];

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarVolumes = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isFavoritesExpanded = true;

    [ObservableProperty]
    private bool _isVolumesExpanded = true;

    [ObservableProperty]
    private bool _isNetworkExpanded = true;

    [ObservableProperty]
    private bool _isNextcloudExpanded = true;

    [ObservableProperty]
    private bool _isTagsExpanded = true;

    [ObservableProperty]
    private string _itemCountText = "";

    public SidebarItemViewModel SidebarHome { get; }
    public SidebarItemViewModel SidebarNetwork { get; }
    public SidebarItemViewModel SidebarNextcloud { get; }

    public MainWindowViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SidebarHome = new SidebarItemViewModel("Home", userProfile, GetSidebarIconUri("home"));
        SidebarNetwork = new SidebarItemViewModel("Network", string.Empty, GetSidebarIconUri("network"), canNavigate: false);
        SidebarNextcloud = new SidebarItemViewModel("Nextcloud", string.Empty, GetSidebarIconUri("nextcloud"), canNavigate: false);
        SidebarHome.IsSelected = true;

        InitializeSidebar();
        _ = NavigateToAsync(CurrentPath);
    }

    private void InitializeSidebar()
    {
        var sidebarItems = _fileSystem.GetSidebarItems();

        SidebarFavorites = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Favorites)
                .Select(i => new SidebarItemViewModel(
                    i.Label, i.Path,
                    GetSidebarIconUri(i.IconKey))));

        SidebarVolumes = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Volumes)
                .Select(i => new SidebarItemViewModel(
                    i.Label, i.Path,
                    GetSidebarIconUri(i.IconKey))));
    }

    [RelayCommand]
    private async Task NavigateToAsync(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
            return;

        CurrentPath = path;
        var folderName = Path.GetFileName(path);
        WindowTitle = string.IsNullOrEmpty(folderName)
            ? $"Finder Explorer — {path}"
            : $"Finder Explorer — {folderName}";
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
            ClearSidebarSelection();
            item.IsSelected = true;

            if (item.CanNavigate && !string.IsNullOrWhiteSpace(item.Path))
                await NavigateToAsync(item.Path);
        }
    }

    [RelayCommand]
    private async Task BreadcrumbNavigateAsync(BreadcrumbSegmentViewModel? segment)
    {
        if (segment is not null)
            await NavigateToAsync(segment.FullPath);
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
        var accumulated = "";
        foreach (var part in parts)
        {
            accumulated = string.IsNullOrEmpty(accumulated)
                ? part + Path.DirectorySeparatorChar
                : Path.Combine(accumulated, part);
            BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel(part, accumulated));
        }
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
            var fsItems = await _fileSystem.GetItemsAsync(CurrentPath, sort: null, filter: null, ct);
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

            var dirs = Items.Count(i => i.IsDirectory);
            var files = Items.Count - dirs;
            ItemCountText = $"{dirs} pastas, {files} arquivos";
        }
        catch (OperationCanceledException) { /* Expected on rapid navigation */ }
        catch { /* Access denied or other FS error */ }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearSidebarSelection()
    {
        foreach (var item in EnumerateSidebarItems())
            item.IsSelected = false;
    }

    private IEnumerable<SidebarItemViewModel> EnumerateSidebarItems()
    {
        yield return SidebarHome;
        yield return SidebarNetwork;
        yield return SidebarNextcloud;

        foreach (var fav in SidebarFavorites)
            yield return fav;

        foreach (var vol in SidebarVolumes)
            yield return vol;
    }

    private static string GetSidebarIconUri(string iconKey) => iconKey switch
    {
        "home" => "avares://FinderExplorer/Assets/Icons/WinUI/Home.png",
        "desktop" => "avares://FinderExplorer/Assets/Icons/WinUI/Desktop.png",
        "documents" => "avares://FinderExplorer/Assets/Icons/WinUI/Documents.png",
        "downloads" => "avares://FinderExplorer/Assets/Icons/WinUI/Downloads.png",
        "pictures" => "avares://FinderExplorer/Assets/Icons/WinUI/Pictures.png",
        "music" => "avares://FinderExplorer/Assets/Icons/WinUI/Music.png",
        "videos" => "avares://FinderExplorer/Assets/Icons/WinUI/Videos.png",
        "drive" => "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png",
        "network" => "avares://FinderExplorer/Assets/Icons/WinUI/Network.png",
        "nextcloud" => "avares://FinderExplorer/Assets/Icons/WinUI/CloudDrive.png",
        _ => "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png"
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

/// <summary>
/// Represents a clickable breadcrumb segment with its full path for navigation.
/// </summary>
public partial class BreadcrumbSegmentViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _fullPath;

    public BreadcrumbSegmentViewModel(string label, string fullPath)
    {
        _label = label;
        _fullPath = fullPath;
    }
}
