// Runtime smoke test for RadhaNative.dll — runs on the Windows CI runner and asserts that the
// virtual-desktop entry points are safe on both paths:
//
//   * supported backend   → count >= 1, current in [0, count), switching to the current desktop
//                           succeeds, and the index is unchanged afterwards
//   * unsupported backend → count == 0, current == -1, switch/move return FALSE, no crash
//
// It also hammers the boundary conditions (negative, past the end) because a wrong vtable slot
// faulting inside the shell is exactly the failure mode this code has to avoid.
//
// Build: cmake --build build --config Release --target RadhaNativeSmoke
// Run:   RadhaNativeSmoke.exe   (exit code 0 = pass)

#include "../include/radha_native.h"

#include <cstdio>
#include <string>

namespace {

int g_checks = 0;
int g_failures = 0;

void Check(const char* what, bool ok) {
    ++g_checks;
    if (!ok) {
        ++g_failures;
        std::printf("  FAIL  %s\n", what);
    } else {
        std::printf("  ok    %s\n", what);
    }
}

} // namespace

int main() {
    std::printf("RadhaNative · virtual desktop smoke test\n\n");

    const int build = RadhaWorkspaceBuild();
    const int backend = RadhaWorkspaceBackend();
    const int count = RadhaWorkspaceCount();
    const int current = RadhaWorkspaceCurrent();

    std::printf("build=%d  backend=%d  count=%d  current=%d\n\n", build, backend, count, current);

    Check("build number is plausible (>= 10240)", build >= 10240);
    Check("backend is one of 0/1", backend == 0 || backend == 1);

    if (backend == 1) {
        std::printf("  (live internal COM interface)\n");
        Check("count is sane (1..256)", count >= 1 && count <= 256);
        Check("current index inside range", current >= 0 && current < count);

        // Switching to where we already are must succeed and must not move us.
        Check("switch to current succeeds", RadhaWorkspaceSwitch(current) != FALSE);
        Check("current unchanged after self-switch", RadhaWorkspaceCurrent() == current);

        // Walk every desktop once and come back — proves the index mapping is consistent.
        bool everySwitchWorked = true;
        for (int i = 0; i < count; ++i) {
            if (!RadhaWorkspaceSwitch(i)) everySwitchWorked = false;
            if (RadhaWorkspaceCurrent() != i) everySwitchWorked = false;
        }
        if (!RadhaWorkspaceSwitch(current)) everySwitchWorked = false;
        Check("every desktop reachable by index", everySwitchWorked);
    } else {
        std::printf("  (no verified layout for this build, or no interactive shell)\n");
        Check("count reports unsupported as 0", count == 0);
        Check("current reports unsupported as -1", current == -1);
        Check("switch returns FALSE instead of guessing", RadhaWorkspaceSwitch(0) == FALSE);
        Check("move returns FALSE instead of guessing", RadhaWorkspaceMoveActiveWindow(0) == FALSE);
    }

    // Boundary conditions: must be inert, never a crash.
    Check("switch(-1) is rejected", RadhaWorkspaceSwitch(-1) == FALSE);
    Check("switch(9999) is rejected", RadhaWorkspaceSwitch(9999) == FALSE);
    Check("move(-5) is rejected", RadhaWorkspaceMoveActiveWindow(-5) == FALSE);
    Check("move(9999) is rejected", RadhaWorkspaceMoveActiveWindow(9999) == FALSE);

    std::printf("\n%d checks, %d failure(s)\n", g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
