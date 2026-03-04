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
        NormalizeLegacyStartupCommand();
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
                    // Start normally; hidden launch is deferred until tray integration is fully wired.
                    key.SetValue(AppName, $"\"{exePath}\"");
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

    private static void NormalizeLegacyStartupCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(AppName) is not string startupCommand)
                return;

            if (!startupCommand.Contains("--hidden", StringComparison.OrdinalIgnoreCase))
                return;

            var normalized = startupCommand
                .Replace(" --hidden", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("--hidden", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (!string.IsNullOrWhiteSpace(normalized))
                key.SetValue(AppName, normalized);
        }
        catch
        {
            // Best-effort migration only.
        }
    }
}
