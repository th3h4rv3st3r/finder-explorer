// Copyright (c) Finder Explorer. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Loads and persists <see cref="FinderExplorer.Core.Models.AppSettings"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>Current in-memory settings.  Always non-null after Load.</summary>
    Models.AppSettings Current { get; }

    /// <summary>Loads settings from disk (or returns defaults if file not found).</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Persists current settings to disk.</summary>
    Task SaveAsync(CancellationToken ct = default);
}
