// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System.Collections.Generic;
using System.Threading;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Full-text file search.  Backed by Everything when available;
/// falls back to a recursive directory scan otherwise.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Returns whether the fast Everything backend is currently available.
    /// </summary>
    bool IsEverythingAvailable { get; }

    /// <summary>
    /// Searches for <paramref name="query"/> within <paramref name="scope"/>
    /// (or everywhere when <paramref name="scope"/> is null).
    /// Results stream via <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    IAsyncEnumerable<SearchResult> SearchAsync(
        string            query,
        string?           scope,
        uint              maxResults = 1000,
        CancellationToken ct        = default);
}
