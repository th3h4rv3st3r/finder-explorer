// Copyright (c) Finder Explorer. All rights reserved.

using System;
using System.Collections.Generic;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Shows the native Windows Explorer context menu for one or more filesystem paths.
/// </summary>
public interface IShellContextMenuService
{
    /// <summary>
    /// Tries to show the native shell context menu at the provided screen coordinates.
    /// </summary>
    /// <returns>True when the request was dispatched successfully.</returns>
    bool TryShowContextMenu(
        IReadOnlyList<string> paths,
        IntPtr                ownerHwnd,
        int                   screenX,
        int                   screenY);
}
