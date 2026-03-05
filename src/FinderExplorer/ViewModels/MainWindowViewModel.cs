// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinderExplorer.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _searchService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private string _windowTitle = "Finder Explorer";

    [ObservableProperty]
    private ObservableCollection<FileItemViewModel> _items = [];

    [ObservableProperty]
    private ObservableCollection<BreadcrumbSegmentViewModel> _breadcrumbSegments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedItemCommand))]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarFavorites = [];

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarVolumes = [];

    [ObservableProperty]
    private ObservableCollection<ExplorerTabViewModel> _tabs = [];

    [ObservableProperty]
    private ExplorerTabViewModel? _selectedTab;

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
    private bool _isDetailsPaneVisible = true;

    [ObservableProperty]
    private bool _isDetailsTabSelected = true;

    [ObservableProperty]
    private bool _isPreviewTabSelected;

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _itemCountText = string.Empty;

    [ObservableProperty]
    private string _detailsIconResourceKey = "Icon.Files.App.ThemedIcons.Folder";

    [ObservableProperty]
    private string _detailsTitleText = string.Empty;

    [ObservableProperty]
    private string _detailsMetricLabel = "Item count";

    [ObservableProperty]
    private string _detailsMetricValue = string.Empty;

    [ObservableProperty]
    private string _detailsDateModifiedText = string.Empty;

    [ObservableProperty]
    private string _detailsDateCreatedText = string.Empty;

    [ObservableProperty]
    private string _detailsPathText = string.Empty;

    [ObservableProperty]
    private string? _previewFilePath;

    [ObservableProperty]
    private bool _isPreviewAvailable;

    [ObservableProperty]
    private string _previewStatusText = "Select a file to preview";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateBackCommand))]
    private bool _canNavigateBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateForwardCommand))]
    private bool _canNavigateForward;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateUpCommand))]
    private bool _canNavigateUp;

    public SidebarItemViewModel SidebarHome { get; }
    public SidebarItemViewModel SidebarNetwork { get; }
    public SidebarItemViewModel SidebarNextcloud { get; }

    private bool HasSelectedItem => SelectedItem is not null;

    public MainWindowViewModel(
        IFileSystemService fileSystem,
        ISearchService searchService)
    {
        _fileSystem = fileSystem;
        _searchService = searchService;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        SidebarHome = new SidebarItemViewModel("Home", userProfile, GetSidebarIconUri("home"));
        SidebarNetwork = new SidebarItemViewModel("Network", string.Empty, GetSidebarIconUri("network"), canNavigate: false);
        SidebarNextcloud = new SidebarItemViewModel("Nextcloud", string.Empty, GetSidebarIconUri("nextcloud"), canNavigate: false);
        SidebarHome.IsSelected = true;

        InitializeSidebar();

        var initialTab = CreateTab(userProfile);
        initialTab.IsSelected = true;
        Tabs.Add(initialTab);
        SelectedTab = initialTab;

        _ = NavigateToPathAsync(initialTab.Path, addToHistory: false, forceReload: true);
    }

    partial void OnSelectedItemChanged(FileItemViewModel? value)
    {
        UpdateDetailsState();
        UpdatePreviewState();
    }

    private void InitializeSidebar()
    {
        var sidebarItems = _fileSystem.GetSidebarItems();

        SidebarFavorites = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Favorites)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIconUri(i.IconKey))));

        SidebarVolumes = new ObservableCollection<SidebarItemViewModel>(
            sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Volumes)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIconUri(i.IconKey))));
    }

    [RelayCommand]
    private async Task NavigateToAsync(string path)
    {
        await NavigateToPathAsync(path, addToHistory: true);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private async Task NavigateBackAsync()
    {
        if (SelectedTab is null || SelectedTab.BackHistory.Count == 0)
            return;

        var targetPath = SelectedTab.BackHistory.Pop();
        if (!string.IsNullOrWhiteSpace(CurrentPath))
            SelectedTab.ForwardHistory.Push(CurrentPath);

        await NavigateToPathAsync(targetPath, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private async Task NavigateForwardAsync()
    {
        if (SelectedTab is null || SelectedTab.ForwardHistory.Count == 0)
            return;

        var targetPath = SelectedTab.ForwardHistory.Pop();
        if (!string.IsNullOrWhiteSpace(CurrentPath))
            SelectedTab.BackHistory.Push(CurrentPath);

        await NavigateToPathAsync(targetPath, addToHistory: false);
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private async Task NavigateUpAsync()
    {
        var parent = _fileSystem.GetParentPath(CurrentPath);
        if (parent is null)
            return;

        await NavigateToPathAsync(parent, addToHistory: true);
    }

    [RelayCommand]
    private async Task OpenItemAsync(FileItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.IsDirectory)
            await NavigateToPathAsync(item.FullPath, addToHistory: true);
        else
            _fileSystem.OpenFile(item.FullPath);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task OpenSelectedItemAsync()
    {
        await OpenItemAsync(SelectedItem);
    }

    [RelayCommand]
    private async Task SidebarNavigateAsync(SidebarItemViewModel? item)
    {
        if (item is null)
            return;

        ClearSidebarSelection();
        item.IsSelected = true;

        if (item.CanNavigate && !string.IsNullOrWhiteSpace(item.Path))
            await NavigateToPathAsync(item.Path, addToHistory: true);
    }

    [RelayCommand]
    private async Task BreadcrumbNavigateAsync(BreadcrumbSegmentViewModel? segment)
    {
        if (segment is null)
            return;

        await NavigateToPathAsync(segment.FullPath, addToHistory: true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsSearchActive && !string.IsNullOrWhiteSpace(SearchQuery))
            await SearchAsync();
        else
            await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchActive = false;
            await LoadDirectoryAsync();
            UpdateWindowTitle();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsLoading = true;
        IsSearchActive = true;

        try
        {
            Items.Clear();
            SelectedItem = null;

            await foreach (var result in _searchService.SearchAsync(query, CurrentPath, 1000, ct))
            {
                ct.ThrowIfCancellationRequested();

                Items.Add(new FileItemViewModel
                {
                    Name = result.Name,
                    FullPath = result.FullPath,
                    IsDirectory = result.IsDirectory,
                    Size = result.Size,
                    Modified = result.LastModified,
                    Created = result.LastModified,
                    Icon = result.IsDirectory ? "Folder" : GetFileIcon(result.Extension)
                });
            }

            var dirs = Items.Count(i => i.IsDirectory);
            var files = Items.Count - dirs;
            ItemCountText = $"{dirs} pastas, {files} arquivos";
            UpdateDetailsState();
        }
        catch (OperationCanceledException)
        {
            // Expected when user types quickly.
        }
        finally
        {
            IsLoading = false;
        }

        UpdateWindowTitle();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        IsSearchActive = false;
        await LoadDirectoryAsync();
        UpdateWindowTitle();
    }

    [RelayCommand]
    private async Task DeleteItemAsync(FileItemViewModel? item)
    {
        if (item is null)
            return;

        await _fileSystem.DeleteAsync(item.FullPath);
        Items.Remove(item);

        if (ReferenceEquals(SelectedItem, item))
            SelectedItem = null;

        UpdateItemCountText();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task DeleteSelectedItemAsync()
    {
        await DeleteItemAsync(SelectedItem);
    }

    [RelayCommand]
    private void ToggleDetailsPane()
    {
        IsDetailsPaneVisible = !IsDetailsPaneVisible;
    }

    [RelayCommand]
    private void ShowDetailsTab()
    {
        IsDetailsTabSelected = true;
        IsPreviewTabSelected = false;
    }

    [RelayCommand]
    private void ShowPreviewTab()
    {
        IsDetailsTabSelected = false;
        IsPreviewTabSelected = true;
    }

    [RelayCommand]
    private void ShowProperties()
    {
        var targetPath = SelectedItem?.FullPath ?? CurrentPath;
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(targetPath)
            {
                UseShellExecute = true,
                Verb = "properties"
            });
        }
        catch
        {
            // Ignore shell failures to keep UI responsive.
        }
    }

    [RelayCommand]
    private async Task NewTabAsync()
    {
        var newPath = string.IsNullOrWhiteSpace(CurrentPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : CurrentPath;

        var tab = CreateTab(newPath);
        Tabs.Add(tab);
        SetSelectedTab(tab);

        await NavigateToPathAsync(tab.Path, addToHistory: false, forceReload: true);
    }

    [RelayCommand]
    private async Task SelectTabAsync(ExplorerTabViewModel? tab)
    {
        if (tab is null || ReferenceEquals(tab, SelectedTab))
            return;

        SetSelectedTab(tab);
        await NavigateToPathAsync(tab.Path, addToHistory: false, forceReload: true);
    }

    [RelayCommand]
    private async Task CloseTabAsync(ExplorerTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
            return;

        if (Tabs.Count == 1)
            return;

        var closingIndex = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(tab, SelectedTab);
        Tabs.Remove(tab);

        if (!wasSelected)
            return;

        var nextIndex = Math.Clamp(closingIndex, 0, Tabs.Count - 1);
        var nextTab = Tabs[nextIndex];
        SetSelectedTab(nextTab);

        await NavigateToPathAsync(nextTab.Path, addToHistory: false, forceReload: true);
    }

    [RelayCommand]
    private async Task CloseCurrentTabAsync()
    {
        await CloseTabAsync(SelectedTab);
    }

    [RelayCommand]
    private async Task CloseOtherTabsAsync()
    {
        if (SelectedTab is null || Tabs.Count <= 1)
            return;

        var keep = SelectedTab;
        for (var i = Tabs.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Tabs[i], keep))
                Tabs.RemoveAt(i);
        }

        SetSelectedTab(keep);
        await NavigateToPathAsync(keep.Path, addToHistory: false, forceReload: true);
    }

    private async Task NavigateToPathAsync(string path, bool addToHistory, bool forceReload = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!_fileSystem.DirectoryExists(path))
            return;

        var normalizedPath = NormalizePath(path);
        var samePath = PathsEqual(CurrentPath, normalizedPath);

        if (SelectedTab is not null && addToHistory && !string.IsNullOrWhiteSpace(CurrentPath) && !samePath)
        {
            SelectedTab.BackHistory.Push(CurrentPath);
            SelectedTab.ForwardHistory.Clear();
        }

        CurrentPath = normalizedPath;
        IsSearchActive = false;

        if (SelectedTab is not null)
        {
            SelectedTab.Path = normalizedPath;
            SelectedTab.Title = BuildTabTitle(normalizedPath);
        }

        UpdateBreadcrumbs();

        if (!samePath || forceReload || Items.Count == 0)
            await LoadDirectoryAsync();
        else
            UpdateItemCountText();

        UpdateWindowTitle();
        UpdateNavigationState();
        UpdateDetailsState();
    }

    private void UpdateBreadcrumbs()
    {
        BreadcrumbSegments.Clear();

        if (string.IsNullOrWhiteSpace(CurrentPath))
            return;

        var normalized = NormalizePath(CurrentPath);
        var segments = new Stack<string>();
        var cursor = normalized;

        while (!string.IsNullOrWhiteSpace(cursor))
        {
            segments.Push(cursor);
            var parent = _fileSystem.GetParentPath(cursor);
            if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, cursor))
                break;
            cursor = parent;
        }

        var showSeparator = false;
        while (segments.Count > 0)
        {
            var full = segments.Pop();
            BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel(BuildBreadcrumbLabel(full), full, showSeparator));
            showSeparator = true;
        }
    }

    private async Task LoadDirectoryAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;

        try
        {
            var fsItems = await _fileSystem.GetItemsAsync(CurrentPath, sort: null, filter: null, ct);
            ct.ThrowIfCancellationRequested();

            Items.Clear();
            SelectedItem = null;

            foreach (var fsItem in fsItems)
            {
                Items.Add(new FileItemViewModel
                {
                    Name = fsItem.Name,
                    FullPath = fsItem.FullPath,
                    IsDirectory = fsItem.IsDirectory,
                    Size = fsItem.Size,
                    Modified = fsItem.LastModified,
                    Created = fsItem.DateCreated,
                    Icon = fsItem.IsDirectory ? "Folder" : GetFileIcon(fsItem.Extension)
                });
            }

            UpdateItemCountText();
        }
        catch (OperationCanceledException)
        {
            // Expected on rapid navigation.
        }
        catch
        {
            // Access denied or unexpected IO errors.
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateDetailsState()
    {
        if (SelectedItem is not null)
        {
            DetailsTitleText = SelectedItem.Name;
            DetailsDateModifiedText = FormatDate(SelectedItem.Modified);
            DetailsDateCreatedText = FormatDate(SelectedItem.Created);
            DetailsPathText = SelectedItem.FullPath;

            if (SelectedItem.IsDirectory)
            {
                DetailsIconResourceKey = "Icon.Files.App.ThemedIcons.Folder";
                DetailsMetricLabel = "Item count";
                DetailsMetricValue = TryGetDirectoryEntryCount(SelectedItem.FullPath) is int selectedCount
                    ? selectedCount.ToString()
                    : "--";
            }
            else
            {
                DetailsIconResourceKey = "Icon.Files.App.ThemedIcons.File";
                DetailsMetricLabel = "Size";
                DetailsMetricValue = SelectedItem.SizeDisplay;
            }

            return;
        }

        DetailsIconResourceKey = "Icon.Files.App.ThemedIcons.Folder";
        DetailsTitleText = BuildTabTitle(CurrentPath);
        DetailsMetricLabel = "Item count";
        DetailsMetricValue = ItemCountText;
        DetailsPathText = CurrentPath;

        try
        {
            var dirInfo = new DirectoryInfo(CurrentPath);
            DetailsDateModifiedText = FormatDate(dirInfo.LastWriteTime);
            DetailsDateCreatedText = FormatDate(dirInfo.CreationTime);
        }
        catch
        {
            DetailsDateModifiedText = "--";
            DetailsDateCreatedText = "--";
        }
    }

    private void UpdatePreviewState()
    {
        if (SelectedItem is null)
        {
            PreviewFilePath = null;
            IsPreviewAvailable = false;
            PreviewStatusText = "Select a file to preview";
            return;
        }

        if (SelectedItem.IsDirectory)
        {
            PreviewFilePath = null;
            IsPreviewAvailable = false;
            PreviewStatusText = "Preview is not available for folders";
            return;
        }

        if (!File.Exists(SelectedItem.FullPath))
        {
            PreviewFilePath = null;
            IsPreviewAvailable = false;
            PreviewStatusText = "Preview is not available";
            return;
        }

        var extension = SelectedItem.Extension.ToLowerInvariant();
        if (extension is ".exe" or ".dll" or ".sys" or ".bin")
        {
            PreviewFilePath = null;
            IsPreviewAvailable = false;
            PreviewStatusText = "Preview is not available for this file type";
            return;
        }

        PreviewFilePath = SelectedItem.FullPath;
        IsPreviewAvailable = true;
        PreviewStatusText = string.Empty;
    }

    private void UpdateItemCountText()
    {
        var dirs = Items.Count(i => i.IsDirectory);
        var files = Items.Count - dirs;
        ItemCountText = $"{dirs} pastas, {files} arquivos";
        if (SelectedItem is null)
            UpdateDetailsState();
    }

    private static string FormatDate(DateTime value) =>
        value == default ? "--" : value.ToString("dd MMMM yyyy HH:mm");

    private static int? TryGetDirectoryEntryCount(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Count();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateWindowTitle()
    {
        var title = BuildTabTitle(CurrentPath);
        if (IsSearchActive && !string.IsNullOrWhiteSpace(SearchQuery))
            WindowTitle = $"Finder Explorer — {title} — pesquisa: {SearchQuery}";
        else
            WindowTitle = $"Finder Explorer — {title}";
    }

    private void UpdateNavigationState()
    {
        CanNavigateBack = SelectedTab?.BackHistory.Count > 0;
        CanNavigateForward = SelectedTab?.ForwardHistory.Count > 0;
        CanNavigateUp = _fileSystem.GetParentPath(CurrentPath) is not null;
    }

    private void SetSelectedTab(ExplorerTabViewModel tab)
    {
        if (ReferenceEquals(SelectedTab, tab))
            return;

        if (SelectedTab is not null)
            SelectedTab.IsSelected = false;

        SelectedTab = tab;
        SelectedTab.IsSelected = true;

        UpdateNavigationState();
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

    private static ExplorerTabViewModel CreateTab(string path)
    {
        var normalizedPath = NormalizePath(path);
        return new ExplorerTabViewModel(BuildTabTitle(normalizedPath), normalizedPath);
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string BuildBreadcrumbLabel(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(path, root))
            return root.TrimEnd(Path.DirectorySeparatorChar);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static string BuildTabTitle(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(path, root))
            return root.TrimEnd(Path.DirectorySeparatorChar);

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
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
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "Image",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" => "Video",
        ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" => "Audio",
        ".pdf" => "PDF",
        ".doc" or ".docx" or ".odt" => "Doc",
        ".xls" or ".xlsx" or ".csv" => "Sheet",
        ".ppt" or ".pptx" => "Slides",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Archive",
        ".exe" or ".msi" => "App",
        ".txt" or ".md" or ".log" => "Text",
        ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" => "Code",
        _ => "File"
    };
}

/// <summary>
/// Represents a clickable breadcrumb segment with its full path for navigation.
/// </summary>
public partial class BreadcrumbSegmentViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _fullPath;
    [ObservableProperty] private bool _showSeparator;

    public BreadcrumbSegmentViewModel(string label, string fullPath, bool showSeparator)
    {
        _label = label;
        _fullPath = fullPath;
        _showSeparator = showSeparator;
    }
}

/// <summary>
/// Represents an explorer tab and its per-tab navigation history.
/// </summary>
public partial class ExplorerTabViewModel : ObservableObject
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _path;
    [ObservableProperty] private bool _isSelected;

    internal Stack<string> BackHistory { get; } = new();
    internal Stack<string> ForwardHistory { get; } = new();

    public ExplorerTabViewModel(string title, string path)
    {
        _title = title;
        _path = path;
    }
}
