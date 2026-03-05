// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Nextcloud integration using WebDAV for file navigation and the OCS API for sharing.
/// </summary>
public interface INextcloudService
{
    /// <summary>
    /// Checks connectivity and authentication using the configured settings.
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks connectivity using explicit credentials (for the setup wizard, before saving to settings).
    /// </summary>
    Task<bool> TestConnectionAsync(string url, string user, string appPassword, CancellationToken ct = default);

    /// <summary>
    /// Lists files and folders at the given Nextcloud remote path (e.g., "/Documents").
    /// </summary>
    Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(
        string remotePath,
        SortOptions? sort = null,
        FilterOptions? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a public Nextcloud share link (external) for the given remote path.
    /// Returns the URL, or null if it failed.
    /// </summary>
    Task<string?> CreatePublicShareAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Generates an internal Nextcloud share link for the given remote path.
    /// Returns the URL, or null if it failed.
    /// </summary>
    Task<string?> CreateInternalShareAsync(string remotePath, CancellationToken ct = default);

    /// <summary>Creates a new remote folder.</summary>
    Task<bool> CreateFolderAsync(string remotePath, CancellationToken ct = default);

    /// <summary>Deletes a remote file or folder.</summary>
    Task<bool> DeleteAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Downloads a remote file to a local cache path and returns that local file path.
    /// Returns null when the remote file could not be retrieved.
    /// </summary>
    Task<string?> DownloadFileToCacheAsync(string remotePath, CancellationToken ct = default);

    /// <summary>
    /// Downloads a remote file to a local cache path and returns that local file path.
    /// An optional <paramref name="extensionHint"/> can be provided when the remote path
    /// does not include a file extension.
    /// Returns null when the remote file could not be retrieved.
    /// </summary>
    Task<string?> DownloadFileToCacheAsync(string remotePath, string? extensionHint, CancellationToken ct = default);
}
