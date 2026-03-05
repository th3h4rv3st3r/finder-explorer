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

    public Task LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();
                return Task.CompletedTask;
            }

            using var stream = File.OpenRead(SettingsPath);
            Current = JsonSerializer.Deserialize(
                          stream,
                          AppSettingsJsonContext.Default.AppSettings)
                      ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file — start with defaults
            Current = new AppSettings();
        }

        return Task.CompletedTask;
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
                ct).ConfigureAwait(false);

        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
