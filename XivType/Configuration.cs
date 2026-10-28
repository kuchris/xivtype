using Dalamud.Configuration;

namespace XivType;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int TypingDelayMs { get; set; } = 100;
    public int ClearDelayMs { get; set; } = 0;
    public int KeyHoldMs { get; set; } = 0;
    public bool BackgroundTyping { get; set; } = true;
    public TargetPriority Priority { get; set; } = TargetPriority.ShortestPrompt;
    // Automatic captures (UI text history, actors, word arrays, idle timeline) are for development only.
    public bool Diagnostics { get; set; }
    public bool ShowChatInActivity { get; set; }

    public void Sanitize()
    {
        TypingDelayMs = Math.Clamp(TypingDelayMs, 0, 250);
        ClearDelayMs = Math.Clamp(ClearDelayMs, 0, 250);
        KeyHoldMs = Math.Clamp(KeyHoldMs, 0, 80);
        if (!Enum.IsDefined(Priority)) Priority = TargetPriority.ShortestPrompt;
    }
}
