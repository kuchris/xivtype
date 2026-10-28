using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

// Keybound Brawler hides the chat log windows. When enabled, re-show the ones that were visible in the lobby,
// and keep the chat input from taking focus there so Enter keeps opening the activity input instead.
internal static unsafe class ActivityChat
{
    private static readonly string[] Names = ["ChatLog", "ChatLogPanel_0", "ChatLogPanel_1", "ChatLogPanel_2", "ChatLogPanel_3"];
    private static readonly HashSet<string> lobbyVisible = [];
    private static long next;
    private static int reshown;

    internal static string Describe()
    {
        var parts = new List<string>();
        foreach (var name in Names)
        {
            var a = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
            if (a == null) { parts.Add($"{name}=missing"); continue; }
            var root = a->RootNode;
            parts.Add($"{name}: visible={a->IsVisible} visFlags=0x{a->VisibilityFlags:X} hideCount={a->UiFlagsHideCount} showHide=0x{a->ShowHideFlags:X} alpha={a->Alpha} pos=({a->X},{a->Y}) scale={a->Scale} " +
                $"root={(root == null ? "null" : $"{root->IsVisible()}/{root->Color.A}")} ignoreDisplayMode={a->IgnoreUIDisplayMode}");
        }
        return string.Join(" || ", parts);
    }

    internal static void Update(long now, bool enabled)
    {
        var territory = Plugin.Client.TerritoryType;
        if (territory == 1360)
        {
            if (now < next) return;
            next = now + 250;
            // Remember the user's own chat layout; panels they turned off stay off.
            lobbyVisible.Clear();
            foreach (var name in Names)
            {
                var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
                if (addon != null && addon->IsVisible) lobbyVisible.Add(name);
            }
            reshown = 0;
            return;
        }
        if (!enabled || territory != 1359) return;
        // Loaded mid-activity: the lobby layout is unknown, so restore only the main chat log.
        // Enforced every frame: the activity clears the visible flag again each frame.
        foreach (var name in lobbyVisible.Count > 0 ? lobbyVisible : ["ChatLog", "ChatLogPanel_0"])
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
            if (addon == null || addon->RootNode == null) continue;
            // The activity hides chat by clearing visibility and fading the root node to alpha 0.
            if (addon->IsVisible && addon->RootNode->IsVisible() && addon->RootNode->Color.A == 255) continue;
            addon->ShowHideFlags = 0;
            addon->IsVisible = true;
            addon->RootNode->ToggleVisibility(true);
            addon->RootNode->Color.A = 255;
            if (reshown++ < 10) Plugin.Log.Information("XivType showing {Addon} during the activity", name);
        }
        if (GameInput.Focus == GameInput.TextFocus.Other)
        {
            var module = RaptureAtkModule.Instance();
            if (module == null) return;
            module->AtkModule.ClearFocus();
            Plugin.Log.Information("XivType cleared chat input focus during the activity");
        }
    }
}
