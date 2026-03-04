// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// Filtering criteria for a file list.
/// </summary>
public sealed record FilterOptions(
    string?   NameContains,
    string[]? Extensions,
    bool      ShowHidden)
{
    /// <summary>Default: no filter, hidden items excluded.</summary>
    public static readonly FilterOptions Default = new(null, null, ShowHidden: false);
}
