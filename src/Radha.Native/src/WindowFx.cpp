#include "../include/radha_native.h"
#include "Internal.h"
#include <dwmapi.h>

#pragma comment(lib, "dwmapi.lib")

extern "C" {

// Click-through + layered tool window for OSD / wallpaper layers.
RADHA_API BOOL RadhaSetClickThrough(HWND hwnd, BOOL enable) {
    if (!hwnd) return FALSE;
    LONG_PTR exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    if (enable) exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
    else        exStyle &= ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
    SetWindowLongPtrW(hwnd, GWL_EXSTYLE, exStyle);
    return TRUE;
}

// Legacy blur-behind (pre-22H2). Newer builds use DWMWA_SYSTEMBACKDROP_TYPE instead.
RADHA_API BOOL RadhaEnableBlurBehind(HWND hwnd, BOOL enable) {
    if (!hwnd) return FALSE;
    DWM_BLURBEHIND blur{};
    blur.dwFlags = DWM_BB_ENABLE;
    blur.fEnable = enable ? TRUE : FALSE;
    blur.hRgnBlur = nullptr;
    return SUCCEEDED(DwmEnableBlurBehindWindow(hwnd, &blur)) ? TRUE : FALSE;
}

RADHA_API BOOL RadhaSetMica(HWND hwnd, int backdropType) {
    if (!hwnd) return FALSE;
    // 38 = DWMWA_SYSTEMBACKDROP_TYPE; 3 = Mica, 2 = Acrylic, 1 = auto, 4 = tabbed
    int type = backdropType;
    HRESULT hr = DwmSetWindowAttribute(hwnd, 38, &type, sizeof(type));
    if (FAILED(hr) && backdropType == 3) {
        // Windows 10 fallback: solid blur behind.
        return RadhaEnableBlurBehind(hwnd, TRUE);
    }
    return SUCCEEDED(hr) ? TRUE : FALSE;
}

RADHA_API BOOL RadhaSetTopMost(HWND hwnd, BOOL topMost) {
    if (!hwnd) return FALSE;
    return SetWindowPos(hwnd,
                        topMost ? HWND_TOPMOST : HWND_NOTOPMOST,
                        0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE) ? TRUE : FALSE;
}

} // extern "C"
