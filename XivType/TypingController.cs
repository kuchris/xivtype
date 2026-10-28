using System.Diagnostics;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;

namespace XivType;

internal sealed unsafe class TypingController(DiagnosticStore diagnostics, Configuration config)
{
    private readonly ConcurrentQueue<string> requests = new();
    private readonly ActivityKeepAlive activity = new(new TargetedActivityInput());
    private readonly InputActivityProbe activityProbe = new(diagnostics);
    private bool running;
    private bool continuous;
    private bool probe;
    private string target = "";
    private uint targetNode;
    private string expected = "";
    private long sentAt;
    private bool pending;
    private bool submitted;
    private bool clearing;
    private bool ownsInput;
    private string sentFrom = "";
    private int editRetries;
    private int submitRetries;
    private int adaptiveHoldMs;
    private bool inputWasBlocked;
    private long nextAction;
    private long nextReport;
    private long nextIdleLog;
    private string lastReport = "";
    private uint scoreBefore;
    private int accepted;
    private int submissions;
    public bool Running => running;
    public int Submissions => submissions;
    public int Accepted => accepted;
    public int VisiblePrompts { get; private set; }
    public int KnownDistances { get; private set; }
    public string Status { get; private set; } = "Ready. Automatic typing is off.";
    public void Request(string command) => requests.Enqueue(command);

    public void Start(bool repeat, bool singleCharacter = false)
    {
        activity.Reset();
        running = true;
        continuous = repeat;
        probe = singleCharacter;
        target = "";
        pending = false;
        submitted = false;
        clearing = false;
        ownsInput = false;
        editRetries = 0;
        submitRetries = 0;
        inputWasBlocked = false;
        nextAction = Environment.TickCount64 + 500;
        Status = singleCharacter ? "Testing one character." : "Waiting for a visible English prompt.";
        Plugin.Log.Information("XivType typing started; continuous={Continuous}, probe={Probe}", repeat, singleCharacter);
    }

    public void Stop(string reason)
    {
        activity.Cancel();
        NativeInput.Release();
        running = false;
        pending = false;
        submitted = false;
        target = "";
        clearing = false;
        ownsInput = false;
        Status = reason;
        Plugin.Log.Information("XivType typing: {Status}", reason);
    }

