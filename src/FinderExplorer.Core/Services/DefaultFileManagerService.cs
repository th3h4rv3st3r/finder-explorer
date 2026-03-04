// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Registers/unregisters Finder Explorer as the Windows default file manager.
/// All HKCU writes require no elevation.
/// IFEO redirect (intercept explorer.exe) requires administrator.
/// </summary>
public sealed class DefaultFileManagerService : IDefaultFileManagerService
{
    private static readonly string ExePath =
        Process.GetCurrentProcess().MainModule?.FileName
        ?? Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine executable path.");

    // Registry keys written by this service (HKCU — no elevation)
    private const string RegAppPath    = @"SOFTWARE\FinderExplorer";
    private const string RegCapabilities     = @"SOFTWARE\FinderExplorer\Capabilities";
    private const string RegRegisteredApps   = @"SOFTWARE\RegisteredApplications";
    private const string RegFolderVerb       = @"SOFTWARE\Classes\Directory\shell\FinderExplorer";
    private const string RegFolderVerbCmd    = @"SOFTWARE\Classes\Directory\shell\FinderExplorer\command";
    private const string RegDriveVerb        = @"SOFTWARE\Classes\Drive\shell\FinderExplorer";
    private const string RegDriveVerbCmd     = @"SOFTWARE\Classes\Drive\shell\FinderExplorer\command";
    private const string RegProtocol         = @"SOFTWARE\Classes\finderexplorer";
    private const string RegProtocolCmd      = @"SOFTWARE\Classes\finderexplorer\shell\open\command";

    // IFEO key (HKLM — requires admin)
    private const string RegIfeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\explorer.exe";

    // -----------------------------------------------------------------------
    // IsRegistered
    // -----------------------------------------------------------------------

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegFolderVerb);
            return key is not null;
        }
    }

    // -----------------------------------------------------------------------
    // RegisterAsync
    // -----------------------------------------------------------------------

    public Task RegisterAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            // 1. App capabilities (RegisteredApplications)
            using (var cap = Registry.CurrentUser.CreateSubKey(RegCapabilities))
            {
                cap.SetValue("ApplicationName", "Finder Explorer");
                cap.SetValue("ApplicationDescription", "A modern file manager for Windows.");
            }
            using (var regApps = Registry.CurrentUser.OpenSubKey(RegRegisteredApps, writable: true))
                regApps?.SetValue("FinderExplorer", RegCapabilities);

            // 2. Shell verb on Directory (right-click on folder → "Open with Finder Explorer")
            using (var verb = Registry.CurrentUser.CreateSubKey(RegFolderVerb))
            {
                verb.SetValue("", "Open with Finder Explorer");
                verb.SetValue("Icon", $"\"{ExePath}\",0");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey(RegFolderVerbCmd))
                cmd.SetValue("", $"\"{ExePath}\" \"%1\"");

            // 3. Shell verb on Drive
            using (var verb = Registry.CurrentUser.CreateSubKey(RegDriveVerb))
            {
                verb.SetValue("", "Open with Finder Explorer");
                verb.SetValue("Icon", $"\"{ExePath}\",0");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey(RegDriveVerbCmd))
                cmd.SetValue("", $"\"{ExePath}\" \"%1\"");

            // 4. finderexplorer:// URL protocol handler
            using (var proto = Registry.CurrentUser.CreateSubKey(RegProtocol))
            {
                proto.SetValue("", "URL:FinderExplorer Protocol");
                proto.SetValue("URL Protocol", "");
            }
            using (var cmd = Registry.CurrentUser.CreateSubKey(RegProtocolCmd))
                cmd.SetValue("", $"\"{ExePath}\" \"%1\"");

        }, ct);

    // -----------------------------------------------------------------------
    // UnregisterAsync
    // -----------------------------------------------------------------------

    public Task UnregisterAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            TryDeleteSubKeyTree(Registry.CurrentUser, @"SOFTWARE\FinderExplorer");
            TryDeleteSubKeyTree(Registry.CurrentUser, @"SOFTWARE\Classes\Directory\shell\FinderExplorer");
            TryDeleteSubKeyTree(Registry.CurrentUser, @"SOFTWARE\Classes\Drive\shell\FinderExplorer");
            TryDeleteSubKeyTree(Registry.CurrentUser, @"SOFTWARE\Classes\finderexplorer");

            using var regApps = Registry.CurrentUser.OpenSubKey(RegRegisteredApps, writable: true);
            regApps?.DeleteValue("FinderExplorer", throwOnMissingValue: false);
        }, ct);

    // -----------------------------------------------------------------------
    // IFEO redirect (admin required)
    // -----------------------------------------------------------------------

    public async Task<bool> TrySetIfeoRedirectAsync(bool enable, CancellationToken ct = default)
    {
        if (!IsAdministrator())
        {
            // Re-launch as admin to toggle the key
            return await RequestElevationAsync(enable ? "--ifeo-enable" : "--ifeo-disable", ct);
        }

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(RegIfeo);
                if (enable)
                    key.SetValue("Debugger", $"\"{ExePath}\"");
                else
                    key.DeleteValue("Debugger", throwOnMissingValue: false);
                return true;
            }
            catch { return false; }
        }, ct);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static Task<bool> RequestElevationAsync(string arg, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var psi = new ProcessStartInfo(ExePath, arg)
                {
                    UseShellExecute = true,
                    Verb            = "runas"
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }, ct);
    }

    private static void TryDeleteSubKeyTree(RegistryKey root, string key)
    {
        try { root.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
        catch { /* Access denied or already gone */ }
    }
}
