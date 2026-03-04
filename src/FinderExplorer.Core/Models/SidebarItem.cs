// Copyright (c) Finder Explorer. All rights reserved.

namespace FinderExplorer.Core.Models;

/// <summary>
/// Represents a sidebar entry (favorite folder, volume, etc).
/// </summary>
public sealed class SidebarItem
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public required string IconKey { get; init; }
    public SidebarSection Section { get; init; }
}

public enum SidebarSection
{
    Favorites,
    Volumes,
    Network
}
