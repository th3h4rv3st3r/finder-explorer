// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// A single entry inside an archive, as returned by <see cref="Services.IArchiveService"/>.
/// </summary>
public sealed record ArchiveEntry(
    string Path,
    long   CompressedSize,
    long   UncompressedSize,
    bool   IsDirectory)
{
    public string Name => System.IO.Path.GetFileName(Path);
}
