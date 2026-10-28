using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace XivType;

// Messages target only the hosting FFXIV process's window. No focus changes or global SendInput.
internal sealed unsafe partial class TargetedActivityInput : IActivityInput
{
    private nint heldWindow;
    public bool ShiftObserved
    {
        get { var data = UIInputData.Instance(); return data != null && (data->GetKeyState(0x10) & (KeyStateFlags.Down | KeyStateFlags.Pressed)) != 0; }
    }
    public float IdleSeconds
    {
        get { var timer = InputTimerModule.Instance(); return timer != null ? timer->InputTimer : float.NaN; }
    }

    public bool PressShift()
    {
        using var process = Process.GetCurrentProcess();
        var window = process.MainWindowHandle;
        if (window == 0 || heldWindow != 0) return false;
        GetWindowThreadProcessId(window, out var owner);
        if (owner != Environment.ProcessId || !PostMessageW(window, 0x100, 0x10, 0x002A0001)) return false;
        heldWindow = window;
        return true;
    }

    public void ReleaseShift()
    {
        if (heldWindow != 0) PostMessageW(heldWindow, 0x101, 0x10, unchecked((nint)0xC02A0001));
        heldWindow = 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint key, nint parameter);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
