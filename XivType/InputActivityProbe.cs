using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace XivType;

// Explicit, bounded lobby diagnostic. No timer writes and no global SendInput.
internal sealed unsafe partial class InputActivityProbe(DiagnosticStore diagnostics)
{
    private TargetedActivityInput input = new();
    private readonly List<object> samples = [];
    private long started;
    private bool active;
    private bool held;
    private nint window;
    private float before;
    private bool sawShift;
    private bool sawReset;
    private bool allBackground;
    private bool allMinimized;

    internal void Begin(string key = "shift")
    {
        if (!Plugin.Client.IsLoggedIn || Plugin.Client.TerritoryType != 1360 || active) return;
        input = key == "f13" ? new(0x7C, 0x64) : new();
        var timers = InputTimerModule.Instance();
        if (timers == null || timers->InputTimer < 2) return;
        window = Process.GetCurrentProcess().MainWindowHandle;
        if (window == 0) return;
        samples.Clear();
        before = timers->InputTimer;
        started = Environment.TickCount64;
        sawShift = sawReset = false;
        allBackground = !NativeInput.IsGameForeground();
        allMinimized = IsIconic(window);
        active = true;
        held = input.PressKey();
        Plugin.Log.Information("XivType activity probe started; targeted key {Key:X2} queued={Queued}; input timer={Before}", input.VirtualKey, held, before);
    }

    internal void Update(long now)
    {
        if (!active) return;
        var timers = InputTimerModule.Instance();
        var data = UIInputData.Instance();
        if (timers != null && data != null)
        {
            var shift = data->GetKeyState(input.VirtualKey);
            var foreground = NativeInput.IsGameForeground();
            allBackground &= !foreground;
            var minimized = IsIconic(window);
            allMinimized &= minimized;
            // FFXIV's key-state table does not report F13; its keyboard-changed flag still fires.
            sawShift |= (shift & (KeyStateFlags.Down | KeyStateFlags.Pressed)) != 0 || data->KeyboardInputsChanged;
            sawReset |= timers->InputTimer < 0.5f;
            samples.Add(new { ElapsedMs = now - started, Foreground = foreground, Minimized = minimized,
                InputTimer = timers->InputTimer, ShiftState = shift.ToString(), KeyboardChanged = data->KeyboardInputsChanged });
        }
        if (held && now - started >= 250)
        {
            input.ReleaseKey();
            held = false;
        }
        if (now - started < 3000) return;
        active = false;
        var report = new { Key = input.VirtualKey, BeforeInputTimer = before, SawShift = sawShift, SawNaturalTimerReset = sawReset,
            AllBackground = allBackground, AllMinimized = allMinimized, Passed = sawShift && sawReset && allBackground, Samples = samples };
        diagnostics.Write("activity-probe.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Plugin.Log.Information("XivType activity probe complete: keyboard={Keyboard}, natural reset={Reset}, background={Background}", sawShift, sawReset, allBackground);
    }

    internal void Dispose()
    {
        if (held) input.ReleaseKey();
        held = active = false;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

}
