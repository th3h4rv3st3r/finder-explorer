// Copyright (c) Finder Explorer. All rights reserved.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FinderExplorer.Core.Models;
using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using FinderExplorer.Views.Dialogs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FinderExplorer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _searchService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ISettingsService _settingsService;
    private readonly ILifecycleService _lifecycleService;
    private readonly IDefaultFileManagerService _defaultFileManagerService;
    private readonly INextcloudService _nextcloud;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _iconCts;
    private CancellationTokenSource? _detailsIconCts;
    private CancellationTokenSource? _previewCts;
    private readonly SemaphoreSlim _iconResolveLimiter = new(8);
    private readonly string _shellIconCacheDir;
    private readonly Dictionary<string, bool> _nativePreviewSupportByExtension = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSettingsSync;
    private readonly List<string> _clipboardPaths = [];
    private string? _quickLookExecutablePath;
    private bool _quickLookPathResolved;
    private readonly List<SettingsActionItemViewModel> _allSettingsActionItems = [];
    private ClipboardOperation _clipboardOperation = ClipboardOperation.None;

    private const int OperationCanceledHResult = unchecked((int)0x800704C7);
    private const string NetworkSentinelPath = @"\\Network";
    private static readonly IReadOnlyList<SettingsActionDefinition> SettingsActionCatalog =
    [
        new("Navigate back", "NavigateBackCommand", "Alt+Left"),
        new("Navigate forward", "NavigateForwardCommand", "Alt+Right"),
        new("Navigate up", "NavigateUpCommand", "Alt+Up"),
        new("Refresh current folder", "RefreshCommand", "F5"),
        new("Search in current folder", "SearchCommand", "Not set"),
        new("Clear current search", "ClearSearchCommand", "Not set"),
        new("Create new folder", "CreateNewFolderCommand", "Ctrl+Shift+N"),
        new("Open selected item", "OpenSelectedItemCommand", "Enter"),
        new("Preview with QuickLook", "OpenQuickLookCommand", "Space"),
        new("Delete selected item", "DeleteSelectedItemCommand", "Delete"),
        new("Cut selected item", "CutSelectedItemCommand", "Ctrl+X"),
        new("Copy selected item", "CopySelectedItemCommand", "Ctrl+C"),
        new("Paste from clipboard", "PasteFromClipboardCommand", "Ctrl+V"),
        new("Rename selected item", "RenameSelectedItemCommand", "F2"),
        new("Copy selected path", "CopyPathCommand", "Not set"),
        new("Share selected item", "ShareSelectedItemCommand", "Not set"),
        new("Generate Nextcloud public link", "GeneratePublicLinkCommand", "Not set"),
        new("Generate Nextcloud internal link", "GenerateInternalLinkCommand", "Not set"),
        new("Open new tab", "NewTabCommand", "Ctrl+T"),
        new("Close current tab", "CloseCurrentTabCommand", "Ctrl+W"),
        new("Close other tabs", "CloseOtherTabsCommand", "Not set"),
        new("Toggle details pane", "ToggleDetailsPaneCommand", "Not set"),
        new("Show details pane tab", "ShowDetailsTabCommand", "Not set"),
        new("Show preview pane tab", "ShowPreviewTabCommand", "Not set"),
        new("Open properties", "ShowPropertiesCommand", "Not set"),
        new("Toggle hidden items", "ToggleHiddenItemsCommand", "Not set"),
        new("Open settings", "OpenSettingsCommand", "Not set"),
        new("Close settings", "CloseSettingsCommand", "Escape")
    ];

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
    [NotifyCanExecuteChangedFor(nameof(CutSelectedItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameSelectedItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShareSelectedItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenQuickLookCommand))]
    private FileItemViewModel? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarFavorites = [];

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarVolumes = [];

    [ObservableProperty]
    private ObservableCollection<SidebarItemViewModel> _sidebarNetworkLocations = [];

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
    [NotifyCanExecuteChangedFor(nameof(CloseSettingsCommand))]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _settingsSection = "general";

    [ObservableProperty]
    private bool _settingsRunAtStartup;

    [ObservableProperty]
    private bool _settingsRunInBackground;

    [ObservableProperty]
    private bool _settingsShowTrayIcon;

    [ObservableProperty]
    private bool _settingsSmoothScrolling = true;

    [ObservableProperty]
    private bool _settingsShowDetailsPane = true;

    [ObservableProperty]
    private bool _settingsShowHiddenFiles;

    [ObservableProperty]
    private bool _settingsUseEverythingSearch = true;

    [ObservableProperty]
    private bool _settingsUseGpuAcceleration = true;

    [ObservableProperty]
    private bool _settingsConfirmBeforeDelete = true;

    [ObservableProperty]
    private bool _settingsDefaultFileManager;

    [ObservableProperty]
    private bool _settingsRestorePreviousTabs;

    [ObservableProperty]
    private string _settingsLanguage = "pt-BR";

    [ObservableProperty]
    private string _settingsOperationNotice = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SettingsActionItemViewModel> _settingsActionItems = [];

    [ObservableProperty]
    private string _settingsActionSearchQuery = string.Empty;

    // -----------------------------------------------------------------------
    // Nextcloud Setup Wizard State
    // -----------------------------------------------------------------------

    [ObservableProperty]
    private int _ncSetupStep; // 0 = overview, 1 = URL, 2 = credentials

    [ObservableProperty]
    private string _ncSetupUrl = string.Empty;

    [ObservableProperty]
    private string _ncSetupUser = string.Empty;

    [ObservableProperty]
    private string _ncSetupPassword = string.Empty;

    [ObservableProperty]
    private bool _isNcSetupUrlValid;

    [ObservableProperty]
    private bool _isNcSetupConnecting;

    [ObservableProperty]
    private string _ncSetupStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isNcConnected;

    [ObservableProperty]
    private bool _isDetailsPaneVisible = true;

    [ObservableProperty]
    private GridLength _sidebarPaneWidth = new(240, GridUnitType.Pixel);

    [ObservableProperty]
    private double _detailsPaneWidth = 320;

    [ObservableProperty]
    private double _detailsSplitterWidth = 5;

    [ObservableProperty]
    private bool _isDetailsTabSelected = true;

    [ObservableProperty]
    private bool _isPreviewTabSelected;

    [ObservableProperty]
    private bool _isSearchActive;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SortField _activeSortField = SortField.Name;

    [ObservableProperty]
    private bool _isSortAscending = true;

    [ObservableProperty]
    private bool _showHiddenItems;

    [ObservableProperty]
    private bool _showFileExtensions = true;

    [ObservableProperty]
    private bool _autoChooseBestLayout = true;

    [ObservableProperty]
    private double _viewSizeSliderValue = 1.0;

    [ObservableProperty]
    private ExplorerLayoutMode _activeLayoutMode = ExplorerLayoutMode.Details;

    [ObservableProperty]
    private GridLength _nameColumnWidth = new(360);

    [ObservableProperty]
    private GridLength _dateModifiedColumnWidth = new(190);

    [ObservableProperty]
    private GridLength _typeColumnWidth = new(130);

    [ObservableProperty]
    private GridLength _sizeColumnWidth = new(100);

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
    private string? _previewFallbackImagePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewZoomOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewZoomResetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewFitCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewRotateLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImagePreviewRotateRightCommand))]
    private string? _imagePreviewFilePath;

    [ObservableProperty]
    private string? _mediaPreviewFilePath;

    [ObservableProperty]
    private string? _textPreviewContent;

    [ObservableProperty]
    private double _imagePreviewZoom = 1.0;

    [ObservableProperty]
    private int _imagePreviewRotation;

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
    private const int WindowsFolderIconResolveSizePx = 256;
    private const int PreviewRasterResolveSizePx = 1024;

    private static readonly HashSet<string> IconOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".sys", ".drv", ".ocx", ".cpl", ".mui", ".scr",
        ".msi", ".msp", ".msu", ".cab", ".cat", ".inf", ".reg",
        ".lnk", ".url", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".js",
        ".iso", ".img", ".vhd", ".vhdx"
    };

    private static readonly HashSet<string> ImagePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".gif", ".webp",
        ".tif", ".tiff", ".ico", ".cur", ".avif", ".heic", ".heif",
        ".svg", ".svgz", ".jxl", ".jxr", ".jp2", ".j2k"
    };

    private static readonly HashSet<string> MediaPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpeg", ".mpg", ".3gp",
        ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".wma", ".aiff", ".mid", ".midi"
    };

    private static readonly HashSet<string> TextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".toml",
        ".log", ".csv", ".tsv", ".sql", ".ps1", ".bat", ".cmd", ".sh", ".xaml", ".csproj", ".sln",
        ".cs", ".cpp", ".cxx", ".c", ".h", ".hpp", ".java", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css"
    };

    private static readonly HashSet<string> DedicatedNativePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private static readonly HashSet<string> RawPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".orf", ".rw2", ".raf", ".pef", ".srw", ".x3f", ".raw"
    };

    private enum ClipboardOperation
    {
        None,
        Copy,
        Cut
    }

    public enum ExplorerLayoutMode
    {
        Details,
        List,
        Cards,
        Grid,
        Columns
    }

    public SidebarItemViewModel SidebarHome { get; }
    public SidebarItemViewModel SidebarNetwork { get; }
    public SidebarItemViewModel SidebarNextcloud { get; }

    private bool HasSelectedItem => SelectedItem is not null;
    public bool HasSelection => SelectedItem is not null;
    public bool IsSortByName => ActiveSortField == SortField.Name;
    public bool IsSortByModified => ActiveSortField == SortField.Modified;
    public bool IsSortByType => ActiveSortField == SortField.Type;
    public bool IsSortBySize => ActiveSortField == SortField.Size;
    public bool IsSortDescending => !IsSortAscending;
    public string SortDirectionIconKey => IsSortAscending ? "Icon.ChevronUp" : "Icon.ChevronDown";
    public bool IsLayoutDetails => ActiveLayoutMode == ExplorerLayoutMode.Details;
    public bool IsLayoutList => ActiveLayoutMode == ExplorerLayoutMode.List;
    public bool IsLayoutCards => ActiveLayoutMode == ExplorerLayoutMode.Cards;
    public bool IsLayoutGrid => ActiveLayoutMode == ExplorerLayoutMode.Grid;
    public bool IsLayoutColumns => ActiveLayoutMode == ExplorerLayoutMode.Columns;
    public double FileListIconSize => Math.Clamp(14 + (ViewSizeSliderValue * 2), 14, 24);
    public bool HasClipboardItems => _clipboardPaths.Count > 0;
    public bool IsSettingsGeneralSection => string.Equals(SettingsSection, "general", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsAppearanceSection => string.Equals(SettingsSection, "appearance", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsLayoutSection => string.Equals(SettingsSection, "layout", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsFilesFoldersSection => string.Equals(SettingsSection, "files_folders", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsActionsSection => string.Equals(SettingsSection, "actions", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsAdvancedSection => string.Equals(SettingsSection, "advanced", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsCloudSection => string.Equals(SettingsSection, "cloud", StringComparison.OrdinalIgnoreCase);
    public bool IsSettingsAboutSection => string.Equals(SettingsSection, "about", StringComparison.OrdinalIgnoreCase);
    public double DetailsPaneMinWidth => IsDetailsPaneVisible ? 220 : 0;
    public GridLength DetailsPaneGridLength => IsDetailsPaneVisible
        ? new GridLength(DetailsPaneWidth, GridUnitType.Pixel)
        : new GridLength(0);
    public bool IsNcSetupStepOverview => NcSetupStep == 0;
    public bool IsNcSetupStepUrl => NcSetupStep == 1;
    public bool IsNcSetupStepCredentials => NcSetupStep == 2;
    public bool HasSettingsOperationNotice => !string.IsNullOrWhiteSpace(SettingsOperationNotice);
    public string AboutVersionText =>
        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string AboutAuthorText => "th3h4rv3st3r";
    public string SupportedLanguagesText => "pt-BR, en_GB";
    public string SettingsActionCountText => $"{SettingsActionItems.Count} commands";
    public string SettingsSectionTitle => SettingsSection switch
    {
        "general" => "General",
        "appearance" => "Appearance",
        "layout" => "Layout",
        "files_folders" => "Files & folders",
        "actions" => "Actions",
        "advanced" => "Advanced",
        "cloud" => "Cloud",
        "about" => "About",
        _ => "General"
    };

    public MainWindowViewModel(
        IFileSystemService fileSystem,
        ISearchService searchService,
        IThumbnailService thumbnailService,
        ISettingsService settingsService,
        ILifecycleService lifecycleService,
        IDefaultFileManagerService defaultFileManagerService,
        INextcloudService nextcloudService)
    {
        _fileSystem = fileSystem;
        _searchService = searchService;
        _thumbnailService = thumbnailService;
        _settingsService = settingsService;
        _lifecycleService = lifecycleService;
        _defaultFileManagerService = defaultFileManagerService;
        _nextcloud = nextcloudService;
        _shellIconCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinderExplorer",
            "icons");
        Directory.CreateDirectory(_shellIconCacheDir);
        LoadPersistedSettings();
        InitializeSettingsActions();

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        SidebarHome = new SidebarItemViewModel("Home", userProfile, GetSidebarIconUri("home"));
        SidebarNetwork = new SidebarItemViewModel("Network", NetworkSentinelPath, GetSidebarIconUri("network"), canNavigate: true, itemType: SidebarItemType.Network);
        SidebarNextcloud = new SidebarItemViewModel("Nextcloud", "nc:///", GetSidebarIconUri("nextcloud"), canNavigate: true, itemType: SidebarItemType.Nextcloud);
        SidebarHome.IsSelected = true;

        InitializeSidebar();
        InitializeNetworkLocations();

        if (SettingsRestorePreviousTabs && _settingsService.Current.PreviousTabs is { Count: > 0 } prevTabs)
        {
            ExplorerTabViewModel? lastTab = null;
            foreach (var path in prevTabs)
            {
                var tab = CreateTab(path);
                Tabs.Add(tab);
                lastTab = tab;
            }

            if (lastTab != null)
            {
                lastTab.IsSelected = true;
                SelectedTab = lastTab;
                _ = NavigateToPathAsync(lastTab.Path, addToHistory: false, forceReload: true);
            }
        }
        else
        {
            var initialTab = CreateTab(userProfile);
            initialTab.IsSelected = true;
            Tabs.Add(initialTab);
            SelectedTab = initialTab;

            _ = NavigateToPathAsync(initialTab.Path, addToHistory: false, forceReload: true);
        }
    }

    partial void OnSelectedItemChanged(FileItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        UpdateDetailsState();
        UpdatePreviewState();
        _ = ResolveSelectedItemDetailsIconAsync(value);
        _ = ResolveSelectedItemPreviewAsync(value);
    }

    partial void OnCurrentPathChanged(string value)
    {
        PasteFromClipboardCommand.NotifyCanExecuteChanged();
    }

    public bool DetailsHasImageIcon => !string.IsNullOrWhiteSpace(DetailsIconImagePath);
    public bool HasPreviewFallbackImage => !string.IsNullOrWhiteSpace(PreviewFallbackImagePath);
    public bool IsImagePreviewAvailable => !string.IsNullOrWhiteSpace(ImagePreviewFilePath);
    public bool IsMediaPreviewAvailable => !string.IsNullOrWhiteSpace(MediaPreviewFilePath);
    public bool IsTextPreviewAvailable => !string.IsNullOrWhiteSpace(TextPreviewContent);
    public string ImagePreviewZoomText => $"{(int)Math.Round(ImagePreviewZoom * 100)}%";
    public bool ShowPreviewStatusText => !IsPreviewAvailable && !HasPreviewFallbackImage && !IsImagePreviewAvailable && !IsMediaPreviewAvailable && !IsTextPreviewAvailable;
    public bool IsNativePreviewEnabled => SettingsUseGpuAcceleration;

    partial void OnDetailsIconImagePathChanged(string? value) => OnPropertyChanged(nameof(DetailsHasImageIcon));
    partial void OnPreviewFallbackImagePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPreviewFallbackImage));
        OnPropertyChanged(nameof(ShowPreviewStatusText));
    }

    partial void OnIsPreviewAvailableChanged(bool value)
        => OnPropertyChanged(nameof(ShowPreviewStatusText));

    partial void OnImagePreviewFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsImagePreviewAvailable));
        OnPropertyChanged(nameof(ShowPreviewStatusText));
    }

    partial void OnMediaPreviewFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsMediaPreviewAvailable));
        OnPropertyChanged(nameof(ShowPreviewStatusText));
    }

    partial void OnTextPreviewContentChanged(string? value)
    {
        OnPropertyChanged(nameof(IsTextPreviewAvailable));
        OnPropertyChanged(nameof(ShowPreviewStatusText));
    }

    partial void OnImagePreviewZoomChanged(double value)
    {
        var clamped = Math.Clamp(value, 0.1, 8.0);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            ImagePreviewZoom = clamped;
            return;
        }

        OnPropertyChanged(nameof(ImagePreviewZoomText));
    }

    partial void OnActiveSortFieldChanged(SortField value)
    {
        OnPropertyChanged(nameof(IsSortByName));
        OnPropertyChanged(nameof(IsSortByModified));
        OnPropertyChanged(nameof(IsSortByType));
        OnPropertyChanged(nameof(IsSortBySize));

        if (_suppressSettingsSync)
            return;

        _settingsService.Current.SortField = value;
        _ = SaveSettingsAsync();
    }

    partial void OnIsSortAscendingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortDirectionIconKey));

        if (_suppressSettingsSync)
            return;

        _settingsService.Current.SortAscending = value;
        _ = SaveSettingsAsync();
    }

    partial void OnShowHiddenItemsChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.ShowHiddenFiles = value;
        _suppressSettingsSync = true;
        SettingsShowHiddenFiles = value;
        _suppressSettingsSync = false;
        _ = LoadDirectoryAsync();
        _ = SaveSettingsAsync();
    }

    partial void OnShowFileExtensionsChanged(bool value)
    {
        foreach (var item in Items)
            item.ShowFileExtension = value;
    }

    partial void OnViewSizeSliderValueChanged(double value)
    {
        OnPropertyChanged(nameof(FileListIconSize));
    }

    partial void OnActiveLayoutModeChanged(ExplorerLayoutMode value)
    {
        OnPropertyChanged(nameof(IsLayoutDetails));
        OnPropertyChanged(nameof(IsLayoutList));
        OnPropertyChanged(nameof(IsLayoutCards));
        OnPropertyChanged(nameof(IsLayoutGrid));
        OnPropertyChanged(nameof(IsLayoutColumns));
    }

    partial void OnSidebarPaneWidthChanged(GridLength value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.SidebarPaneWidth = value.Value;
        _ = SaveSettingsAsync();
    }

    partial void OnDetailsPaneWidthChanged(double value)
    {
        OnPropertyChanged(nameof(DetailsPaneGridLength));

        if (_suppressSettingsSync)
            return;

        _settingsService.Current.DetailsPaneWidth = value;
        _ = SaveSettingsAsync();
    }

    partial void OnIsDetailsPaneVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailsPaneMinWidth));
        OnPropertyChanged(nameof(DetailsPaneGridLength));

        if (value)
        {
            if (DetailsPaneWidth <= 0)
                DetailsPaneWidth = 360;

            DetailsSplitterWidth = 5;
        }
        else
        {
            DetailsSplitterWidth = 0;
        }

        if (_suppressSettingsSync)
            return;

        _settingsService.Current.ShowDetailsPane = value;
        _suppressSettingsSync = true;
        SettingsShowDetailsPane = value;
        _suppressSettingsSync = false;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsSettingsGeneralSection));
        OnPropertyChanged(nameof(IsSettingsAppearanceSection));
        OnPropertyChanged(nameof(IsSettingsLayoutSection));
        OnPropertyChanged(nameof(IsSettingsFilesFoldersSection));
        OnPropertyChanged(nameof(IsSettingsActionsSection));
        OnPropertyChanged(nameof(IsSettingsAdvancedSection));
        OnPropertyChanged(nameof(IsSettingsCloudSection));
        OnPropertyChanged(nameof(IsSettingsAboutSection));
        OnPropertyChanged(nameof(SettingsSectionTitle));

        // When entering Cloud section, load current settings
        if (value == "cloud")
            LoadNcSetupFromSettings();
    }

    partial void OnSettingsOperationNoticeChanged(string value)
        => OnPropertyChanged(nameof(HasSettingsOperationNotice));

    partial void OnSettingsActionItemsChanged(ObservableCollection<SettingsActionItemViewModel> value)
        => OnPropertyChanged(nameof(SettingsActionCountText));

    partial void OnSettingsActionSearchQueryChanged(string value)
        => ApplySettingsActionFilter();

    partial void OnNcSetupStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsNcSetupStepOverview));
        OnPropertyChanged(nameof(IsNcSetupStepUrl));
        OnPropertyChanged(nameof(IsNcSetupStepCredentials));
    }

    partial void OnSettingsRunInBackgroundChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.MinimizeToTray = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsRestorePreviousTabsChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.RestorePreviousTabs = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsSmoothScrollingChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.SmoothScrolling = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsShowDetailsPaneChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        IsDetailsPaneVisible = value;
        _settingsService.Current.ShowDetailsPane = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsShowHiddenFilesChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        ShowHiddenItems = value;
        _settingsService.Current.ShowHiddenFiles = value;
        _ = LoadDirectoryAsync();
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsUseEverythingSearchChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.UseEverythingSearch = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsUseGpuAccelerationChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.UseGpuThumbnails = value;
        _settingsService.Current.UseGpuPreview = value;
        _thumbnailService.InvalidateAll();
        OnPropertyChanged(nameof(IsNativePreviewEnabled));
        UpdatePreviewState();
        _ = ResolveSelectedItemPreviewAsync(SelectedItem);
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsConfirmBeforeDeleteChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _settingsService.Current.ConfirmBeforeDelete = value;
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsLanguageChanged(string value)
    {
        if (_suppressSettingsSync)
            return;

        var normalized = NormalizeLanguageCode(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            _suppressSettingsSync = true;
            SettingsLanguage = normalized;
            _suppressSettingsSync = false;
        }

        _settingsService.Current.LanguageCode = normalized;
        UpdateItemCountText();
        UpdateDetailsState();
        _ = SaveSettingsAsync();
    }

    partial void OnSettingsRunAtStartupChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _ = ApplyRunAtStartupChangeAsync(value);
    }

    partial void OnSettingsDefaultFileManagerChanged(bool value)
    {
        if (_suppressSettingsSync)
            return;

        _ = ApplyDefaultFileManagerChangeAsync(value);
    }

    private void LoadPersistedSettings()
    {
        var settings = _settingsService.Current;

        _suppressSettingsSync = true;
        try
        {
            ShowHiddenItems = settings.ShowHiddenFiles;
            ActiveSortField = settings.SortField;
            IsSortAscending = settings.SortAscending;
            IsDetailsPaneVisible = settings.ShowDetailsPane;

            SidebarPaneWidth = new GridLength(settings.SidebarPaneWidth, GridUnitType.Pixel);
            DetailsPaneWidth = settings.DetailsPaneWidth;

            SettingsRunInBackground = settings.MinimizeToTray;
            SettingsRestorePreviousTabs = settings.RestorePreviousTabs;
            SettingsRunAtStartup = settings.RunAtStartup || _lifecycleService.IsRunAtStartupEnabled();
            SettingsShowTrayIcon = false; // not wired yet in native tray bridge
            SettingsSmoothScrolling = settings.SmoothScrolling;
            SettingsShowDetailsPane = settings.ShowDetailsPane;
            SettingsShowHiddenFiles = settings.ShowHiddenFiles;
            SettingsUseEverythingSearch = settings.UseEverythingSearch;
            var gpuAccelerationEnabled = settings.UseGpuThumbnails || settings.UseGpuPreview;
            SettingsUseGpuAcceleration = gpuAccelerationEnabled;
            SettingsConfirmBeforeDelete = settings.ConfirmBeforeDelete;
            SettingsDefaultFileManager = settings.IsDefaultFileManager || _defaultFileManagerService.IsRegistered;
            SettingsLanguage = NormalizeLanguageCode(settings.LanguageCode);
            settings.UseGpuThumbnails = gpuAccelerationEnabled;
            settings.UseGpuPreview = gpuAccelerationEnabled;
            settings.RunAtStartup = SettingsRunAtStartup;
            settings.IsDefaultFileManager = SettingsDefaultFileManager;
        }
        finally
        {
            _suppressSettingsSync = false;
        }
    }

    private async Task ApplyRunAtStartupChangeAsync(bool enabled)
    {
        try
        {
            await _lifecycleService.SetRunAtStartupAsync(enabled);
            var applied = _lifecycleService.IsRunAtStartupEnabled();

            _suppressSettingsSync = true;
            SettingsRunAtStartup = applied;
            _suppressSettingsSync = false;

            _settingsService.Current.RunAtStartup = applied;
            SettingsOperationNotice = string.Empty;
            await SaveSettingsAsync();
        }
        catch
        {
            _suppressSettingsSync = true;
            SettingsRunAtStartup = _settingsService.Current.RunAtStartup;
            _suppressSettingsSync = false;
            SettingsOperationNotice = "Could not update Windows startup option.";
        }
    }

    private async Task ApplyDefaultFileManagerChangeAsync(bool enabled)
    {
        try
        {
            if (enabled)
                await _defaultFileManagerService.RegisterAsync();
            else
                await _defaultFileManagerService.UnregisterAsync();

            var applied = _defaultFileManagerService.IsRegistered;
            _suppressSettingsSync = true;
            SettingsDefaultFileManager = applied;
            _suppressSettingsSync = false;

            _settingsService.Current.IsDefaultFileManager = applied;
            SettingsOperationNotice = string.Empty;
            await SaveSettingsAsync();
        }
        catch
        {
            _suppressSettingsSync = true;
            SettingsDefaultFileManager = _settingsService.Current.IsDefaultFileManager;
            _suppressSettingsSync = false;
            SettingsOperationNotice = "Default file manager change failed. Try running as administrator.";
        }
    }

    private async Task SaveSettingsAsync()
    {
        _settingsService.Current.ShowHiddenFiles = ShowHiddenItems;
        _settingsService.Current.SortField = ActiveSortField;
        _settingsService.Current.SortAscending = IsSortAscending;
        _settingsService.Current.ShowDetailsPane = IsDetailsPaneVisible;
        _settingsService.Current.MinimizeToTray = SettingsRunInBackground;
        _settingsService.Current.UseEverythingSearch = SettingsUseEverythingSearch;
        _settingsService.Current.UseGpuThumbnails = SettingsUseGpuAcceleration;
        _settingsService.Current.UseGpuPreview = SettingsUseGpuAcceleration;
        _settingsService.Current.ConfirmBeforeDelete = SettingsConfirmBeforeDelete;
        _settingsService.Current.IsDefaultFileManager = SettingsDefaultFileManager;
        _settingsService.Current.LanguageCode = NormalizeLanguageCode(SettingsLanguage);
        _settingsService.Current.SmoothScrolling = SettingsSmoothScrolling;
        _settingsService.Current.RunAtStartup = SettingsRunAtStartup;
        _settingsService.Current.RestorePreviousTabs = SettingsRestorePreviousTabs;

        _settingsService.Current.PreviousTabs = Tabs.Select(t => t.Path).ToList();

        try
        {
            await _settingsService.SaveAsync();
        }
        catch
        {
            SettingsOperationNotice = "Could not persist settings right now.";
        }
    }

    private static string NormalizeLanguageCode(string? code)
    {
        if (string.Equals(code, "en_GB", StringComparison.OrdinalIgnoreCase))
            return "en_GB";

        return "pt-BR";
    }

    private void InitializeSidebar()
    {
        // Fast local placeholders so startup never blocks on drive/network probes.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SidebarFavorites = new ObservableCollection<SidebarItemViewModel>
        {
            new("Desktop", Path.Combine(userProfile, "Desktop"), GetSidebarIconUri("desktop")),
            new("Documents", Path.Combine(userProfile, "Documents"), GetSidebarIconUri("documents")),
            new("Downloads", Path.Combine(userProfile, "Downloads"), GetSidebarIconUri("downloads")),
            new("Pictures", Path.Combine(userProfile, "Pictures"), GetSidebarIconUri("pictures")),
            new("Music", Path.Combine(userProfile, "Music"), GetSidebarIconUri("music")),
            new("Videos", Path.Combine(userProfile, "Videos"), GetSidebarIconUri("videos"))
        };
        SidebarVolumes = [];

        _ = Task.Run(() =>
        {
            IReadOnlyList<SidebarItem> sidebarItems;
            try
            {
                sidebarItems = _fileSystem.GetSidebarItems();
            }
            catch
            {
                return;
            }

            var favorites = sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Favorites)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIconUri(i.IconKey)))
                .ToList();

            var volumes = sidebarItems
                .Where(i => i.Section == Core.Models.SidebarSection.Volumes)
                .Select(i => new SidebarItemViewModel(i.Label, i.Path, GetSidebarIconUri(i.IconKey)))
                .ToList();

            Dispatcher.UIThread.Post(() =>
            {
                SidebarFavorites = new ObservableCollection<SidebarItemViewModel>(favorites);
                SidebarVolumes = new ObservableCollection<SidebarItemViewModel>(volumes);
            }, DispatcherPriority.Background);
        });
    }

    private void InitializeNetworkLocations()
    {
        var bookmarks = _settingsService.Current.NetworkLocations ?? [];
        var mapped = new List<SidebarItemViewModel>();

        foreach (var bookmark in bookmarks)
        {
            if (bookmark is null || string.IsNullOrWhiteSpace(bookmark.Path))
                continue;

            var normalizedPath = NormalizeNetworkShortcutPath(bookmark.Path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            var label = string.IsNullOrWhiteSpace(bookmark.Label)
                ? BuildNetworkLocationLabel(normalizedPath)
                : bookmark.Label.Trim();

            var iconKey = IsUncPath(normalizedPath) ? "network" : "folder";
            mapped.Add(new SidebarItemViewModel(
                label,
                normalizedPath,
                GetSidebarIconUri(iconKey),
                canNavigate: true,
                itemType: SidebarItemType.Network));
        }

        SidebarNetworkLocations = new ObservableCollection<SidebarItemViewModel>(
            mapped
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase));
    }

    private async Task SaveNetworkLocationsAsync()
    {
        _settingsService.Current.NetworkLocations = SidebarNetworkLocations
            .Select(item => new NetworkLocationBookmark
            {
                Label = item.Label,
                Path = item.Path
            })
            .ToList();

        try
        {
            await _settingsService.SaveAsync();
        }
        catch
        {
            SettingsOperationNotice = "Could not persist network locations right now.";
        }
    }

    private void InitializeSettingsActions()
    {
        var availableCommandNames = GetAvailableCommandNames();
        _allSettingsActionItems.Clear();
        _allSettingsActionItems.AddRange(
            SettingsActionCatalog
                .Where(definition => availableCommandNames.Contains(definition.CommandName))
                .Select(definition =>
                    new SettingsActionItemViewModel(
                        definition.Title,
                        definition.CommandName,
                        definition.Shortcut))
                .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase));

        ApplySettingsActionFilter();
    }

    private HashSet<string> GetAvailableCommandNames()
    {
        return GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void ApplySettingsActionFilter()
    {
        var query = SettingsActionSearchQuery?.Trim();
        IEnumerable<SettingsActionItemViewModel> filtered = _allSettingsActionItems;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.CommandName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Shortcut.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        SettingsActionItems = new ObservableCollection<SettingsActionItemViewModel>(filtered);
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
        if (IsVirtualNextcloudPath(CurrentPath))
        {
            var remotePart = GetNextcloudRemotePath(CurrentPath);
            if (string.IsNullOrWhiteSpace(remotePart) || remotePart == "/")
                return; // Already at nc root

            var parentRemote = remotePart.TrimEnd('/').Contains('/')
                ? remotePart[..remotePart.TrimEnd('/').LastIndexOf('/')]
                : "/";
            if (string.IsNullOrWhiteSpace(parentRemote)) parentRemote = "/";

            await NavigateToPathAsync("nc://" + parentRemote, addToHistory: true);
            return;
        }

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
        {
            await NavigateToPathAsync(item.FullPath, addToHistory: true);
        }
        else if (!item.IsNextcloudItem)
        {
            _fileSystem.OpenFile(item.FullPath);
        }
        else
        {
            try
            {
                var localPath = await _nextcloud.DownloadFileToCacheAsync(item.NextcloudRemotePath, item.Extension);
                if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                    _fileSystem.OpenFile(localPath);
            }
            catch
            {
                // Ignore download/open failures to keep navigation responsive.
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task OpenSelectedItemAsync()
    {
        await OpenItemAsync(SelectedItem);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task OpenQuickLookAsync()
    {
        var item = SelectedItem;
        if (item is null)
            return;

        var quickLookExe = ResolveQuickLookExecutablePath();
        if (string.IsNullOrWhiteSpace(quickLookExe) || !File.Exists(quickLookExe))
        {
            IsPreviewAvailable = false;
            PreviewStatusText = "QuickLook is not installed. Install it and press Space again.";
            return;
        }

        var targetPath = await ResolveQuickLookTargetPathAsync(item);
        if (string.IsNullOrWhiteSpace(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath)))
        {
            IsPreviewAvailable = false;
            PreviewStatusText = "QuickLook could not open this item.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = quickLookExe,
                Arguments = $"\"{targetPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            IsPreviewAvailable = false;
            PreviewStatusText = "Could not launch QuickLook.";
        }
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
    private async Task CreateNewFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath))
            return;

        if (IsVirtualNextcloudPath(CurrentPath))
        {
            const string defaultRemoteName = "New folder";
            var remoteBase = GetNextcloudRemotePath(CurrentPath).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(remoteBase))
                remoteBase = "/";

            var remoteFolderName = defaultRemoteName;
            var remoteSequence = 2;
            var existingNames = Items
                .Where(item => item.IsDirectory)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            while (existingNames.Contains(remoteFolderName))
                remoteFolderName = $"{defaultRemoteName} ({remoteSequence++})";

            var remotePath = remoteBase == "/"
                ? "/" + remoteFolderName
                : $"{remoteBase}/{remoteFolderName}";

            try
            {
                var created = await _nextcloud.CreateFolderAsync(remotePath);
                if (!created)
                    return;

                await LoadDirectoryAsync();
                SelectedItem = Items.FirstOrDefault(item =>
                    item.IsDirectory &&
                    item.Name.Equals(remoteFolderName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Ignore create failures for remote providers.
            }
            return;
        }

        if (IsNetworkSentinelPath(CurrentPath) || !_fileSystem.DirectoryExists(CurrentPath))
            return;

        const string defaultName = "New folder";
        var folderName = defaultName;
        var sequence = 2;

        while (Directory.Exists(Path.Combine(CurrentPath, folderName)) ||
               File.Exists(Path.Combine(CurrentPath, folderName)))
        {
            folderName = $"{defaultName} ({sequence++})";
        }

        try
        {
            await _fileSystem.CreateFolderAsync(CurrentPath, folderName);
            await LoadDirectoryAsync();
            SelectedItem = Items.FirstOrDefault(item =>
                item.IsDirectory &&
                item.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Ignore create failures (permissions, invalid path, race conditions).
        }
    }

    [RelayCommand]
    private void SelectAllItems()
    {
        foreach (var item in Items)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
        SelectedItem = null;
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var item in Items)
            item.IsSelected = !item.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void CutSelectedItem()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0 && SelectedItem is not null)
            selected.Add(SelectedItem);
        if (selected.Count == 0)
            return;

        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(selected.Select(i => i.FullPath));
        _clipboardOperation = ClipboardOperation.Cut;
        OnPropertyChanged(nameof(HasClipboardItems));
        PasteFromClipboardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void CopySelectedItem()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0 && SelectedItem is not null)
            selected.Add(SelectedItem);
        if (selected.Count == 0)
            return;

        _clipboardPaths.Clear();
        _clipboardPaths.AddRange(selected.Select(i => i.FullPath));
        _clipboardOperation = ClipboardOperation.Copy;
        OnPropertyChanged(nameof(HasClipboardItems));
        PasteFromClipboardCommand.NotifyCanExecuteChanged();
    }

    private bool CanPasteFromClipboard()
    {
        return _clipboardPaths.Count > 0
               && _clipboardOperation != ClipboardOperation.None
               && !string.IsNullOrWhiteSpace(CurrentPath)
               && _fileSystem.DirectoryExists(CurrentPath);
    }

    [RelayCommand(CanExecute = nameof(CanPasteFromClipboard))]
    private async Task PasteFromClipboardAsync()
    {
        if (!CanPasteFromClipboard())
            return;

        var sources = _clipboardPaths.ToArray();
        var destination = CurrentPath;
        var isMove = _clipboardOperation == ClipboardOperation.Cut;
        var operationCompleted = false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var hwnd = TryGetOwnerWindowHandle();
                var hr = isMove
                    ? NativeBridge.Shell_MoveItems(sources, sources.Length, destination, hwnd)
                    : NativeBridge.Shell_CopyItems(sources, sources.Length, destination, hwnd);

                if (hr == OperationCanceledHResult)
                    return;

                operationCompleted = hr >= 0;
            }

            if (!operationCompleted)
            {
                foreach (var source in sources)
                {
                    if (isMove)
                        await _fileSystem.MoveAsync(source, destination);
                    else
                        await _fileSystem.CopyAsync(source, destination);
                }
            }
        }
        catch
        {
            // Keep UI responsive and leave clipboard state unchanged on failure.
            return;
        }

        if (isMove)
        {
            _clipboardPaths.Clear();
            _clipboardOperation = ClipboardOperation.None;
            OnPropertyChanged(nameof(HasClipboardItems));
            PasteFromClipboardCommand.NotifyCanExecuteChanged();
        }

        await LoadDirectoryAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task RenameSelectedItemAsync()
    {
        if (SelectedItem is null)
            return;

        var dialog = new TextInputDialog(
            title: "Rename",
            message: "Enter a new name",
            confirmButtonText: "Rename",
            initialValue: SelectedItem.Name);

        var owner = TryGetOwnerWindow();
        if (owner is null)
            return;

        var newName = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        newName = newName.Trim();
        if (string.Equals(newName, SelectedItem.Name, StringComparison.Ordinal))
            return;

        try
        {
            var renamed = false;
            if (OperatingSystem.IsWindows())
            {
                var hr = NativeBridge.Shell_RenameItem(SelectedItem.FullPath, newName, TryGetOwnerWindowHandle());
                if (hr == OperationCanceledHResult)
                    return;

                renamed = hr >= 0;
            }

            if (!renamed)
                await _fileSystem.RenameAsync(SelectedItem.FullPath, newName);
        }
        catch
        {
            return;
        }

        await LoadDirectoryAsync();
        SelectedItem = Items.FirstOrDefault(item =>
            item.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task ShareSelectedItemAsync()
    {
        if (SelectedItem is null)
            return;

        var owner = TryGetOwnerWindow();
        if (owner?.Clipboard is null)
            return;

        await owner.Clipboard.SetTextAsync(SelectedItem.FullPath);
    }

    private static Window? TryGetOwnerWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.MainWindow;
    }

    // -----------------------------------------------------------------------
    // Nextcloud Share Commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task GeneratePublicLinkAsync()
    {
        if (SelectedItem is null || !SelectedItem.IsNextcloudItem)
            return;

        try
        {
            var remotePath = SelectedItem.NextcloudRemotePath;
            var url = await _nextcloud.CreatePublicShareAsync(remotePath);

            if (!string.IsNullOrWhiteSpace(url))
            {
                var owner = TryGetOwnerWindow();
                if (owner?.Clipboard is not null)
                    await owner.Clipboard.SetTextAsync(url);
            }
        }
        catch { /* Share creation failed silently */ }
    }

    [RelayCommand]
    private async Task GenerateInternalLinkAsync()
    {
        if (SelectedItem is null || !SelectedItem.IsNextcloudItem)
            return;

        try
        {
            var remotePath = SelectedItem.NextcloudRemotePath;
            var url = await _nextcloud.CreateInternalShareAsync(remotePath);

            if (!string.IsNullOrWhiteSpace(url))
            {
                var owner = TryGetOwnerWindow();
                if (owner?.Clipboard is not null)
                    await owner.Clipboard.SetTextAsync(url);
            }
        }
        catch { /* Share creation failed silently */ }
    }

    [RelayCommand]
    private async Task CopyPathAsync()
    {
        if (SelectedItem is null)
            return;

        var owner = TryGetOwnerWindow();
        if (owner?.Clipboard is not null)
            await owner.Clipboard.SetTextAsync(SelectedItem.FullPath);
    }

    [RelayCommand]
    private async Task NewTabFromPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var tab = CreateTab(path);
        Tabs.Add(tab);
        await SelectTabAsync(tab);
    }

    private static IntPtr TryGetOwnerWindowHandle()
    {
        return TryGetOwnerWindow()?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    [RelayCommand]
    private async Task SetSortFieldAsync(string? sortFieldName)
    {
        if (string.IsNullOrWhiteSpace(sortFieldName) ||
            !Enum.TryParse(sortFieldName, ignoreCase: true, out SortField requestedField))
        {
            return;
        }

        if (ActiveSortField == requestedField)
            return;

        ActiveSortField = requestedField;
        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task SortByColumnAsync(string? sortFieldName)
    {
        if (string.IsNullOrWhiteSpace(sortFieldName) ||
            !Enum.TryParse(sortFieldName, ignoreCase: true, out SortField requestedField))
        {
            return;
        }

        if (ActiveSortField == requestedField)
            IsSortAscending = !IsSortAscending;
        else
            ActiveSortField = requestedField;

        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task SetSortAscendingAsync()
    {
        if (IsSortAscending)
            return;

        IsSortAscending = true;
        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private async Task SetSortDescendingAsync()
    {
        if (!IsSortAscending)
            return;

        IsSortAscending = false;
        await LoadDirectoryAsync();
    }

    [RelayCommand]
    private Task ToggleHiddenItemsAsync()
    {
        ShowHiddenItems = !ShowHiddenItems;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SetLayoutMode(string? layoutModeName)
    {
        if (string.IsNullOrWhiteSpace(layoutModeName) ||
            !Enum.TryParse(layoutModeName, ignoreCase: true, out ExplorerLayoutMode layoutMode))
        {
            return;
        }

        ActiveLayoutMode = layoutMode;
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
                    ShowFileExtension = ShowFileExtensions,
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
            ItemCountText = FormatItemCount(dirs, files);
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

        if (SettingsConfirmBeforeDelete)
        {
            var confirmDialog = new ConfirmationDialog(
                title: "Delete item",
                message: $"Move \"{item.Name}\" to Recycle Bin?",
                primaryButtonText: "Delete",
                closeButtonText: "Cancel",
                isPrimaryDestructive: true);

            var owner = TryGetOwnerWindow();
            if (owner is null)
                return;

            var confirmed = await confirmDialog.ShowDialog<bool>(owner);
            if (!confirmed)
                return;
        }

        try
        {
            if (item.IsNextcloudItem)
            {
                var remotePath = item.NextcloudRemotePath;
                if (string.IsNullOrWhiteSpace(remotePath))
                    return;

                var deletedRemote = await _nextcloud.DeleteAsync(remotePath);
                if (!deletedRemote)
                    return;

                await LoadDirectoryAsync();
                return;
            }

            var deleted = false;
            if (OperatingSystem.IsWindows())
            {
                var hr = NativeBridge.Shell_DeleteItems([item.FullPath], 1, TryGetOwnerWindowHandle(), recycle: true);
                if (hr == OperationCanceledHResult)
                    return;

                deleted = hr >= 0;
            }

            if (!deleted)
                await _fileSystem.DeleteAsync(item.FullPath);
        }
        catch
        {
            return;
        }

        await LoadDirectoryAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task DeleteSelectedItemAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0 && SelectedItem is not null)
            selected.Add(SelectedItem);
        if (selected.Count == 0)
            return;

        if (SettingsConfirmBeforeDelete)
        {
            var message = selected.Count == 1
                ? $"Move \"{selected[0].Name}\" to Recycle Bin?"
                : $"Move {selected.Count} items to Recycle Bin?";

            var confirmDialog = new ConfirmationDialog(
                title: "Delete items",
                message: message,
                primaryButtonText: "Delete",
                closeButtonText: "Cancel",
                isPrimaryDestructive: true);

            var owner = TryGetOwnerWindow();
            if (owner is null)
                return;

            var confirmed = await confirmDialog.ShowDialog<bool>(owner);
            if (!confirmed)
                return;
        }

        try
        {
            // Handle Nextcloud items separately
            var ncItems = selected.Where(i => i.IsNextcloudItem).ToList();
            var localItems = selected.Where(i => !i.IsNextcloudItem).ToList();

            foreach (var item in ncItems)
            {
                var remotePath = item.NextcloudRemotePath;
                if (!string.IsNullOrWhiteSpace(remotePath))
                    await _nextcloud.DeleteAsync(remotePath);
            }

            if (localItems.Count > 0)
            {
                var deleted = false;
                if (OperatingSystem.IsWindows())
                {
                    var paths = localItems.Select(i => i.FullPath).ToArray();
                    var hr = NativeBridge.Shell_DeleteItems(paths, paths.Length, TryGetOwnerWindowHandle(), recycle: true);
                    if (hr == OperationCanceledHResult)
                        return;
                    deleted = hr >= 0;
                }

                if (!deleted)
                {
                    foreach (var item in localItems)
                        await _fileSystem.DeleteAsync(item.FullPath);
                }
            }
        }
        catch
        {
            return;
        }

        SelectedItem = null;
        await LoadDirectoryAsync();
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

    private bool CanAdjustImagePreview => IsImagePreviewAvailable;

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewZoomIn()
    {
        ImagePreviewZoom = Math.Min(8.0, ImagePreviewZoom + 0.25);
    }

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewZoomOut()
    {
        ImagePreviewZoom = Math.Max(0.1, ImagePreviewZoom - 0.25);
    }

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewZoomReset()
    {
        ImagePreviewZoom = 1.0;
    }

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewFit()
    {
        ImagePreviewZoom = 1.0;
    }

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewRotateLeft()
    {
        ImagePreviewRotation = NormalizeRotationAngle(ImagePreviewRotation - 90);
    }

    [RelayCommand(CanExecute = nameof(CanAdjustImagePreview))]
    private void ImagePreviewRotateRight()
    {
        ImagePreviewRotation = NormalizeRotationAngle(ImagePreviewRotation + 90);
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
    private void OpenSettings()
    {
        LoadPersistedSettings();
        SettingsOperationNotice = string.Empty;
        SettingsSection = "general";
        IsSettingsOpen = true;
    }

    [RelayCommand(CanExecute = nameof(IsSettingsOpen))]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    [RelayCommand]
    private void SelectSettingsSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return;

        SettingsSection = section.Trim().ToLowerInvariant();
    }

    [RelayCommand]
    private void AddCommandMapping()
    {
        SettingsOperationNotice = "Add command is not implemented yet.";
    }

    [RelayCommand]
    private void RestoreDefaultActionMappings()
    {
        SettingsActionSearchQuery = string.Empty;
        InitializeSettingsActions();
        SettingsOperationNotice = "Actions restored to current defaults.";
    }

    [RelayCommand]
    private async Task AddNetworkLocationAsync()
    {
        var owner = TryGetOwnerWindow();
        if (owner is null)
            return;

        var dialog = new TextInputDialog(
            title: "Add network location",
            message: "Enter a network path (example: \\\\server\\share)",
            confirmButtonText: "Add",
            initialValue: "\\\\");

        var value = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(value))
            return;

        await AddNetworkLocationCoreAsync(value, requireUncPath: true);
    }

    [RelayCommand]
    private async Task AddNetworkFolderAsync()
    {
        var owner = TryGetOwnerWindow();
        if (owner is null)
            return;

        var dialog = new TextInputDialog(
            title: "Add folder shortcut",
            message: "Enter a local or network folder path",
            confirmButtonText: "Add",
            initialValue: CurrentPath);

        var value = await dialog.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(value))
            return;

        await AddNetworkLocationCoreAsync(value, requireUncPath: false);
    }

    [RelayCommand]
    private async Task RemoveNetworkLocationAsync(SidebarItemViewModel? item)
    {
        if (item is null)
            return;

        var existing = SidebarNetworkLocations.FirstOrDefault(x =>
            string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;

        SidebarNetworkLocations.Remove(existing);
        if (string.Equals(CurrentPath, NetworkSentinelPath, StringComparison.OrdinalIgnoreCase))
            await LoadDirectoryAsync();

        await SaveNetworkLocationsAsync();
    }

    private async Task AddNetworkLocationCoreAsync(string rawPath, bool requireUncPath)
    {
        var normalizedPath = NormalizeNetworkShortcutPath(rawPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        if (requireUncPath && !IsUncPath(normalizedPath))
        {
            SettingsOperationNotice = "Use a network path starting with \\\\.";
            return;
        }

        if (!requireUncPath && !IsUncPath(normalizedPath) && !_fileSystem.DirectoryExists(normalizedPath))
        {
            SettingsOperationNotice = "Folder does not exist.";
            return;
        }

        var existing = SidebarNetworkLocations.FirstOrDefault(item =>
            string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SettingsOperationNotice = "Location already exists in Network.";
            return;
        }

        var iconKey = IsUncPath(normalizedPath) ? "network" : "folder";
        SidebarNetworkLocations.Add(new SidebarItemViewModel(
            BuildNetworkLocationLabel(normalizedPath),
            normalizedPath,
            GetSidebarIconUri(iconKey),
            canNavigate: true,
            itemType: SidebarItemType.Network));

        SortSidebarNetworkLocations();
        await SaveNetworkLocationsAsync();

        if (string.Equals(CurrentPath, NetworkSentinelPath, StringComparison.OrdinalIgnoreCase))
            await LoadDirectoryAsync();
    }

    // -----------------------------------------------------------------------
    // Nextcloud Setup Wizard Commands
    // -----------------------------------------------------------------------

    private void LoadNcSetupFromSettings()
    {
        var s = _settingsService.Current;
        NcSetupUrl = s.NextcloudUrl;
        NcSetupUser = s.NextcloudUser;
        NcSetupPassword = s.NextcloudAppPassword;
        IsNcConnected = s.NextcloudEnabled && !string.IsNullOrWhiteSpace(s.NextcloudUrl);
        IsNcSetupUrlValid = IsValidNextcloudUrl(NcSetupUrl);
        NcSetupStep = IsNcConnected ? 0 : 0;
        NcSetupStatusMessage = string.Empty;
        IsNcSetupConnecting = false;
    }

    partial void OnNcSetupUrlChanged(string value)
    {
        IsNcSetupUrlValid = IsValidNextcloudUrl(value);
        NcSetupStatusMessage = string.Empty;
    }

    private static bool IsValidNextcloudUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    [RelayCommand]
    private void NcSetupStartSetup()
    {
        NcSetupStep = 1;
        NcSetupStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NcSetupGoToCredentials()
    {
        if (!IsNcSetupUrlValid)
            return;

        NcSetupStep = 2;
        NcSetupStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void NcSetupBack()
    {
        if (NcSetupStep > 0)
            NcSetupStep--;

        NcSetupStatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task NcSetupTestAndSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NcSetupUser) || string.IsNullOrWhiteSpace(NcSetupPassword))
        {
            NcSetupStatusMessage = "Please enter username and app password.";
            return;
        }

        IsNcSetupConnecting = true;
        NcSetupStatusMessage = "Connecting…";

        try
        {
            var url = NcSetupUrl.TrimEnd('/');
            var ok = await _nextcloud.TestConnectionAsync(url, NcSetupUser, NcSetupPassword);

            if (ok)
            {
                // Persist to settings
                var s = _settingsService.Current;
                s.NextcloudEnabled = true;
                s.NextcloudUrl = url;
                s.NextcloudUser = NcSetupUser;
                s.NextcloudAppPassword = NcSetupPassword;
                await SaveSettingsAsync();

                IsNcConnected = true;
                NcSetupStep = 0;
                NcSetupStatusMessage = "Connected successfully! Your Nextcloud is ready.";
                SettingsOperationNotice = "Nextcloud connected – navigate to it from the sidebar.";
            }
            else
            {
                NcSetupStatusMessage = "Connection failed. Please check your URL and credentials.";
            }
        }
        catch (Exception ex)
        {
            NcSetupStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsNcSetupConnecting = false;
        }
    }

    [RelayCommand]
    private async Task NcSetupDisconnectAsync()
    {
        var s = _settingsService.Current;
        s.NextcloudEnabled = false;
        s.NextcloudUrl = string.Empty;
        s.NextcloudUser = string.Empty;
        s.NextcloudAppPassword = string.Empty;
        await SaveSettingsAsync();

        NcSetupUrl = string.Empty;
        NcSetupUser = string.Empty;
        NcSetupPassword = string.Empty;
        IsNcConnected = false;
        IsNcSetupUrlValid = false;
        NcSetupStep = 0;
        NcSetupStatusMessage = "Nextcloud disconnected.";
        SettingsOperationNotice = "Nextcloud disconnected.";
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
        _ = SaveSettingsAsync();
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
        _ = SaveSettingsAsync();
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
        _ = SaveSettingsAsync();
    }

    private async Task NavigateToPathAsync(string path, bool addToHistory, bool forceReload = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var isVirtualNc = IsVirtualNextcloudPath(path);
        var isNetworkSentinel = IsNetworkSentinelPath(path);
        var isUncPath = IsUncPath(path);

        // For local paths, verify directory exists; skip check for virtual and UNC paths.
        if (!isVirtualNc && !isNetworkSentinel && !isUncPath && !_fileSystem.DirectoryExists(path))
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
            ApplyTabPresentation(SelectedTab, normalizedPath);
        }

        UpdateBreadcrumbs();

        if (!samePath || forceReload || Items.Count == 0)
            await LoadDirectoryAsync();
        else
            UpdateItemCountText();

        UpdateWindowTitle();
        UpdateNavigationState();
        UpdateDetailsState();

        _ = SaveSettingsAsync();
    }

    private void UpdateBreadcrumbs()
    {
        BreadcrumbSegments.Clear();

        if (string.IsNullOrWhiteSpace(CurrentPath))
            return;

        // Nextcloud virtual paths: nc:///Documents/Sub → [Nextcloud] > [Documents] > [Sub]
        if (IsVirtualNextcloudPath(CurrentPath))
        {
            var remotePath = GetNextcloudRemotePath(CurrentPath).TrimStart('/');
            var parts = string.IsNullOrWhiteSpace(remotePath)
                ? Array.Empty<string>()
                : remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Root segment
            BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel(
                "Nextcloud", "nc:///", false, parts.Length > 0));

            // Path segments
            var running = "nc://";
            for (int i = 0; i < parts.Length; i++)
            {
                running += "/" + parts[i];
                BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel(
                    parts[i], running, true, i < parts.Length - 1));
            }
            return;
        }

        // Network sentinel
        if (IsNetworkSentinelPath(CurrentPath))
        {
            BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel("Network", CurrentPath, false, false));
            return;
        }

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
            var showChevronButton = segments.Count > 0;
            BreadcrumbSegments.Add(new BreadcrumbSegmentViewModel(
                BuildBreadcrumbLabel(full),
                full,
                showSeparator,
                showChevronButton));
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
            IReadOnlyList<FileSystemItem> fsItems;
            bool isNcPath = IsVirtualNextcloudPath(CurrentPath);

            if (isNcPath)
            {
                var remotePath = GetNextcloudRemotePath(CurrentPath);
                fsItems = await _nextcloud.GetItemsAsync(
                    remotePath,
                    sort: BuildSortOptions(),
                    filter: BuildFilterOptions(),
                    ct);
            }
            else if (IsNetworkSentinelPath(CurrentPath))
            {
                fsItems = BuildNetworkVirtualItems();
            }
            else
            {
                fsItems = await _fileSystem.GetItemsAsync(
                    CurrentPath,
                    sort: BuildSortOptions(),
                    filter: BuildFilterOptions(),
                    ct);
            }

            ct.ThrowIfCancellationRequested();

            Items.Clear();
            SelectedItem = null;

            foreach (var fsItem in fsItems)
            {
                var (iconImagePath, iconResourceKey) = ResolveFileItemIconResources(fsItem.IsDirectory, fsItem.Extension);

                // For Nextcloud items, convert the WebDAV href back to nc:// path
                var fullPath = isNcPath
                    ? ConvertWebDavHrefToNcPath(fsItem.FullPath)
                    : fsItem.FullPath;

                Items.Add(new FileItemViewModel
                {
                    IconImagePath = iconImagePath,
                    IconResourceKey = iconResourceKey,
                    ShowFileExtension = ShowFileExtensions,
                    Name = fsItem.Name,
                    FullPath = fullPath,
                    IsDirectory = fsItem.IsDirectory,
                    Size = fsItem.Size,
                    Modified = fsItem.LastModified,
                    Created = fsItem.DateCreated,
                    Icon = fsItem.IsDirectory ? "Folder" : GetFileIcon(fsItem.Extension),
                    IsNextcloudItem = isNcPath
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

    private IReadOnlyList<FileSystemItem> BuildNetworkVirtualItems()
    {
        var result = new List<FileSystemItem>();

        foreach (var location in SidebarNetworkLocations)
        {
            if (string.IsNullOrWhiteSpace(location.Path))
                continue;

            var lastModified = default(DateTime);
            var created = default(DateTime);
            var attributes = FileAttributes.Directory;

            try
            {
                if (Directory.Exists(location.Path))
                {
                    var info = new DirectoryInfo(location.Path);
                    lastModified = info.LastWriteTime;
                    created = info.CreationTime;
                    attributes = info.Attributes;
                }
            }
            catch
            {
                // Keep unavailable/offline paths navigable as virtual entries.
            }

            result.Add(new FileSystemItem
            {
                Name = location.Label,
                FullPath = location.Path,
                IsDirectory = true,
                Size = null,
                LastModified = lastModified,
                DateCreated = created,
                Attributes = attributes
            });
        }

        return result
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SortOptions BuildSortOptions() => new(ActiveSortField, IsSortAscending);

    private FilterOptions BuildFilterOptions() => new(
        NameContains: null,
        Extensions: null,
        ShowHidden: ShowHiddenItems);

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

        var normalizedCurrentPath = string.IsNullOrWhiteSpace(CurrentPath) ? string.Empty : NormalizePath(CurrentPath);
        DetailsIconImagePath = ResolveDetailsPaneIconPath(normalizedCurrentPath, SelectedTab?.IconImagePath);
        DetailsIconResourceKey = SelectedTab?.IconResourceKey ?? "Icon.Files.App.ThemedIcons.Folder";
        DetailsTitleText = SelectedTab?.Title ?? GetDisplayTitleForPath(CurrentPath);
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
        _previewCts?.Cancel();
        PreviewFilePath = null;
        IsPreviewAvailable = false;
        PreviewFallbackImagePath = null;
        ImagePreviewFilePath = null;
        MediaPreviewFilePath = null;
        TextPreviewContent = null;
        ImagePreviewZoom = 1.0;
        ImagePreviewRotation = 0;

        if (SelectedItem is null)
        {
            PreviewStatusText = "Select a file to preview";
            return;
        }

        if (SelectedItem.IsDirectory)
        {
            PreviewStatusText = "Preview is not available for folders.";
            return;
        }

        var extension = SelectedItem.Extension.ToLowerInvariant();
        if (IsCompressedExtension(extension))
        {
            EnableRasterPreview(string.IsNullOrWhiteSpace(DetailsIconImagePath) ? NanaZipIconUri : DetailsIconImagePath);
            return;
        }

        if (SelectedItem.IsNextcloudItem)
        {
            PreviewStatusText = "Loading preview...";
            return;
        }

        if (!File.Exists(SelectedItem.FullPath))
        {
            PreviewStatusText = "Preview is not available";
            return;
        }

        if (TryEnableImagePreview(SelectedItem.FullPath, SelectedItem.Extension))
            return;

        if (TryEnableMediaPreview(SelectedItem.FullPath, SelectedItem.Extension))
            return;

        PreviewStatusText = "Loading preview...";
    }

    private async Task ResolveSelectedItemPreviewAsync(FileItemViewModel? item)
    {
        if (item is null || item.IsDirectory || IsCompressedExtension(item.Extension))
            return;

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            // Debounce: wait 150ms before trying to generate the preview.
            // If the user's selection changes rapidly, this task gets cancelled and we skip the heavy load.
            await Task.Delay(150, ct);

            string? localPath;
            string? textPreviewContent = null;
            if (item.IsNextcloudItem)
            {
                var remotePath = item.NextcloudRemotePath;
                if (string.IsNullOrWhiteSpace(remotePath))
                    return;

                localPath = await DownloadNextcloudPreviewFileAsync(remotePath, item.Extension, ct);
            }
            else
            {
                localPath = item.FullPath;
            }

            if (ct.IsCancellationRequested)
                return;

            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(SelectedItem, item))
                        return;

                    PreviewFilePath = null;
                    IsPreviewAvailable = false;
                    PreviewStatusText = "Preview is not available";
                });

                return;
            }

            string? rasterPreviewPath = null;
            var canUseNative = ShouldTryNativePreview(localPath, item.Extension) && CanUseNativePreview(localPath);
            if (!canUseNative)
            {
                var previewSize = IsRawPreviewExtension(item.Extension)
                    ? Math.Max(PreviewRasterResolveSizePx, 1600)
                    : PreviewRasterResolveSizePx;
                rasterPreviewPath = await TryGetShellIconPathAsync(localPath, previewSize, ct, preferThumbnail: true);
            }

            textPreviewContent = await TryLoadTextPreviewContentAsync(localPath, item.Extension, ct);

            if (ct.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(SelectedItem, item))
                    return;

                if (TryEnableImagePreview(localPath, item.Extension))
                    return;

                if (TryEnableMediaPreview(localPath, item.Extension))
                    return;

                if (!string.IsNullOrWhiteSpace(textPreviewContent))
                {
                    EnableTextPreview(textPreviewContent);
                    return;
                }

                if (canUseNative)
                {
                    PreviewFilePath = localPath;
                    IsPreviewAvailable = true;
                    PreviewFallbackImagePath = null;
                    ImagePreviewFilePath = null;
                    MediaPreviewFilePath = null;
                    TextPreviewContent = null;
                    PreviewStatusText = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(rasterPreviewPath))
                {
                    EnableRasterPreview(rasterPreviewPath);
                }
                else if (!string.IsNullOrWhiteSpace(DetailsIconImagePath))
                {
                    EnableRasterPreview(DetailsIconImagePath);
                }
                else
                {
                    PreviewFilePath = null;
                    IsPreviewAvailable = false;
                    PreviewFallbackImagePath = null;
                    ImagePreviewFilePath = null;
                    PreviewStatusText = "Preview is not available for this file type.";
                }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Selection changed while resolving preview.
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(SelectedItem, item))
                    return;

                PreviewFilePath = null;
                IsPreviewAvailable = false;
                PreviewStatusText = "Preview is not available";
                MediaPreviewFilePath = null;
                TextPreviewContent = null;
            });
        }
    }

    private async Task<string?> DownloadNextcloudPreviewFileAsync(string remotePath, string? extensionHint, CancellationToken ct)
    {
        var localPath = await _nextcloud.DownloadFileToCacheAsync(remotePath, extensionHint, ct);
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            return localPath;

        // Retry once in background to absorb transient WebDAV/cache races.
        await Task.Delay(180, ct);
        localPath = await _nextcloud.DownloadFileToCacheAsync(remotePath, extensionHint, ct);
        return !string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath)
            ? localPath
            : null;
    }

    private async Task<string?> ResolveQuickLookTargetPathAsync(FileItemViewModel item)
    {
        if (!item.IsNextcloudItem)
            return item.FullPath;

        if (item.IsDirectory)
            return null;

        var remotePath = item.NextcloudRemotePath;
        if (string.IsNullOrWhiteSpace(remotePath))
            return null;

        try
        {
            return await _nextcloud.DownloadFileToCacheAsync(remotePath, item.Extension);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveQuickLookExecutablePath()
    {
        if (_quickLookPathResolved)
            return _quickLookExecutablePath;

        var candidates = new List<string>();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            candidates.Add(Path.Combine(localAppData, "Programs", "QuickLook", "QuickLook.exe"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            candidates.Add(Path.Combine(programFiles, "QuickLook", "QuickLook.exe"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            candidates.Add(Path.Combine(programFilesX86, "QuickLook", "QuickLook.exe"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _quickLookExecutablePath = candidate;
                _quickLookPathResolved = true;
                return _quickLookExecutablePath;
            }
        }

        try
        {
            using var whereProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "QuickLook.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });

            if (whereProcess is not null)
            {
                var line = whereProcess.StandardOutput.ReadLine();
                whereProcess.WaitForExit(1500);
                if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                    _quickLookExecutablePath = line.Trim();
            }
        }
        catch
        {
            // Ignore executable discovery failures.
        }

        _quickLookPathResolved = true;
        return _quickLookExecutablePath;
    }

    private bool TryEnableImagePreview(string path, string? extensionHint = null)
    {
        if (!IsImagePreviewExtension(path, extensionHint) || !File.Exists(path))
            return false;

        EnableRasterPreview(path, GetInitialImagePreviewRotation(path));
        return true;
    }

    private bool TryEnableMediaPreview(string path, string? extensionHint = null)
    {
        if (!IsMediaPreviewExtension(path, extensionHint) || !File.Exists(path))
            return false;

        EnableMediaPreview(path);
        return true;
    }

    private void EnableRasterPreview(string imagePath, int rotation = 0)
    {
        PreviewFilePath = null;
        IsPreviewAvailable = false;
        PreviewFallbackImagePath = null;
        ImagePreviewFilePath = imagePath;
        MediaPreviewFilePath = null;
        TextPreviewContent = null;
        ImagePreviewZoom = 1.0;
        ImagePreviewRotation = NormalizeRotationAngle(rotation);
        PreviewStatusText = string.Empty;
    }

    private void EnableMediaPreview(string mediaPath)
    {
        PreviewFilePath = null;
        IsPreviewAvailable = false;
        PreviewFallbackImagePath = null;
        ImagePreviewFilePath = null;
        MediaPreviewFilePath = mediaPath;
        TextPreviewContent = null;
        ImagePreviewZoom = 1.0;
        ImagePreviewRotation = 0;
        PreviewStatusText = string.Empty;
    }

    private void EnableTextPreview(string content)
    {
        PreviewFilePath = null;
        IsPreviewAvailable = false;
        PreviewFallbackImagePath = null;
        ImagePreviewFilePath = null;
        MediaPreviewFilePath = null;
        TextPreviewContent = content;
        ImagePreviewZoom = 1.0;
        ImagePreviewRotation = 0;
        PreviewStatusText = string.Empty;
    }

    private static bool IsImagePreviewExtension(string path, string? extensionHint = null)
    {
        var extension = NormalizeExtension(Path.GetExtension(path));
        if (string.IsNullOrWhiteSpace(extension))
            extension = NormalizeExtension(extensionHint);

        return !string.IsNullOrWhiteSpace(extension) && ImagePreviewExtensions.Contains(extension);
    }

    private static bool IsMediaPreviewExtension(string path, string? extensionHint = null)
    {
        var extension = NormalizeExtension(Path.GetExtension(path));
        if (string.IsNullOrWhiteSpace(extension))
            extension = NormalizeExtension(extensionHint);

        return !string.IsNullOrWhiteSpace(extension) && MediaPreviewExtensions.Contains(extension);
    }

    private static bool IsTextPreviewExtension(string path, string? extensionHint = null)
    {
        var extension = NormalizeExtension(Path.GetExtension(path));
        if (string.IsNullOrWhiteSpace(extension))
            extension = NormalizeExtension(extensionHint);

        return !string.IsNullOrWhiteSpace(extension) && TextPreviewExtensions.Contains(extension);
    }

    private static bool IsRawPreviewExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && RawPreviewExtensions.Contains(NormalizeExtension(extension));

    private bool ShouldTryNativePreview(string path, string? extensionHint)
    {
        if (IsNativePreviewEnabled)
            return true;

        var extension = NormalizeExtension(Path.GetExtension(path));
        if (string.IsNullOrWhiteSpace(extension))
            extension = NormalizeExtension(extensionHint);

        return !string.IsNullOrWhiteSpace(extension) && DedicatedNativePreviewExtensions.Contains(extension);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var normalized = extension.Trim();
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
            normalized = "." + normalized;

        return normalized.ToLowerInvariant();
    }

    private static async Task<string?> TryLoadTextPreviewContentAsync(string path, string? extensionHint, CancellationToken ct)
    {
        if (!IsTextPreviewExtension(path, extensionHint) || !File.Exists(path))
            return null;

        const int maxChars = 200_000;
        try
        {
            await using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var reader = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[4096];
            var builder = new StringBuilder(maxChars);

            while (builder.Length < maxChars)
            {
                ct.ThrowIfCancellationRequested();
                var toRead = Math.Min(buffer.Length, maxChars - builder.Length);
                var read = await reader.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (read <= 0)
                    break;

                builder.Append(buffer, 0, read);
            }

            if (builder.Length == 0)
                return "(Empty file)";

            if (!reader.EndOfStream)
                builder.AppendLine().AppendLine("... (truncated)");

            return builder.ToString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int GetInitialImagePreviewRotation(string path)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        try
        {
            using var image = System.Drawing.Image.FromFile(path);
            const int ExifOrientationId = 0x0112;
            if (Array.IndexOf(image.PropertyIdList, ExifOrientationId) < 0)
                return 0;

            var propertyItem = image.GetPropertyItem(ExifOrientationId);
            if (propertyItem is null || propertyItem.Value is null || propertyItem.Value.Length < 2)
                return 0;

            var orientation = BitConverter.ToUInt16(propertyItem.Value, 0);
            return orientation switch
            {
                3 => 180,
                6 => 90,
                8 => 270,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    private static int NormalizeRotationAngle(int angle)
    {
        var normalized = angle % 360;
        if (normalized < 0)
            normalized += 360;

        return normalized;
    }

    private bool CanUseNativePreview(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension) &&
            _nativePreviewSupportByExtension.TryGetValue(extension, out var cachedSupport))
        {
            return cachedSupport;
        }

        bool isSupported;
        try
        {
            isSupported = NativeBridge.Preview_CanHandle(path) == 1;
        }
        catch (DllNotFoundException)
        {
            isSupported = false;
        }
        catch (EntryPointNotFoundException)
        {
            isSupported = false;
        }
        catch
        {
            isSupported = false;
        }

        if (!string.IsNullOrWhiteSpace(extension))
            _nativePreviewSupportByExtension[extension] = isSupported;

        return isSupported;
    }

    private void UpdateItemCountText()
    {
        var dirs = Items.Count(i => i.IsDirectory);
        var files = Items.Count - dirs;
        ItemCountText = FormatItemCount(dirs, files);
        if (SelectedItem is null)
            UpdateDetailsState();
    }

    private string FormatItemCount(int dirs, int files)
    {
        if (string.Equals(SettingsLanguage, "en_GB", StringComparison.OrdinalIgnoreCase))
        {
            var folderLabel = dirs == 1 ? "folder" : "folders";
            var fileLabel = files == 1 ? "file" : "files";
            return $"{dirs} {folderLabel}, {files} {fileLabel}";
        }

        var pastaLabel = dirs == 1 ? "pasta" : "pastas";
        var arquivoLabel = files == 1 ? "arquivo" : "arquivos";
        return $"{dirs} {pastaLabel}, {files} {arquivoLabel}";
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

        string queryPath = item.FullPath;
        if (item.IsNextcloudItem)
        {
            queryPath = item.IsDirectory 
                ? Environment.GetFolderPath(Environment.SpecialFolder.System) 
                : (string.IsNullOrWhiteSpace(item.Extension) ? ".bin" : item.Extension);
        }

        string? iconPath;
        await _iconResolveLimiter.WaitAsync(ct);
        try
        {
            iconPath = await TryGetShellIconPathAsync(queryPath, ListIconResolveSizePx, ct, preferThumbnail: !item.IsNextcloudItem);
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

        var normalizedPath = NormalizePath(item.FullPath);
        if (!item.IsNextcloudItem && TryGetKnownLocationIconKey(normalizedPath, out var knownIconKey))
        {
            var knownIconPath = GetHighQualityLocationIconPath(knownIconKey);
            if (!string.IsNullOrWhiteSpace(knownIconPath))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ReferenceEquals(SelectedItem, item))
                        DetailsIconImagePath = knownIconPath;
                });
                return;
            }
        }

        if (IsCompressedExtension(item.Extension))
        {
            string compressedQueryPath;
            if (item.IsNextcloudItem)
                compressedQueryPath = string.IsNullOrWhiteSpace(item.Extension) ? ".zip" : item.Extension;
            else
                compressedQueryPath = item.FullPath;

            string? compressedIconPath = null;
            await _iconResolveLimiter.WaitAsync(ct);
            try
            {
                compressedIconPath = await TryGetShellIconPathAsync(
                    compressedQueryPath,
                    DetailsIconResolveSizePx,
                    ct,
                    preferThumbnail: false);
            }
            catch
            {
                // Fallback below.
            }
            finally
            {
                _iconResolveLimiter.Release();
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(SelectedItem, item))
                    return;

                var resolvedPath = string.IsNullOrWhiteSpace(compressedIconPath)
                    ? NanaZipIconUri
                    : compressedIconPath;

                DetailsIconImagePath = resolvedPath;
                DetailsIconResourceKey = "Icon.Files.App.ThemedIcons.Zip";

                // Keep the Preview fallback in sync with the high-resolution archive icon.
                PreviewFallbackImagePath = resolvedPath;
            });
            return;
        }

        string queryPath = item.FullPath;
        var preferThumbnail = !item.IsDirectory;
        if (item.IsNextcloudItem)
        {
            if (item.IsDirectory)
            {
                queryPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                preferThumbnail = false;
            }
            else
            {
                var remotePath = item.NextcloudRemotePath;
                string? localPath = null;
                if (!string.IsNullOrWhiteSpace(remotePath))
                {
                    try
                    {
                        localPath = await _nextcloud.DownloadFileToCacheAsync(remotePath, item.Extension, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch
                    {
                        // Fall back to extension icon below.
                    }
                }

                if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
                {
                    queryPath = localPath;
                    preferThumbnail = true;
                }
                else
                {
                    queryPath = string.IsNullOrWhiteSpace(item.Extension) ? ".bin" : item.Extension;
                    preferThumbnail = false;
                }
            }
        }

        string? iconPath;
        await _iconResolveLimiter.WaitAsync(ct);
        try
        {
            iconPath = await TryGetShellIconPathAsync(queryPath, DetailsIconResolveSizePx, ct, preferThumbnail: preferThumbnail);
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

        if (preferThumbnail)
        {
            if (await TrySaveImageThumbnailAsPng(path, cachePath, sizePx, ct))
                return cachePath;
        }

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

    private static async Task<bool> TrySaveImageThumbnailAsPng(string path, string cachePath, int sizePx, CancellationToken ct)
    {
        var ext = Path.GetExtension(path);
        if (!string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
                
                var maxD = Math.Max(image.Width, image.Height);
                if (maxD <= 0) return false;

                double scale = (double)sizePx / maxD;
                if (scale > 1.0) scale = 1.0;

                int newW = Math.Max(1, (int)(image.Width * scale));
                int newH = Math.Max(1, (int)(image.Height * scale));

                using var thumbnail = new System.Drawing.Bitmap(newW, newH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var gfx = System.Drawing.Graphics.FromImage(thumbnail))
                {
                    gfx.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    gfx.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    gfx.DrawImage(image, 0, 0, newW, newH);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                thumbnail.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png);
                return true;
            }
            catch
            {
                return false;
            }
        }, ct);
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

    private static bool TrySaveShellItemImageFactoryAsPng(string path, string cachePath, int sizePx)
    {
        IShellItemImageFactory? imageFactory = null;
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            var iid = IID_IShellItemImageFactory;
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out imageFactory);
            if (hr != 0 || imageFactory is null)
                return false;

            var flags = SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK;
            hr = imageFactory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, flags, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var bitmap = System.Drawing.Image.FromHbitmap(hBitmap);
            bitmap.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);

            if (imageFactory is not null)
                Marshal.ReleaseComObject(imageFactory);
        }
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
    private const uint SIIGBF_BIGGERSIZEOK = 0x00000001;
    private const uint SIIGBF_ICONONLY = 0x00000004;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int SHIL_SMALL = 0x0;
    private const int SHIL_LARGE = 0x1;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_TRANSPARENT = 0x1;
    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

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

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr phbm);
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
        var title = GetDisplayTitleForPath(CurrentPath);
        if (IsSearchActive && !string.IsNullOrWhiteSpace(SearchQuery))
            WindowTitle = $"Finder Explorer — {title} — pesquisa: {SearchQuery}";
        else
            WindowTitle = $"Finder Explorer — {title}";
    }

    private void UpdateNavigationState()
    {
        CanNavigateBack = SelectedTab?.BackHistory.Count > 0;
        CanNavigateForward = SelectedTab?.ForwardHistory.Count > 0;

        if (IsVirtualNextcloudPath(CurrentPath))
        {
            // Can go up if not at nc root
            var remote = GetNextcloudRemotePath(CurrentPath);
            CanNavigateUp = !string.IsNullOrWhiteSpace(remote) && remote != "/";
        }
        else
        {
            CanNavigateUp = _fileSystem.GetParentPath(CurrentPath) is not null;
        }
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

        foreach (var networkLocation in SidebarNetworkLocations)
            yield return networkLocation;

        foreach (var fav in SidebarFavorites)
            yield return fav;

        foreach (var vol in SidebarVolumes)
            yield return vol;
    }

    private ExplorerTabViewModel CreateTab(string path)
    {
        var normalizedPath = NormalizePath(path);
        var tab = new ExplorerTabViewModel(BuildTabTitle(normalizedPath), normalizedPath);
        ApplyTabPresentation(tab, normalizedPath);
        return tab;
    }

    private void ApplyTabPresentation(ExplorerTabViewModel tab, string normalizedPath)
    {
        if (TryGetCanonicalTabMetadata(normalizedPath, out var canonicalTitle, out var canonicalIconPath))
        {
            tab.Title = canonicalTitle;
            tab.IconImagePath = canonicalIconPath;
            tab.IconResourceKey = "Icon.Files.App.ThemedIcons.Folder";
            return;
        }

        tab.Title = BuildTabTitle(normalizedPath);
        ApplyTabIcon(tab, normalizedPath);
    }

    private string GetDisplayTitleForPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        return TryGetCanonicalTabMetadata(normalizedPath, out var canonicalTitle, out _)
            ? canonicalTitle
            : BuildTabTitle(normalizedPath);
    }

    private bool TryGetCanonicalTabMetadata(string normalizedPath, out string title, out string iconPath)
    {
        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.UserProfile))
        {
            title = SidebarHome.Label;
            iconPath = SidebarHome.IconPath;
            return true;
        }

        if (IsRecycleBinPath(normalizedPath))
        {
            var recycleBinItem = SidebarFavorites.FirstOrDefault(i =>
                i.Label.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase));

            title = recycleBinItem?.Label ?? "Recycle Bin";
            iconPath = recycleBinItem?.IconPath ?? GetSidebarIconUri("recyclebin");
            return true;
        }

        title = string.Empty;
        iconPath = string.Empty;
        return false;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (IsVirtualNextcloudPath(path))
            return path.Trim();

        if (IsNetworkSentinelPath(path))
            return NetworkSentinelPath;

        if (IsUncPath(path))
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.TrimEnd(Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static string NormalizeNetworkShortcutPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return string.Empty;

        var trimmed = rawPath.Trim();
        if (IsVirtualNextcloudPath(trimmed) || IsNetworkSentinelPath(trimmed))
            return string.Empty;

        if (IsUncPath(trimmed))
            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            return NormalizePath(trimmed);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildNetworkLocationLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Network";

        if (IsUncPath(path))
        {
            var segments = path.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
                return segments[1];
            if (segments.Length == 1)
                return segments[0];
            return "Network";
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void SortSidebarNetworkLocations()
    {
        SidebarNetworkLocations = new ObservableCollection<SidebarItemViewModel>(
            SidebarNetworkLocations.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsNetworkSentinelPath(string path)
        => string.Equals(path, NetworkSentinelPath, StringComparison.OrdinalIgnoreCase);

    private static bool IsUncPath(string path)
        => !string.IsNullOrWhiteSpace(path) && path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);

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
        var normalizedPath = NormalizePath(path);
        if (TryGetKnownLocationTitle(normalizedPath, out var knownTitle))
            return knownTitle;

        var root = Path.GetPathRoot(normalizedPath);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(normalizedPath, root))
            return root.TrimEnd(Path.DirectorySeparatorChar);

        var name = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }

    private static void ApplyTabIcon(ExplorerTabViewModel tab, string normalizedPath)
    {
        var (imagePath, resourceKey) = ResolveTabIcon(normalizedPath);
        tab.IconImagePath = imagePath;
        tab.IconResourceKey = resourceKey;
    }

    private static (string? ImagePath, string ResourceKey) ResolveTabIcon(string normalizedPath)
    {
        if (TryGetKnownLocationIconKey(normalizedPath, out var iconKey))
            return (GetSidebarIconUri(iconKey), "Icon.Files.App.ThemedIcons.Folder");

        return (GetSidebarIconUri("folder"), "Icon.Files.App.ThemedIcons.Folder");
    }

    private static bool IsSpecialFolder(string normalizedPath, Environment.SpecialFolder folder)
    {
        var folderPath = Environment.GetFolderPath(folder);
        return !string.IsNullOrWhiteSpace(folderPath) && PathsEqual(normalizedPath, NormalizePath(folderPath));
    }

    /// <summary>
    /// Returns true when the path is a virtual Nextcloud <c>nc://</c> path.
    /// </summary>
    private static bool IsVirtualNextcloudPath(string path)
        => !string.IsNullOrWhiteSpace(path) && path.StartsWith("nc://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the WebDAV-relative remote path from a virtual <c>nc://</c> path.
    /// e.g. "nc:///Documents/file.txt" → "/Documents/file.txt"
    /// </summary>
    private static string GetNextcloudRemotePath(string ncPath)
    {
        var remote = ncPath[5..]; // strip "nc://"
        if (string.IsNullOrWhiteSpace(remote)) return "/";
        return remote.StartsWith('/') ? remote : "/" + remote;
    }

    /// <summary>
    /// Converts a WebDAV href (returned by PROPFIND) back to a virtual nc:// path.
    /// The href typically looks like: /remote.php/webdav/Documents/file.txt
    /// </summary>
    private static string ConvertWebDavHrefToNcPath(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return "nc:///";

        var normalized = Uri.UnescapeDataString(href).Replace('\\', '/').Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
            normalized = absoluteUri.AbsolutePath;

        const string webDavPrefix = "/remote.php/webdav/";
        const string davFilesPrefix = "/remote.php/dav/files/";
        string relativePath;

        var webDavIdx = normalized.IndexOf(webDavPrefix, StringComparison.OrdinalIgnoreCase);
        if (webDavIdx >= 0)
        {
            relativePath = normalized[(webDavIdx + webDavPrefix.Length)..];
        }
        else
        {
            var davFilesIdx = normalized.IndexOf(davFilesPrefix, StringComparison.OrdinalIgnoreCase);
            if (davFilesIdx >= 0)
            {
                var afterPrefix = normalized[(davFilesIdx + davFilesPrefix.Length)..];
                var slashIndex = afterPrefix.IndexOf('/');
                relativePath = slashIndex >= 0 ? afterPrefix[(slashIndex + 1)..] : string.Empty;
            }
            else
            {
                relativePath = normalized;
            }
        }

        relativePath = relativePath.TrimStart('/');
        return string.IsNullOrWhiteSpace(relativePath) ? "nc:///" : "nc:///" + relativePath;
    }

    private static bool IsNextcloudPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        // Check virtual nc:// paths first
        if (IsVirtualNextcloudPath(normalizedPath))
            return true;

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

    private static bool IsRecycleBinPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        var trimmed = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.IndexOf($"{Path.DirectorySeparatorChar}$Recycle.Bin", StringComparison.OrdinalIgnoreCase) >= 0
               || trimmed.EndsWith("$Recycle.Bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetKnownLocationTitle(string normalizedPath, out string title)
    {
        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.UserProfile))
        {
            title = "Home";
            return true;
        }

        if (IsRecycleBinPath(normalizedPath))
        {
            title = "Recycle Bin";
            return true;
        }

        if (IsNextcloudPath(normalizedPath))
        {
            title = "Nextcloud";
            return true;
        }

        if (IsNetworkSentinelPath(normalizedPath))
        {
            title = "Network";
            return true;
        }

        title = string.Empty;
        return false;
    }

    private static bool TryGetKnownLocationIconKey(string normalizedPath, out string iconKey)
    {
        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.UserProfile))
        {
            iconKey = "home";
            return true;
        }

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.DesktopDirectory))
        {
            iconKey = "desktop";
            return true;
        }

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyDocuments))
        {
            iconKey = "documents";
            return true;
        }

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyMusic))
        {
            iconKey = "music";
            return true;
        }

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyPictures))
        {
            iconKey = "pictures";
            return true;
        }

        if (IsSpecialFolder(normalizedPath, Environment.SpecialFolder.MyVideos))
        {
            iconKey = "videos";
            return true;
        }

        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(downloads))
        {
            var downloadsPath = NormalizePath(Path.Combine(downloads, "Downloads"));
            if (PathsEqual(normalizedPath, downloadsPath))
            {
                iconKey = "downloads";
                return true;
            }
        }

        if (IsRecycleBinPath(normalizedPath))
        {
            iconKey = "recyclebin";
            return true;
        }

        if (IsNextcloudPath(normalizedPath))
        {
            iconKey = "nextcloud";
            return true;
        }

        var root = Path.GetPathRoot(normalizedPath);
        if (!string.IsNullOrWhiteSpace(root) && PathsEqual(normalizedPath, root))
        {
            iconKey = "drive";
            return true;
        }

        if (normalizedPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
        {
            iconKey = "network";
            return true;
        }

        iconKey = string.Empty;
        return false;
    }

    private static string? ResolveDetailsPaneIconPath(string normalizedPath, string? fallbackIconPath)
    {
        if (TryGetKnownLocationIconKey(normalizedPath, out var knownIconKey))
            return GetHighQualityLocationIconPath(knownIconKey);

        return fallbackIconPath;
    }

    private static string GetHighQualityLocationIconPath(string iconKey) => iconKey switch
    {
        "desktop" => GetWindowsIconPathForKey("desktop", GetSidebarIconUri("desktop")),
        "documents" => GetWindowsIconPathForKey("documents", GetSidebarIconUri("documents")),
        "downloads" => GetWindowsIconPathForKey("downloads", GetSidebarIconUri("downloads")),
        "pictures" => GetWindowsIconPathForKey("pictures", GetSidebarIconUri("pictures")),
        "music" => GetWindowsIconPathForKey("music", GetSidebarIconUri("music")),
        "videos" => GetWindowsIconPathForKey("videos", GetSidebarIconUri("videos")),
        "recyclebin" => GetWindowsIconPathForKey("recyclebin", GetSidebarIconUri("recyclebin")),
        "drive" => GetWindowsIconPathForKey("drive", GetSidebarIconUri("drive")),
        "folder" => GetWindowsIconPathForKey("folder", GetSidebarIconUri("folder")),
        _ => GetSidebarIconUri(iconKey)
    };

    private static string GetWindowsIconPathForKey(string key, string fallbackUri)
    {
        if (!OperatingSystem.IsWindows())
            return fallbackUri;

        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FinderExplorer",
                "icons");
            Directory.CreateDirectory(cacheDir);

            var iconPath = Path.Combine(cacheDir, $"windows-{key}-{WindowsFolderIconResolveSizePx}.png");
            if (File.Exists(iconPath))
                return iconPath;

            var seedPath = ResolveWindowsIconSeedPath(key);

            if (!string.IsNullOrWhiteSpace(seedPath) &&
                TrySaveAssociatedShellIconAsPng(seedPath, iconPath, WindowsFolderIconResolveSizePx))
            {
                return iconPath;
            }
        }
        catch
        {
            // Fallback below.
        }

        return fallbackUri;
    }

    private static string ResolveWindowsIconSeedPath(string key)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))
                         ?? Path.GetPathRoot(Environment.SystemDirectory)
                         ?? "C:\\";

        return key switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "downloads" => Path.Combine(userProfile, "Downloads"),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "drive" => systemRoot,
            "recyclebin" => Path.Combine(systemRoot, "$Recycle.Bin"),
            "folder" => userProfile,
            _ => userProfile
        };
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
        "folder" => GetWindowsIconPathForKey("folder", "avares://FinderExplorer/Assets/Icons/WinUI/Drive.png"),
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

    private sealed record SettingsActionDefinition(string Title, string CommandName, string Shortcut);
}

/// <summary>
/// Represents a clickable breadcrumb segment with its full path for navigation.
/// </summary>
public partial class BreadcrumbSegmentViewModel : ObservableObject
{
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _fullPath;
    [ObservableProperty] private bool _showSeparator;
    [ObservableProperty] private bool _showChevronButton;

    public BreadcrumbSegmentViewModel(string label, string fullPath, bool showSeparator, bool showChevronButton)
    {
        _label = label;
        _fullPath = fullPath;
        _showSeparator = showSeparator;
        _showChevronButton = showChevronButton;
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

public sealed class SettingsActionItemViewModel
{
    public string Title { get; }
    public string CommandName { get; }
    public string Shortcut { get; }
    public IReadOnlyList<string> ShortcutTokens { get; }

    public SettingsActionItemViewModel(string title, string commandName, string shortcut)
    {
        Title = title;
        CommandName = commandName;
        Shortcut = shortcut;

        ShortcutTokens = string.IsNullOrWhiteSpace(shortcut)
            ? ["Not set"]
            : shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
