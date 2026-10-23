// RADHA SHELL native bridge — stable C ABI consumed by C# through P/Invoke.
#pragma once

#include <windows.h>

#ifdef RADHANATIVE_EXPORTS
#define RADHA_API extern "C" __declspec(dllexport)
#else
#define RADHA_API extern "C" __declspec(dllimport)
#endif

// ---- application desktop toolbar (bar docking) ----
RADHA_API BOOL RadhaAppBarRegister(HWND hwnd, int edge, int thicknessPx, int screenWidthPx);
RADHA_API BOOL RadhaAppBarSetPosition(HWND hwnd, int edge, int thicknessPx, int screenWidthPx);
RADHA_API BOOL RadhaAppBarUnregister(HWND hwnd);

// ---- window chrome / effects ----
RADHA_API BOOL RadhaSetClickThrough(HWND hwnd, BOOL enable);
RADHA_API BOOL RadhaEnableBlurBehind(HWND hwnd, BOOL enable);
RADHA_API BOOL RadhaSetMica(HWND hwnd, int backdropType); // 1 = auto, 2 = none, 3 = mica
RADHA_API BOOL RadhaSetTopMost(HWND hwnd, BOOL topMost);

// ---- global hotkeys ----
// The bridge owns a message-only window; callbacks fire on its thread and post
// "radha <module> <command>" into the named pipe, so the UI never blocks on input.
RADHA_API BOOL  RadhaHotkeyRegister(int id, UINT modifiers, UINT virtualKey);
RADHA_API BOOL  RadhaHotkeyUnregister(int id);
RADHA_API void  RadhaHotkeySetPipeEndpoint(LPCWSTR pipeName);

// ---- virtual desktops ----
// Real switching through the shell's IVirtualDesktopManagerInternal, version-dispatched per
// Windows build (see src/Workspaces.cpp). Everything reports "unsupported" rather than guessing
// when the running build has no verified layout.
RADHA_API int  RadhaWorkspaceBuild();        // OS build number (e.g. 26100), 0 if unknown
RADHA_API int  RadhaWorkspaceBackend();      // 0 = unsupported/unavailable, 1 = internal COM live
RADHA_API int  RadhaWorkspaceCount();        // desktop count, 0 = unsupported
RADHA_API int  RadhaWorkspaceCurrent();      // 0-based current desktop, -1 = unsupported
RADHA_API BOOL RadhaWorkspaceSwitch(int index);
RADHA_API BOOL RadhaWorkspaceMoveActiveWindow(int index); // moves the foreground window