    public void Update(long now)
    {
        NativeInput.KeyHoldMs = Math.Max(config.KeyHoldMs, adaptiveHoldMs);
        while (diagnostics.TryGetCommand(out var externalCommand)) requests.Enqueue(externalCommand);
        while (requests.TryDequeue(out var command))
        {
            switch (command.ToLowerInvariant())
            {
                case "probe": Start(false, true); break;
                case "once": Start(false); break;
                case "on": Start(true); break;
                case "off": Stop("Stopped by local development control."); break;
                case "activity-probe": if (!running) activityProbe.Begin(); break;
                case "activity-probe f13": if (!running) activityProbe.Begin("f13"); break;
            }
        }
        activityProbe.Update(now);
        var state = Read();
        VisiblePrompts = state.Words.Length;
        KnownDistances = state.Words.Count(w => w.Distance.HasValue);
        var permission = GameInput.Permission;
        if (running) Tick(now, state, permission);
        var pulsesBefore = activity.Acknowledged;
        activity.Update(now, config.PreventAfk && config.BackgroundTyping, running,
            GameInput.Permission.Allowed && !(NativeInput.IsGameForeground() && Plugin.KeyboardCaptured),
            (ushort)Plugin.Client.TerritoryType);
        if (activity.Acknowledged != pulsesBefore)
            Plugin.Log.Information("XivType targeted activity key acknowledged {Count}; natural input idle {Before:0.00}s -> {After:0.00}s",
                activity.Acknowledged, activity.LastInputBefore, activity.LastInputAfter);
        if (running && config.PreventAfk && config.BackgroundTyping && activity.Unavailable)
            Stop("Stopped: the game did not acknowledge its targeted activity key. AFK protection is unavailable.");
        if (Plugin.Client.TerritoryType == 1359 && now >= nextIdleLog)
        {
            // The duty inactivity check ignored acknowledged activity keys on 2026-10-28; log every candidate signal.
            nextIdleLog = now + 5000;
            var timers = InputTimerModule.Instance();
            Plugin.Log.Information("XivType idle timeline: running={Running}; foreground={Foreground}; minimized={Minimized}; systemIdle={SystemIdle:0.0}s; afk={Afk:0.0}; content={Content:0.0}; input={Input:0.0}; submissions={Submissions}; activityKeys={Keys}",
                running, NativeInput.IsGameForeground(), NativeInput.IsGameMinimized(), NativeInput.SystemIdleSeconds(),
                timers != null ? timers->AfkTimer : float.NaN, timers != null ? timers->ContentInputTimer : float.NaN,
                timers != null ? timers->InputTimer : float.NaN, submissions, activity.Acknowledged);
        }
        if (now < nextReport) return;
        nextReport = now + 100;
        var report = JsonSerializer.Serialize(new
        {
            Version = typeof(Plugin).Assembly.GetName().Version?.ToString(),
            Priority = config.Priority.ToString(), MappedPrompts = KnownDistances, ActorScreens = actorScreens.Select(a => new { a.Entity, a.Screen.X, a.Screen.Y }),
            GameForeground = NativeInput.IsGameForeground(), config.BackgroundTyping, GameInputRoute = GameInput.Describe(),
            NativeInput.SystemKeyEvents,
            InputAllowed = permission.Allowed, InputLockSeconds = permission.LockSeconds, InputBlockReason = permission.Reason,
            EnemyJammerActive = Plugin.EnemyLock.Blocked, EnemyJammerAction = Plugin.EnemyLock.ActionId,
            Plugin.EnemyCastLabels,
            GameInput.EditOperations, GameInput.SubmitOperations,
            config.PreventAfk, AfkKeysSent = activity.Sent, AfkKeysAcknowledged = activity.Acknowledged, AfkStatus = activity.Status,
            activity.LastInputBefore, activity.LastInputAfter,
            InputTimers = ActivityKeepAlive.Snapshot(InputTimerModule.Instance()),
            running, continuous, probe, Status, target, targetNode, expected, pending, submitted, clearing, ownsInput,
            config.TypingDelayMs, config.ClearDelayMs, config.KeyHoldMs,
            EffectiveKeyHoldMs = NativeInput.KeyHoldMs, editRetries, submitRetries,
            state.Input, state.Words, state.Score, state.Ready, accepted, submissions,
        }, new JsonSerializerOptions { WriteIndented = true });
        if (report == lastReport) return;
        lastReport = report;
        diagnostics.Write("typing-state.json", report);
    }

