// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Watches one or more directory paths for changes and fires <see cref="Changed"/>
/// after a short debounce window to batch rapid events.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Fired (on a thread-pool thread) when a change is detected.
    /// Debounced to ~150 ms.
    /// </summary>
    event Action<FileChangeEvent> Changed;

    /// <summary>Begins watching <paramref name="path"/>.</summary>
    void Watch(string path);

    /// <summary>Stops watching <paramref name="path"/>.</summary>
    void Unwatch(string path);
}
