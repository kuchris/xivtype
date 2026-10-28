using System.Text.Json;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivType;

internal static unsafe class WordArrayDiagnostics
{
    internal static string? Capture(AtkUnitBase* addon, HashSet<uint> actors)
    {
        var words = PromptReader.ReadWords(addon).Select(p => p.Text).ToHashSet(StringComparer.Ordinal);
        return Capture(words, actors);
    }

    internal static string? Capture(HashSet<string> words, HashSet<uint> actors)
    {
        var stage = AtkStage.Instance();
        if (words.Count == 0 || stage == null || stage->AtkArrayDataHolder == null) return null;
        var holder = stage->AtkArrayDataHolder;
        var numbers = new List<object>();
        var strings = new List<object>();
        // New activity arrays are beyond the older named HUD/chat array ranges.
        for (var n = 127; holder->NumberArrays != null && n < Math.Min((int)holder->NumberArrayCount, 512); n++)
        {
            var data = holder->NumberArrays[n];
            if (data == null || data->IntArray == null || data->Size is < 1 or > 4096) continue;
            var values = new ReadOnlySpan<int>(data->IntArray, data->Size).ToArray();
            var matches = values.Select((value, index) => new { value, index }).Where(p => actors.Contains((uint)p.value)).ToArray();
            if (matches.Length > 0) numbers.Add(new { Index = n, Size = data->Size, Matches = matches, Values = values });
        }
        for (var n = 127; holder->StringArrays != null && n < Math.Min((int)holder->StringArrayCount, 512); n++)
        {
            var data = holder->StringArrays[n];
            if (data == null || data->StringArray == null || data->Size is < 1 or > 1024) continue;
            for (var i = 0; i < data->Size; i++)
            {
                var text = data->StringArray[i].ToString();
                if (words.Contains(text)) strings.Add(new { Array = n, Index = i, Text = text });
            }
        }
        return JsonSerializer.Serialize(new { NumberCount = holder->NumberArrayCount, StringCount = holder->StringArrayCount,
            Numbers = numbers, Strings = strings }, new JsonSerializerOptions { WriteIndented = true });
    }
}
