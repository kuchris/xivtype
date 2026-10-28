using Lumina;
using System.Reflection;
using System.Text.Json;

var game = new GameData(@"D:\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack");
var assembly = typeof(GameData).Assembly;
var rows = new List<object>();
foreach (var inspectType in new[] { typeof(Lumina.Excel.Sheets.KTGBNpc), typeof(Lumina.Excel.Sheets.KTGParam), typeof(Lumina.Excel.Sheets.VFX) })
    Console.WriteLine(inspectType.Name + " fields: " + string.Join(", ", inspectType.GetProperties().Select(p => p.Name + ":" + p.PropertyType.Name)));
Console.WriteLine("Relevant sheets: " + string.Join(", ", typeof(Lumina.Excel.Sheets.Status).Assembly.GetTypes()
    .Where(t => t.Namespace == "Lumina.Excel.Sheets" && System.Text.RegularExpressions.Regex.IsMatch(t.Name, "KTG|Typing|VFX|Keybound", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
    .Select(t => t.Name)));
foreach (var action in game.Excel.GetSheet<Lumina.Excel.Sheets.Action>())
{
    var name = action.Name.ExtractText();
    if (action.RowId is >= 49516 and <= 49555 or >= 49690 and <= 49720)
        Console.WriteLine($"KTG ACTION {action.RowId}: {name}; cast={action.Cast100ms}");
    if (System.Text.RegularExpressions.Regex.IsMatch(name, @"typing|sensory|jammer|prohib|keybound|keyboard", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        Console.WriteLine($"ACTION {action.RowId}: {name}");
    if (action.RowId is 49532 or 49702)
        foreach (var property in typeof(Lumina.Excel.Sheets.Action).GetProperties().Where(p => p.PropertyType.IsPrimitive))
            Console.WriteLine($"  {property.Name}={property.GetValue(action)}");
}
foreach (var status in game.Excel.GetSheet<Lumina.Excel.Sheets.Status>())
{
    var name = status.Name.ExtractText();
    var description = status.Description.ExtractText();
    if (System.Text.RegularExpressions.Regex.IsMatch(name + " " + description,
            @"typing|keyboard|keybound|magityp|sensory|input|入力|タイピング", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
    {
        Console.WriteLine($"STATUS {status.RowId}: {name}: {description}");
        rows.Add(new { status.RowId, Name = name, Description = description, status.Icon });
    }
}
Directory.CreateDirectory("docs/ui-inspection");
File.WriteAllText("docs/ui-inspection/typing-statuses.json", JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
var type = assembly.GetTypes().FirstOrDefault(t => t.Name == "UldFile");
Console.WriteLine(type?.FullName ?? "No UldFile type.");
if (type == null) return;
var get = typeof(GameData).GetMethods().First(m => m.Name == "GetFile" && m.IsGenericMethodDefinition &&
    m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)).MakeGenericMethod(type);
foreach (var name in new[] { "KTGEWord", "KTGEnemyCastBar", "KTGIGaugeHp" })
{
    var file = get.Invoke(game, [$"ui/uld/{name.ToLowerInvariant()}.uld"]);
    Console.WriteLine($"{name}: {(file == null ? "missing" : "loaded")}");
    if (file == null) continue;
    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine($"  {field.Name}: {field.FieldType}");
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .ToDictionary(f => f.Name, f => Plain(f.GetValue(file), 0));
    var json = JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true });
    Directory.CreateDirectory("docs/ui-inspection");
    File.WriteAllText($"docs/ui-inspection/{name}.json", json);
}

static object? Plain(object? value, int depth)
{
    if (value == null || depth > 24) return null;
    var type = value.GetType();
    if (value is string || type.IsPrimitive || type.IsEnum || value is decimal) return value;
    if (value is char[] chars) return new string(chars);
    if (value is System.Collections.IEnumerable sequence)
        return sequence.Cast<object?>().Select(v => Plain(v, depth + 1)).ToArray();
    return type.GetFields(BindingFlags.Public | BindingFlags.Instance)
        .ToDictionary(f => f.Name, f => Plain(f.GetValue(value), depth + 1));
}
