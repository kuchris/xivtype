using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Bounded recording of how the user enters the activity: every window that opens, its text and AtkValue
// strings, plus the current target and player position. Read-only; used to design the run loop.
internal static unsafe class EntryRecorder
{
    private static long until;
    internal static bool Active => Environment.TickCount64 < until;

    internal static void Register()
    {
        Plugin.Lifecycle.RegisterListener(AddonEvent.PostSetup, OnAddon);
        Plugin.Lifecycle.RegisterListener(AddonEvent.PostRefresh, OnAddon);
        Plugin.Lifecycle.RegisterListener(AddonEvent.PreReceiveEvent, OnEvent);
    }

    // Button clicks: the event type and parameter are what an automated click must reproduce.
    private static void OnEvent(AddonEvent type, AddonArgs args)
    {
        if (!Active || args is not AddonReceiveEventArgs click || !Relevant(args.AddonName)) return;
        var eventType = (AtkEventType)(byte)click.AtkEventType;
        if (eventType is AtkEventType.MouseOver or AtkEventType.MouseOut or AtkEventType.MouseMove) return;
        Plugin.Log.Information("XivType entry click {Addon}: type={Type} ({Raw}) param={Param}", args.AddonName, eventType, (byte)click.AtkEventType, click.EventParam);
    }

    private static bool Relevant(string name) =>
        name.StartsWith("KTG", StringComparison.Ordinal) || name.StartsWith("Select", StringComparison.Ordinal) ||
        name is "Talk" or "ContentsFinderConfirm" or "JournalAccept";

    internal static void Unregister() => Plugin.Lifecycle.UnregisterListener(OnAddon, OnEvent);

    internal static void Start(int seconds)
    {
        until = Environment.TickCount64 + seconds * 1000L;
        Plugin.Log.Information("XivType entry recording started for {Seconds}s", seconds);
        LogContext("start");
    }

    private static void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (!Active) return;
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;
        var strings = new List<string>();
        for (var i = 0; i < Math.Min((int)addon->AtkValuesCount, 64); i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString && value.String.Value != null)
            {
                var text = value.String.ToString();
                if (!string.IsNullOrWhiteSpace(text)) strings.Add($"[{i}]{text}");
            }
            else if (value.Type is AtkValueType.Int or AtkValueType.UInt && i < 8) strings.Add($"[{i}]#{(value.Type == AtkValueType.Int ? value.Int : (long)value.UInt)}");
        }
        if (type == AddonEvent.PostSetup && Relevant(args.AddonName))
        {
            var texts = new List<string>();
            CollectText(&addon->UldManager, texts, 0);
            strings.Add("texts: " + string.Join(" / ", texts));
        }
        Plugin.Log.Information("XivType entry {Event} {Addon}: {Values}", type, args.AddonName, string.Join(" | ", strings));
        LogContext(args.AddonName);
    }

    private static void CollectText(AtkUldManager* manager, List<string> texts, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > 8) return;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 512); i++)
        {
            var node = manager->NodeList[i];
            if (node == null) continue;
            if (node->Type == NodeType.Text)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(text)) texts.Add($"{node->NodeId}:{text}");
            }
            else if ((int)node->Type >= 1000 && ((AtkComponentNode*)node)->Component != null)
                CollectText(&((AtkComponentNode*)node)->Component->UldManager, texts, depth + 1);
        }
    }

    private static void LogContext(string reason)
    {
        var target = Plugin.Targets.Target;
        var player = Plugin.Objects.LocalPlayer;
        Plugin.Log.Information("XivType entry context ({Reason}): territory={Territory}; player={Position}; target={Name} kind={Kind} baseId={BaseId} entity={Entity:X8} at {TargetPosition}",
            reason, Plugin.Client.TerritoryType, player?.Position.ToString() ?? "none", target?.Name.TextValue ?? "none",
            target?.ObjectKind.ToString() ?? "-", target?.BaseId ?? 0, target?.EntityId ?? 0, target?.Position.ToString() ?? "-");
    }
}
