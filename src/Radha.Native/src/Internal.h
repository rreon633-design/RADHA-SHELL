#pragma once
#include <windows.h>
#include <string>
#include <vector>

namespace radha {

// Shared state for the native bridge (single UI thread assumption).
struct BridgeState {
    HWND appBarWindow = nullptr;
    int  appBarEdge = 1;      // ABE_TOP
    int  appBarThickness = 42;
    HWND hotkeyWindow = nullptr;
    std::wstring pipeName = L"radha-shell";
    bool running = false;
};

BridgeState& State();

// Creates (once) the message-only window used for WM_HOTKEY delivery.
HWND EnsureHotkeyWindow();

} // namespace radha
