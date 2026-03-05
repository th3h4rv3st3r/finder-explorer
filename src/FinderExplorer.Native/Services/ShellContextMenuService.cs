// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FinderExplorer.Native.Services;

/// <summary>
/// Native Windows shell context menu implementation backed by C++ bridge.
/// </summary>
public sealed class ShellContextMenuService : IShellContextMenuService
{
    public bool TryShowContextMenu(
        IReadOnlyList<string> paths,
        IntPtr                ownerHwnd,
        int                   screenX,
        int                   screenY)
    {
        if (paths is null || paths.Count == 0)
            return false;

        var validPaths = paths.Where(static p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (validPaths.Length == 0)
            return false;

        try
        {
            NativeBridge.Shell_ShowContextMenu(validPaths, validPaths.Length, ownerHwnd, screenX, screenY);
            return true;
        }
        catch
        {
            // If native bridge isn't present or fails, caller can fallback to managed context menu.
            return false;
        }
    }
}
