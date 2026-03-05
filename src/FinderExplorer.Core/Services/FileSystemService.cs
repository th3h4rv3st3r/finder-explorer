// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Local file system implementation of <see cref="IFileSystemService"/>.
/// Supports sort/filter, CreateFolder, and OpenFile (shell execute).
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    // -----------------------------------------------------------------------
    // GetItemsAsync — with sort + filter
    // -----------------------------------------------------------------------

    public Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(
        string            path,
        SortOptions?      sort   = null,
        FilterOptions?    filter = null,
        CancellationToken ct     = default)
    {
        sort   ??= SortOptions.Default;
        filter ??= FilterOptions.Default;

        return Task.Run(() =>
        {
            var items   = new List<FileSystemItem>();
            var dirInfo = new DirectoryInfo(path);

            // --- Directories ---
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!filter.ShowHidden && dir.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;
                    if (!MatchesNameFilter(dir.Name, filter.NameContains))
                        continue;

                    items.Add(new FileSystemItem
                    {
                        Name         = dir.Name,
                        FullPath     = dir.FullName,
                        IsDirectory  = true,
                        Size         = null,
                        LastModified = dir.LastWriteTime,
                        DateCreated  = dir.CreationTime,
                        Attributes   = dir.Attributes
                    });
                }
                catch { /* access denied */ }
            }

            // --- Files ---
            foreach (var file in dirInfo.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!filter.ShowHidden && file.Attributes.HasFlag(FileAttributes.Hidden))
                        continue;
                    if (!MatchesNameFilter(file.Name, filter.NameContains))
                        continue;
                    if (!MatchesExtensionFilter(file.Extension, filter.Extensions))
                        continue;

                    items.Add(new FileSystemItem
                    {
                        Name         = file.Name,
                        FullPath     = file.FullName,
                        IsDirectory  = false,
                        Size         = file.Length,
                        LastModified = file.LastWriteTime,
                        DateCreated  = file.CreationTime,
                        Attributes   = file.Attributes
                    });
                }
                catch { /* access denied */ }
            }

            // --- Sort ---
            var sorted = ApplySort(items, sort);
            return (IReadOnlyList<FileSystemItem>)sorted;

        }, ct);
    }

    // -----------------------------------------------------------------------
    // Sidebar
    // -----------------------------------------------------------------------

    public IReadOnlyList<SidebarItem> GetSidebarItems()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var items = new List<SidebarItem>
        {
            new() { Label = "Desktop",   Path = Path.Combine(userProfile, "Desktop"),   IconKey = "desktop",   Section = SidebarSection.Favorites },
            new() { Label = "Documents", Path = Path.Combine(userProfile, "Documents"), IconKey = "documents", Section = SidebarSection.Favorites },
            new() { Label = "Downloads", Path = Path.Combine(userProfile, "Downloads"), IconKey = "downloads", Section = SidebarSection.Favorites },
            new() { Label = "Pictures",  Path = Path.Combine(userProfile, "Pictures"),  IconKey = "pictures",  Section = SidebarSection.Favorites },
            new() { Label = "Music",     Path = Path.Combine(userProfile, "Music"),     IconKey = "music",     Section = SidebarSection.Favorites },
            new() { Label = "Videos",    Path = Path.Combine(userProfile, "Videos"),    IconKey = "videos",    Section = SidebarSection.Favorites },
        };

        var recycleBinPath = TryGetRecycleBinPath();
        if (!string.IsNullOrWhiteSpace(recycleBinPath))
        {
            items.Add(new SidebarItem
            {
                Label = "Recycle Bin",
                Path = recycleBinPath,
                IconKey = "recyclebin",
                Section = SidebarSection.Favorites
            });
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

            items.Add(new SidebarItem
            {
                Label   = label,
                Path    = drive.Name,
                IconKey = "drive",
                Section = SidebarSection.Volumes
            });
        }

        return items;
    }

    // -----------------------------------------------------------------------
    // Navigation helpers
    // -----------------------------------------------------------------------

    public bool   DirectoryExists(string path)          => Directory.Exists(path);
    public string? GetParentPath(string path)            => Directory.GetParent(path)?.FullName;

    // -----------------------------------------------------------------------
    // File operations (permanent — for Shell-based ops use NativeBridge directly)
    // -----------------------------------------------------------------------

    public Task DeleteAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }, ct);

    public Task RenameAsync(string oldPath, string newName, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var dir     = Path.GetDirectoryName(oldPath)!;
            var newPath = Path.Combine(dir, newName);
            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
            else if (Directory.Exists(oldPath))
                Directory.Move(oldPath, newPath);
        }, ct);

    public Task CopyAsync(string sourcePath, string destinationFolder, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(destinationFolder, name);
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, dest, overwrite: false);
            else if (Directory.Exists(sourcePath))
                CopyDirectoryRecursive(sourcePath, dest, ct);
        }, ct);

    public Task MoveAsync(string sourcePath, string destinationFolder, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(destinationFolder, name);
            if (File.Exists(sourcePath))
                File.Move(sourcePath, dest);
            else if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, dest);
        }, ct);

    public Task CreateFolderAsync(string parentPath, string name, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(parentPath, name));
        }, ct);

    public void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static bool MatchesNameFilter(string name, string? pattern) =>
        pattern is null || name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesExtensionFilter(string ext, string[]? extensions) =>
        extensions is null || extensions.Length == 0
        || extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));

    private static List<FileSystemItem> ApplySort(List<FileSystemItem> items, SortOptions sort)
    {
        // Directories always first
        IOrderedEnumerable<FileSystemItem> ordered = sort.Field switch
        {
            SortField.Size     => sort.Ascending
                ? items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Size ?? 0)
                : items.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Size ?? 0),
            SortField.Modified => sort.Ascending
                ? items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.LastModified)
                : items.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.LastModified),
            SortField.Type     => sort.Ascending
                ? items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Extension)
                : items.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Extension),
            _ /* Name */       => sort.Ascending
                ? items.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }

    private static void CopyDirectoryRecursive(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)), ct);
        }
    }

    private static string? TryGetRecycleBinPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;

            var candidate = Path.Combine(drive.Name, "$Recycle.Bin", sid);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
