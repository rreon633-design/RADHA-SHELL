using System.Runtime.InteropServices;

namespace Radha.App.Interop;

/// <summary>
/// Entry points of <c>RadhaNative.dll</c> for virtual-desktop control.
///
/// Everything here maps 1:1 onto the C ABI in <c>src/Radha.Native/include/radha_native.h</c>. The
/// native side owns the undocumented COM work; the managed side only reads the results, so a build
/// Windows does not expose a verified interface for degrades to "unsupported" instead of throwing.
/// </summary>
internal static class NativeBridge
{
    private const string Dll = "RadhaNative.dll";

    /// <summary>OS build number (e.g. 26100); 0 when it could not be read.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RadhaWorkspaceBuild();

    /// <summary>0 = no backend (unsupported build or shell missing), 1 = internal COM interface live.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RadhaWorkspaceBackend();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RadhaWorkspaceCount();

    /// <summary>0-based index of the current desktop, or -1 when unsupported.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RadhaWorkspaceCurrent();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RadhaWorkspaceSwitch(int index);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RadhaWorkspaceMoveActiveWindow(int index);

    // ---- global hotkeys -------------------------------------------------
    // The native sink owns the window and the WM_HOTKEY loop; it writes one text line
    // ("module command") into a dedicated pipe, which HotkeyService validates against
    // CommandCatalog before anything executes.

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RadhaHotkeyRegister(int id, uint modifiers, uint virtualKey);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RadhaHotkeyUnregister(int id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern void RadhaHotkeySetPipeEndpoint(string? pipeName);

    /// <summary>
    /// Probes the bridge once. Returns false when the DLL is missing or the running Windows build
    /// has no verified virtual-desktop layout — callers then use the labelled fallback.
    /// </summary>
    internal static bool TryProbe(out int build)
    {
        build = 0;
        try
        {
            build = RadhaWorkspaceBuild();
            return RadhaWorkspaceBackend() == 1;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }
}
