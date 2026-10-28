using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace XivType;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle Lifecycle { get; private set; } = null!;
    internal static bool KeyboardCaptured { get; private set; }
    internal static EnemyInputLock EnemyLock { get; } = new();
    internal static string[] EnemyCastLabels { get; private set; } = [];

    private bool visible;
    private bool watch;
    private bool dumpRequested;
    private bool faulted;
    private string selected = "";
    private string status = "Select the activity addon, then capture its text.";
    private string snapshot = "";
    private string lastSnapshot = "";
    private string inventory = "";
    private string[] addons = [];
    private long nextScan;
    private long nextCapture;
    private long nextActivityCapture;
    private bool captureActivity = true;
    private string lastActivity = "";
    private readonly string output = Path.Combine(Interface.GetPluginConfigDirectory(), "diagnostics");
    private readonly TypingController typing;
    private readonly DiagnosticStore diagnostics;
    private readonly Configuration config;
    private long nextActorCapture;
    private long nextWordArrayCapture;

    public Plugin()
    {
        diagnostics = new DiagnosticStore(output);
        config = Interface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Sanitize();
        typing = new TypingController(diagnostics, config);
        Commands.AddHandler("/xivtype", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open XivType. /xivtype on | once | off | status | dump <addon name>",
        });
        Framework.Update += Update;
        Interface.UiBuilder.Draw += Draw;
        Interface.UiBuilder.OpenMainUi += Open;
        Interface.UiBuilder.OpenConfigUi += Open;
        Log.Information("XivType diagnostics ready. Output: {Output}", output);
    }

    private void Open() => visible = true;

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();
        if (args.Equals("on", StringComparison.OrdinalIgnoreCase)) typing.Request("on");
        else if (args.Equals("once", StringComparison.OrdinalIgnoreCase)) typing.Request("once");
        else if (args.Equals("status", StringComparison.OrdinalIgnoreCase)) Chat.Print($"[XivType] {typing.Status}");
        else if (args.Equals("stop", StringComparison.OrdinalIgnoreCase) || args.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            watch = false;
            captureActivity = false;
            typing.Request("off");
            dumpRequested = false;
            Chat.Print("[XivType] Typing and capture stopped.");
        }
        else if (args.StartsWith("dump ", StringComparison.OrdinalIgnoreCase))
        {
            selected = args[5..].Trim();
            dumpRequested = true;
        }
        else
        {
            nextScan = 0;
            Open();
        }
    }

    // Only the framework callback reads native UI pointers; only managed strings reach Draw.
    private unsafe void Update(IFramework framework)
    {
        NativeInput.Update(Environment.TickCount64);
        if (faulted) return;
        if (!Client.IsLoggedIn)
        {
            if (typing.Running) typing.Stop("Stopped after logout.");
            return;
        }
        var now = Environment.TickCount64;
        try
        {
            var casts = Client.TerritoryType == 1359
                ? Objects.OfType<IBattleChara>().Where(o => o.ObjectKind == ObjectKind.BattleNpc)
                    .Select(o => new EnemyCast(o.CastActionId, o.IsCasting)).ToArray()
                : [];
            var castAddon = GameGui.GetAddonByName<AtkUnitBase>("KTGEnemyCastBar");
            EnemyCastLabels = Client.TerritoryType == 1359 && castAddon != null && castAddon->IsVisible
                ? JammerCastBarReader.ReadLabels(castAddon) : [];
            var uiLock = JammerCastBarReader.LockingAction(EnemyCastLabels);
            EnemyLock.Observe(now, Client.TerritoryType == 1359,
                uiLock != 0 ? casts.Append(new EnemyCast(uiLock, true)).ToArray() : casts);
            if (Client.TerritoryType == 1359 && now >= nextActorCapture)
            {
                nextActorCapture = now + 1000;
                var actors = Objects.Where(o => o.ObjectKind is ObjectKind.BattleNpc or ObjectKind.EventNpc or ObjectKind.EventObj)
                    .Select(o => new { Kind = o.ObjectKind.ToString(), Name = o.Name.TextValue, o.BaseId, o.EntityId, o.ObjectIndex,
                        X = o.Position.X, Y = o.Position.Y, Z = o.Position.Z,
                        Casting = o is IBattleChara b && b.IsCasting,
                        Action = o is IBattleChara c ? c.CastActionId : 0 }).ToArray();
                diagnostics.Write("activity-actors.json", JsonSerializer.Serialize(actors, new JsonSerializerOptions { WriteIndented = true }));
            }
            typing.Update(now);
            if (Client.TerritoryType == 1359 && now >= nextWordArrayCapture)
            {
                nextWordArrayCapture = now + 5000;
                var wordAddon = GameGui.GetAddonByName<AtkUnitBase>("KTGEWord");
                if (wordAddon != null && WordArrayDiagnostics.Capture(wordAddon,
                    Objects.Where(o => o.ObjectKind == ObjectKind.BattleNpc).Select(o => o.EntityId).ToHashSet()) is { } data)
                    diagnostics.Write("word-array-map.json", data);
            }
            else if (Client.TerritoryType != 1359 && now >= nextWordArrayCapture)
            {
                nextWordArrayCapture = now + 5000;
                var requestPath = Path.Combine(output, "word-array-probe-request.json");
                if (File.Exists(requestPath))
                {
                    using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
                    var words = request.RootElement.GetProperty("Words").EnumerateArray().Select(v => v.GetString()!).ToHashSet();
                    var actors = request.RootElement.GetProperty("Actors").EnumerateArray().Select(v => v.GetUInt32()).ToHashSet();
                    if (WordArrayDiagnostics.Capture(words, actors) is { } data) diagnostics.Write("word-array-map.json", data);
                    File.Move(requestPath, requestPath + ".processed", true);
                }
            }
            if (captureActivity && now >= nextActivityCapture)
            {
                nextActivityCapture = now + 250;
                CaptureActivity();
            }
            if (now >= nextScan)
            {
                nextScan = now + 1000;
                var stage = AtkStage.Instance();
                if (stage == null || stage->RaptureAtkUnitManager == null) return;
                var list = &stage->RaptureAtkUnitManager->AllLoadedUnitsList;
                var found = new List<string>();
                for (var i = 0; i < Math.Min((int)list->Count, 256); i++)
                {
                    var addon = list->Entries[i].Value;
                    if (addon != null && addon->IsVisible)
                        found.Add(addon->NameString);
                }
                addons = found.Distinct().Order(StringComparer.Ordinal).ToArray();
                var current = $"Language={Client.ClientLanguage}; Territory={Client.TerritoryType}\n" + string.Join('\n', addons);
                if (current != inventory)
                {
                    inventory = current;
                    diagnostics.Write("visible-addons.txt", current);
                    Log.Information("XivType visible addons: {Inventory}", current.Replace('\n', ' '));
                }
            }
            if (!dumpRequested && (!watch || now < nextCapture)) return;
            dumpRequested = false;
            nextCapture = now + 250;
            if (string.IsNullOrWhiteSpace(selected)) return;
            // Never capture chat or unrelated player text automatically: the addon is selected explicitly.
            var target = GameGui.GetAddonByName<AtkUnitBase>(selected);
            if (target == null || !target->IsVisible)
            {
                status = $"{selected} is not visible.";
                return;
            }
            var builder = new StringBuilder();
            builder.AppendLine($"Addon={selected}; Language={Client.ClientLanguage}; Territory={Client.TerritoryType}");
            if (target->AtkValues != null)
                for (var i = 0; i < Math.Min((int)target->AtkValuesCount, 512); i++)
                {
                    var value = target->AtkValues[i];
                    var type = value.Type.ToString();
                    builder.AppendLine(type.Contains("String")
                        ? $"value[{i}] {type}: {value.String}"
                        : $"value[{i}] {type}: int={value.Int} uint={value.UInt} float={value.Float}");
                }
            var visited = new HashSet<nint>();
            Walk(target->RootNode, "", true, 0, visited, builder);
            snapshot = builder.ToString();
            status = $"Captured {selected}: {visited.Count} nodes. {DateTime.Now:HH:mm:ss}";
            if (snapshot == lastSnapshot) return;
            lastSnapshot = snapshot;
            diagnostics.Write("latest-snapshot.txt", snapshot);
            diagnostics.Append($"captures-{DateTime.Now:yyyy-MM-dd}.txt",
                $"\n--- {DateTimeOffset.Now:O} ---\n{snapshot}");
            Log.Information("XivType captured {Addon}; {Count} nodes. See {Output}", selected, visited.Count, output);
        }
        catch (Exception ex)
        {
            faulted = true;
            watch = false;
            typing.Stop("Stopped after diagnostic error.");
            status = "Capture stopped after an error. See /xllog; reload to retry.";
            Log.Error(ex, "XivType diagnostic capture failed");
        }
    }

    private unsafe void CaptureActivity()
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->RaptureAtkUnitManager == null) return;
        var list = &stage->RaptureAtkUnitManager->AllLoadedUnitsList;
        var builder = new StringBuilder();
        builder.AppendLine($"Language={Client.ClientLanguage}; Territory={Client.TerritoryType}");
        for (var i = 0; i < Math.Min((int)list->Count, 256); i++)
        {
            var addon = list->Entries[i].Value;
            if (addon == null || !addon->IsVisible) continue;
            var name = addon->NameString;
            // Capture only activity prompts and input, including all instances of enemy words.
            if (name is not ("KTGInput" or "KTGEWord" or "KTGHudScore" or "KTGHudTodo" or "KTGPlayGuide" or "KTGEnemyCastBar")) continue;
            builder.AppendLine($"Addon={name}; Id={addon->Id}; FocusNode={(addon->FocusNode == null ? 0 : addon->FocusNode->NodeId)}; ComponentFocus={(addon->ComponentFocusNode == null ? 0 : addon->ComponentFocusNode->NodeId)}");
            if (addon->AtkValues != null)
                for (var n = 0; n < Math.Min((int)addon->AtkValuesCount, 512); n++)
                {
                    var v = addon->AtkValues[n];
                    var type = v.Type.ToString();
                    builder.AppendLine(type.Contains("String") ? $"value[{n}] {type}: {v.String}" : $"value[{n}] {type}: {v.Int}");
                }
            Walk(addon->RootNode, "", true, 0, new HashSet<nint>(), builder, true);
        }
        var data = builder.ToString();
        if (data == lastActivity) return;
        lastActivity = data;
        diagnostics.Write("activity-latest.txt", data);
        diagnostics.Append("activity-history.txt", $"\n--- {DateTimeOffset.Now:O} ---\n{data}");
    }

    private static unsafe void Walk(AtkResNode* node, string parent, bool parentVisible, int depth,
        HashSet<nint> visited, StringBuilder builder, bool compact = false)
    {
        if (node == null || depth > 24 || visited.Count >= 4096 || !visited.Add((nint)node)) return;
        var path = parent.Length == 0 ? node->NodeId.ToString() : $"{parent}/{node->NodeId}";
        var shown = parentVisible && node->IsVisible();
        if (compact && !shown) return;
        var text = node->Type == NodeType.Text ? ((AtkTextNode*)node)->NodeText.ToString() : "";
        if (!compact || text.Length > 0)
            builder.AppendLine($"node {path} type={(int)node->Type} visible={shown} text={text}");
        if ((int)node->Type >= 1000)
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null && component->UldManager.NodeList != null)
                for (var i = 0; i < Math.Min((int)component->UldManager.NodeListCount, 4096); i++)
                    Walk(component->UldManager.NodeList[i], path, shown, depth + 1, visited, builder, compact);
        }
        var siblingGuard = new HashSet<nint>();
        for (var child = node->ChildNode; child != null && siblingGuard.Count < 4096 && siblingGuard.Add((nint)child); child = child->PrevSiblingNode)
            Walk(child, path, shown, depth + 1, visited, builder, compact);
    }

    private void Draw()
    {
        KeyboardCaptured = ImGui.GetIO().WantCaptureKeyboard;
        if (!visible) return;
        ImGui.SetNextWindowSize(new Vector2(760, 580), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("XivType", ref visible))
        {
            ImGui.TextUnformatted("English Keybound Brawler - development build");
            ImGui.TextWrapped(typing.Status);
            ImGui.TextUnformatted($"Acknowledged submissions: {typing.Submissions} | Acknowledged edits: {typing.Accepted}");
            var priority = (int)config.Priority;
            if (ImGui.Combo("Target priority", ref priority, new[] { "Shortest prompt", "Nearest enemy" }, 2))
            {
                config.Priority = (TargetPriority)priority;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled("Finishes the current prompt before selecting again. Distance is measured from your character.");
            if (config.Priority == TargetPriority.NearestEnemy)
            {
                ImGui.TextDisabled($"Enemy distances available: {typing.KnownDistances}/{typing.VisiblePrompts}");
                if (typing.VisiblePrompts > 0 && typing.KnownDistances == 0)
                    ImGui.TextWrapped("Distance unavailable: using shortest prompt for now.");
            }
            if (ImGui.Button("Type one prompt")) typing.Request("once");
            ImGui.SameLine();
            if (ImGui.Button("Start automatic typing")) typing.Request("on");
            ImGui.SameLine();
            if (ImGui.Button("Stop")) typing.Request("off");
            var backgroundTyping = config.BackgroundTyping;
            if (ImGui.Checkbox("Allow background typing", ref backgroundTyping))
            {
                typing.Request("off");
                config.BackgroundTyping = backgroundTyping;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled("Escape stops typing while the game is focused.");
            if (backgroundTyping) ImGui.TextDisabled("Input goes directly to the activity, even when another app has focus.");
            var preventAfk = config.PreventAfk;
            if (ImGui.Checkbox("Send activity key during background typing (experimental)", ref preventAfk))
            {
                config.PreventAfk = preventAfk;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextWrapped("Sends Shift only to FFXIV every 5 seconds after confirmed typing. Pauses during input prohibition. Full-duty AFK protection is unverified.");
            var typingDelay = config.TypingDelayMs;
            var clearDelay = config.ClearDelayMs;
            var keyHold = config.KeyHoldMs;
            if (ImGui.SliderInt("Typing delay", ref typingDelay, 0, 250, "%d ms"))
            {
                config.TypingDelayMs = typingDelay;
                Interface.SavePluginConfig(config);
            }
            if (ImGui.SliderInt("Clear delay", ref clearDelay, 0, 250, "%d ms"))
            {
                config.ClearDelayMs = clearDelay;
                Interface.SavePluginConfig(config);
            }
            if (!backgroundTyping && ImGui.SliderInt("Key hold", ref keyHold, 20, 80, "%d ms"))
            {
                config.KeyHoldMs = keyHold;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled("Lower delay is faster. Every edit still waits for game acknowledgement.");
            if (!backgroundTyping) ImGui.TextDisabled($"Effective key hold: {NativeInput.KeyHoldMs} ms (adapts after a missed key).");
            if (diagnostics.Error is { } diagnosticError) ImGui.TextWrapped($"Diagnostic file error: {diagnosticError}");
            if (!ImGui.CollapsingHeader("Diagnostics")) { ImGui.End(); return; }
            ImGui.Checkbox("Capture activity prompts and input automatically", ref captureActivity);
            ImGui.TextWrapped(status);
            ImGui.SetNextItemWidth(320);
            if (ImGui.BeginCombo("Visible addon", selected.Length == 0 ? "Select activity UI" : selected))
            {
                foreach (var name in addons)
                    if (ImGui.Selectable(name, name == selected))
                    {
                        selected = name;
                        watch = false;
                        lastSnapshot = "";
                        dumpRequested = true;
                    }
                ImGui.EndCombo();
            }
            if (ImGui.Button("Capture once")) dumpRequested = true;
            ImGui.SameLine();
            ImGui.Checkbox("Watch selected addon (250 ms)", ref watch);
            ImGui.TextWrapped($"Output: {output}");
            ImGui.Separator();
            if (ImGui.BeginChild("snapshot", Vector2.Zero)) ImGui.TextUnformatted(snapshot);
            ImGui.EndChild();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        typing.Dispose();
        NativeInput.Release();
        Framework.Update -= Update;
        Interface.UiBuilder.Draw -= Draw;
        Interface.UiBuilder.OpenMainUi -= Open;
        Interface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/xivtype");
        diagnostics.Dispose();
    }
}
