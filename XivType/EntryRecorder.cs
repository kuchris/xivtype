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
    }

    internal static void Unregister() => Plugin.Lifecycle.UnregisterListener(OnAddon);

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
        Plugin.Log.Information("XivType entry {Event} {Addon}: {Values}", type, args.AddonName, string.Join(" | ", strings));
        LogContext(args.AddonName);
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
