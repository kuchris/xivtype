namespace XivType;

internal readonly record struct EnemyCast(uint ActionId, bool IsCasting);

internal sealed class EnemyInputLock
{
    private long blockedUntil;
    public bool Blocked { get; private set; }
    public uint ActionId { get; private set; }

    internal void Observe(long now, bool inActivity, ReadOnlySpan<EnemyCast> casts)
    {
        if (!inActivity)
        {
            blockedUntil = 0;
            Blocked = false;
            ActionId = 0;
            return;
        }
        // Current client Action rows: both Dynamic Sensory Jammer variants have 5s casts.
        // There is deliberately no boss, target or actor-name filter.
        foreach (var cast in casts)
            if (cast.IsCasting && cast.ActionId is 49532 or 49702)
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
