using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace XivType;

// Mirror of chat, because Keybound Brawler hides the game's chat log. It listens to messages and sends what
// the user types through the game's own chat entry; it never touches the game's chat windows or Enter.
internal sealed unsafe class ChatWindow : IDisposable
{
    private static readonly HashSet<XivChatType> PlayerChannels =
    [
        XivChatType.Say, XivChatType.Shout, XivChatType.Yell, XivChatType.TellIncoming, XivChatType.TellOutgoing,
        XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance, XivChatType.FreeCompany, XivChatType.NoviceNetwork,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4, XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    private readonly Queue<(DateTime Time, XivChatType Channel, string Sender, string Text)> lines = new();
    private bool scrollToEnd;
    private bool playerOnly;
    private string draft = "";
    private int channel;
    private string sendError = "";
    private static readonly (string Label, string Command, XivChatType Type)[] FixedChannels =
    [
        ("Party", "/p", XivChatType.Party), ("Say", "/s", XivChatType.Say), ("Free Company", "/fc", XivChatType.FreeCompany),
        ("Alliance", "/a", XivChatType.Alliance), ("Shout", "/sh", XivChatType.Shout), ("Yell", "/y", XivChatType.Yell),
        ("Echo", "/e", XivChatType.Echo),
    ];
    private static readonly XivChatType[] LinkshellTypes =
        [XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4, XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8];
    private static readonly XivChatType[] CrossLinkshellTypes =
    [
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];
    private (string Label, string Command, XivChatType Type)[] SendChannels = FixedChannels;
    private long nextChannelRefresh;

    // All 8 linkshells and 8 cross-world linkshells; CWLS show their names when the game has them.
    private void RefreshChannels()
    {
        var now = Environment.TickCount64;
        if (now < nextChannelRefresh) return;
        nextChannelRefresh = now + 5000;
        var list = FixedChannels.ToList();
        var cwls = InfoProxyCrossWorldLinkshell.Instance();
        for (var i = 0; i < 8; i++)
        {
            var name = cwls == null ? null : cwls->GetCrossworldLinkshellName((uint)i);
            var label = name != null && name->ToString() is { Length: > 0 } text ? $"CWLS {i + 1}: {text}" : $"CWLS {i + 1}";
            list.Add((label, $"/cwl{i + 1}", CrossLinkshellTypes[i]));
        }
        for (var i = 0; i < 8; i++) list.Add(($"Linkshell {i + 1}", $"/l{i + 1}", LinkshellTypes[i]));
        SendChannels = list.ToArray();
        channel = Math.Min(channel, SendChannels.Length - 1);
    }

    // ChatMessage sees every message, including ones other plugins later handle; the Unhandled event missed those.
    internal ChatWindow() => Plugin.Chat.ChatMessage += OnMessage;

    public void Dispose() => Plugin.Chat.ChatMessage -= OnMessage;

    private void OnMessage(IHandleableChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Message.TextValue)) return;
        lines.Enqueue((DateTime.Now, message.LogKind, message.Sender.TextValue, message.Message.TextValue));
        while (lines.Count > 200) lines.Dequeue();
        scrollToEnd = true;
    }

    internal void Draw(ref bool open)
    {
        if (!open) return;
        ImGui.SetNextWindowSize(new Vector2(420, 220), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("XivType Chat", ref open))
        {
            var inputHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing() * 2;
            ImGui.BeginChild("log", new Vector2(0, -inputHeight));
            ImGui.Checkbox("Player chat only", ref playerOnly);
            ImGui.Separator();
            if (lines.Count == 0) ImGui.TextDisabled("Chat appears here while the activity hides the game's chat.");
            foreach (var (time, channel, sender, text) in lines)
            {
                if (playerOnly && !PlayerChannels.Contains(channel)) continue;
                ImGui.TextColored(Colour(channel), sender.Length > 0 ? $"[{time:HH:mm}] [{Label(channel)}] {sender}:" : $"[{time:HH:mm}] [{Label(channel)}]");
                if (ImGui.IsItemClicked()) ReplyTo(channel);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to reply in this channel");
                ImGui.SameLine();
                ImGui.TextWrapped(text);
            }
            if (scrollToEnd) { ImGui.SetScrollHereY(1); scrollToEnd = false; }
            ImGui.EndChild();
            DrawInput();
        }
        ImGui.End();
    }

    // Kept in its own window region below the log so the input stays visible while the log scrolls.
    private void DrawInput()
    {
        RefreshChannels();
        ImGui.Separator();
        ImGui.SetNextItemWidth(110);
        if (ImGui.BeginCombo("##channel", SendChannels[channel].Label))
        {
            for (var i = 0; i < SendChannels.Length; i++)
                if (ImGui.Selectable(SendChannels[i].Label, i == channel)) channel = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-60);
        var send = ImGui.InputText("##message", ref draft, 400, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        send |= ImGui.Button("Send");
        if (send && draft.Trim().Length > 0)
        {
            var text = draft.Trim();
            // A leading slash is sent as typed (e.g. /tell Name@World hi); otherwise use the chosen channel.
            sendError = Send(text.StartsWith('/') ? text : $"{SendChannels[channel].Command} {text}") ? "" : "Message contains characters the game rejects.";
            if (sendError.Length == 0) draft = "";
            ImGui.SetKeyboardFocusHere(-1);
        }
        if (sendError.Length > 0) ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), sendError);
        ImGui.TextDisabled("Typing pauses while this box has focus.");
    }

    // Clicking a message picks its channel; tells start a /r reply.
    private void ReplyTo(XivChatType type)
    {
        if (type == XivChatType.TellIncoming) { draft = draft.StartsWith("/r ") ? draft : "/r " + draft; return; }
        if (type == XivChatType.CrossParty) type = XivChatType.Party;
        var index = Array.FindIndex(SendChannels, c => c.Type == type);
        if (index >= 0) channel = index;
    }

    private static bool Send(string text)
    {
        if (text.Any(char.IsControl) || System.Text.Encoding.UTF8.GetByteCount(text) > 500) return false;
        var message = Utf8String.FromString(text);
        try
        {
            // Same check the game applies to typed chat: refuse anything sanitising would change.
            message->SanitizeString((AllowedEntities)0x27F, null);
            if (message->ToString() != text) return false;
            UIModule.Instance()->ProcessChatBoxEntry(message, nint.Zero, false);
            return true;
        }
        finally { message->Dtor(true); }
    }

    private static string Label(XivChatType channel) => channel switch
    {
        XivChatType.TellIncoming => "Tell from",
        XivChatType.TellOutgoing => "Tell to",
        XivChatType.FreeCompany => "FC",
        XivChatType.NoviceNetwork => "NN",
        XivChatType.CrossParty => "Party",
        _ when Array.IndexOf(CrossLinkshellTypes, channel) is var cw and >= 0 => $"CWLS{cw + 1}",
        _ when Array.IndexOf(LinkshellTypes, channel) is var ls and >= 0 => $"LS{ls + 1}",
        _ => channel.ToString(),
    };

    private static Vector4 Colour(XivChatType channel) => channel switch
    {
        XivChatType.TellIncoming or XivChatType.TellOutgoing => new(1f, 0.6f, 0.85f, 1f),
        XivChatType.Party or XivChatType.CrossParty or XivChatType.Alliance => new(0.4f, 0.8f, 1f, 1f),
        XivChatType.FreeCompany => new(0.6f, 0.9f, 0.6f, 1f),
        XivChatType.Shout or XivChatType.Yell => new(1f, 0.75f, 0.4f, 1f),
        _ => new(0.85f, 0.85f, 0.85f, 1f),
    };
}
