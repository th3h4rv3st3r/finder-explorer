// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FinderExplorer.Native.Services;

/// <summary>
/// <see cref="ISearchService"/> backed by Everything64.dll when available,
/// with a recursive .NET directory-scan fallback.
/// </summary>
public sealed class EverythingSearchService : ISearchService
{
    private readonly ISettingsService _settings;

    public EverythingSearchService(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool IsEverythingAvailable =>
        _settings.Current.UseEverythingSearch && NativeBridge.ES_IsAvailable() == 1;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        string            query,
        string?           scope,
        uint              maxResults = 1000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (IsEverythingAvailable)
        {
            var results = await System.Threading.Tasks.Task.Run(
                () => RunEverythingSearch(query, scope, maxResults, ct), ct);

            foreach (var r in results)
            {
                ct.ThrowIfCancellationRequested();
                yield return r;
            }
        }
        else
        {
            await foreach (var r in FallbackSearchAsync(query, scope, maxResults, ct))
                yield return r;
        }
    }

    // -----------------------------------------------------------------------
    // Everything backend
    // -----------------------------------------------------------------------

    private static List<SearchResult> RunEverythingSearch(
        string query, string? scope, uint maxResults, CancellationToken ct)
    {
        int count = NativeBridge.ES_Search(query, scope, maxResults);
        var list  = new List<SearchResult>(count > 0 ? Math.Min(count, (int)maxResults) : 0);
        if (count <= 0) return list;

        var buf = new char[32768];

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();

            int ok = NativeBridge.ES_GetResult(
                i, buf, buf.Length,
                out long size, out long ft, out bool isDir);

            if (ok != 1) continue;

            int    len  = Array.IndexOf(buf, '\0');
            string path = new(buf, 0, len >= 0 ? len : buf.Length);
            if (string.IsNullOrEmpty(path)) continue;

            DateTime modified = ft > 0 ? DateTime.FromFileTime(ft) : DateTime.MinValue;
            list.Add(new SearchResult(path, isDir, size < 0 ? null : size, modified));
        }

        NativeBridge.ES_Reset();
        return list;
    }

    // -----------------------------------------------------------------------
    // Recursive .NET fallback (no Everything installed)
    // -----------------------------------------------------------------------

    private static async IAsyncEnumerable<SearchResult> FallbackSearchAsync(
        string query, string? scope, uint maxResults,
        [EnumeratorCancellation] CancellationToken ct)
    {
        string root = scope ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(root)) yield break;

        uint  count = 0;
        var   queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0 && count < maxResults)
        {
            ct.ThrowIfCancellationRequested();
            string dir = queue.Dequeue();

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (string entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (count >= maxResults) yield break;

                bool   isDir = Directory.Exists(entry);
                string name  = Path.GetFileName(entry);

                if (!name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    if (isDir) queue.Enqueue(entry);
                    continue;
                }

                long?    size     = null;
                DateTime modified = DateTime.MinValue;
                try
                {
                    if (!isDir) { var fi = new FileInfo(entry); size = fi.Length; modified = fi.LastWriteTime; }
                    else         modified = new DirectoryInfo(entry).LastWriteTime;
                }
                catch { /* access denied */ }

                yield return new SearchResult(entry, isDir, size, modified);
                count++;
                if (isDir) queue.Enqueue(entry);

                if (count % 50 == 0)
                    await System.Threading.Tasks.Task.Yield();
            }
        }
    }
}
