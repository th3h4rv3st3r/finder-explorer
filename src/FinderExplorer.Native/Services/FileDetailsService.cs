// Copyright (c) Finder Explorer. All rights reserved.

using FinderExplorer.Core.Services;
using FinderExplorer.Native.Bridge;
using System.Threading;
using System.Threading.Tasks;

namespace FinderExplorer.Native.Services;

/// <summary>
/// Implementation of <see cref="IFileDetailsService"/> backed by C++ IShellItem2::GetPropertyStore.
/// </summary>
public sealed class FileDetailsService : IFileDetailsService
{
    public Task<FileDetails?> GetDetailsAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            int len = 256;
            string type = new string('\0', len);
            string dims = new string('\0', len);
            string date = new string('\0', len);
            string auth = new string('\0', len);

            // We must use unsafe/fixed or pass char arrays. C# string ref is tricky with LibraryImport.
            // Let's use char arrays for buffers that the native code will fill.
            char[] tBuf = new char[len];
            char[] dBuf = new char[len];
            char[] dtBuf = new char[len];
            char[] aBuf = new char[len];

            int ok = NativeBridge.Property_GetDetails(path, 
                ref tBuf[0], len,
                ref dBuf[0], len,
                ref dtBuf[0], len,
                ref aBuf[0], len);

            if (ok == 0) return null;

            return new FileDetails(
                Type: GetString(tBuf),
                Dimensions: GetString(dBuf),
                DateTaken: GetString(dtBuf),
                Authors: GetString(aBuf));
        }, ct);
    }

    private static string GetString(char[] buffer)
    {
        int nullIdx = System.Array.IndexOf(buffer, '\0');
        return nullIdx > 0 ? new string(buffer, 0, nullIdx) : string.Empty;
    }
}
