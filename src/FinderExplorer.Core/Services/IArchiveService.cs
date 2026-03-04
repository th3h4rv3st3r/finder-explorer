// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Archive operations (list, extract, compress) delegated to NanaZip.
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Returns true if NanaZip (7z.exe) was found on the system.
    /// </summary>
    bool IsNanaZipAvailable { get; }

    /// <summary>
    /// Lists all entries inside <paramref name="archivePath"/>.
    /// </summary>
    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string            archivePath,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts <paramref name="archivePath"/> to <paramref name="destination"/>.
    /// <paramref name="progress"/> receives values in the range [0, 1].
    /// </summary>
    Task ExtractAsync(
        string             archivePath,
        string             destination,
        IProgress<double>? progress = null,
        CancellationToken  ct       = default);

    /// <summary>
    /// Compresses <paramref name="sources"/> into <paramref name="destinationArchive"/>.
    /// </summary>
    Task CompressAsync(
        IReadOnlyList<string> sources,
        string                destinationArchive,
        IProgress<double>?    progress = null,
        CancellationToken     ct       = default);
}
