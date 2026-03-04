// Copyright (c) Finder Explorer. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Exposes native Windows Shell metadata properties (Item Type, Dimensions, Date, Authors).
/// </summary>
public interface IFileDetailsService
{
    /// <summary>
    /// Fetches extended metadata for the specified file.
    /// Properties that are not available for the file type will be null or empty.
    /// </summary>
    Task<FileDetails?> GetDetailsAsync(string path, CancellationToken ct = default);
}

/// <summary>Details object holding native Shell properties.</summary>
public sealed record FileDetails(
    string Type,
    string Dimensions,
    string DateTaken,
    string Authors);
