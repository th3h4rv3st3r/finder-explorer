// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// Controls how a file list is sorted.
/// </summary>
public sealed record SortOptions(SortField Field, bool Ascending)
{
    /// <summary>Default: name A→Z.</summary>
    public static readonly SortOptions Default = new(SortField.Name, Ascending: true);
}

public enum SortField
{
    Name,
    Size,
    Modified,
    Type
}
