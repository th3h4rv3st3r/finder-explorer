// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FinderExplorer.Core.Services;

/// <summary>
/// <see cref="IFileWatcherService"/> implementation using <see cref="FileSystemWatcher"/>.
/// Changes are debounced by 150 ms before firing the <see cref="Changed"/> event,
/// so rapid saves/renames don't flood the UI.
/// </summary>
public sealed class FileWatcherService : IFileWatcherService
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    // Debounce: we use a single Timer per change that resets on each event.
    private Timer?           _debounceTimer;
    private FileChangeEvent? _pendingEvent;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);

    public event Action<FileChangeEvent>? Changed;

    // -----------------------------------------------------------------------
    // Watch / Unwatch
    // -----------------------------------------------------------------------

    public void Watch(string path)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(path)) return;
            if (!Directory.Exists(path)) return;

            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents   = true
            };

            watcher.Created += (_, e) => ScheduleEvent(new FileChangeEvent(e.FullPath, FileChangeKind.Created));
            watcher.Deleted += (_, e) => ScheduleEvent(new FileChangeEvent(e.FullPath, FileChangeKind.Deleted));
            watcher.Changed += (_, e) => ScheduleEvent(new FileChangeEvent(e.FullPath, FileChangeKind.Modified));
            watcher.Renamed += (_, e) => ScheduleEvent(new FileChangeEvent(e.FullPath, FileChangeKind.Renamed));
            watcher.Error   += (_, _) => { /* Swallow — buffer overflow or access errors */ };

            _watchers[path] = watcher;
        }
    }

    public void Unwatch(string path)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(path);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Debounce
    // -----------------------------------------------------------------------

    private void ScheduleEvent(FileChangeEvent evt)
    {
        lock (_lock)
        {
            _pendingEvent = evt;

            if (_debounceTimer is null)
                _debounceTimer = new Timer(FireDebounced, null, DebounceDelay, Timeout.InfiniteTimeSpan);
            else
                _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FireDebounced(object? _)
    {
        FileChangeEvent? evt;
        lock (_lock)
        {
            evt           = _pendingEvent;
            _pendingEvent = null;
        }
        if (evt is not null)
            Changed?.Invoke(evt);
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            foreach (var w in _watchers.Values)
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            _watchers.Clear();
        }
    }
}
