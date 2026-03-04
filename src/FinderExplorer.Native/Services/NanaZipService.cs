// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Native.Services;

/// <summary>
/// <see cref="IArchiveService"/> backed by NanaZip's bundled 7z.exe.
/// </summary>
public sealed class NanaZipService : IArchiveService
{
    private const int JsonBufLen = 1 * 1024 * 1024; // 1 MB — generous for large archives

    public bool IsNanaZipAvailable
    {
        get
        {
            Span<char> probe = stackalloc char[4];
            // FE_NanaZip_List returns -2 specifically when 7z.exe is not found
            return NativeBridge.NanaZip_List(string.Empty, probe, probe.Length) != -2;
        }
    }

    private static void ThrowIfUnavailable()
    {
        Span<char> probe = stackalloc char[4];
        if (NativeBridge.NanaZip_List(string.Empty, probe, probe.Length) == -2)
            throw new InvalidOperationException(
                "NanaZip not found. Install from https://github.com/M2Team/NanaZip");
    }

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfUnavailable();

            var  buf  = new char[JsonBufLen];
            int  code = NativeBridge.NanaZip_List(archivePath, buf, buf.Length);
            if (code != 0)
                throw new InvalidOperationException($"NanaZip list failed (exit {code}).");

            int    len  = Array.IndexOf(buf, '\0');
            string json = new(buf, 0, len >= 0 ? len : buf.Length);
            return (IReadOnlyList<ArchiveEntry>)ParseJson(json);
        }, ct);

    public Task ExtractAsync(
        string archivePath, string destination,
        IProgress<double>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfUnavailable();

            NativeBridge.ProgressCallback? cb = progress is null
                ? null : frac => progress.Report(frac);

            int code = NativeBridge.NanaZip_Extract(archivePath, destination, cb);
            if (code is not 0 and not -2)
                throw new InvalidOperationException($"NanaZip extract failed (exit {code}).");
        }, ct);

    public Task CompressAsync(
        IReadOnlyList<string> sources, string destinationArchive,
        IProgress<double>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfUnavailable();

            var arr = new string[sources.Count];
            for (int i = 0; i < sources.Count; i++) arr[i] = sources[i];

            NativeBridge.ProgressCallback? cb = progress is null
                ? null : frac => progress.Report(frac);

            int code = NativeBridge.NanaZip_Compress(arr, arr.Length, destinationArchive, cb);
            if (code is not 0 and not -2)
                throw new InvalidOperationException($"NanaZip compress failed (exit {code}).");
        }, ct);

    // -----------------------------------------------------------------------
    // JSON parsing for 7z -slt list output
    // -----------------------------------------------------------------------

    private static List<ArchiveEntry> ParseJson(string json)
    {
        var list = new List<ArchiveEntry>();
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string path  = item.GetProperty("path").GetString() ?? string.Empty;
                long   size  = item.TryGetProperty("size",  out var sv) ? sv.GetInt64()   : 0;
                bool   isDir = item.TryGetProperty("isDir", out var dv) && dv.GetBoolean();
                list.Add(new ArchiveEntry(path, CompressedSize: 0, UncompressedSize: size, isDir));
            }
        }
        catch (JsonException) { /* Malformed — return partial list */ }
        return list;
    }
}
