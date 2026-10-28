namespace XivType;

internal enum TargetPriority { ShortestPrompt, NearestEnemy }

internal static class PromptSelection
{
    internal static Prompt? Choose(IEnumerable<Prompt> prompts, string prefix, TargetPriority priority)
    {
        var candidates = prompts.Where(p => p.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return priority == TargetPriority.NearestEnemy
            ? candidates.OrderBy(p => p.Distance ?? PromptActorMap.EstimateDistance(p.Scale) ?? float.MaxValue).ThenBy(p => p.Text.Length).ThenBy(p => p.NodeId).FirstOrDefault()
            : candidates.OrderBy(p => p.Text.Length).ThenBy(p => p.NodeId).FirstOrDefault();
    }
}
