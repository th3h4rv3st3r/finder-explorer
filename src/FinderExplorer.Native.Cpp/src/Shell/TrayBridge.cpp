// Copyright (c) Finder Explorer. All rights reserved.
// TrayBridge.cpp — System tray icon via Shell_NotifyIcon on a hidden message-only window.
// Fires a callback on a thread-pool thread when the user interacts with the tray icon.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <shellapi.h>
#include <atomic>
#include <thread>
#include "../../include/FinderExplorer.h"

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
static constexpr UINT WM_TRAY     = WM_APP + 1;
static constexpr UINT TRAY_ID     = 1;
static constexpr wchar_t kClass[] = L"FE_TrayHost";

// Tray action codes sent to the C# callback
static constexpr int TRAY_ACTION_LEFTCLICK  = 0; // single left-click / activate
static constexpr int TRAY_ACTION_DBLCLICK   = 1; // double-click
static constexpr int TRAY_ACTION_EXIT       = 2; // user chose Exit from context menu

// Context menu item IDs
static constexpr UINT MENU_OPEN = 1;
static constexpr UINT MENU_EXIT = 2;

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
static std::atomic<FE_TrayCallback> g_callback{ nullptr };
static std::atomic<HWND>            g_hwnd{ nullptr };
static std::atomic<bool>            g_running{ false };
static std::thread                  g_thread;

// ---------------------------------------------------------------------------
// Window procedure
// ---------------------------------------------------------------------------
static LRESULT CALLBACK TrayWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_TRAY)
    {
        auto cb  = g_callback.load(std::memory_order_acquire);
        UINT evt = LOWORD(lp);

        if (evt == WM_LBUTTONUP || evt == NIN_SELECT)
        {
            if (cb) std::thread([cb]{ cb(TRAY_ACTION_LEFTCLICK); }).detach();
        }
        else if (evt == WM_LBUTTONDBLCLK || evt == NIN_KEYSELECT)
        {
            if (cb) std::thread([cb]{ cb(TRAY_ACTION_DBLCLICK); }).detach();
        }
        else if (evt == WM_RBUTTONUP || evt == WM_CONTEXTMENU)
        {
            // Show context menu at cursor position
            POINT pt{};
            ::GetCursorPos(&pt);

            HMENU hMenu = ::CreatePopupMenu();
            ::InsertMenuW(hMenu, 0, MF_BYPOSITION | MF_STRING, MENU_OPEN, L"Abrir Finder Explorer");
            ::InsertMenuW(hMenu, 1, MF_BYPOSITION | MF_SEPARATOR, 0, nullptr);
            ::InsertMenuW(hMenu, 2, MF_BYPOSITION | MF_STRING, MENU_EXIT, L"Sair");
            ::SetMenuDefaultItem(hMenu, MENU_OPEN, FALSE);

            // Required to dismiss menu on click-away
            ::SetForegroundWindow(hwnd);
            UINT cmd = ::TrackPopupMenuEx(
                hMenu,
                TPM_RETURNCMD | TPM_NONOTIFY | TPM_LEFTALIGN | TPM_BOTTOMALIGN,
                pt.x, pt.y, hwnd, nullptr);
            ::DestroyMenu(hMenu);

            if (cmd == MENU_OPEN && cb)
                std::thread([cb]{ cb(TRAY_ACTION_LEFTCLICK); }).detach();
            else if (cmd == MENU_EXIT && cb)
                std::thread([cb]{ cb(TRAY_ACTION_EXIT); }).detach();
        }
        return 0;
    }
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

// ---------------------------------------------------------------------------
// Message-pump thread
// ---------------------------------------------------------------------------
static void TrayThread(HICON hIcon, const wchar_t* tooltip)
{
    HINSTANCE hInst = ::GetModuleHandleW(nullptr);

    WNDCLASSEXW wc  = {};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc  = TrayWndProc;
    wc.hInstance    = hInst;
    wc.lpszClassName = kClass;
    ::RegisterClassExW(&wc);

    HWND hwnd = ::CreateWindowExW(
        0, kClass, L"", WS_OVERLAPPED,
        0, 0, 0, 0, HWND_MESSAGE,
        nullptr, hInst, nullptr);

    // Set up NOTIFYICONDATA
    NOTIFYICONDATAW nid = {};
    nid.cbSize           = sizeof(nid);
    nid.hWnd             = hwnd;
    nid.uID              = TRAY_ID;
    nid.uFlags           = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_SHOWTIP;
    nid.uCallbackMessage = WM_TRAY;
    nid.hIcon            = hIcon;
    nid.uVersion         = NOTIFYICON_VERSION_4;
    ::wcsncpy_s(nid.szTip, tooltip, _TRUNCATE);
    ::Shell_NotifyIconW(NIM_ADD, &nid);
    ::Shell_NotifyIconW(NIM_SETVERSION, &nid);

    g_hwnd.store(hwnd, std::memory_order_release);

    MSG msg{};
    while (g_running.load(std::memory_order_acquire) && ::GetMessageW(&msg, hwnd, 0, 0) > 0)
    {
        ::TranslateMessage(&msg);
        ::DispatchMessageW(&msg);
    }

    // Remove icon before destroying window
    ::Shell_NotifyIconW(NIM_DELETE, &nid);
    ::DestroyWindow(hwnd);
    ::UnregisterClassW(kClass, hInst);
    g_hwnd.store(nullptr, std::memory_order_release);
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

FINDER_API void FE_Tray_Create(HWND ownerHwnd, const wchar_t* tooltip, FE_TrayCallback callback)
{
    if (g_running.load()) return;

    g_callback.store(callback, std::memory_order_release);
    g_running.store(true,  std::memory_order_release);

    // Extract icon from the main exe
    HICON hIcon = nullptr;
    wchar_t exePath[MAX_PATH] = {};
    if (::GetModuleFileNameW(nullptr, exePath, MAX_PATH))
        ::ExtractIconExW(exePath, 0, nullptr, &hIcon, 1);
    if (!hIcon) hIcon = ::LoadIconW(nullptr, IDI_APPLICATION);

    wchar_t tip[128] = L"Finder Explorer";
    if (tooltip && tooltip[0]) ::wcsncpy_s(tip, tooltip, _TRUNCATE);

    g_thread = std::thread(TrayThread, hIcon, tip);
}

FINDER_API void FE_Tray_Destroy()
{
    if (!g_running.load()) return;
    g_running.store(false, std::memory_order_release);

    HWND hw = g_hwnd.load(std::memory_order_acquire);
    if (hw) ::PostMessageW(hw, WM_QUIT, 0, 0);

    if (g_thread.joinable())
        g_thread.join();

    g_callback.store(nullptr, std::memory_order_release);
}

FINDER_API void FE_Tray_ShowBalloon(const wchar_t* title, const wchar_t* msg, UINT timeoutMs)
{
    HWND hw = g_hwnd.load(std::memory_order_acquire);
    if (!hw) return;

    NOTIFYICONDATAW nid = {};
    nid.cbSize    = sizeof(nid);
    nid.hWnd      = hw;
    nid.uID       = TRAY_ID;
    nid.uFlags    = NIF_INFO;
    nid.dwInfoFlags = NIIF_INFO | NIIF_RESPECT_QUIET_TIME;
    nid.uTimeout  = timeoutMs;
    ::wcsncpy_s(nid.szInfoTitle, title, _TRUNCATE);
    ::wcsncpy_s(nid.szInfo,      msg,   _TRUNCATE);
    ::Shell_NotifyIconW(NIM_MODIFY, &nid);
}
