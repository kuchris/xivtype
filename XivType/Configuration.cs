using Dalamud.Configuration;

namespace XivType;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int TypingDelayMs { get; set; } = 10;
    public int ClearDelayMs { get; set; } = 0;
    public int KeyHoldMs { get; set; } = 35;
    public bool BackgroundTyping { get; set; } = true;
    public TargetPriority Priority { get; set; } = TargetPriority.ShortestPrompt;

    public void Sanitize()
    {
        TypingDelayMs = Math.Clamp(TypingDelayMs, 0, 250);
        ClearDelayMs = Math.Clamp(ClearDelayMs, 0, 250);
        KeyHoldMs = Math.Clamp(KeyHoldMs, 10, 80);
        if (!Enum.IsDefined(Priority)) Priority = TargetPriority.ShortestPrompt;
    }
}