    private void Tick(long now, LiveState state, InputPermission permission)
    {
        if (Plugin.Client.TerritoryType != 1359 || Plugin.Client.ClientLanguage.ToString() != "English")
        {
            Stop("Stopped outside the supported English activity.");
            return;
        }
        if (NativeInput.IsGameForeground() && (NativeInput.GetAsyncKeyState(0x1B) & 0x8000) != 0)
        {
            Stop("Stopped by Escape.");
            return;
        }
        if (!state.Ready)
        {
            target = "";
            pending = false;
            submitted = false;
            clearing = false;
            ownsInput = false;
            Status = "Waiting for the English activity input and prompts.";
            return;
        }
        // The activity's own prohibition takes precedence over acknowledgements, retries,
        // partial-input cleanup, prompt selection and both input routes.
        if (!permission.Allowed)
        {
            NativeInput.Release();
            if (!inputWasBlocked) Plugin.Log.Information("XivType input paused: {Reason}", permission.Reason);
            inputWasBlocked = true;
            if (pending || submitted) sentAt = now;
            Status = permission.Reason;
            return;
        }
        if (inputWasBlocked)
        {
            inputWasBlocked = false;
            nextAction = Math.Max(nextAction, now + 150);
            if (pending || submitted) sentAt = now;
            Plugin.Log.Information("XivType input permission restored; resuming after a short settling delay.");
        }
        if ((!config.BackgroundTyping && (!NativeInput.IsGameForeground() || NativeInput.ModifierDown())) ||
            (NativeInput.IsGameForeground() && Plugin.KeyboardCaptured))
        {
            // Do not expire a pending acknowledgement just because the user switched windows.
            if (pending || submitted) sentAt = now;
            Status = "Paused - focus the game and release modifier keys.";
            return;
        }
        if (pending)
        {
            if (state.Input.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                pending = false;
                ownsInput = true;
                accepted++;
                if (!RecordActivity(now, permission)) return;
                if (probe)
                {
                    Stop($"Character accepted by game: '{state.Input}'.");
                    return;
                }
                nextAction = now + (clearing ? config.ClearDelayMs : config.TypingDelayMs);
            }
            else if (now - sentAt > 1500)
            {
                var targetStillActive = clearing || state.Words.Any(w => w.NodeId == targetNode && w.Text == target);
                if (editRetries == 0 && state.Input.Equals(sentFrom, StringComparison.OrdinalIgnoreCase) &&
                    targetStillActive && NativeInput.Ready(now))
                {
                    adaptiveHoldMs = Math.Min(80, Math.Max(60, NativeInput.KeyHoldMs + 15));
                    NativeInput.KeyHoldMs = adaptiveHoldMs;
                    var sent = config.BackgroundTyping ? GameInput.ChangeText(state.Input, expected) :
                        clearing ? NativeInput.Backspace(adaptiveHoldMs) :
                            NativeInput.SendCharacter(Process.GetCurrentProcess().MainWindowHandle, expected[^1]);
                    if (sent)
                    {
                        editRetries++;
                        sentAt = now;
                        Status = $"Retrying one unacknowledged edit with a {adaptiveHoldMs} ms key hold.";
                        Plugin.Log.Information("XivType retrying unchanged input '{Input}', expected '{Expected}', hold={Hold}", state.Input, expected, adaptiveHoldMs);
                        return;
                    }
                }
                Stop($"Input was not acknowledged. Expected '{expected}', observed '{state.Input}'.");
                return;
            }
            else return;
        }
        if (submitted)
        {
            if (state.Input.Length == 0 && (state.Score > scoreBefore || !state.Words.Any(w => w.NodeId == targetNode && w.Text == target)))
            {
                submissions++;
                if (!RecordActivity(now, permission)) return;
                Plugin.Log.Information("XivType submission acknowledged: {Target}; score {Before}->{After}", target, scoreBefore, state.Score);
                submitted = false;
                ownsInput = false;
                target = "";
                nextAction = now + 250;
                if (!continuous)
                {
                    Stop($"Prompt submitted and acknowledged. Score: {state.Score}.");
                    return;
                }
            }
            else if (now - sentAt > 2000)
            {
                if (submitRetries == 0 && state.Input.Equals(target, StringComparison.OrdinalIgnoreCase) &&
                    state.Words.Any(w => w.NodeId == targetNode && w.Text == target) && NativeInput.Ready(now))
                {
                    adaptiveHoldMs = 80;
                    NativeInput.KeyHoldMs = adaptiveHoldMs;
                    if (SendSubmit(target))
                    {
                        submitRetries++;
                        sentAt = now;
                        Plugin.Log.Information("XivType retrying Enter after unchanged input for {Target}", target);
                        return;
                    }
                }
                Stop($"Submission was not acknowledged for '{target}'. Observed input: '{state.Input}'.");
                return;
            }
            else return;
        }
        if (now < nextAction || (!config.BackgroundTyping && !NativeInput.Ready(now))) return;
        if (clearing)
        {
            if (state.Input.Length == 0)
            {
                clearing = false;
                ownsInput = false;
                target = "";
            }
            else
            {
                var next = config.BackgroundTyping ? "" : state.Input[..^1];
                if (!(config.BackgroundTyping ? GameInput.ChangeText(state.Input, next) : NativeInput.Backspace()))
                {
                    Stop("Could not clear the expired prompt input.");
                    return;
                }
                expected = next;
                sentFrom = state.Input;
                editRetries = 0;
                pending = true;
                sentAt = now;
                Status = "Clearing partial input for an expired enemy prompt.";
                return;
            }
        }
        if (target.Length == 0)
        {
            var choice = PromptSelection.Choose(state.Words, state.Input, config.Priority);
            if (choice == null)
            {
                Status = state.Input.Length == 0 ? "Waiting for an enemy prompt." : "Paused - existing input does not match a visible prompt. Clear it manually.";
                return;
            }
            target = choice.Text;
            targetNode = choice.NodeId;
            Plugin.Log.Information("XivType selected prompt {Node}: {Target}; masked={Masked}; priority={Priority}; distance={Distance}; scale={Scale:0.000}; estimated={Estimated}; candidates={Candidates}; existing input '{Input}'",
                targetNode, target, choice.Masked, config.Priority, choice.Distance?.ToString("0.0") ?? "unknown", choice.Scale,
                PromptActorMap.EstimateDistance(choice.Scale)?.ToString("0.0") ?? "none", state.Words.Length, state.Input);
        }
        if (!state.Words.Any(w => w.NodeId == targetNode && w.Text == target))
        {
            target = "";
            clearing = ownsInput && state.Input.Length > 0;
            Status = "Selected enemy prompt disappeared; checking again.";
            return;
        }
        if (!target.StartsWith(state.Input, StringComparison.OrdinalIgnoreCase))
        {
            Stop("Input changed unexpectedly. Clear the input manually, then restart.");
            return;
        }
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (state.Input.Length < target.Length)
        {
            var ch = target[state.Input.Length];
            if (!(config.BackgroundTyping ? GameInput.ChangeText(state.Input, state.Input + ch) : NativeInput.SendCharacter(hwnd, ch)))
            {
                Stop(config.BackgroundTyping ? $"Game input route unavailable: {GameInput.Describe()}" :
                    $"Windows rejected character input (error {Marshal.GetLastWin32Error()}).");
                return;
            }
            expected = state.Input + ch;
            sentFrom = state.Input;
            editRetries = 0;
            pending = true;
            sentAt = now;
            Status = $"Typing '{target}' ({state.Input.Length + 1}/{target.Length}).";
            Plugin.Log.Debug("XivType character sent '{Character}', expecting '{Expected}'", ch, expected);
        }
        else
        {
            if (probe) { Stop("The selected prompt is already complete."); return; }
            if (!SendSubmit(target)) { Stop("Input route rejected submission."); return; }
            submitted = true;
            submitRetries = 0;
            sentAt = now;
            scoreBefore = state.Score;
            Status = $"Submitted '{target}', waiting for game acknowledgement.";
            Plugin.Log.Information("XivType Enter sent for {Target}; score={Score}", target, scoreBefore);
        }
    }

