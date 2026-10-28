using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;

namespace XivType;

// Read-only mirror of player chat, because Keybound Brawler hides the game's chat log. It only listens to
// messages; it never touches the game's chat windows, chat input or Enter.
internal sealed class ChatWindow : IDisposable
{
    private static readonly HashSet<XivChatType> Channels =
    [
        XivChatType.Say, XivChatType.Shout, XivChatType.Yell, XivChatType.TellIncoming, XivChatType.TellOutgoing,
        XivChatType.Party, XivChatType.CrossParty, XivChatType.Alliance, XivChatType.FreeCompany, XivChatType.NoviceNetwork,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4, XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    private readonly Queue<(DateTime Time, XivChatType Channel, string Sender, string Text)> lines = new();
    private bool scrollToEnd;

    internal ChatWindow() => Plugin.Chat.ChatMessageUnhandled += OnMessage;

    public void Dispose() => Plugin.Chat.ChatMessageUnhandled -= OnMessage;

    private void OnMessage(IChatMessage message)
    {
        if (!Channels.Contains(message.LogKind)) return;
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
            if (lines.Count == 0) ImGui.TextDisabled("Player chat appears here while the activity hides the game's chat.");
            foreach (var (time, channel, sender, text) in lines)
            {
                ImGui.TextColored(Colour(channel), $"[{time:HH:mm}] [{Label(channel)}] {sender}:");
                ImGui.SameLine();
                ImGui.TextWrapped(text);
            }
            if (scrollToEnd) { ImGui.SetScrollHereY(1); scrollToEnd = false; }
        }
        ImGui.End();
    }

    private static string Label(XivChatType channel) => channel switch
    {
        XivChatType.TellIncoming => "Tell from",
        XivChatType.TellOutgoing => "Tell to",
        XivChatType.FreeCompany => "FC",
        XivChatType.NoviceNetwork => "NN",
        XivChatType.CrossParty => "Party",
        _ when channel.ToString().StartsWith("CrossLinkShell", StringComparison.Ordinal) => "CWLS",
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
