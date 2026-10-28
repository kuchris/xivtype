namespace XivType;

internal enum TargetPriority { ShortestPrompt, NearestEnemy }

internal static class PromptSelection
{
    // Accented prompt letters (e.g. a with umlaut) are accepted when typed as the base letter (user,
    // 2026-10-28), and the base letter is what a keyboard layout can produce.
    internal static string Typeable(string text)
    {
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
            System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).Normalize(System.Text.NormalizationForm.FormC);
    }

    internal static Prompt? Choose(IEnumerable<Prompt> prompts, string prefix, TargetPriority priority)
    {
        var candidates = prompts.Where(p => Typeable(p.Text).StartsWith(Typeable(prefix), StringComparison.OrdinalIgnoreCase));
        return priority == TargetPriority.NearestEnemy
            ? candidates.OrderBy(p => p.Distance ?? PromptActorMap.EstimateDistance(p.Scale) ?? float.MaxValue).ThenBy(p => p.Text.Length).ThenBy(p => p.NodeId).FirstOrDefault()
            : candidates.OrderBy(p => p.Text.Length).ThenBy(p => p.NodeId).FirstOrDefault();
    }
}
