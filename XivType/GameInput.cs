using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Background route: keystroke messages posted to FFXIV's own window, never global SendInput.
// Writing text straight into the component (0.1.9-0.1.26) was accepted and scored, but every run
// on that route was expelled for inactivity while physical keystrokes were not (2026-10-28).
internal static unsafe partial class GameInput
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
                return new(false, 0, $"Paused - an enemy is using {EnemyInputLock.Name(Plugin.EnemyLock.ActionId)}.");
            return TryGet(out var addon, out var input)
                ? InputAvailability.Read(addon, input->Enabled)
                : new(false, 0, "Waiting - activity input component is unavailable.");
        }
    }

    // Posted characters reach whatever FFXIV has focused, so require the activity input to be the active field.
    private static bool Ready(out AtkComponentTextInput* input)
    {
        input = null;
        return !Plugin.EnemyLock.Blocked && TryGet(out var addon, out input) && input->IsActive &&
            InputAvailability.Read(addon, input->Enabled).Allowed;
    }

    internal static bool TypeCharacter(char value)
    {
        if (!Ready(out _) || !NativeInput.VirtualKey(value, out var key, out var scan)) return false;
        EditOperations++;
        return Post(key, scan, value);
    }

    internal static bool Backspace()
    {
        if (!Ready(out _)) return false;
        EditOperations++;
        return Post(0x08, 0x0E, (char)0x08);
    }

    internal static bool Submit(string expected)
    {
        if (!Ready(out var input) || !input->EvaluatedString.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        SubmitOperations++;
        return Post(0x0D, 0x1C, (char)0x0D);
    }

    private static bool Post(ushort key, uint scan, char value)
    {
        using var process = Process.GetCurrentProcess();
        var window = process.MainWindowHandle;
        if (window == 0) return false;
        var down = (nint)(1 | scan << 16);
        var up = unchecked((nint)(0xC0000001u | scan << 16));
        return PostMessageW(window, 0x100, key, down) && PostMessageW(window, 0x102, value, down) &&
            PostMessageW(window, 0x101, key, up);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nuint key, nint parameter);
}
