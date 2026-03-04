// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// Persisted application settings.
/// Serialisable to JSON so it can be saved to a file by ISettingsService.
/// </summary>
public sealed class AppSettings
{
    // -----------------------------------------------------------------------
    // Search
    // -----------------------------------------------------------------------

    /// <summary>
    /// When true and Everything is installed, uses Everything64.dll for instant search.
    /// Falls back to recursive directory scan when false or Everything is unavailable.
    /// </summary>
    public bool UseEverythingSearch { get; set; } = true;

    // -----------------------------------------------------------------------
    // Thumbnails
    // -----------------------------------------------------------------------

    /// <summary>
    /// When true, extracts thumbnails via IShellItemImageFactory (GPU-composited, DWM path).
    /// When false, falls back to IExtractImage (software rasteriser, lower quality but always available).
    /// </summary>
    public bool UseGpuThumbnails { get; set; } = true;

    /// <summary>Thumbnail display size in pixels (square).  Typical values: 64, 128, 256.</summary>
    public int ThumbnailSize { get; set; } = 128;

    // -----------------------------------------------------------------------
    // UI / General
    // -----------------------------------------------------------------------

    /// <summary>Show hidden files and folders in the file list.</summary>
    public bool ShowHiddenFiles { get; set; } = false;

    /// <summary>Active sort field for the file list.</summary>
    public SortField SortField { get; set; } = SortField.Name;

    /// <summary>Ascending sort order when true.</summary>
    public bool SortAscending { get; set; } = true;

    // -----------------------------------------------------------------------
    // Hot Open (spring-loaded folders — open on drag-hover)
    // -----------------------------------------------------------------------

    /// <summary>Opens a folder automatically when dragging items over it and hovering.</summary>
    public bool HotOpenEnabled { get; set; } = true;

    /// <summary>Hover duration in milliseconds before a folder spring-opens. Default: 500 ms.</summary>
    public int HotOpenDelayMs { get; set; } = 500;

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    /// <summary>Minimise to system tray instead of closing when the window is closed.</summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>Launch Finder Explorer when Windows starts.</summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>Register as the default file manager (intercepts folder opens).</summary>
    public bool IsDefaultFileManager { get; set; } = false;

    // -----------------------------------------------------------------------
    // Nextcloud Integration
    // -----------------------------------------------------------------------

    /// <summary>Enable Nextcloud integration.</summary>
    public bool NextcloudEnabled { get; set; } = false;

    /// <summary>Base URL of the Nextcloud instance (e.g., https://cloud.example.com).</summary>
    public string NextcloudUrl { get; set; } = string.Empty;

    /// <summary>Nextcloud Username.</summary>
    public string NextcloudUser { get; set; } = string.Empty;

    /// <summary>Nextcloud App Password (generated in Nextcloud Security settings).</summary>
    public string NextcloudAppPassword { get; set; } = string.Empty;
}
