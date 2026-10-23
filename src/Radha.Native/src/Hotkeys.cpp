#include "../include/radha_native.h"
#include "Internal.h"

namespace {

struct Binding {
    int id;
    const wchar_t* module;
    const wchar_t* command;
};

// Defaults mirror the Caelestia keybinds; the CLI can rebind later via config.
constexpr Binding kBindings[] = {
    {1, L"launcher",      L"toggle"},
    {2, L"dashboard",     L"toggle"},
    {3, L"controlcenter", L"toggle"},
    {4, L"notifications", L"toggle"},
    {5, L"wallpaper",     L"next"},
    {6, L"session",       L"menu"},
};

const Binding* FindBinding(int id) {
    for (const auto& binding : kBindings)
        if (binding.id == id) return &binding;
    return nullptr;
}

// Fire-and-forget: write one command line into the shell's named pipe.
// The pipe server (Radha.App) validates module/command against CommandCatalog,
// so a compromised hotkey cannot reach arbitrary code.
void SendToShell(const std::wstring& pipeName, const std::wstring& line) {
    const std::wstring path = L"\\\\.\\pipe\\" + pipeName;
    HANDLE pipe = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr,
                              OPEN_EXISTING, FILE_FLAG_WRITE_THROUGH, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return;

    DWORD written = 0;
    WriteFile(pipe, line.c_str(), static_cast<DWORD>(line.size() * sizeof(wchar_t)), &written, nullptr);
    CloseHandle(pipe);
}

LRESULT CALLBACK HotkeyWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message != WM_HOTKEY) return DefWindowProcW(hwnd, message, wParam, lParam);

    const auto* binding = FindBinding(static_cast<int>(wParam));
    if (binding == nullptr) return 0;

    std::wstring line = std::wstring(binding->module) + L" " + binding->command + L"\n";
    SendToShell(radha::State().pipeName, line);
    return 0;
}

} // namespace

namespace radha {

HWND EnsureHotkeyWindow() {
    auto& state = State();
    if (state.hotkeyWindow) return state.hotkeyWindow;

    const wchar_t* className = L"RadhaHotkeySink";
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(WNDCLASSEXW);
    wc.lpfnWndProc = HotkeyWndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = className;
    RegisterClassExW(&wc);

    // HWND_MESSAGE: never visible, never focusable, cannot steal input.
    state.hotkeyWindow = CreateWindowExW(0, className, L"RadhaHotkeySink", 0,
                                         0, 0, 0, 0, HWND_MESSAGE,
                                         nullptr, wc.hInstance, nullptr);
    return state.hotkeyWindow;
}

} // namespace radha

extern "C" {

RADHA_API BOOL RadhaHotkeyRegister(int id, UINT modifiers, UINT virtualKey) {
    HWND sink = radha::EnsureHotkeyWindow();
    if (!sink) return FALSE;
    // MOD_NOREPEAT keeps a held key from spamming the pipe.
    return RegisterHotKey(sink, id, modifiers | MOD_NOREPEAT, virtualKey) ? TRUE : FALSE;
}

RADHA_API BOOL RadhaHotkeyUnregister(int id) {
    HWND sink = radha::State().hotkeyWindow;
    return sink && UnregisterHotKey(sink, id) ? TRUE : FALSE;
}

RADHA_API void RadhaHotkeySetPipeEndpoint(LPCWSTR pipeName) {
    if (pipeName) radha::State().pipeName = pipeName;
}

} // extern "C"
