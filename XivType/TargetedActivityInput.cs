using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace XivType;

// Messages target only the hosting FFXIV process's window. No focus changes or global SendInput.
// Default is Shift (VK 0x10, scan 0x2A); the lobby probe can try other keys such as F13 (VK 0x7C, scan 0x64).
internal sealed unsafe partial class TargetedActivityInput(ushort virtualKey = 0x10, uint scanCode = 0x2A) : IActivityInput
{
    private nint heldWindow;
    internal ushort VirtualKey => virtualKey;
    public bool KeyObserved
    {
        get { var data = UIInputData.Instance(); return data != null && (data->GetKeyState(virtualKey) & (KeyStateFlags.Down | KeyStateFlags.Pressed)) != 0; }
    }
    public float IdleSeconds
    {
        get { var timer = InputTimerModule.Instance(); return timer != null ? timer->InputTimer : float.NaN; }
    }

    public bool PressKey()
    {
        using var process = Process.GetCurrentProcess();
        var window = process.MainWindowHandle;
        if (window == 0 || heldWindow != 0) return false;
        GetWindowThreadProcessId(window, out var owner);
        if (owner != Environment.ProcessId || !PostMessageW(window, 0x100, virtualKey, (nint)(1 | scanCode << 16))) return false;
        heldWindow = window;
        return true;
    }

    public void ReleaseKey()
    {
        if (heldWindow != 0) PostMessageW(heldWindow, 0x101, virtualKey, unchecked((nint)(0xC0000001u | scanCode << 16)));
        heldWindow = 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint key, nint parameter);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
