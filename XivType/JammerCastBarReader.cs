using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

internal static unsafe class JammerCastBarReader
{
    internal static string[] ReadLabels(AtkUnitBase* addon, Func<nint, bool>? shown = null)
    {
        if (addon == null) return [];
        shown ??= PromptReader.NativeShown;
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<nint>();
        Read(&addon->UldManager, shown, labels, visited, 0);
        return labels.Order(StringComparer.Ordinal).ToArray();
    }

    // Returns a representative action ID for a visible locking caption, or 0.
    internal static uint LockingAction(IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            if (label.Contains("Dynamic Sensory Jammer", StringComparison.OrdinalIgnoreCase)) return 49532;
            if (label.Contains("Cursed Gaze", StringComparison.OrdinalIgnoreCase)) return 49529;
        }
        return 0;
    }

    private static void Read(AtkUldManager* manager, Func<nint, bool> shown, HashSet<string> labels,
        HashSet<nint> visited, int depth)
    {
        if (manager == null || manager->NodeList == null || depth > 12 || visited.Count >= 4096) return;
        for (var i = 0; i < Math.Min((int)manager->NodeListCount, 4096); i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node) || !shown((nint)node)) continue;
            // Live KTGEnemyCastBar capture: text node 4 is the action caption (e.g. Burst).
            // Node 3 is the larger auxiliary label, not the active action name.
            if (node->Type == NodeType.Text && node->NodeId == 4)
            {
                var text = ((AtkTextNode*)node)->NodeText.ToString();
                if (!string.IsNullOrWhiteSpace(text)) labels.Add(text);
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) Read(&component->UldManager, shown, labels, visited, depth + 1);
            }
        }
    }
}
