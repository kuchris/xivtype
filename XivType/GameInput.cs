using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Background route guard and counters. Writing text straight into the component (0.1.9-0.1.26)
// scored, but every run on that route was expelled for inactivity while physical keystrokes were not
// (2026-10-28). Background typing now posts held keystrokes to FFXIV's window through NativeInput.
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
        return $"TextInput; ownerMatches={input->OwnerAddon == addon}; callback={input->Callback != null}; target={TargetName(input)}; textInputActive={RaptureAtkModule.Instance()->AtkModule.IsTextInputActive()}; enabled={input->Enabled}";
    }

    internal static InputPermission Permission
    {
        get
        {
            if (Plugin.EnemyLock.Blocked)
                return new(false, 0, $"Paused - an enemy is using {EnemyInputLock.Name(Plugin.EnemyLock.ActionId)}.");
            return TryGet(out var addon, out var input)
                ? InputAvailability.Read(addon, input->Enabled)
                : new(false, 0, "Waiting - activity input component is unavailable.");
        }
    }

    // Keystrokes posted to the window reach any focused FFXIV text field (chat included). The activity
    // itself reads the keyboard with no text field focused (target=none live), so refuse only a foreign target.
    internal static bool CanPost()
    {
        if (Plugin.EnemyLock.Blocked || !TryGet(out var addon, out var input) ||
            !InputAvailability.Read(addon, input->Enabled).Allowed) return false;
        var module = RaptureAtkModule.Instance();
        if (module == null) return false;
        var target = module->AtkModule.TextInput.TargetTextInputEventInterface;
        return target == null || target == &input->AtkTextInputEventInterface;
    }

    internal enum TextFocus { None, Activity, Other }

    // The activity input must be activated with Enter before it accepts characters (user, 2026-10-28).
    internal static TextFocus Focus
    {
        get
        {
            var module = RaptureAtkModule.Instance();
            if (module == null || !TryGet(out _, out var input)) return TextFocus.Other;
            var target = module->AtkModule.TextInput.TargetTextInputEventInterface;
            return target == null ? TextFocus.None : target == &input->AtkTextInputEventInterface ? TextFocus.Activity : TextFocus.Other;
        }
    }

    private static string TargetName(AtkComponentTextInput* input)
    {
        var module = RaptureAtkModule.Instance();
        var target = module == null ? null : module->AtkModule.TextInput.TargetTextInputEventInterface;
        return target == null ? "none" : target == &input->AtkTextInputEventInterface ? "activity" : "other";
    }

    // Japanese client: the 0.1.9-0.1.26 direct write, kept for text posted keys cannot produce (kana). It follows posted
    // romaji keys and precedes a posted Enter, so the duty still sees keyboard activity (see JapaneseInput).
    internal static bool WriteText(string text)
    {
        if (Plugin.EnemyLock.Blocked || !TryGet(out var addon, out var input) || !InputAvailability.Read(addon, input->Enabled).Allowed)
        {
            Plugin.Log.Information("XivType direct write '{Text}' refused: input locked", text);
            return false;
        }
        EditOperations++;
        input->SetText(text);
        input->Callback(addon, InputCallbackType.TextChanged,
            input->RawString.StringPtr, input->EvaluatedString.StringPtr, input->CallbackEventKind);
        Plugin.Log.Information("XivType direct write '{Text}' -> evaluated '{Evaluated}'", text, input->EvaluatedString.ToString());
        return true;
    }

    internal static void CountEdit() => EditOperations++;
    internal static void CountSubmit() => SubmitOperations++;
}
