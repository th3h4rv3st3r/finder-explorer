// Copyright (c) Finder Explorer. All rights reserved.

using System;
using System.IO;

namespace FinderExplorer.Core.Models;

/// <summary>
/// Represents a file or directory in the file system.
/// </summary>
public sealed class FileSystemItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long? Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name);

    public string SizeDisplay => IsDirectory ? "--" : FormatSize(Size ?? 0);

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
