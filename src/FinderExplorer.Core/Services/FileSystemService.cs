// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Local file system implementation of <see cref="IFileSystemService"/>.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    public Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var items = new List<FileSystemItem>();
            var dirInfo = new DirectoryInfo(path);

            // Directories first
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    items.Add(new FileSystemItem
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        IsDirectory = true,
                        Size = null,
                        LastModified = dir.LastWriteTime
                    });
                }
                catch { /* Skip inaccessible */ }
            }

            // Then files
            foreach (var file in dirInfo.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    items.Add(new FileSystemItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime
                    });
                }
                catch { /* Skip inaccessible */ }
            }

            return (IReadOnlyList<FileSystemItem>)items;
        }, ct);
    }

    public IReadOnlyList<SidebarItem> GetSidebarItems()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var items = new List<SidebarItem>
        {
            new() { Label = "Desktop", Path = Path.Combine(userProfile, "Desktop"), IconKey = "desktop", Section = SidebarSection.Favorites },
            new() { Label = "Documents", Path = Path.Combine(userProfile, "Documents"), IconKey = "documents", Section = SidebarSection.Favorites },
            new() { Label = "Downloads", Path = Path.Combine(userProfile, "Downloads"), IconKey = "downloads", Section = SidebarSection.Favorites },
            new() { Label = "Pictures", Path = Path.Combine(userProfile, "Pictures"), IconKey = "pictures", Section = SidebarSection.Favorites },
            new() { Label = "Music", Path = Path.Combine(userProfile, "Music"), IconKey = "music", Section = SidebarSection.Favorites },
            new() { Label = "Videos", Path = Path.Combine(userProfile, "Videos"), IconKey = "videos", Section = SidebarSection.Favorites },
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                items.Add(new SidebarItem
                {
                    Label = label,
                    Path = drive.Name,
                    IconKey = "drive",
                    Section = SidebarSection.Volumes
                });
            }
        }

        return items;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string? GetParentPath(string path)
    {
        var parent = Directory.GetParent(path);
        return parent?.FullName;
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }, ct);
    }

    public Task RenameAsync(string oldPath, string newName, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(oldPath)!;
            var newPath = Path.Combine(dir, newName);

            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
            else if (Directory.Exists(oldPath))
                Directory.Move(oldPath, newPath);
        }, ct);
    }

    public Task CopyAsync(string sourcePath, string destinationFolder, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(destinationFolder, name);

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, dest, overwrite: false);
            }
            else if (Directory.Exists(sourcePath))
            {
                CopyDirectoryRecursive(sourcePath, dest, ct);
            }
        }, ct);
    }

    public Task MoveAsync(string sourcePath, string destinationFolder, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(destinationFolder, name);

            if (File.Exists(sourcePath))
                File.Move(sourcePath, dest);
            else if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, dest);
        }, ct);
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
}