    private bool SendSubmit(string text) => config.BackgroundTyping ? GameInput.Submit(text) :
        NativeInput.Submit(Process.GetCurrentProcess().MainWindowHandle);

    private bool RecordActivity(long now, InputPermission permission)
    {
        if (permission.Allowed) activity.RecordAcknowledgement(now);
        return true;
    }

    internal void Dispose()
    {
        activity.Cancel();
        activityProbe.Dispose();
    }

    private static ActorScreen[] actorScreens = [];

    private static LiveState Read()
    {
        actorScreens = [];
        if (Plugin.Client.TerritoryType != 1359 || Plugin.Client.ClientLanguage.ToString() != "English")
            return new(false, "", [], 0);
        var input = Plugin.GameGui.GetAddonByName<AtkUnitBase>("KTGInput");
        var wordAddon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("KTGEWord");
        if (input == null || !input->IsVisible || wordAddon == null || !wordAddon->IsVisible)
            return new(false, "", [], 0);
        var inputComponent = input->GetComponentNodeById(7);
        if (inputComponent == null || inputComponent->Component == null) return new(false, "", [], 0);
        var text = PromptReader.FindNode(&inputComponent->Component->UldManager, 16);
        if (text == null || text->Type != NodeType.Text) return new(false, "", [], 0);
        var entered = ((AtkTextNode*)text)->NodeText.ToString();
        var words = PromptReader.ReadWords(wordAddon);
        if (Plugin.Objects.LocalPlayer is { } player)
        {
            actorScreens = Plugin.Objects
                .Where(o => o.ObjectKind == ObjectKind.BattleNpc && o.IsTargetable && !o.IsDead)
                .Select(o => Plugin.GameGui.WorldToScreen(o.Position, out var screen) ? new ActorScreen(o.EntityId, screen, o.Position) : (ActorScreen?)null)
                .OfType<ActorScreen>().ToArray();
            words = PromptActorMap.WithDistances(words, actorScreens, player.Position);
        }
        var scoreAddon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("KTGHudScore");
        var score = scoreAddon != null && scoreAddon->AtkValues != null && scoreAddon->AtkValuesCount > 1
            ? scoreAddon->AtkValues[1].UInt : 0;
        return new(true, entered, words, score);
    }

