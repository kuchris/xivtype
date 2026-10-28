using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

internal readonly record struct InputPermission(bool Allowed, int LockSeconds, string Reason);

internal static unsafe class InputAvailability
{
    internal static InputPermission Read(AtkUnitBase* addon, bool componentEnabled)
    {
        if (addon == null || addon->AtkValues == null || addon->AtkValuesCount < 5 ||
            addon->AtkValues[2].Type != AtkValueType.Bool ||
            addon->AtkValues[4].Type is not (AtkValueType.Int or AtkValueType.UInt))
            return new(false, 0, "Waiting - activity input permission is unavailable.");
        var seconds = Math.Max(0, addon->AtkValues[4].Int);
        if (seconds > 0) return new(false, seconds, $"Paused - typing prohibited ({seconds}s remaining).");
        if (!addon->AtkValues[2].Bool) return new(false, 0, "Paused - the activity prohibits typing.");
        return componentEnabled ? new(true, 0, "Ready.") : new(false, 0, "Waiting - the activity disabled its input field.");
    }
}
