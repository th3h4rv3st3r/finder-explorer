// Copyright (c) Finder Explorer. All rights reserved.

using System;

namespace FinderExplorer.Core.Models;

/// <summary>
/// A single result returned by <see cref="Services.ISearchService"/>.
/// </summary>
public sealed record SearchResult(
    string   FullPath,
    bool     IsDirectory,
    long?    Size,
    DateTime LastModified)
{
    public string Name      => System.IO.Path.GetFileName(FullPath);
    public string Extension => IsDirectory ? string.Empty : System.IO.Path.GetExtension(FullPath);
}
