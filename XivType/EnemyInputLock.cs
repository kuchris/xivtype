namespace XivType;

internal readonly record struct EnemyCast(uint ActionId, bool IsCasting);

internal sealed class EnemyInputLock
{
    private long blockedUntil;
    public bool Blocked { get; private set; }
    public uint ActionId { get; private set; }

    // Typing while Cursed Gaze resolves triggers the 7s lock (user, 2026-10-28); jammer locks regardless.
    internal static bool IsLocking(uint actionId) => actionId is 49532 or 49702 or 49529;
    internal static string Name(uint actionId) => actionId == 49529 ? "Cursed Gaze" : "Dynamic Sensory Jammer";

    internal void Observe(long now, bool inActivity, ReadOnlySpan<EnemyCast> casts)
    {
        if (!inActivity)
        {
            blockedUntil = 0;
            Blocked = false;
            ActionId = 0;
            return;
        }
        // Current client Action rows: both Dynamic Sensory Jammer variants and Cursed Gaze have 5s casts.
        // There is deliberately no boss, target or actor-name filter.
        foreach (var cast in casts)
            if (cast.IsCasting && IsLocking(cast.ActionId))
            {
                ActionId = cast.ActionId;
                blockedUntil = now + 750;
                Blocked = true;
                return;
            }
        Blocked = now < blockedUntil;
        if (!Blocked) ActionId = 0;
    }
}
