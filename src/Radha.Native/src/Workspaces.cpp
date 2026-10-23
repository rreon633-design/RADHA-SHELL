#include "../include/radha_native.h"
#include "Internal.h"

#include <objbase.h>
#include <winternl.h>

#include <cstring>
#include <mutex>

// ---------------------------------------------------------------------------------------------
// Virtual desktop control.
//
// Windows exposes two things here:
//
//   * IVirtualDesktopManager (DOCUMENTED, GUID stable since Win10): can answer "which desktop owns
//     this window" and can MOVE a window to a desktop given that desktop's GUID. It cannot
//     enumerate desktops and cannot switch between them.
//
//   * IVirtualDesktopManagerInternal (UNDOCUMENTED): full control — count, current, switch, and the
//     desktop GUIDs needed to move windows. It lives inside the shell's ImmersiveShell service
//     provider, and its IID *and* vtable layout change between Windows releases.
//
// So this file does not hard-code one interface. It carries a table of layouts, each pinned to the
// Windows builds it was verified on, and at runtime:
//
//   1. picks the layout whose build range matches,
//   2. asks the service provider for that exact IID (a wrong IID fails cleanly with
//      E_NOINTERFACE — it is the IID that identifies the layout, so a successful QI means the
//      slot numbers and signatures next to it are the right ones),
//   3. validates the result by calling GetCount and requiring 1..256 desktops.
//
// If any step fails, every entry point reports "unsupported" — it never guesses a slot number,
// because calling the wrong vtable slot can fault inside the shell.
//
// Window moves go through the *documented* IVirtualDesktopManager::MoveWindowToDesktop, using a
// desktop GUID obtained from the internal interface. Switching has no documented equivalent, so it
// uses the internal SwitchDesktop.
//
// Layouts verified against:
//   26100/26200 (Win11 24H2/25H2) — Ciantic/VirtualDesktopAccessor + superj8888/VirtualDesktopTaskbar
//   22621–22635 (Win11 22H2/23H2) — mzomparelli/zVirtualDesktop wiki, mntone/VirtualDesktop
//   10240–19045 (Win10 1507–22H2) — Ciantic/VirtualDesktopAccessor (Win10Desktops.h)
// ---------------------------------------------------------------------------------------------

