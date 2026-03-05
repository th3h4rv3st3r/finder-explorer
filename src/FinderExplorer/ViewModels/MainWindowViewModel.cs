// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinderExplorer.Core.Services;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _searchService;
    private readonly IThumbnailService _thumbnailService;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _iconCts;
    private CancellationTokenSource? _detailsIconCts;
    private readonly SemaphoreSlim _iconResolveLimiter = new(8);
    private readonly string _shellIconCacheDir;

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
    private string? _detailsIconImagePath;

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

    private const string NanaZipIconUri = "avares://FinderExplorer/Assets/Icons/WinUI/NanaZip.png";
    private const string IconCacheVersion = "v5";
    private const int ListIconResolveSizePx = 32;
    private const int DetailsIconResolveSizePx = 256;
    private const int WindowsFolderIconResolveSizePx = 64;

    private static readonly HashSet<string> IconOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".sys", ".drv", ".ocx", ".cpl", ".mui", ".scr",
        ".msi", ".msp", ".msu", ".cab", ".cat", ".inf", ".reg",
        ".lnk", ".url", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".iso", ".img", ".vhd", ".vhdx"
    };

    public SidebarItemViewModel SidebarHome { get; }
    public SidebarItemViewModel SidebarNetwork { get; }
    public SidebarItemViewModel SidebarNextcloud { get; }

    private bool HasSelectedItem => SelectedItem is not null;

    public MainWindowViewModel(
        IFileSystemService fileSystem,
        ISearchService searchService,
        IThumbnailService thumbnailService)
    {
        _fileSystem = fileSystem;
        _searchService = searchService;
        _thumbnailService = thumbnailService;
        _shellIconCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinderExplorer",
            "icons");
        Directory.CreateDirectory(_shellIconCacheDir);

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
        _ = ResolveSelectedItemDetailsIconAsync(value);
    }

    public bool DetailsHasImageIcon => !string.IsNullOrWhiteSpace(DetailsIconImagePath);

    partial void OnDetailsIconImagePathChanged(string? value) => OnPropertyChanged(nameof(DetailsHasImageIcon));

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

                var (iconImagePath, iconResourceKey) = ResolveFileItemIconResources(result.IsDirectory, result.Extension);

                Items.Add(new FileItemViewModel
                {
                    IconImagePath = iconImagePath,
                    IconResourceKey = iconResourceKey,
                    Name = result.Name,
                    FullPath = result.FullPath,
                    IsDirectory = result.IsDirectory,
                    Size = result.Size,
                    Modified = result.LastModified,
                    Created = result.LastModified,
                    Icon = result.IsDirectory ? "Folder" : GetFileIcon(result.Extension)
                });
            }

            BeginResolveVisibleItemIcons();

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
            ApplyTabIcon(SelectedTab, normalizedPath);
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
                var (iconImagePath, iconResourceKey) = ResolveFileItemIconResources(fsItem.IsDirectory, fsItem.Extension);

                Items.Add(new FileItemViewModel
                {
                    IconImagePath = iconImagePath,
                    IconResourceKey = iconResourceKey,
                    Name = fsItem.Name,
                    FullPath = fsItem.FullPath,
                    IsDirectory = fsItem.IsDirectory,
                    Size = fsItem.Size,
                    Modified = fsItem.LastModified,
                    Created = fsItem.DateCreated,
                    Icon = fsItem.IsDirectory ? "Folder" : GetFileIcon(fsItem.Extension)
                });
            }

            BeginResolveVisibleItemIcons();
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
            DetailsIconImagePath = SelectedItem.IconImagePath;
            DetailsIconResourceKey = SelectedItem.IconResourceKey;

            if (SelectedItem.IsDirectory)
            {
                DetailsMetricLabel = "Item count";
                DetailsMetricValue = TryGetDirectoryEntryCount(SelectedItem.FullPath) is int selectedCount
                    ? selectedCount.ToString()
                    : "--";
            }
            else
            {
                DetailsMetricLabel = "Size";
                DetailsMetricValue = SelectedItem.SizeDisplay;
            }

            return;
        }

        DetailsIconImagePath = SelectedTab?.IconImagePath;
        DetailsIconResourceKey = SelectedTab?.IconResourceKey ?? "Icon.Files.App.ThemedIcons.Folder";
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

    private void BeginResolveVisibleItemIcons()
    {
        _iconCts?.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;

        var snapshot = Items.ToList();
        _ = Task.Run(async () =>
        {
            var tasks = new List<Task>(snapshot.Count);
            foreach (var item in snapshot)
            {
                if (ct.IsCancellationRequested)
                    break;

                if (IsCompressedExtension(item.Extension))
                    continue;

                tasks.Add(ResolveFileItemShellIconAsync(item, ct));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Navigation changed while resolving icons.
            }
        }, ct);
    }

    private async Task ResolveFileItemShellIconAsync(FileItemViewModel item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.FullPath))
            return;

        string? iconPath;
        await _iconResolveLimiter.WaitAsync(ct);
        try
        {
            iconPath = await TryGetShellIconPathAsync(item.FullPath, ListIconResolveSizePx, ct, preferThumbnail: false);
        }
        catch
        {
            return;
        }
        finally
        {
            _iconResolveLimiter.Release();
        }

        if (string.IsNullOrWhiteSpace(iconPath) || ct.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!Items.Contains(item))
                return;

            item.IconImagePath = iconPath;
            if (ReferenceEquals(item, SelectedItem))
                UpdateDetailsState();
        });
    }

    private async Task ResolveSelectedItemDetailsIconAsync(FileItemViewModel? item)
    {
        _detailsIconCts?.Cancel();

        if (item is null || string.IsNullOrWhiteSpace(item.FullPath))
            return;

        _detailsIconCts = new CancellationTokenSource();
        var ct = _detailsIconCts.Token;

        string? iconPath;
        await _iconResolveLimiter.WaitAsync(ct);
        try
        {
            iconPath = await TryGetShellIconPathAsync(item.FullPath, DetailsIconResolveSizePx, ct, preferThumbnail: true);
        }
        catch
        {
            return;
        }
        finally
        {
            _iconResolveLimiter.Release();
        }

        if (string.IsNullOrWhiteSpace(iconPath) || ct.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ReferenceEquals(SelectedItem, item))
                return;

            DetailsIconImagePath = iconPath;
        });
    }

    private async Task<string?> TryGetShellIconPathAsync(string path, int sizePx, CancellationToken ct, bool preferThumbnail)
    {
        var cachePath = BuildIconCachePath(path, sizePx, preferThumbnail);
        if (File.Exists(cachePath))
            return cachePath;

        if (!preferThumbnail)
            return TrySaveAssociatedShellIconAsPng(path, cachePath, sizePx) ? cachePath : null;

        // Keep folder visuals crisp and consistent with Windows system icons.
        if (Directory.Exists(path))
            return TrySaveAssociatedShellIconAsPng(path, cachePath, sizePx) ? cachePath : null;

        var extension = Path.GetExtension(path);
        if (ShouldPreferAssociatedIcon(extension))
            return TrySaveAssociatedShellIconAsPng(path, cachePath, sizePx) ? cachePath : null;

        ThumbnailData? thumb = null;
        try
        {
            thumb = await _thumbnailService.GetThumbnailAsync(path, sizePx, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            // If native thumbnail extraction fails, keep going with extension icon fallback.
        }

        if (thumb is not null && thumb.Pixels.Length > 0 && IsThumbnailQualityAcceptable(thumb, sizePx))
        {
            try
            {
                SaveThumbnailAsPng(cachePath, thumb);
                return cachePath;
            }
            catch
            {
                return null;
            }
        }

        if (ct.IsCancellationRequested)
            return null;

        // Fallback for files without thumbnails: use shell-associated extension icon.
        return TrySaveAssociatedShellIconAsPng(path, cachePath, sizePx) ? cachePath : null;
    }

    private static bool IsThumbnailQualityAcceptable(ThumbnailData thumb, int requestedSizePx)
    {
        var minSide = Math.Min(thumb.Width, thumb.Height);
        var minRequired = requestedSizePx >= 192 ? 96
            : requestedSizePx >= 64 ? 32
            : 16;

        return minSide >= minRequired;
    }

    private static bool ShouldPreferAssociatedIcon(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return IconOnlyExtensions.Contains(extension);
    }

    private string BuildIconCachePath(string path, int sizePx, bool preferThumbnail)
    {
        var mode = preferThumbnail ? "thumb" : "icon";
        var key = $"{IconCacheVersion}|{path}|{sizePx}|{mode}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        var file = Convert.ToHexString(hash).ToLowerInvariant();
        return Path.Combine(_shellIconCacheDir, $"{file}.png");
    }

    private static void SaveThumbnailAsPng(string filePath, ThumbnailData thumb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        using var bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(thumb.Width, thumb.Height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var fb = bitmap.Lock())
        {
            Marshal.Copy(thumb.Pixels, 0, fb.Address, thumb.Pixels.Length);
        }

        using var fs = File.Create(filePath);
        bitmap.Save(fs);
    }

    private static bool TrySaveAssociatedShellIconAsPng(string path, string cachePath, int sizePx)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if ((TryGetSystemImageListFileIconHandle(path, sizePx, out var fileIcon) ||
             TryGetShellFileIconHandle(path, sizePx, out fileIcon)) &&
            TryWriteIconHandleAsPng(fileIcon, cachePath, sizePx))
            return true;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return (TryGetSystemImageListExtensionIconHandle(extension, sizePx, out var extIcon) ||
                TryGetShellExtensionIconHandle(extension, sizePx, out extIcon))
            && TryWriteIconHandleAsPng(extIcon, cachePath, sizePx);
    }

    private static bool TryGetShellFileIconHandle(string path, int sizePx, out IntPtr hIcon)
    {
        var info = new SHFILEINFOW();
        var flags = SHGFI_ICON | (sizePx <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON);
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            flags);

        hIcon = info.hIcon;
        return result != IntPtr.Zero && hIcon != IntPtr.Zero;
    }

    private static bool TryGetShellExtensionIconHandle(string extension, int sizePx, out IntPtr hIcon)
    {
        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = $".{extension}";

        var info = new SHFILEINFOW();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (sizePx <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON);
        var result = SHGetFileInfo(
            extension,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            flags);

        hIcon = info.hIcon;
        return result != IntPtr.Zero && hIcon != IntPtr.Zero;
    }

    private static bool TryGetSystemImageListFileIconHandle(string path, int sizePx, out IntPtr hIcon)
    {
        var info = new SHFILEINFOW();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            SHGFI_SYSICONINDEX);

        if (result == IntPtr.Zero || info.iIcon < 0)
        {
            hIcon = IntPtr.Zero;
            return false;
        }

        return TryGetImageListIconHandle(info.iIcon, sizePx, out hIcon);
    }

    private static bool TryGetSystemImageListExtensionIconHandle(string extension, int sizePx, out IntPtr hIcon)
    {
        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = $".{extension}";

        var info = new SHFILEINFOW();
        var result = SHGetFileInfo(
            extension,
            FILE_ATTRIBUTE_NORMAL,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFOW>(),
            SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES);

        if (result == IntPtr.Zero || info.iIcon < 0)
        {
            hIcon = IntPtr.Zero;
            return false;
        }

        return TryGetImageListIconHandle(info.iIcon, sizePx, out hIcon);
    }

    private static bool TryGetImageListIconHandle(int iconIndex, int sizePx, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        var imageListKind = sizePx >= 192 ? SHIL_JUMBO
            : sizePx >= 64 ? SHIL_EXTRALARGE
            : sizePx >= 32 ? SHIL_LARGE
            : SHIL_SMALL;

        var iid = IID_IImageList;
        IImageList? imageList = null;
        try
        {
            var hr = SHGetImageList(imageListKind, ref iid, out imageList);
            if (hr != 0 || imageList is null)
                return false;

            hr = imageList.GetIcon(iconIndex, ILD_TRANSPARENT, out hIcon);
            return hr == 0 && hIcon != IntPtr.Zero;
        }
        catch
        {
            hIcon = IntPtr.Zero;
            return false;
        }
        finally
        {
            if (imageList is not null)
                Marshal.ReleaseComObject(imageList);
        }
    }

    private static bool TryWriteIconHandleAsPng(IntPtr hIcon, string cachePath, int sizePx)
    {
        if (hIcon == IntPtr.Zero)
            return false;

        System.Drawing.Icon? icon = null;
        try
        {
            using var sourceIcon = System.Drawing.Icon.FromHandle(hIcon);
            icon = (System.Drawing.Icon)sourceIcon.Clone();
        }
        catch
        {
            return false;
        }
        finally
        {
            DestroyIcon(hIcon);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

            if (icon is null)
                return false;

            using (icon)
            using (var bitmap = icon.ToBitmap())
            {
                if (bitmap.Width != sizePx || bitmap.Height != sizePx)
                {
                    using var resized = new System.Drawing.Bitmap(sizePx, sizePx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var gfx = System.Drawing.Graphics.FromImage(resized))
                    {
                        gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        gfx.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        gfx.DrawImage(bitmap, new System.Drawing.Rectangle(0, 0, sizePx, sizePx));
                    }

                    resized.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png);
                }
                else
                {
                    bitmap.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int SHIL_SMALL = 0x0;
    private const int SHIL_LARGE = 0x1;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_TRANSPARENT = 0x1;
    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFOW psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(
        int iImageList,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
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
        var tab = new ExplorerTabViewModel(BuildTabTitle(normalizedPath), normalizedPath);
        ApplyTabIcon(tab, normalizedPath);
        return tab;
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

    private static void ApplyTabIcon(ExplorerTabViewModel tab, string normalizedPath)
    {
        var (imagePath, resourceKey) = ResolveTabIcon(normalizedPath);
        tab.IconImagePath = imagePath;
        tab.IconResourceKey = resourceKey;
    }

    private static (string? ImagePath, string ResourceKey) ResolveTabIcon(string normalizedPath)
    {
        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.DesktopDirectory))
            return (GetSidebarIconUri("desktop"), "Icon.Files.App.ThemedIcons.Folder");

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyDocuments))
            return (GetSidebarIconUri("documents"), "Icon.Files.App.ThemedIcons.Folder");

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.UserProfile))
            return (GetSidebarIconUri("folder"), "Icon.Files.App.ThemedIcons.Folder");

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyMusic))
            return (GetSidebarIconUri("music"), "Icon.Files.App.ThemedIcons.Folder");

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyPictures))
            return (GetSidebarIconUri("pictures"), "Icon.Files.App.ThemedIcons.Folder");

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyVideos))
            return (GetSidebarIconUri("videos"), "Icon.Files.App.ThemedIcons.Folder");

        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(downloads))
        {
            var downloadsPath = NormalizePath(Path.Combine(downloads, "Downloads"));
            if (PathsEqual(normalizedPath, downloadsPath))
                return (GetSidebarIconUri("downloads"), "Icon.Files.App.ThemedIcons.Folder");
        }

        if (IsNextcloudPath(normalizedPath))
            return (GetSidebarIconUri("nextcloud"), "Icon.Files.App.ThemedIcons.Folder");

        var root = Path.GetPathRoot(normalizedPath);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(normalizedPath, root))
            return (GetSidebarIconUri("drive"), "Icon.Files.App.ThemedIcons.Folder");

        if (normalizedPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return (GetSidebarIconUri("network"), "Icon.Files.App.ThemedIcons.Folder");

        return (GetSidebarIconUri("folder"), "Icon.Files.App.ThemedIcons.Folder");
    }

    private static bool IsSpecialFolder(string normalizedPath, Environment.SpecialFolder folder)
    {
        var folderPath = Environment.GetFolderPath(folder);
        return !string.IsNullOrWhiteSpace(folderPath) && PathsEqual(normalizedPath, NormalizePath(folderPath));
    }

    private static bool IsNextcloudPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var profileNextcloudRoot = NormalizePath(Path.Combine(userProfile, "Nextcloud"));
            if (PathsEqual(normalizedPath, profileNextcloudRoot) ||
                normalizedPath.StartsWith($"{profileNextcloudRoot}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var segmentMarker = $"{Path.DirectorySeparatorChar}Nextcloud{Path.DirectorySeparatorChar}";
        if (normalizedPath.Contains(segmentMarker, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedPath.EndsWith($"{Path.DirectorySeparatorChar}Nextcloud", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("\\Nextcloud", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.EndsWith("/Nextcloud", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowsFolderIconPath()
    {
        if (!OperatingSystem.IsWindows())
            return "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png";

        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FinderExplorer",
                "icons");
            Directory.CreateDirectory(cacheDir);

            var iconPath = Path.Combine(cacheDir, $"windows-folder-{WindowsFolderIconResolveSizePx}.png");
            if (File.Exists(iconPath))
                return iconPath;

            var folderSeed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(folderSeed) || !Directory.Exists(folderSeed))
                folderSeed = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            if (!string.IsNullOrWhiteSpace(folderSeed) &&
                TrySaveAssociatedShellIconAsPng(folderSeed, iconPath, WindowsFolderIconResolveSizePx))
            {
                return iconPath;
            }
        }
        catch
        {
            // Fallback below.
        }

        return "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png";
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
        "folder" => GetWindowsFolderIconPath(),
        "recyclebin" => "avares://FinderExplorer/Assets/Icons/WinUI/RecycleBin.png",
        "drive" => "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png",
        "network" => "avares://FinderExplorer/Assets/Icons/WinUI/Network.png",
        "nextcloud" => "avares://FinderExplorer/Assets/Icons/WinUI/Nextcloud.ico",
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
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".zst" or ".cab" or ".iso" => "Archive",
        ".exe" or ".msi" => "App",
        ".txt" or ".md" or ".log" => "Text",
        ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" => "Code",
        _ => "File"
    };

    private static (string? ImagePath, string ResourceKey) ResolveFileItemIconResources(bool isDirectory, string? extension)
    {
        if (isDirectory)
            return (null, "Icon.Files.App.ThemedIcons.Folder");

        return IsCompressedExtension(extension)
            ? (NanaZipIconUri, "Icon.Files.App.ThemedIcons.Zip")
            : (null, ResolveStandardFileIconResourceKey(extension));
    }

    private static string ResolveStandardFileIconResourceKey(string? extension)
    {
        var normalized = extension?.Trim().ToLowerInvariant();

        return normalized switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".heic" or ".avif" or ".tif" or ".tiff" => "Icon.Pictures",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".m4v" or ".mpeg" or ".mpg" or ".3gp" => "Icon.Videos",
            ".mp3" or ".flac" or ".wav" or ".ogg" or ".aac" or ".m4a" or ".wma" or ".aiff" or ".mid" or ".midi" => "Icon.Music",
            ".pdf" or ".doc" or ".docx" or ".odt" or ".xls" or ".xlsx" or ".csv" or ".tsv" or ".ppt" or ".pptx" or ".rtf" => "Icon.Documents",
            ".txt" or ".md" or ".log" or ".json" or ".xml" or ".yaml" or ".yml" or ".ini" or ".conf" or ".toml" => "Icon.Documents",
            ".html" or ".htm" or ".css" or ".js" or ".ts" or ".tsx" or ".jsx" or ".cs" or ".cpp" or ".cxx" or ".c" or ".h" or ".hpp" or ".java" or ".py" or ".go" or ".rs" or ".php" or ".sql" or ".ps1" or ".bat" or ".cmd" or ".vbs" => "Icon.Documents",
            ".url" => "Icon.Files.App.ThemedIcons.URL",
            _ => "Icon.Files.App.ThemedIcons.File"
        };
    }

    private static bool IsCompressedExtension(string? extension)
    {
        var normalized = extension?.Trim().ToLowerInvariant();
        return normalized is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".zst" or ".cab" or ".iso";
    }
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
    [ObservableProperty] private string? _iconImagePath;
    [ObservableProperty] private string _iconResourceKey = "Icon.Files.App.ThemedIcons.Folder";

    internal Stack<string> BackHistory { get; } = new();
    internal Stack<string> ForwardHistory { get; } = new();

    public bool HasImageIcon => !string.IsNullOrWhiteSpace(IconImagePath);

    public ExplorerTabViewModel(string title, string path)
    {
        _title = title;
        _path = path;
    }

    partial void OnIconImagePathChanged(string? value) => OnPropertyChanged(nameof(HasImageIcon));
}
