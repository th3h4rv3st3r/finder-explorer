// Copyright (c) Finder Explorer. All rights reserved.
// NanaZipBridge.cpp — NanaZip (7z) CLI wrapper with progress piping.
// Detects NanaZip path via registry, spawns 7z.exe with CreateProcess,
// reads stdout for progress parsing, fires callback per update.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <vector>
#include "../../include/FinderExplorer.h"

// ---------------------------------------------------------------------------
// Helper: locate 7z.exe shipped with NanaZip
// ---------------------------------------------------------------------------
namespace
{
    static bool s_pathResolved = false;
    static wchar_t s_sevenZipPath[MAX_PATH] = {};

    const wchar_t* GetSevenZipPath()
    {
        if (s_pathResolved) return s_sevenZipPath[0] ? s_sevenZipPath : nullptr;
        s_pathResolved = true;

        // 1. Registry: HKLM\SOFTWARE\NanaZip → InstallLocation
        {
            wchar_t buf[MAX_PATH] = {};
            DWORD   cb            = sizeof(buf);
            if (::RegGetValueW(
                    HKEY_LOCAL_MACHINE,
                    L"SOFTWARE\\NanaZip",
                    L"InstallLocation",
                    RRF_RT_REG_SZ, nullptr,
                    buf, &cb) == ERROR_SUCCESS)
            {
                ::_snwprintf_s(s_sevenZipPath, _countof(s_sevenZipPath), _TRUNCATE, L"%s\\7z.exe", buf);
                if (::GetFileAttributesW(s_sevenZipPath) != INVALID_FILE_ATTRIBUTES)
                    return s_sevenZipPath;
            }
        }

        // 2. HKCU variant (user-install)
        {
            wchar_t buf[MAX_PATH] = {};
            DWORD   cb            = sizeof(buf);
            if (::RegGetValueW(
                    HKEY_CURRENT_USER,
                    L"SOFTWARE\\NanaZip",
                    L"InstallLocation",
                    RRF_RT_REG_SZ, nullptr,
                    buf, &cb) == ERROR_SUCCESS)
            {
                ::_snwprintf_s(s_sevenZipPath, _countof(s_sevenZipPath), _TRUNCATE, L"%s\\7z.exe", buf);
                if (::GetFileAttributesW(s_sevenZipPath) != INVALID_FILE_ATTRIBUTES)
                    return s_sevenZipPath;
            }
        }

        // 3. Known default paths
        static const wchar_t* kPaths[] = {
            L"%ProgramFiles%\\NanaZip\\7z.exe",
            L"%LocalAppData%\\Programs\\NanaZip\\7z.exe",
        };
        for (auto* tpl : kPaths)
        {
            if (::ExpandEnvironmentStringsW(tpl, s_sevenZipPath, MAX_PATH) > 0)
                if (::GetFileAttributesW(s_sevenZipPath) != INVALID_FILE_ATTRIBUTES)
                    return s_sevenZipPath;
        }

        s_sevenZipPath[0] = L'\0';
        return nullptr;
    }

    // Run 7z.exe, capture stdout, optionally parse progress lines.
    // Returns exit code, or -1 if launch failed.
    // Progress lines look like: "  6% - filename" or just percentage values on the line.
    int RunSevenZip(
        const wchar_t*      cmdArgs,   // everything after "7z.exe "
        FE_ProgressCallback cb,
        std::wstring*       outCapture) // if non-null, captures all stdout text
    {
        const wchar_t* exe = GetSevenZipPath();
        if (!exe) return -2; // NanaZip not found

        // Build full command line
        wchar_t cmdLine[4096];
        ::_snwprintf_s(cmdLine, _countof(cmdLine), _TRUNCATE, L"\"%s\" %s", exe, cmdArgs);

        // Create anonymous pipe for stdout
        HANDLE hReadPipe  = nullptr;
        HANDLE hWritePipe = nullptr;
        SECURITY_ATTRIBUTES sa = {};
        sa.nLength              = sizeof(sa);
        sa.bInheritHandle       = TRUE;
        if (!::CreatePipe(&hReadPipe, &hWritePipe, &sa, 0))
            return -1;
        ::SetHandleInformation(hReadPipe, HANDLE_FLAG_INHERIT, 0); // read end not inherited

        STARTUPINFOW si   = {};
        si.cb             = sizeof(si);
        si.dwFlags        = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
        si.wShowWindow    = SW_HIDE;
        si.hStdOutput     = hWritePipe;
        si.hStdError      = hWritePipe;

        PROCESS_INFORMATION pi = {};
        if (!::CreateProcessW(
                nullptr, cmdLine,
                nullptr, nullptr,
                TRUE,  // inherit handles (pipe write end)
                CREATE_NO_WINDOW,
                nullptr, nullptr,
                &si, &pi))
        {
            ::CloseHandle(hReadPipe);
            ::CloseHandle(hWritePipe);
            return -1;
        }

        // Close write end in parent — so ReadFile returns when process exits
        ::CloseHandle(hWritePipe);

        // Read stdout
        char  buf[2048];
        DWORD read = 0;
        std::string accum;

        while (::ReadFile(hReadPipe, buf, sizeof(buf) - 1, &read, nullptr) && read > 0)
        {
            buf[read] = '\0';
            accum += buf;

            if (outCapture)
            {
                // Convert narrow to wide
                int wLen = ::MultiByteToWideChar(CP_ACP, 0, buf, static_cast<int>(read), nullptr, 0);
                if (wLen > 0)
                {
                    std::vector<wchar_t> wBuf(wLen + 1, 0);
                    ::MultiByteToWideChar(CP_ACP, 0, buf, static_cast<int>(read), wBuf.data(), wLen);
                    *outCapture += wBuf.data();
                }
            }

            // Parse progress: 7z prints lines like "  7% - filename"
            if (cb)
            {
                size_t pos = 0;
                while (pos < accum.size())
                {
                    size_t eol = accum.find('\n', pos);
                    size_t end = (eol == std::string::npos) ? accum.size() : eol + 1;
                    std::string line = accum.substr(pos, end - pos);

                    int pct = -1;
                    if (::sscanf_s(line.c_str(), " %d%%", &pct) == 1 && pct >= 0 && pct <= 100)
                        cb(pct / 100.0);

                    pos = end;
                }
                // Keep any incomplete line (no newline yet)
                size_t lastNL = accum.rfind('\n');
                if (lastNL != std::string::npos)
                    accum = accum.substr(lastNL + 1);
            }
        }

        ::CloseHandle(hReadPipe);
        ::WaitForSingleObject(pi.hProcess, INFINITE);

        DWORD exitCode = 1;
        ::GetExitCodeProcess(pi.hProcess, &exitCode);
        ::CloseHandle(pi.hProcess);
        ::CloseHandle(pi.hThread);

        return static_cast<int>(exitCode);
    }
} // namespace

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

