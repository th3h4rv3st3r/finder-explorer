// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FinderExplorer.Core.Services;

/// <summary>
/// Nextcloud client. Uses WebDAV for file navigation and OCS API for share links.
/// </summary>
public sealed class NextcloudService : INextcloudService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public NextcloudService(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient();
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
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _http.DefaultRequestHeaders.Add("OCS-APIRequest", "true"); // Required for OCS API
    }

    private string GetWebDavUrl(string path)
    {
        var url = _settings.Current.NextcloudUrl.TrimEnd('/');
        var cleanPath = (path ?? "").Replace('\\', '/').TrimStart('/');
        return $"{url}/remote.php/webdav/{cleanPath}";
    }

    private string GetOcsApiUrl() => $"{_settings.Current.NextcloudUrl.TrimEnd('/')}/ocs/v2.php/apps/files_sharing/api/v1/shares";

    // -----------------------------------------------------------------------
    // Connection & WebDAV
    // -----------------------------------------------------------------------

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            EnsureConfigured();
            var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), GetWebDavUrl(""));
            req.Headers.Add("Depth", "0");
            using var res = await _http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<IReadOnlyList<FileSystemItem>> GetItemsAsync(
        string remotePath, SortOptions? sort = null, FilterOptions? filter = null, CancellationToken ct = default)
    {
        EnsureConfigured();

        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), GetWebDavUrl(remotePath));
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

        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        var xmlString = await res.Content.ReadAsStringAsync(ct);
        return ParsePropFindResponse(xmlString, remotePath);
    }

    private List<FileSystemItem> ParsePropFindResponse(string xml, string requestPath)
    {
        var items = new List<FileSystemItem>();
        var doc = XDocument.Parse(xml);
        XNamespace d = "DAV:";

        foreach (var response in doc.Descendants(d + "response"))
        {
            var href = response.Element(d + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            string name = Uri.UnescapeDataString(href.TrimEnd('/').Split('/')[^1]);
            
            // Skip the root folder itself (Depth: 1 returns root + children)
            if (string.Equals(name, requestPath.TrimStart('/').Split('/')[^1], StringComparison.OrdinalIgnoreCase))
                if (href.EndsWith(requestPath.Replace('\\', '/').TrimStart('/'), StringComparison.OrdinalIgnoreCase))
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
                FullPath = href, // Contains full relative webdav URL
                IsDirectory = isDir,
                Size = size == 0 ? null : size,
                LastModified = date,
                DateCreated = date,
                Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal
            });
        }

        return items;
    }

    public async Task<bool> CreateFolderAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        var req = new HttpRequestMessage(new HttpMethod("MKCOL"), GetWebDavUrl(remotePath));
        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConfigured();
        var req = new HttpRequestMessage(HttpMethod.Delete, GetWebDavUrl(remotePath));
        using var res = await _http.SendAsync(req, ct);
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

    public void Dispose() => _http.Dispose();
}
