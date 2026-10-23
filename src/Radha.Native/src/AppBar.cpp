#include "../include/radha_native.h"
#include "Internal.h"
#include <shellapi.h>

namespace {

APPBARDATA MakeAppBarData(HWND hwnd, int edge, int thickness, int width) {
    APPBARDATA data{};
    data.cbSize = sizeof(APPBARDATA);
    data.hWnd = hwnd;
    data.uEdge = static_cast<UINT>(edge);
    data.rc = RECT{0, 0, width, thickness};
    return data;
}

} // namespace

extern "C" {

// Reserve a strip of the work area so maximised windows stop below the bar.
RADHA_API BOOL RadhaAppBarRegister(HWND hwnd, int edge, int thicknessPx, int screenWidthPx) {
    if (!hwnd || thicknessPx <= 0) return FALSE;

    auto& state = radha::State();
    state.appBarWindow = hwnd;
    state.appBarEdge = edge;
    state.appBarThickness = thicknessPx;

    auto data = MakeAppBarData(hwnd, edge, thicknessPx, screenWidthPx);
    SHAppBarMessage(ABM_NEW, &data);
    return SHAppBarMessage(ABM_SETPOS, &data) ? TRUE : FALSE;
}

RADHA_API BOOL RadhaAppBarSetPosition(HWND hwnd, int edge, int thicknessPx, int screenWidthPx) {
    if (!hwnd) return FALSE;
    auto data = MakeAppBarData(hwnd, edge, thicknessPx, screenWidthPx);
    if (!SHAppBarMessage(ABM_SETPOS, &data)) return FALSE;

    // The shell owns the rect; move the window to match.
    SetWindowPos(hwnd, nullptr,
                 data.rc.left, data.rc.top,
                 data.rc.right - data.rc.left, data.rc.bottom - data.rc.top,
                 SWP_NOACTIVATE | SWP_NOZORDER);
    return TRUE;
}

RADHA_API BOOL RadhaAppBarUnregister(HWND hwnd) {
    if (!hwnd) return FALSE;
    APPBARDATA data{};
    data.cbSize = sizeof(APPBARDATA);
    data.hWnd = hwnd;
    SHAppBarMessage(ABM_REMOVE, &data);

    auto& state = radha::State();
    if (state.appBarWindow == hwnd) state.appBarWindow = nullptr;
    return TRUE;
}

} // extern "C"
