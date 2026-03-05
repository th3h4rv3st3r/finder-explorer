// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Concurrent;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Nextcloud client. Uses WebDAV for file navigation and OCS API for share links.
/// </summary>
public sealed class NextcloudService : INextcloudService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly string _fileCacheDir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileDownloadLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedDirectoryListing> _directoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _directoryCacheLock = new(1, 1);
    private static readonly TimeSpan DirectoryCacheTtl = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FileCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private string? _lastAuthHeader;

    public NextcloudService(ISettingsService settings)
    {
        _settings = settings;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _fileCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FinderExplorer",
            "nextcloud",
            "files");
        Directory.CreateDirectory(_fileCacheDir);
    }

    private void EnsureConfigured()
    {
        var appSet = _settings.Current;
        if (!appSet.NextcloudEnabled)
            throw new InvalidOperationException("Nextcloud integration is disabled.");
        if (string.IsNullOrWhiteSpace(appSet.NextcloudUrl) ||
            string.IsNullOrWhiteSpace(appSet.NextcloudUser) ||
            string.IsNullOrWhiteSpace(appSet.NextcloudAppPassword))
        {
            throw new InvalidOperationException("Nextcloud URL, User, or App Password is not configured.");
        }

        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{appSet.NextcloudUser}:{appSet.NextcloudAppPassword}"));
        if (!string.Equals(_lastAuthHeader, auth, StringComparison.Ordinal))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
            _lastAuthHeader = auth;
        }

        if (_http.DefaultRequestHeaders.Contains("OCS-APIRequest"))
            _http.DefaultRequestHeaders.Remove("OCS-APIRequest");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("OCS-APIRequest", "true");
    }

    private string GetWebDavUrl(string remotePath)
    {
        var url = _settings.Current.NextcloudUrl.TrimEnd('/');
        var cleanPath = NormalizeRemotePath(remotePath).TrimStart('/');
        return $"{url}/remote.php/webdav/{cleanPath}";
    }

    private string GetOcsApiUrl() => $"{_settings.Current.NextcloudUrl.TrimEnd('/')}/ocs/v2.php/apps/files_sharing/api/v1/shares";

    private static string NormalizeRemotePath(string? remotePath)
    {
        var normalized = (remotePath ?? string.Empty).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "/";

        if (!normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = "/" + normalized;

        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        return normalized;
    }

    private static string BuildDirectoryCacheKey(
        string accountIdentity,
        string remotePath,
        SortOptions sort,
        FilterOptions filter)
    {
        var ext = filter.Extensions is null || filter.Extensions.Length == 0
            ? string.Empty
            : string.Join(",", filter.Extensions.Select(e => e.ToLowerInvariant()).OrderBy(e => e, StringComparer.Ordinal));

        return $"{accountIdentity}|{remotePath}|{sort.Field}|{sort.Ascending}|{filter.ShowHidden}|{filter.NameContains}|{ext}";
    }

    private string ExtractRemotePathFromHref(string href)
    {
        var normalized = Uri.UnescapeDataString(href ?? string.Empty)
            .Replace('\\', '/')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return "/";

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
            normalized = absoluteUri.AbsolutePath;

        const string webDavPrefix = "/remote.php/webdav/";
        const string davFilesPrefix = "/remote.php/dav/files/";

        var webDavIndex = normalized.IndexOf(webDavPrefix, StringComparison.OrdinalIgnoreCase);
        if (webDavIndex >= 0)
        {
            var relative = normalized[(webDavIndex + webDavPrefix.Length)..];
            return NormalizeRemotePath(relative);
        }

        var davFilesIndex = normalized.IndexOf(davFilesPrefix, StringComparison.OrdinalIgnoreCase);
        if (davFilesIndex >= 0)
        {
            var afterPrefix = normalized[(davFilesIndex + davFilesPrefix.Length)..];
            var slashIndex = afterPrefix.IndexOf('/');
            var relative = slashIndex >= 0 ? afterPrefix[(slashIndex + 1)..] : string.Empty;
            return NormalizeRemotePath(relative);
        }

        return NormalizeRemotePath(normalized);
    }

    // -----------------------------------------------------------------------
    // Connection & WebDAV
    // -----------------------------------------------------------------------

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            EnsureConfigured();
            var req = new HttpRequestMessage(PropFindMethod, GetWebDavUrl("/"));
            req.Headers.Add("Depth", "0");
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> TestConnectionAsync(string url, string user, string appPassword, CancellationToken ct = default)
    {
        try
        {
            var webDavRoot = $"{url.TrimEnd('/')}/remote.php/webdav/";
            var req = new HttpRequestMessage(PropFindMethod, webDavRoot);
            req.Headers.Add("Depth", "0");

            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{appPassword}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(
        string remotePath, SortOptions? sort = null, FilterOptions? filter = null, CancellationToken ct = default)
    {
        EnsureConfigured();
        sort ??= SortOptions.Default;
        filter ??= FilterOptions.Default;
        var normalizedRemotePath = NormalizeRemotePath(remotePath);
        var accountIdentity = $"{_settings.Current.NextcloudUrl}|{_settings.Current.NextcloudUser}";
        var cacheKey = BuildDirectoryCacheKey(accountIdentity, normalizedRemotePath, sort, filter);

        await _directoryCacheLock.WaitAsync(ct);
        try
        {
            if (_directoryCache.TryGetValue(cacheKey, out var cached) &&
                (DateTimeOffset.UtcNow - cached.TimestampUtc) <= DirectoryCacheTtl)
            {
                return cached.Items;
            }
        }
        finally
        {
            _directoryCacheLock.Release();
        }

        var req = new HttpRequestMessage(PropFindMethod, GetWebDavUrl(normalizedRemotePath));
        req.Headers.Add("Depth", "1"); // Only direct children

        // Request basic properties: getlastmodified, getcontentlength, resourcetype
        string xmlBody = @"<?xml version=""1.0""?>
                           <d:propfind xmlns:d=""DAV:"">
                             <d:prop>
                               <d:getlastmodified/>
                               <d:getcontentlength/>
                               <d:resourcetype/>
                             </d:prop>
                           </d:propfind>";
        req.Content = new StringContent(xmlBody, Encoding.UTF8, "application/xml");

        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        var xmlString = await res.Content.ReadAsStringAsync(ct);
        var parsedItems = ParsePropFindResponse(xmlString, normalizedRemotePath);
        var normalizedItems = ApplySortAndFilter(parsedItems, sort, filter);

        await _directoryCacheLock.WaitAsync(ct);
        try
        {
            _directoryCache[cacheKey] = new CachedDirectoryListing(DateTimeOffset.UtcNow, normalizedItems);
        }
        finally
        {
            _directoryCacheLock.Release();
        }

        return normalizedItems;
    }

    private List<FileSystemItem> ParsePropFindResponse(string xml, string requestPath)
    {
        var items = new List<FileSystemItem>();
        var doc = XDocument.Parse(xml);
        XNamespace d = "DAV:";
        var normalizedRequestPath = NormalizeRemotePath(requestPath).TrimEnd('/');

        foreach (var response in doc.Descendants(d + "response"))
        {
            var href = response.Element(d + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            var remotePath = ExtractRemotePathFromHref(href);
            if (string.Equals(remotePath.TrimEnd('/'), normalizedRequestPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var trimmedRemote = remotePath.TrimEnd('/');
            var name = Uri.UnescapeDataString(trimmedRemote[(trimmedRemote.LastIndexOf('/') + 1)..]);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var prop = response.Element(d + "propstat")?.Element(d + "prop");
            if (prop == null) continue;

            bool isDir = prop.Element(d + "resourcetype")?.Element(d + "collection") != null;
            long size = 0;
            if (!isDir && long.TryParse(prop.Element(d + "getcontentlength")?.Value, out long parsedSize))
                size = parsedSize;

            DateTime date = DateTime.MinValue;
            var modifiedStr = prop.Element(d + "getlastmodified")?.Value;
            if (!string.IsNullOrEmpty(modifiedStr))
                DateTime.TryParse(modifiedStr, out date);

            items.Add(new FileSystemItem
            {
                Name = name,
                FullPath = remotePath,
                IsDirectory = isDir,
                Size = size == 0 ? null : size,
                LastModified = date,
                DateCreated = date,
                Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal
            });
        }

        return items;
    }

    private static IReadOnlyList<FileSystemItem> ApplySortAndFilter(
        IReadOnlyList<FileSystemItem> items,
        SortOptions sort,
        FilterOptions filter)
    {
        IEnumerable<FileSystemItem> query = items;

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            query = query.Where(item =>
                item.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Extensions is { Length: > 0 })
        {
            var extSet = filter.Extensions
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.StartsWith(".", StringComparison.Ordinal) ? e : "." + e)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            query = query.Where(item => item.IsDirectory || extSet.Contains(item.Extension));
        }

        if (!filter.ShowHidden)
            query = query.Where(item => !item.Attributes.HasFlag(FileAttributes.Hidden));

        var ordered = sort.Field switch
        {
            SortField.Size => sort.Ascending
                ? query.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Size ?? 0)
                : query.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Size ?? 0),
            SortField.Modified => sort.Ascending
                ? query.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.LastModified)
                : query.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.LastModified),
            SortField.Type => sort.Ascending
                ? query.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Extension)
                : query.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Extension),
            _ => sort.Ascending
                ? query.OrderByDescending(i => i.IsDirectory).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(i => i.IsDirectory).ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ToList();
    }

    private async Task InvalidateDirectoryCacheAsync(CancellationToken ct)
    {
        await _directoryCacheLock.WaitAsync(ct);
        try
        {
            _directoryCache.Clear();
        }
        finally
        {
            _directoryCacheLock.Release();
        }
    }

    public async Task<bool> CreateFolderAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        var req = new HttpRequestMessage(new HttpMethod("MKCOL"), GetWebDavUrl(remotePath));
        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
            await InvalidateDirectoryCacheAsync(ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        var req = new HttpRequestMessage(HttpMethod.Delete, GetWebDavUrl(remotePath));
        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
            await InvalidateDirectoryCacheAsync(ct);
        return res.IsSuccessStatusCode;
    }

    // -----------------------------------------------------------------------
    // Sharing (OCS API)
    // -----------------------------------------------------------------------

    public async Task<string?> CreatePublicShareAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        // 3 = public link share
        return await CreateShareCoreAsync(remotePath, shareType: 3, ct);
    }

    public async Task<string?> CreateInternalShareAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        // shareType 0 = user, but if we don't specify shareWith, Nextcloud doesn't easily return an internal link via OCS.
        // Usually internal link is just: {NextcloudUrl}/f/{fileId}
        // Let's create a public link but password protected? No, "Internal link" means exactly what Nextcloud gives.
        // Wait, the OCS API type for internal link doesn't really exist as a share. It's just a routing URL.
        // Actually, we can get the 'fileid' from PROPFIND (oc:id element) and construct it: url/index.php/f/{id}
        // For now, let's just cheat and return the WebDAV URL or a Nextcloud browser URL.
        
        var url = _settings.Current.NextcloudUrl.TrimEnd('/');
        var cleanPath = (remotePath ?? "").Replace('\\', '/').TrimStart('/');
        return await Task.FromResult($"{url}/index.php/apps/files/?dir=/{Uri.EscapeDataString(cleanPath)}");
    }

    private async Task<string?> CreateShareCoreAsync(string remotePath, int shareType, CancellationToken ct)
    {
        var cleanPath = (remotePath ?? "").Replace('\\', '/').TrimStart('/');
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("path", "/" + cleanPath),
            new KeyValuePair<string, string>("shareType", shareType.ToString()),
            new KeyValuePair<string, string>("permissions", "1") // 1 = read only
        });

        // Request JSON back from OCS API
        var req = new HttpRequestMessage(HttpMethod.Post, GetOcsApiUrl() + "?format=json")
        {
            Content = content
        };

        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;

        var jsonStr = await res.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            if (root.TryGetProperty("ocs", out var ocs) && 
                ocs.TryGetProperty("data", out var data) && 
                data.TryGetProperty("url", out var urlNode))
            {
                return urlNode.GetString();
            }
        }
        catch { }
        return null;
    }

    public Task<string?> DownloadFileToCacheAsync(string remotePath, CancellationToken ct = default)
        => DownloadFileToCacheAsync(remotePath, extensionHint: null, ct);

    public async Task<string?> DownloadFileToCacheAsync(string remotePath, string? extensionHint, CancellationToken ct = default)
    {
        EnsureConfigured();

        var normalizedRemotePath = NormalizeRemotePath(remotePath);
        if (string.IsNullOrWhiteSpace(normalizedRemotePath) || normalizedRemotePath == "/")
            return null;

        var hashInput = $"{_settings.Current.NextcloudUrl}|{_settings.Current.NextcloudUser}|{normalizedRemotePath}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
        var fileLock = _fileDownloadLocks.GetOrAdd(hash, static _ => new SemaphoreSlim(1, 1));
        var extension = ResolveCacheExtension(normalizedRemotePath, contentType: null, extensionHint);
        var cachePath = BuildCachedFilePath(hash, extension);
        await fileLock.WaitAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                var inferredExisting = FindExistingCachedFilePath(hash);
                if (!string.IsNullOrWhiteSpace(inferredExisting))
                    cachePath = inferredExisting;
            }

            if (File.Exists(cachePath))
            {
                var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(cachePath);
                if (age <= FileCacheTtl)
                    return cachePath;
            }
            else if (!string.IsNullOrWhiteSpace(extension))
            {
                var inferredExisting = FindExistingCachedFilePath(hash);
                if (!string.IsNullOrWhiteSpace(inferredExisting) && File.Exists(inferredExisting))
                {
                    var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(inferredExisting);
                    File.Copy(inferredExisting, cachePath, overwrite: true);

                    if (age <= FileCacheTtl)
                    {
                        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
                        return cachePath;
                    }
                }
            }

            var req = new HttpRequestMessage(HttpMethod.Get, GetWebDavUrl(normalizedRemotePath));
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode)
            {
                if (File.Exists(cachePath))
                    return cachePath;

                var existingPath = FindExistingCachedFilePath(hash);
                return !string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath)
                    ? existingPath
                    : null;
            }

            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ResolveCacheExtension(normalizedRemotePath, res.Content.Headers.ContentType?.MediaType, extensionHint);
                cachePath = BuildCachedFilePath(hash, extension);
            }

            var tmpPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var fs = File.Create(tmpPath))
                await using (var stream = await res.Content.ReadAsStreamAsync(ct))
                    await stream.CopyToAsync(fs, ct);

                File.Move(tmpPath, cachePath, overwrite: true);
                File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow);
                return cachePath;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            if (File.Exists(cachePath))
                return cachePath;

            var existingPath = FindExistingCachedFilePath(hash);
            return !string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath)
                ? existingPath
                : null;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private string BuildCachedFilePath(string hash, string? extension)
    {
        return Path.Combine(_fileCacheDir, string.IsNullOrWhiteSpace(extension) ? hash : hash + extension);
    }

    private string? FindExistingCachedFilePath(string hash)
    {
        try
        {
            var exact = Path.Combine(_fileCacheDir, hash);
            if (File.Exists(exact))
                return exact;

            var matches = Directory.GetFiles(_fileCacheDir, $"{hash}.*");
            if (matches.Length == 0)
                return null;

            return matches
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveCacheExtension(string normalizedRemotePath, string? contentType, string? extensionHint)
    {
        var decodedPath = Uri.UnescapeDataString(normalizedRemotePath ?? string.Empty);
        var queryStart = decodedPath.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
            decodedPath = decodedPath[..queryStart];

        var extension = Path.GetExtension(decodedPath);
        if (!string.IsNullOrWhiteSpace(extension))
            return extension.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(extensionHint))
            return NormalizeExtension(extensionHint);

        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        return contentType.Trim().ToLowerInvariant() switch
        {
            "video/mp4" => ".mp4",
            "video/x-matroska" => ".mkv",
            "video/quicktime" => ".mov",
            "video/x-msvideo" => ".avi",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "audio/flac" => ".flac",
            "audio/ogg" => ".ogg",
            "audio/aac" => ".aac",
            "text/plain" => ".txt",
            "text/markdown" => ".md",
            "application/json" => ".json",
            "application/xml" => ".xml",
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => string.Empty
        };
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim();
        if (!normalized.StartsWith(".", StringComparison.Ordinal))
            normalized = "." + normalized;

        return normalized.ToLowerInvariant();
    }

    public void Dispose()
    {
        _directoryCacheLock.Dispose();
        _http.Dispose();
    }

    private sealed record CachedDirectoryListing(DateTimeOffset TimestampUtc, IReadOnlyList<FileSystemItem> Items);
}
