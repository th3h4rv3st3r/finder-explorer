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
}