    private sealed record LiveState(bool Ready, string Input, Prompt[] Words, uint Score);
}

internal static partial class NativeInput
{
    private static ushort heldScan;
    private static bool heldExtended;
    private static bool heldShift;
    private static long releaseAt;
    private static long nextKeyAt;
    internal static int KeyHoldMs { get; set; } = 35;
    internal static long SystemKeyEvents { get; private set; }
    internal static bool Ready(long now) => heldScan == 0 && now >= nextKeyAt;
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetTickCount();

    private struct LastInputInfo { public uint Size; public uint Time; }

    internal static bool IsGameMinimized()
    {
        using var process = Process.GetCurrentProcess();
        return process.MainWindowHandle != 0 && IsIconic(process.MainWindowHandle);
    }

    // Seconds since the last physical keyboard/mouse input anywhere in the Windows session.
    internal static float SystemIdleSeconds()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? (GetTickCount() - info.Time) / 1000f : float.NaN;
    }
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, NativeEvent* events, int size);
    [LibraryImport("user32.dll")]
    private static partial nint GetKeyboardLayout(uint threadId);
    [LibraryImport("user32.dll")]
    private static partial short VkKeyScanExW(ushort value, nint layout);
    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyExW(uint code, uint mapType, nint layout);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeEvent
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }

    private static unsafe bool Key(ushort scan, bool extended, bool up)
    {
        var item = new NativeEvent { Type = 1, Scan = scan, Flags = 8u | (extended ? 1u : 0u) | (up ? 2u : 0u) };
        var sent = SendInput(1, &item, sizeof(NativeEvent)) == 1;
        if (sent) SystemKeyEvents++;
        return sent;
    }

    internal static void Update(long now)
    {
        if (heldScan != 0 && now >= releaseAt) Release();
    }

    internal static void Release()
    {
        var released = heldScan != 0 || heldShift;
        if (heldScan != 0) Key(heldScan, heldExtended, true);
        if (heldShift) Key(0x2A, false, true);
        heldScan = 0;
        heldShift = false;
        // A distinct key-up frame is needed for repeated letters such as the two g's in nidhogg.
        if (released) nextKeyAt = Environment.TickCount64 + 10;
    }

    private static bool Press(ushort scan, bool extended, bool shift, int? holdMs = null)
    {
        if (!IsGameForeground() || !Ready(Environment.TickCount64) || !GameInput.Permission.Allowed) return false;
        if (shift && !Key(0x2A, false, false)) return false;
        heldShift = shift;
        if (!Key(scan, extended, false)) { Release(); return false; }
        heldScan = scan;
        heldExtended = extended;
        releaseAt = Environment.TickCount64 + (holdMs ?? KeyHoldMs);
        return true;
    }

    internal static bool IsGameForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == Environment.ProcessId;
    }

    internal static bool ModifierDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }
        .Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);

    internal static bool SendCharacter(nint window, char value)
    {
        if (window == 0) return false;
        var layout = GetKeyboardLayout(0);
        var mapped = VkKeyScanExW(value, layout);
        if (mapped == -1 || (mapped & 0x0600) != 0) return false;
        var scan = MapVirtualKeyExW((uint)(mapped & 0xFF), 4, layout);
        return scan != 0 && Press((ushort)(scan & 0xFF), (scan & 0xFF00) != 0, (mapped & 0x0100) != 0);
    }
    internal static bool Submit(nint window) => window != 0 && Press(0x1C, false, false);
    internal static bool Backspace(int? holdMs = null) => Press(0x0E, false, false, holdMs ?? 20);
}
