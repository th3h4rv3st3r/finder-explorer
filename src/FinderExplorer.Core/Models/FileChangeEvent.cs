// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// Describes a change in the file system observed by <see cref="Services.IFileWatcherService"/>.
/// </summary>
public sealed record FileChangeEvent(string Path, FileChangeKind Kind);

public enum FileChangeKind
{
    Created,
    Deleted,
    Modified,
    Renamed
}
