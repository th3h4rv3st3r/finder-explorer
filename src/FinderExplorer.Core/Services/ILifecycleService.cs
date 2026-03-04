// Copyright (c) Finder Explorer. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Manages the application lifecycle:
/// system tray icon, minimize-to-tray, startup registration.
/// </summary>
public interface ILifecycleService : IDisposable
{
    /// <summary>
    /// Initialises the system tray icon. Call once on startup.
    /// <paramref name="hwnd"/> is the native handle of the main window.
    /// </summary>
    void InitializeTray(IntPtr hwnd);

    /// <summary>Shows the main window (restore from tray).</summary>
    void ShowMainWindow();

    /// <summary>Hides the main window to the system tray.</summary>
    void HideToTray();

    /// <summary>Removes the tray icon and cleans up.</summary>
    void RemoveTrayIcon();

    /// <summary>Registers or removes the app from the Windows startup Run key.</summary>
    Task SetRunAtStartupAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Returns whether the Run key entry currently exists.</summary>
    bool IsRunAtStartupEnabled();
}
