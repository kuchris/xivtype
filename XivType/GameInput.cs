using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Target the activity's own input component on the framework thread. These operations
// never emit OS keyboard events and are independent of the foreground application.
internal static unsafe class GameInput
{
    internal static long EditOperations { get; private set; }
    internal static long SubmitOperations { get; private set; }
    private static bool TryGet(out AtkUnitBase* addon, out AtkComponentTextInput* input)
    {
        input = null;
        addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("KTGInput");
        if (Plugin.Client.TerritoryType != 1359 || addon == null || !addon->IsVisible) return false;
        var node = addon->GetComponentNodeById(7);
        if (node == null || node->Component == null || node->Component->GetComponentType() != ComponentType.TextInput) return false;
        input = (AtkComponentTextInput*)node->Component;
        return input->OwnerAddon == addon && input->Callback != null;
    }

    internal static string Describe()
    {
        var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("KTGInput");
        if (addon == null || !addon->IsVisible) return "Activity input not visible.";
        var node = addon->GetComponentNodeById(7);
        if (node == null || node->Component == null) return "Activity input component missing.";
        var type = node->Component->GetComponentType();
        if (type != ComponentType.TextInput) return $"Input component type: {type}.";
        var input = (AtkComponentTextInput*)node->Component;
        return $"TextInput; ownerMatches={input->OwnerAddon == addon}; callback={input->Callback != null}; active={input->IsActive}; enabled={input->Enabled}";
    }

    internal static InputPermission Permission
    {
        get
        {
            if (Plugin.EnemyLock.Blocked)
                return new(false, 0, "Paused - an enemy is using Dynamic Sensory Jammer.");
            return TryGet(out var addon, out var input)
                ? InputAvailability.Read(addon, input->Enabled)
                : new(false, 0, "Waiting - activity input component is unavailable.");
        }
    }

    internal static bool ChangeText(string previous, string next)
    {
        if (Plugin.EnemyLock.Blocked || !TryGet(out var addon, out var input) || !InputAvailability.Read(addon, input->Enabled).Allowed) return false;
        var node = PromptReader.FindNode(&input->UldManager, 16);
        if (node == null || node->Type != NodeType.Text ||
            !((AtkTextNode*)node)->NodeText.ToString().Equals(previous, StringComparison.OrdinalIgnoreCase)) return false;
        EditOperations++;
        input->SetText(next);
        input->Callback(addon, InputCallbackType.TextChanged,
            input->RawString.StringPtr, input->EvaluatedString.StringPtr, input->CallbackEventKind);
        return true;
    }

    internal static bool Submit(string expected)
    {
        if (Plugin.EnemyLock.Blocked || !TryGet(out var addon, out var input) || !InputAvailability.Read(addon, input->Enabled).Allowed) return false;
        if (!input->EvaluatedString.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        SubmitOperations++;
        var result = input->Callback(addon, InputCallbackType.Enter,
            input->RawString.StringPtr, input->EvaluatedString.StringPtr, input->CallbackEventKind);
        if (result == InputCallbackResult.ClearText)
        {
            input->SetText("");
            input->UpdateCharacterCount(0, 0);
        }
        return true;
    }
}