namespace {

// ---- GUIDs -----------------------------------------------------------------------------------

// CLSID of the shell's ImmersiveShell service provider (stable across all NT 10 builds).
const CLSID kClsidImmersiveShell =
    {0xC2F03A33, 0x21F5, 0x47FA, {0xB4, 0xBB, 0x15, 0x63, 0x62, 0xA2, 0xF2, 0x39}};

// IID we activate ImmersiveShell as: the documented IServiceProvider.
const IID kIidServiceProvider =
    {0x6D5140C1, 0x7436, 0x11CE, {0x80, 0x34, 0x00, 0xAA, 0x00, 0x60, 0x09, 0xFA}};

// QueryService SID: "CLSID_VirtualDesktopManagerInternal". Stable; the *interface* IID is not.
const GUID kSidVirtualDesktopManagerInternal =
    {0xC5E0CDCA, 0x7B6E, 0x41B2, {0x9F, 0xC4, 0xD9, 0x39, 0x75, 0xCC, 0x46, 0x7B}};

// IVirtualDesktop — accepted by IObjectArray::GetAt. IID_IUnknown is rejected with E_NOINTERFACE.
const IID kIidVirtualDesktop =
    {0x3F07F4BE, 0xB107, 0x441A, {0xAF, 0x0F, 0x39, 0xD8, 0x25, 0x29, 0x07, 0x2C}};

// Documented IVirtualDesktopManager (CoCreateInstance target is a *different* GUID below).
const IID kIidVirtualDesktopManager =
    {0xA5CD92FF, 0x29BE, 0x454C, {0x8D, 0x04, 0xD8, 0x28, 0x79, 0xFB, 0x3F, 0x1B}};
const CLSID kClsidVirtualDesktopManager =
    {0xAA509086, 0x5CA9, 0x4C25, {0x8F, 0x95, 0x58, 0x9D, 0x3C, 0x07, 0xB4, 0x8A}};

// ---- layout table ----------------------------------------------------------------------------

struct Layout {
    int minBuild;
    int maxBuild;
    IID iid;              // IID of IVirtualDesktopManagerInternal for this layout
    int slotCount;        // absolute vtable slot (IUnknown occupies 0..2)
    int slotCurrent;
    int slotAll;          // returns IObjectArray of desktops
    int slotSwitch;
    bool takesHwnd;       // Win11 22H2/23H2 pass HWND/HMONITOR as the first argument
};

const Layout kLayouts[] = {
    // 10240 (1507) … 19045 (Win10 22H2)
    {10240, 19045, {0xF31574D6, 0xB682, 0x4CDC, {0xBD, 0x56, 0x18, 0x27, 0x86, 0x0A, 0xBE, 0xC6}},
     3, 6, 7, 9, false},
    // 22000 (Win11 21H2) … 22635 (23H2): GetAllCurrentDesktops was inserted at slot 7, so the
    // desktop array moved to 8 and SwitchDesktop to 10; every call gained an HWND/HMONITOR first arg.
    {22000, 22635, {0xB2F925B9, 0x5A0F, 0x4D2E, {0x9F, 0x4D, 0x2B, 0x15, 0x07, 0x59, 0x3C, 0x10}},
     3, 6, 8, 10, true},
    // 26100 (24H2) … 26200+ (25H2): GetAllCurrentDesktops removed again, HWND/HMONITOR dropped.
    {26100, 0x7FFFFFF, {0x53F5CA0B, 0x158F, 0x4124, {0x90, 0x0C, 0x05, 0x71, 0x58, 0x06, 0x0B, 0x27}},
     3, 6, 7, 9, false},
};

// ---- raw vtable plumbing ---------------------------------------------------------------------

template <typename Fn>
Fn Slot(void* object, int absoluteSlot) {
    auto** vtable = *reinterpret_cast<void***>(object);
    return reinterpret_cast<Fn>(vtable[absoluteSlot]);
}

using FnRelease = ULONG(STDMETHODCALLTYPE*)(void*);
using FnQueryService = HRESULT(STDMETHODCALLTYPE*)(void*, const GUID&, const IID&, void**);
using FnCount = HRESULT(STDMETHODCALLTYPE*)(void*, UINT*);
using FnCountH = HRESULT(STDMETHODCALLTYPE*)(void*, HWND, UINT*);
using FnOutPtr = HRESULT(STDMETHODCALLTYPE*)(void*, void**);
using FnOutPtrH = HRESULT(STDMETHODCALLTYPE*)(void*, HWND, void**);
using FnInPtr = HRESULT(STDMETHODCALLTYPE*)(void*, void*);
using FnInPtrH = HRESULT(STDMETHODCALLTYPE*)(void*, HWND, void*);
using FnArrayCount = HRESULT(STDMETHODCALLTYPE*)(void*, UINT*);
using FnArrayGetAt = HRESULT(STDMETHODCALLTYPE*)(void*, UINT, const IID&, void**);
using FnGetId = HRESULT(STDMETHODCALLTYPE*)(void*, GUID*);
using FnMoveWindow = HRESULT(STDMETHODCALLTYPE*)(void*, HWND, const GUID&);

// IObjectArray / IVirtualDesktop are consumed through raw slots so this file needs no shell
// headers that differ between the Windows SDK and mingw-w64.
inline void Release(void* object) {
    if (object != nullptr) Slot<FnRelease>(object, 2)(object);
}

constexpr int kArrayCount = 3;
constexpr int kArrayGetAt = 4;
constexpr int kDesktopGetId = 3;

// Documented IVirtualDesktopManager slots.
constexpr int kDocMoveWindowToDesktop = 5;

// ---- cached state ----------------------------------------------------------------------------

struct State {
    std::mutex lock;
    void* manager = nullptr;      // IVirtualDesktopManagerInternal*
    const Layout* layout = nullptr;
    int build = 0;
    bool probed = false;
};

State g_state;

int OsBuild() {
    using FnRtlGetVersion = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);
    if (auto ntdll = GetModuleHandleW(L"ntdll.dll")) {
        // memcpy rather than a cast: GCC treats FARPROC -> typed pointer as an incompatible
        // function-type cast (a hard error under -Werror) and the round trip is identical.
        FnRtlGetVersion fn = nullptr;
        if (FARPROC proc = GetProcAddress(ntdll, "RtlGetVersion")) {
            static_assert(sizeof(fn) == sizeof(proc), "function pointer size mismatch");
            std::memcpy(&fn, &proc, sizeof(fn));
        }
        if (fn) {
            RTL_OSVERSIONINFOW info{};
            info.dwOSVersionInfoSize = sizeof(info);
            if (fn(&info) == 0) return static_cast<int>(info.dwBuildNumber);
        }
    }
    return 0;
}

