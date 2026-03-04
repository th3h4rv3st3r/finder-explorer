// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Registers and unregisters the application as the Windows default file manager.
/// </summary>
public interface IDefaultFileManagerService
{
    /// <summary>Returns true if the app is already registered as the default file manager.</summary>
    bool IsRegistered { get; }

    /// <summary>
    /// Registers Finder Explorer as the default file manager.
    /// Writes to HKCU — no elevation required.
    /// Also registers a "Open with Finder Explorer" shell verb for folders.
    /// </summary>
    Task RegisterAsync(CancellationToken ct = default);

    /// <summary>Removes all registrations written by <see cref="RegisterAsync"/>.</summary>
    Task UnregisterAsync(CancellationToken ct = default);

    /// <summary>
    /// Optionally redirects explorer.exe to Finder Explorer via IFEO.
    /// Requires administrator elevation. Returns false if not elevated.
    /// </summary>
    Task<bool> TrySetIfeoRedirectAsync(bool enable, CancellationToken ct = default);
}
