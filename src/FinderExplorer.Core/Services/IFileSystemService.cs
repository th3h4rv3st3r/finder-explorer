// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Abstraction for file system operations.
/// Enables testability and future integration with remote file systems (e.g. Nextcloud WebDAV).
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Lists all items (directories first, then files) in the given path,
    /// applying optional sorting and filtering.
    /// </summary>
    Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(
        string            path,
        SortOptions?      sort   = null,
        FilterOptions?    filter = null,
        CancellationToken ct     = default);

    /// <summary>Gets the sidebar items (favorites + volumes).</summary>
    IReadOnlyList<SidebarItem> GetSidebarItems();

    /// <summary>Checks whether the given path is a directory that exists.</summary>
    bool DirectoryExists(string path);

    /// <summary>Gets the parent directory path, or null if at root.</summary>
    string? GetParentPath(string path);

    /// <summary>
    /// Deletes a file or directory.
    /// Use <see cref="FinderExplorer.Native.Bridge.NativeBridge.Shell_DeleteItems"/> for
    /// Recycle-Bin support — this method performs a permanent delete.
    /// </summary>
    Task DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Renames a file or directory in-place.</summary>
    Task RenameAsync(string oldPath, string newName, CancellationToken ct = default);

    /// <summary>Copies a file or directory to the destination folder.</summary>
    Task CopyAsync(string sourcePath, string destinationFolder, CancellationToken ct = default);

    /// <summary>Moves a file or directory to the destination folder.</summary>
    Task MoveAsync(string sourcePath, string destinationFolder, CancellationToken ct = default);

    /// <summary>Creates a new empty folder at <paramref name="parentPath"/>/<paramref name="name"/>.</summary>
    Task CreateFolderAsync(string parentPath, string name, CancellationToken ct = default);

    /// <summary>Opens a file with its default application via the Windows Shell.</summary>
    void OpenFile(string path);
}
