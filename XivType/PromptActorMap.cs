using System.Numerics;

namespace XivType;

internal readonly record struct ActorScreen(uint Entity, Vector2 Screen, Vector3 World);

internal static class PromptActorMap
{
    // KTGEWord labels float above their enemy. KTGEWord refresh events did not change prompt text
    // live (2026-10-28, changed=0 on every event), so binding uses projected screen position instead.
    internal const float MaxOffset = 48;
    internal const float AmbiguityMargin = 12;

    internal static Prompt[] WithDistances(Prompt[] prompts, IReadOnlyList<ActorScreen> actors, Vector3 origin)
    {
        var result = prompts.ToArray();
        var pairs = new List<(int Prompt, ActorScreen Actor, float Offset)>();
        for (var i = 0; i < prompts.Length; i++)
        {
            var prompt = prompts[i];
            var limit = MaxOffset * Math.Max(prompt.Scale, 0.5f);
            var candidates = actors.Where(a => a.Screen.Y > prompt.ScreenY)
                .Select(a => (Actor: a, Offset: Math.Abs(a.Screen.X - prompt.ScreenX)))
                .OrderBy(c => c.Offset).Take(2).ToArray();
            if (candidates.Length == 0 || candidates[0].Offset > limit) continue;
            // Enemies stacked in depth project to similar X; refuse to guess between them.
            if (candidates.Length > 1 && candidates[1].Offset - candidates[0].Offset < AmbiguityMargin) continue;
            pairs.Add((i, candidates[0].Actor, candidates[0].Offset));
        }
        // An actor can carry only one prompt; contested actors stay unmatched.
        foreach (var pair in pairs.GroupBy(p => p.Actor.Entity).Where(g => g.Count() == 1).Select(g => g.Single()))
        {
            var distance = Vector3.Distance(origin, pair.Actor.World);
            if (float.IsFinite(distance))
                result[pair.Prompt] = result[pair.Prompt] with { EntityId = pair.Actor.Entity, Distance = distance };
        }
        return result;
    }
}