FINDER_API int FE_NanaZip_List(
    const wchar_t* archivePath,
    wchar_t*       outJson,
    int            bufLen)
{
    // 7z l -slt <archive> — structured list with technical info
    wchar_t args[MAX_PATH + 64];
    ::_snwprintf_s(args, _countof(args), _TRUNCATE, L"l -slt \"%s\"", archivePath);

    std::wstring capture;
    int exitCode = RunSevenZip(args, nullptr, &capture);
    if (exitCode != 0) return -1;

    // Parse output: entries start with "Path = ..." blocks
    // Build a minimal JSON array: [{"path":"...","size":...,"isDir":...}, ...]
    std::wstring json = L"[";
    bool first = true;

    std::wstring path;
    long long    size    = -1;
    bool         isDir   = false;
    bool         inBlock = false;

    auto flush = [&]()
    {
        if (!path.empty())
        {
            if (!first) json += L",";
            first = false;

            // Escape backslashes and quotes in path
            std::wstring escaped;
            for (auto c : path)
            {
                if (c == L'\\') escaped += L"\\\\";
                else if (c == L'"') escaped += L"\\\"";
                else escaped += c;
            }
            wchar_t entry[512];
            ::_snwprintf_s(entry, _countof(entry), _TRUNCATE,
                L"{\"path\":\"%s\",\"size\":%lld,\"isDir\":%s}",
                escaped.c_str(), size, isDir ? L"true" : L"false");
            json += entry;
        }
        path.clear();
        size  = -1;
        isDir = false;
    };

    size_t pos = 0;
    while (pos < capture.size())
    {
        size_t eol  = capture.find(L'\n', pos);
        size_t end  = (eol == std::wstring::npos) ? capture.size() : eol + 1;
        std::wstring line = capture.substr(pos, end - pos);
        // Trim trailing \r\n
        while (!line.empty() && (line.back() == L'\r' || line.back() == L'\n'))
            line.pop_back();

        if (line.rfind(L"Path = ", 0) == 0)
        {
            if (inBlock) flush();
            path    = line.substr(7);
            inBlock = true;
        }
        else if (inBlock && line.rfind(L"Size = ", 0) == 0)
            size = ::_wtoi64(line.substr(7).c_str());
        else if (inBlock && line.rfind(L"Attributes = ", 0) == 0)
            isDir = (line.find(L'D') != std::wstring::npos);
        else if (inBlock && line.empty())
            flush();

        pos = end;
    }
    if (inBlock) flush();
    json += L"]";

    // Copy to caller buffer
    ::wcsncpy_s(outJson, bufLen, json.c_str(), _TRUNCATE);
    return 0;
}

FINDER_API int FE_NanaZip_Extract(
    const wchar_t*      archivePath,
    const wchar_t*      dest,
    FE_ProgressCallback cb)
{
    wchar_t args[MAX_PATH * 2 + 64];
    ::_snwprintf_s(args, _countof(args), _TRUNCATE,
        L"x -y -bsp1 -o\"%s\" \"%s\"", dest, archivePath);

    return RunSevenZip(args, cb, nullptr);
}

FINDER_API int FE_NanaZip_Compress(
    const wchar_t**     sources,
    int                 count,
    const wchar_t*      destArchive,
    FE_ProgressCallback cb)
{
    // Build: a -y -bsp1 "dest" "src1" "src2" ...
    std::wstring args = L"a -y -bsp1 \"";
    args += destArchive;
    args += L"\"";
    for (int i = 0; i < count; ++i)
    {
        args += L" \"";
        args += sources[i];
        args += L"\"";
    }

    return RunSevenZip(args.c_str(), cb, nullptr);
}
