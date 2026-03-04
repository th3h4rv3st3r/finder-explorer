// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Implements system tray and startup lifecycle using the native C++ TrayBridge
/// and the Windows Registry.
/// </summary>
public sealed class LifecycleService : ILifecycleService
{
    private readonly ISettingsService _settings;
    private IntPtr _mainWindowHwnd;

    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "FinderExplorer";

    public LifecycleService(ISettingsService settings)
    {
        _settings = settings;
    }

    // -----------------------------------------------------------------------
    // System Tray
    // -----------------------------------------------------------------------

    public void InitializeTray(IntPtr hwnd)
    {
        // Tray integration is implemented in Native project and will be wired later.
        _mainWindowHwnd = hwnd;
    }

    public void ShowMainWindow()
    {
        // No-op for now; Avalonia window activation is handled at UI layer.
    }

    public void HideToTray()
    {
        // No-op until native tray bridge is connected.
    }

    public void RemoveTrayIcon()
    {
        // No-op until native tray bridge is connected.
    }

    // -----------------------------------------------------------------------
    // Startup (Run Key)
    // -----------------------------------------------------------------------

    public bool IsRunAtStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    public Task SetRunAtStartupAsync(bool enabled, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    // Add --hidden arg so it starts in tray
                    key.SetValue(AppName, $"\"{exePath}\" --hidden");
                }
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }, ct);
    }

    public void Dispose()
    {
        RemoveTrayIcon();
    }
}
