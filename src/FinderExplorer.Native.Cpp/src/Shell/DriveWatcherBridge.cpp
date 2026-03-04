// Copyright (c) Finder Explorer. All rights reserved.
// DriveWatcherBridge.cpp — WM_DEVICECHANGE hidden-window USB/drive hotplug detection.
// Runs a dedicated STA message-pump thread; fires caller callback on thread pool.

#define FINDEREXPLORER_EXPORTS
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#include <windows.h>
#include <dbt.h>
#include <atomic>
#include <thread>
#include "../../include/FinderExplorer.h"

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------
static std::atomic<FE_DriveCallback> g_driveCallback{ nullptr };
static std::atomic<HWND>             g_hwnd{ nullptr };
static std::thread                   g_thread;
static std::atomic<bool>             g_running{ false };

static constexpr wchar_t kWndClass[] = L"FE_DriveWatcher";

// ---------------------------------------------------------------------------
// Window procedure — WM_DEVICECHANGE handler
// ---------------------------------------------------------------------------
static LRESULT CALLBACK DriveWatcherWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_DEVICECHANGE)
    {
        if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE)
        {
            auto* hdr = reinterpret_cast<DEV_BROADCAST_HDR*>(lp);
            if (hdr && hdr->dbch_devicetype == DBT_DEVTYP_VOLUME)
            {
                auto* vol = reinterpret_cast<DEV_BROADCAST_VOLUME*>(lp);
                bool arrived = (wp == DBT_DEVICEARRIVAL);

                // Resolve a drive letter from the unit mask
                DWORD mask = vol->dbcv_unitmask;
                wchar_t driveLetter[4] = { L'A', L':', L'\\', L'\0' };
                for (int bit = 0; bit < 26; ++bit)
                {
                    if (mask & (1u << bit))
                    {
                        driveLetter[0] = static_cast<wchar_t>(L'A' + bit);

                        auto cb = g_driveCallback.load(std::memory_order_acquire);
                        if (cb)
                        {
                            // Fire on a thread-pool thread to avoid blocking the msg pump
                            bool arr2 = arrived;
                            wchar_t dl[4];
                            ::wcscpy_s(dl, driveLetter);
                            std::thread([cb, arr2, dl]()
                            {
                                cb(arr2, dl);
                            }).detach();
                        }
                    }
                }
            }
        }
    }
    return ::DefWindowProcW(hwnd, msg, wp, lp);
}

// ---------------------------------------------------------------------------
// Message-pump thread
// ---------------------------------------------------------------------------
static void WatcherThread()
{
    HINSTANCE hInst = ::GetModuleHandleW(nullptr);

    WNDCLASSEXW wc   = {};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc  = DriveWatcherWndProc;
    wc.hInstance    = hInst;
    wc.lpszClassName = kWndClass;
    ::RegisterClassExW(&wc);

    HWND hwnd = ::CreateWindowExW(
        0, kWndClass, L"",
        WS_OVERLAPPED, 0, 0, 0, 0,
        HWND_MESSAGE, // message-only window — no taskbar entry, no visibility
        nullptr, hInst, nullptr);

    g_hwnd.store(hwnd, std::memory_order_release);

    MSG msg{};
    while (g_running.load(std::memory_order_acquire) && ::GetMessageW(&msg, hwnd, 0, 0) > 0)
    {
        ::TranslateMessage(&msg);
        ::DispatchMessageW(&msg);
    }

    ::DestroyWindow(hwnd);
    ::UnregisterClassW(kWndClass, hInst);
    g_hwnd.store(nullptr, std::memory_order_release);
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------
FINDER_API void FE_DriveWatcher_Start(FE_DriveCallback callback)
{
    if (g_running.load()) return; // already running

    g_driveCallback.store(callback, std::memory_order_release);
    g_running.store(true, std::memory_order_release);
    g_thread = std::thread(WatcherThread);
}

FINDER_API void FE_DriveWatcher_Stop()
{
    if (!g_running.load()) return;

    g_running.store(false, std::memory_order_release);

    // Post WM_QUIT to unblock GetMessage on the watcher thread
    HWND hw = g_hwnd.load(std::memory_order_acquire);
    if (hw) ::PostMessageW(hw, WM_QUIT, 0, 0);

    if (g_thread.joinable())
        g_thread.join();

    g_driveCallback.store(nullptr, std::memory_order_release);
}
