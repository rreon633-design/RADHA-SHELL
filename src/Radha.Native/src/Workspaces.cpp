#include "../include/radha_native.h"
#include "Internal.h"
#include <shobjidl.h>

// Virtual desktop control.
//
// The documented IVirtualDesktopManager can only answer "which desktop owns this window".
// Enumerating / switching desktops requires IVirtualDesktopManagerInternal, an undocumented
// COM interface whose IID changes between Windows builds. Shipping a hard-coded vtable that
// silently corrupts on the next feature update is worse than not shipping it, so this bridge
// deliberately exposes the documented part and reports "unsupported" for the rest until the
// version-dispatched implementation lands (tracked in docs/ROADMAP.md, phase 2).
//
// The C# WorkspaceService already drives the UI with the documented contract, so plugging the
// COM implementation in later cannot change call sites.

extern "C" {

RADHA_API int RadhaWorkspaceCount() {
    // 0 = "ask the managed side to fall back to its configured workspace count".
    return 0;
}

RADHA_API int RadhaWorkspaceCurrent() {
    return -1;
}

RADHA_API BOOL RadhaWorkspaceSwitch(int index) {
    (void)index;
    return FALSE;
}

RADHA_API BOOL RadhaWorkspaceMoveActiveWindow(int index) {
    (void)index;
    return FALSE;
}

} // extern "C"
