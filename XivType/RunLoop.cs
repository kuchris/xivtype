using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Repeats whole Keybound Brawler runs, entering the way recorded live on 2026-10-28: talk to the Keybound
// Registrar, KTGEnterDialogue Register (ButtonClick param 0), then Duty Ready Commence (ButtonClick param 8);
// at the end, KTGResult Exit (ButtonClick param 1) instead of waiting out the results timer.
// Clicks reuse the button's own registered event, the same object the game handles for a real click.
internal sealed unsafe class RunLoop(TypingController typing)
{
    private const uint RegistrarBaseId = 1058751;
    private const ushort Lobby = 1360;
    private const ushort Activity = 1359;
    private enum Stage { Talk, Register, Queued, Entering, InRun }

    private Stage stage;
    private long nextStepAt;
    private long stageSince;
    private int talkAttempts;
    private int typingRestarts;
    private long resultSeenAt;
    internal bool Active { get; private set; }
    internal int Runs { get; private set; }
    internal int Completed { get; private set; }
    internal string Status { get; private set; } = "Loop is off.";

    internal void Start(int runs)
    {
        Runs = Math.Clamp(runs, 1, 99);
        Completed = 0;
        Active = true;
        stage = Plugin.Client.TerritoryType == Activity ? Stage.InRun : Stage.Talk;
        stageSince = nextStepAt = Environment.TickCount64;
        talkAttempts = typingRestarts = 0;
        Status = $"Loop started for {Runs} run(s).";
        Plugin.Log.Information("XivType loop started for {Runs} runs", Runs);
    }

    internal void Stop(string reason)
    {
        if (!Active) return;
        Active = false;
        Status = reason;
        Plugin.Log.Information("XivType loop stopped: {Reason}", reason);
    }

    internal void Update(long now)
    {
        if (!Active) return;
        var territory = Plugin.Client.TerritoryType;
        if (territory == Activity)
        {
            if (stage != Stage.InRun) { stage = Stage.InRun; stageSince = now; typingRestarts = 0; }
            Status = $"Run {Completed + 1}/{Runs}: playing.";
            // Leave the results screen after a short look rather than waiting about a minute.
            if (Visible("KTGResult"))
            {
                resultSeenAt = resultSeenAt == 0 ? now : resultSeenAt;
                if (now - resultSeenAt >= 3000 && now >= nextStepAt && Click("KTGResult", 1)) nextStepAt = now + 3000;
                Status = $"Run {Completed + 1}/{Runs}: results, exiting.";
                return;
            }
            resultSeenAt = 0;
            // Typing stops on its own errors; restart it a few times per run, but never after the user stopped it.
            if (!typing.Running && now >= nextStepAt && typingRestarts < 5)
            {
                typing.Request("on");
                typingRestarts++;
                nextStepAt = now + 3000;
            }
            return;
        }
        if (stage == Stage.InRun)
        {
            Completed++;
            Plugin.Log.Information("XivType loop finished run {Completed}/{Runs}", Completed, Runs);
            if (Completed >= Runs) { Stop($"Loop finished {Completed} run(s)."); return; }
            Begin(Stage.Talk, now, 5000);
        }
        if (territory != Lobby) { Status = $"Run {Completed + 1}/{Runs}: waiting for the lobby."; return; }
        if (now < nextStepAt) return;

        // Other clients name the duty differently; there, accept Duty Ready only right after registering here.
        var dutyReady = ContentsFinderReady() ||
            (Plugin.Client.ClientLanguage.ToString() != "English" && stage == Stage.Queued && Visible("ContentsFinderConfirm"));
        if (dutyReady && Click("ContentsFinderConfirm", 8))
        {
            Status = $"Run {Completed + 1}/{Runs}: entering.";
            Begin(Stage.Entering, now, 3000);
            return;
        }
        if (stage == Stage.Entering && now - stageSince < 60000) return;
        if (Click("KTGEnterDialogue", 0))
        {
            Status = $"Run {Completed + 1}/{Runs}: registered, waiting for Duty Ready.";
            Begin(Stage.Queued, now, 2000);
            return;
        }
        if (stage == Stage.Queued)
        {
            if (now - stageSince < 300000) return;
            Stop("Duty Ready did not appear within 5 minutes.");
            return;
        }
        Talk(now);
    }

    private void Begin(Stage next, long now, long delay)
    {
        stage = next;
        stageSince = now;
        nextStepAt = now + delay;
        if (next == Stage.Talk) talkAttempts = 0;
    }

    private void Talk(long now)
    {
        if (talkAttempts >= 3) { Stop("The Keybound Registrar did not open its dialogue."); return; }
        var npc = Plugin.Objects.FirstOrDefault(o => o.BaseId == RegistrarBaseId);
        var player = Plugin.Objects.LocalPlayer;
        if (npc == null || player == null) { Stop("Keybound Registrar not found. Stand next to it to loop."); return; }
        if (System.Numerics.Vector3.Distance(npc.Position, player.Position) > 6) { Stop("Stand next to the Keybound Registrar to loop."); return; }
        var targets = TargetSystem.Instance();
        if (targets == null) return;
        targets->InteractWithObject((GameObject*)npc.Address, true);
        talkAttempts++;
        Status = $"Run {Completed + 1}/{Runs}: talking to the Keybound Registrar.";
        Plugin.Log.Information("XivType loop talking to the Keybound Registrar (attempt {Attempt})", talkAttempts);
        Begin(Stage.Register, now, 2000);
    }

    private static bool Visible(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        return addon != null && addon->IsVisible;
    }

    // Only accept Duty Ready for this activity.
    private static bool ContentsFinderReady()
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("ContentsFinderConfirm");
        if (addon == null || !addon->IsVisible || addon->AtkValuesCount < 2) return false;
        var name = addon->AtkValues[1];
        return name.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString &&
            name.String.Value != null && name.String.ToString() is "Keybound Brawler" or "キーバウンド・ブロウラー";
    }

    private static bool Click(string addonName, uint param)
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(addonName);
        if (addon == null || !addon->IsVisible) return false;
        var buttonEvent = FindEvent(&addon->UldManager, (AtkEventListener*)addon, param, 0);
        if (buttonEvent == null) return false;
        var data = new AtkEventData();
        addon->ReceiveEvent(AtkEventType.ButtonClick, (int)param, buttonEvent, &data);
        Plugin.Log.Information("XivType loop clicked {Addon} button {Param}", addonName, param);
        return true;
    }

    private static AtkEvent* FindEvent(AtkUldManager* manager, AtkEventListener* listener, uint param, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > 6) return null;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 512); i++)
        {
            var node = manager->NodeList[i];
            if (node == null) continue;
            for (var e = node->AtkEventManager.Event; e != null; e = e->NextEvent)
                if (e->State.EventType == AtkEventType.ButtonClick && e->Param == param && e->Listener == listener) return e;
            if ((int)node->Type >= 1000 && ((AtkComponentNode*)node)->Component != null &&
                FindEvent(&((AtkComponentNode*)node)->Component->UldManager, listener, param, depth + 1) is var found && found != null)
                return found;
        }
        return null;
    }
}
