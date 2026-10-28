using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

internal sealed record Prompt(uint NodeId, string Text, bool Masked = false,
    float Depth = 0, float Depth2 = 0, float ScreenX = 0, float ScreenY = 0, float Scale = 1,
    uint EntityId = 0, float? Distance = null);

internal static unsafe class PromptReader
{
    internal static Prompt[] ReadWords(AtkUnitBase* addon, Func<nint, bool>? shown = null)
    {
        shown ??= NativeShown;
        var words = new List<Prompt>();
        if (addon == null) return [];
        var manager = &addon->UldManager;
        if (manager->NodeList == null) return [];
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 4096); i++)
        {
            var node = manager->NodeList[i];
            if (node == null || (int)node->Type < 1000 || !shown((nint)node)) continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null) continue;
            var label = FindNode(&component->UldManager, 20);
            if (label == null || label->Type != NodeType.Text) continue;
            var labelVisible = shown((nint)label);
            // The noise mechanic hides the normal label while the active prompt container
            // and its replacement image tiles remain visible. Hidden labels on inactive
            // components are stale and must never become candidates.
            var masked = !labelVisible && HasNoiseTiles(&component->UldManager, shown);
            if (!labelVisible && !masked) continue;
            var word = ((AtkTextNode*)label)->NodeText.ToString();
            if (word.Length is > 0 and <= 160 && word.All(c => c is >= ' ' and <= '~' || char.IsLetter(c)))
                words.Add(new(node->NodeId, word, masked, node->Depth, node->Depth_2,
                    node->ScreenX + node->Width * node->ScaleX / 2, node->ScreenY, node->ScaleX));
        }
        return words.ToArray();
    }

    private static bool HasNoiseTiles(AtkUldManager* manager, Func<nint, bool> shown)
    {
        var content = FindNode(manager, 4);
        var tiles = FindNode(manager, 10);
        if (content == null || tiles == null || !shown((nint)content) || !shown((nint)tiles)) return false;
        // Shipped KTGEWord ULD maps only 17 and 18 to KTGWordNoise.tex.
        // Nodes 11-16 are ordinary aura effects and cannot justify reading a hidden label.
        for (uint id = 17; id <= 18; id++)
        {
            var image = FindNode(manager, id);
            if (image != null && image->Type == NodeType.Image && shown((nint)image)) return true;
        }
        return false;
    }

    internal static AtkResNode* FindNode(AtkUldManager* manager, uint id)
    {
        if (manager == null || manager->NodeList == null) return null;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 4096); i++)
            if (manager->NodeList[i] != null && manager->NodeList[i]->NodeId == id) return manager->NodeList[i];
        return null;
    }

    internal static bool NativeShown(nint address)
    {
        var node = (AtkResNode*)address;
        for (var depth = 0; node != null && depth < 32; depth++, node = node->ParentNode)
            if (!node->IsVisible()) return false;
        return node == null;
    }
}
