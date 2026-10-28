using Dalamud.Game.Config;
using Dalamud.Interface;
using Dalamud.Utility;
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
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
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
    private string lastActivity = "";
    private readonly string output = Path.Combine(Interface.GetPluginConfigDirectory(), "diagnostics");
    private readonly TypingController typing;
    private readonly RunLoop loop;
    private readonly ChatWindow chatWindow = new();
    private bool chatOpen;
    private bool inActivity;
    private int loopRuns = 5;
    private readonly DiagnosticStore diagnostics;
    private readonly Configuration config;
    private long nextActorCapture;
    private long uiLockSince;
    private long nextWordArrayCapture;

    public Plugin()
    {
        diagnostics = new DiagnosticStore(output);
        config = Interface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Sanitize();
        Loc.Config = config;
        typing = new TypingController(diagnostics, config);
        loop = new RunLoop(typing);
        typing.UserStopped += () => loop.Stop("Loop stopped with typing.");
        typing.OtherCommand += command =>
        {
            if (command.StartsWith("loop", StringComparison.OrdinalIgnoreCase)) OnCommand("/xivtype", command);
        };
        EntryRecorder.Register();
        Commands.AddHandler("/xivtype", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open XivType. /xivtype on | once | off | status | dump <addon name>",
        });
        Framework.Update += Update;
        // The later activity rounds count as a cutscene, which hid the control window mid-run.
        Interface.UiBuilder.DisableCutsceneUiHide = true;
        Interface.UiBuilder.Draw += Draw;
        Interface.UiBuilder.OpenMainUi += Open;
        Interface.UiBuilder.OpenConfigUi += Open;
        Log.Information("XivType diagnostics ready. Output: {Output}", output);
        Log.Information("XivType game frame rate settings: Fps={Fps}, FPSInActive={Inactive}, FPSDownAFK={Afk}",
            ReadSystem(SystemConfigOption.Fps)?.ToString() ?? "?", ReadSystem(SystemConfigOption.FPSInActive)?.ToString() ?? "?",
            ReadSystem(SystemConfigOption.FPSDownAFK)?.ToString() ?? "?");
    }

    private void Open() => visible = true;

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Trim();
        if (args.Equals("on", StringComparison.OrdinalIgnoreCase)) typing.Request("on");
        else if (args.Equals("once", StringComparison.OrdinalIgnoreCase)) typing.Request("once");
        else if (args.Equals("status", StringComparison.OrdinalIgnoreCase)) Chat.Print($"[XivType] {typing.Status}");
        else if (args.Equals("record", StringComparison.OrdinalIgnoreCase)) EntryRecorder.Start(1800);
        else if (args.StartsWith("loop", StringComparison.OrdinalIgnoreCase))
        {
            var count = args[4..].Trim();
            if (count.Equals("off", StringComparison.OrdinalIgnoreCase)) loop.Stop("Loop stopped by command.");
            else loop.Start(int.TryParse(count, out var runs) ? runs : loopRuns);
            Chat.Print($"[XivType] {loop.Status}");
        }
        else if (args.Equals("stop", StringComparison.OrdinalIgnoreCase) || args.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            watch = false;
            config.Diagnostics = false;
            Interface.SavePluginConfig(config);
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
                    .Select(o => new EnemyCast(o.CastActionId, o.IsCasting, o.TotalCastTime > 0 ? o.CurrentCastTime / o.TotalCastTime : 1)).ToArray()
                : [];
            var castAddon = GameGui.GetAddonByName<AtkUnitBase>("KTGEnemyCastBar");
            EnemyCastLabels = Client.TerritoryType == 1359 && castAddon != null && castAddon->IsVisible
                ? JammerCastBarReader.ReadLabels(castAddon) : [];
            var uiLock = JammerCastBarReader.LockingAction(EnemyCastLabels);
            // Caption-only casts carry no progress; both locking casts are 5s, so time it from first sight.
            uiLockSince = uiLock == 0 ? 0 : uiLockSince == 0 ? now : uiLockSince;
            EnemyLock.Observe(now, Client.TerritoryType == 1359,
                uiLock != 0 ? casts.Append(new EnemyCast(uiLock, true, (now - uiLockSince) / 5000f)).ToArray() : casts);
            if (config.Diagnostics && Client.TerritoryType == 1359 && now >= nextActorCapture)
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
            loop.Update(now);
            // The chat mirror opens on entering the activity (which hides the game's chat) and closes on leaving.
            var activity = Client.TerritoryType == 1359;
            if (activity != inActivity) chatOpen = activity && config.ChatWindow;
            // Open the main window on entering too, so players do not forget to start typing.
            if (activity && !inActivity) visible = true;
            inActivity = activity;
            if (config.Diagnostics && Client.TerritoryType == 1359 && now >= nextWordArrayCapture)
            {
                nextWordArrayCapture = now + 5000;
                var wordAddon = GameGui.GetAddonByName<AtkUnitBase>("KTGEWord");
                if (wordAddon != null && WordArrayDiagnostics.Capture(wordAddon,
                    Objects.Where(o => o.ObjectKind == ObjectKind.BattleNpc).Select(o => o.EntityId).ToHashSet()) is { } data)
                    diagnostics.Write("word-array-map.json", data);
            }
            else if (config.Diagnostics && Client.TerritoryType != 1359 && now >= nextWordArrayCapture)
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
            if (config.Diagnostics && now >= nextActivityCapture)
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
                if (config.Diagnostics && current != inventory)
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

    private static uint? ReadSystem(SystemConfigOption option) => GameConfig.TryGet(option, out uint value) ? value : null;

    // Game System Configuration values. Fps=2 was confirmed as 60 fps by the user (2026-10-28); the other
    // labels follow the game's list order and should be checked against System Configuration.
    private static string[] FrameRateLabels =>
        [Loc.T("None (unlimited)", "制限なし", "不限制"), Loc.T("Main display refresh rate", "メインディスプレイのリフレッシュレート", "主螢幕更新率"), "60 fps", "30 fps", "15 fps"];

    private static void DrawFrameRate()
    {
        if (!ImGui.CollapsingHeader(Loc.T("Game frame rate", "ゲームのフレームレート", "遊戲幀率") + "##fps")) return;
        var perCharacter = NativeInput.MeasuredFps > 0 ? 1000 / NativeInput.MeasuredFps : 0;
        ImGui.TextUnformatted(Loc.T($"Measured: {NativeInput.MeasuredFps:0} fps (about {perCharacter:0.0} ms per posted character at best)", $"計測値: {NativeInput.MeasuredFps:0} fps（1文字あたり最短 約{perCharacter:0.0} ms）", $"實測: {NativeInput.MeasuredFps:0} fps（每個字最快約 {perCharacter:0.0} ms）"));
        if (ReadSystem(SystemConfigOption.Fps) is { } fps)
        {
            var selected = (int)Math.Min(fps, (uint)FrameRateLabels.Length - 1);
            if (ImGui.Combo(Loc.T("Frame rate threshold", "フレームレート制限", "幀率上限") + "##fpslimit", ref selected, FrameRateLabels, FrameRateLabels.Length))
            {
                SetFrameRate((uint)selected);
            }
            if (fps >= FrameRateLabels.Length) ImGui.TextDisabled(Loc.T($"Current game value {fps} is outside the known list.", $"現在の設定値 {fps} は一覧にありません。", $"目前遊戲設定值 {fps} 不在清單內。"));
        }
        DrawSystemToggle(Loc.T("Limit frame rate when client is inactive", "非アクティブ時にフレームレートを制限する", "視窗非作用中時限制幀率"), SystemConfigOption.FPSInActive);
        DrawSystemToggle(Loc.T("Limit frame rate when AFK", "離席時にフレームレートを制限する", "離開時限制幀率"), SystemConfigOption.FPSDownAFK);
        ImGui.TextDisabled(Loc.T("Writes the game's System Configuration. Background typing speed follows the frame rate.", "ゲームのシステム設定を変更します。バックグラウンド入力の速さはフレームレートに比例します。", "會修改遊戲的系統設定。背景輸入速度跟隨幀率。"));
    }

    internal static void SetFrameRate(uint value)
    {
        var before = ReadSystem(SystemConfigOption.Fps);
        GameConfig.Set(SystemConfigOption.Fps, value);
        Log.Information("XivType set game Fps {Before} -> {After}", before?.ToString() ?? "?", value);
    }

    private static void DrawSystemToggle(string label, SystemConfigOption option)
    {
        if (ReadSystem(option) is not { } value) return;
        var enabled = value != 0;
        if (ImGui.Checkbox(label, ref enabled))
        {
            GameConfig.Set(option, enabled ? 1u : 0u);
            Log.Information("XivType set game {Option} {Before} -> {After}", option, value, enabled ? 1 : 0);
        }
    }

    // Small heart in the title bar, left of Dalamud's window menu and close buttons. Drawn at absolute
    // coordinates centred on the title bar; cursor placement in the title bar sat too high.
    private static void DrawKofiButton()
    {
        var style = ImGui.GetStyle();
        var titleHeight = ImGui.GetFontSize() + style.FramePadding.Y * 2;
        var windowPos = ImGui.GetWindowPos();
        var icon = FontAwesomeIcon.Heart.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var iconSize = ImGui.CalcTextSize(icon);
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize();
        ImGui.PopFont();
        var min = new Vector2(windowPos.X + ImGui.GetWindowWidth() - titleHeight * 2 - iconSize.X - style.ItemInnerSpacing.X * 4,
            windowPos.Y + (titleHeight - iconSize.Y) / 2);
        var max = min + iconSize;
        // The title bar lies outside the content clip rect, so hit-test without clipping.
        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && ImGui.IsMouseHoveringRect(min, max, false);
        var color = hovered ? new Vector4(1f, 0.6f, 0.72f, 1f) : new Vector4(1f, 0.45f, 0.6f, 1f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(windowPos, windowPos + ImGui.GetWindowSize());
        drawList.AddText(font, fontSize, min, ImGui.GetColorU32(color), icon);
        drawList.PopClipRect();
        if (!hovered) return;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.SetTooltip(Loc.T("Support XivType on Ko-fi", "Ko-fiでXivTypeを支援", "在 Ko-fi 支持 XivType"));
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) Util.OpenLink("https://ko-fi.com/kuchris");
    }

    private void Draw()
    {
        KeyboardCaptured = ImGui.GetIO().WantCaptureKeyboard;
        chatWindow.Draw(ref chatOpen);
        if (!visible) return;
        ImGui.SetNextWindowSize(new Vector2(760, 580), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("XivType", ref visible))
        {
            DrawKofiButton();
            ImGui.TextUnformatted(Loc.T("Keybound Brawler automatic typing", "キーバウンド・ブロウラー 自動入力", "Keybound Brawler 自動輸入"));
            var language = config.UiLanguage;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(110);
            if (ImGui.Combo("##language", ref language, Loc.LanguageLabels, Loc.LanguageLabels.Length))
            {
                config.UiLanguage = language;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextWrapped(typing.Status);
            ImGui.TextUnformatted(Loc.T($"Acknowledged submissions: {typing.Submissions} | Acknowledged edits: {typing.Accepted}", $"送信成功: {typing.Submissions} | 入力成功: {typing.Accepted}", $"成功送出: {typing.Submissions} | 成功輸入: {typing.Accepted}"));
            var priority = (int)config.Priority;
            if (ImGui.Combo(Loc.T("Target priority", "ターゲット優先", "目標優先") + "##priority", ref priority,
                    new[] { Loc.T("Shortest prompt", "最短の単語", "最短的字"), Loc.T("Nearest enemy", "最も近い敵", "最近的敵人") }, 2))
            {
                config.Priority = (TargetPriority)priority;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled(Loc.T("Finishes the current prompt before selecting again. Distance is measured from your character.", "今の単語を打ち終えてから次を選びます。距離は自キャラから測ります。", "打完目前的字才會選下一個。距離以你的角色計算。"));
            if (config.Priority == TargetPriority.NearestEnemy)
            {
                ImGui.TextDisabled(Loc.T($"Enemy distances available: {typing.KnownDistances}/{typing.VisiblePrompts}", $"距離が分かる敵: {typing.KnownDistances}/{typing.VisiblePrompts}", $"已知距離的敵人: {typing.KnownDistances}/{typing.VisiblePrompts}"));
                if (typing.VisiblePrompts > 0 && typing.KnownDistances == 0)
                    ImGui.TextWrapped(Loc.T("Distance unavailable: using shortest prompt for now.", "距離が取得できないため、最短の単語を優先します。", "取得不到距離，暫時優先最短的字。"));
            }
            ImGui.SetNextItemWidth(100);
            ImGui.InputInt(Loc.T("Runs", "回数", "次數") + "##runs", ref loopRuns);
            loopRuns = Math.Clamp(loopRuns, 1, 99);
            ImGui.SameLine();
            if (!loop.Active && ImGui.Button(Loc.T("Start loop", "ループ開始", "開始循環") + "##loop")) loop.Start(loopRuns);
            else if (loop.Active && ImGui.Button(Loc.T("Stop loop", "ループ停止", "停止循環") + "##loop")) loop.Stop("Loop stopped.");
            ImGui.TextDisabled(loop.Active ? loop.Status : loop.Status + Loc.T(" Stand next to the Keybound Registrar; it registers, commences and types each run.", " 受付NPCの隣に立つと、申し込み・突入・入力を自動で繰り返します。", " 站在報名NPC旁邊，會自動報名、進場並輸入每一場。"));
            if (ImGui.Button(Loc.T("Type one prompt", "1つだけ入力", "只輸入一個") + "##once")) typing.Request("once");
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Start automatic typing", "自動入力開始", "開始自動輸入") + "##on")) typing.Request("on");
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Stop", "停止", "停止") + "##off")) typing.Request("off");
            var backgroundTyping = config.BackgroundTyping;
            if (ImGui.Checkbox(Loc.T("Allow background typing", "バックグラウンド入力を許可", "允許背景輸入") + "##background", ref backgroundTyping))
            {
                typing.Request("off");
                config.BackgroundTyping = backgroundTyping;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled(Loc.T("Escape stops typing while the game is focused.", "ゲームが前面にある時、Escで入力を停止します。", "遊戲在前景時，按 Esc 停止輸入。"));
            var chat = config.ChatWindow;
            if (ImGui.Checkbox(Loc.T("Show chat window during the activity", "コンテンツ中にチャットウィンドウを表示", "活動中顯示聊天視窗") + "##chat", ref chat))
            {
                config.ChatWindow = chat;
                Interface.SavePluginConfig(config);
                chatOpen = chat && inActivity;
            }
            DrawFrameRate();
            if (backgroundTyping) ImGui.TextDisabled(Loc.T("Keystrokes are posted only to FFXIV, even when another app has focus.", "他のアプリが前面でも、キー入力はFFXIVにだけ送られます。", "即使其他程式在前景，按鍵也只會送到 FFXIV。"));
            var typingDelay = config.TypingDelayMs;
            var clearDelay = config.ClearDelayMs;
            var keyHold = config.KeyHoldMs;
            if (ImGui.SliderInt(Loc.T("Typing delay", "入力間隔", "輸入間隔") + "##delay", ref typingDelay, 0, 250, "%d ms"))
            {
                config.TypingDelayMs = typingDelay;
                Interface.SavePluginConfig(config);
            }
            if (ImGui.SliderInt(Loc.T("Clear delay", "消去間隔", "清除間隔") + "##clear", ref clearDelay, 0, 250, "%d ms"))
            {
                config.ClearDelayMs = clearDelay;
                Interface.SavePluginConfig(config);
            }
            if (ImGui.SliderInt(Loc.T("Key hold", "キー押下時間", "按鍵時間") + "##hold", ref keyHold, 0, 80, "%d ms"))
            {
                config.KeyHoldMs = keyHold;
                Interface.SavePluginConfig(config);
            }
            ImGui.TextDisabled(Loc.T("Lower delay is faster. Every edit still waits for game acknowledgement.", "間隔が短いほど速くなります。入力はゲームへの反映を確認しながら進みます。", "間隔越短越快。每次輸入仍會等遊戲確認。"));
            ImGui.TextDisabled(Loc.T($"Effective key hold: {NativeInput.KeyHoldMs} ms (adapts after a missed key).", $"実際のキー押下時間: {NativeInput.KeyHoldMs} ms（入力漏れの後は自動調整）", $"實際按鍵時間: {NativeInput.KeyHoldMs} ms（漏字後會自動調整）"));
            if (diagnostics.Error is { } diagnosticError) ImGui.TextWrapped($"Diagnostic file error: {diagnosticError}");
            if (!ImGui.CollapsingHeader("Diagnostics")) { ImGui.End(); return; }
            var capture = config.Diagnostics;
            if (ImGui.Checkbox("Capture activity prompts, actors and timelines automatically", ref capture))
            {
                config.Diagnostics = capture;
                Interface.SavePluginConfig(config);
            }
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
        chatWindow.Dispose();
        EntryRecorder.Unregister();
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
