// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Core.Services;

// ---------------------------------------------------------------------------
// Source-generated JSON context — AOT + trimming safe
// ---------------------------------------------------------------------------
[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(
    WriteIndented        = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas  = true,
    ReadCommentHandling  = JsonCommentHandling.Skip)]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext { }

/// <summary>
/// JSON-backed <see cref="ISettingsService"/>.
/// Settings are stored in %LOCALAPPDATA%\FinderExplorer\settings.json.
/// Uses System.Text.Json source generation — fully AOT and trim-safe.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FinderExplorer",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(SettingsPath);
            Current = await JsonSerializer.DeserializeAsync(
                          stream,
                          AppSettingsJsonContext.Default.AppSettings,
                          ct)
                      ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file — start with defaults
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        // Atomic write: temp file + rename prevents corrupt settings on crash
        var tmp = SettingsPath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(
                stream,
                Current,
                AppSettingsJsonContext.Default.AppSettings,
                ct);

        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