const Layout* LayoutFor(int build) {
    for (const auto& layout : kLayouts)
        if (build >= layout.minBuild && build <= layout.maxBuild) return &layout;
    return nullptr;
}

bool EnsureCom() {
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    return SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE || hr == S_FALSE;
}

// Opens ImmersiveShell -> QueryService(SID, IID) and validates the result with GetCount.
void* OpenManager(const Layout& layout) {
    void* shell = nullptr;
    if (FAILED(CoCreateInstance(kClsidImmersiveShell, nullptr, CLSCTX_LOCAL_SERVER,
                                kIidServiceProvider, &shell)) ||
        shell == nullptr)
        return nullptr;

    void* manager = nullptr;
    HRESULT hr = Slot<FnQueryService>(shell, 3)(shell, kSidVirtualDesktopManagerInternal,
                                                layout.iid, &manager);
    Release(shell);
    if (FAILED(hr) || manager == nullptr) return nullptr;

    // Validate: a layout mismatch either failed QI above or returns nonsense here.
    UINT count = 0;
    hr = layout.takesHwnd ? Slot<FnCountH>(manager, layout.slotCount)(manager, nullptr, &count)
                          : Slot<FnCount>(manager, layout.slotCount)(manager, &count);
    if (FAILED(hr) || count == 0 || count > 256) {
        Release(manager);
        return nullptr;
    }
    return manager;
}

// Returns the cached manager, opening it on first use. Caller must hold g_state.lock.
void* Manager() {
    if (g_state.manager != nullptr) return g_state.manager;
    if (g_state.probed) return nullptr; // already known to be unavailable

    g_state.probed = true;
    if (!EnsureCom()) return nullptr;

    g_state.build = OsBuild();
    const Layout* layout = LayoutFor(g_state.build);
    if (layout == nullptr) return nullptr;

    g_state.manager = OpenManager(*layout);
    if (g_state.manager != nullptr) g_state.layout = layout;
    return g_state.manager;
}

// Drops the cache (explorer restart, HRESULT failure) so the next call re-opens it.
void ResetLocked() {
    Release(g_state.manager);
    g_state.manager = nullptr;
    g_state.layout = nullptr;
    g_state.probed = false;
}

int ReadCount(void* manager, const Layout& layout) {
    UINT count = 0;
    HRESULT hr = layout.takesHwnd ? Slot<FnCountH>(manager, layout.slotCount)(manager, nullptr, &count)
                                  : Slot<FnCount>(manager, layout.slotCount)(manager, &count);
    return SUCCEEDED(hr) ? static_cast<int>(count) : -1;
}

// Fetches the IVirtualDesktop at `index`, transferring ownership to the caller.
void* DesktopAt(void* manager, const Layout& layout, int index, int* outCount = nullptr) {
    void* array = nullptr;
    HRESULT hr = layout.takesHwnd
                     ? Slot<FnOutPtrH>(manager, layout.slotAll)(manager, nullptr, &array)
                     : Slot<FnOutPtr>(manager, layout.slotAll)(manager, &array);
    if (FAILED(hr) || array == nullptr) return nullptr;

    UINT count = 0;
    Slot<FnArrayCount>(array, kArrayCount)(array, &count);
    if (outCount != nullptr) *outCount = static_cast<int>(count);

    void* desktop = nullptr;
    if (index >= 0 && static_cast<UINT>(index) < count)
        Slot<FnArrayGetAt>(array, kArrayGetAt)(array, static_cast<UINT>(index), kIidVirtualDesktop,
                                               &desktop);

    Release(array);
    return desktop;
}

