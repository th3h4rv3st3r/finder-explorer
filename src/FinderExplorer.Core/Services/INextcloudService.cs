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
}