int ReadCurrentIndex(void* manager, const Layout& layout) {
    void* current = nullptr;
    HRESULT hr = layout.takesHwnd
                     ? Slot<FnOutPtrH>(manager, layout.slotCurrent)(manager, nullptr, &current)
                     : Slot<FnOutPtr>(manager, layout.slotCurrent)(manager, &current);
    if (FAILED(hr) || current == nullptr) return -1;

    GUID currentId{};
    if (FAILED(Slot<FnGetId>(current, kDesktopGetId)(current, &currentId))) {
        Release(current);
        return -1;
    }
    Release(current);

    // Compare by desktop GUID: the internal objects have no usable IUnknown identity.
    const int count = ReadCount(manager, layout);
    for (int i = 0; i < count; i++) {
        void* desktop = DesktopAt(manager, layout, i);
        if (desktop == nullptr) continue;
        GUID id{};
        const bool match = SUCCEEDED(Slot<FnGetId>(desktop, kDesktopGetId)(desktop, &id)) &&
                           IsEqualGUID(id, currentId);
        Release(desktop);
        if (match) return i;
    }
    return -1;
}

// Documented manager, used only for the window move.
void* OpenDocumentedManager() {
    void* manager = nullptr;
    if (FAILED(CoCreateInstance(kClsidVirtualDesktopManager, nullptr, CLSCTX_ALL,
                                kIidVirtualDesktopManager, &manager)))
        return nullptr;
    return manager;
}

} // namespace

extern "C" {

RADHA_API int RadhaWorkspaceBuild() {
    if (g_state.build == 0) g_state.build = OsBuild();
    return g_state.build;
}

// 0 = no backend (unsupported build or shell unavailable), 1 = undocumented COM interface live.
RADHA_API int RadhaWorkspaceBackend() {
    std::lock_guard<std::mutex> guard(g_state.lock);
    if (Manager() == nullptr) return 0;
    return 1;
}

RADHA_API int RadhaWorkspaceCount() {
    std::lock_guard<std::mutex> guard(g_state.lock);
    void* manager = Manager();
    if (manager == nullptr) return 0;
    int count = ReadCount(manager, *g_state.layout);
    if (count < 0) { // explorer restarted under us — reopen once
        ResetLocked();
        manager = Manager();
        if (manager == nullptr) return 0;
        count = ReadCount(manager, *g_state.layout);
    }
    return count < 0 ? 0 : count;
}

RADHA_API int RadhaWorkspaceCurrent() {
    std::lock_guard<std::mutex> guard(g_state.lock);
    void* manager = Manager();
    if (manager == nullptr) return -1;
    int index = ReadCurrentIndex(manager, *g_state.layout);
    if (index < 0) {
        ResetLocked();
        manager = Manager();
        if (manager == nullptr) return -1;
        index = ReadCurrentIndex(manager, *g_state.layout);
    }
    return index;
}

RADHA_API BOOL RadhaWorkspaceSwitch(int index) {
    std::lock_guard<std::mutex> guard(g_state.lock);
    void* manager = Manager();
    if (manager == nullptr || index < 0) return FALSE;

    const Layout& layout = *g_state.layout;
    void* desktop = DesktopAt(manager, layout, index);
    if (desktop == nullptr) return FALSE;

    HRESULT hr = layout.takesHwnd
                     ? Slot<FnInPtrH>(manager, layout.slotSwitch)(manager, nullptr, desktop)
                     : Slot<FnInPtr>(manager, layout.slotSwitch)(manager, desktop);
    Release(desktop);

    if (FAILED(hr)) {
        ResetLocked();
        return FALSE;
    }
    return TRUE;
}

RADHA_API BOOL RadhaWorkspaceMoveActiveWindow(int index) {
    HWND window = GetForegroundWindow();
    if (window == nullptr) return FALSE;

    std::lock_guard<std::mutex> guard(g_state.lock);
    void* manager = Manager();
    if (manager == nullptr || index < 0) return FALSE;

    void* desktop = DesktopAt(manager, *g_state.layout, index);
    if (desktop == nullptr) return FALSE;

    GUID target{};
    HRESULT hr = Slot<FnGetId>(desktop, kDesktopGetId)(desktop, &target);
    Release(desktop);
    if (FAILED(hr)) return FALSE;

    // Documented API from here on: move the window to that desktop GUID.
    void* documented = OpenDocumentedManager();
    if (documented == nullptr) return FALSE;
    hr = Slot<FnMoveWindow>(documented, kDocMoveWindowToDesktop)(documented, window, target);
    Release(documented);
    return SUCCEEDED(hr) ? TRUE : FALSE;
}

} // extern "C"
