using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace XivType;

internal interface IActivityInput
{
    bool PressKey();
    void ReleaseKey();
    bool KeyObserved { get; }
    float IdleSeconds { get; }
}

internal sealed unsafe class ActivityKeepAlive(IActivityInput input)
{
    private long lastAcknowledgement;
    private bool hasAcknowledgement;
    private long nextPulse;
    private long releaseAt;
    private long deadline;
    private bool held;
    private bool pending;
    private bool observed;
    public long Sent { get; private set; }
    public long Acknowledged { get; private set; }
    public float LastInputBefore { get; private set; }
    public float LastInputAfter { get; private set; }
    public string Status { get; private set; } = "Waiting for acknowledged activity input.";
    public bool Unavailable { get; private set; }

    internal void RecordAcknowledgement(long now)
    {
        lastAcknowledgement = now;
        hasAcknowledgement = true;
    }

    internal void Update(long now, bool enabled, bool running, bool allowed, ushort territory)
    {
        if (!enabled || !running || territory != 1359)
        {
            Cancel();
            hasAcknowledgement = false;
            return;
        }
        if (!allowed)
        {
            Cancel();
            Status = "Activity pulse paused by input prohibition.";
            return;
        }
        if (pending)
        {
            observed |= input.KeyObserved;
            if (held && now >= releaseAt)
            {
                input.ReleaseKey();
                held = false;
            }
            if (!held && observed)
            {
                LastInputAfter = input.IdleSeconds;
                Acknowledged++;
                pending = false;
                Status = "Targeted activity key observed by the game; full-duty AFK protection remains unverified.";
            }
            else if (now >= deadline)
            {
                Cancel();
                Unavailable = true;
                Status = "Activity key was not observed by the game. Automation must stop.";
            }
            return;
        }
        if (!hasAcknowledgement || now - lastAcknowledgement > 30000 || now < nextPulse || Unavailable) return;
        if (input.KeyObserved) { nextPulse = now + 1000; return; }
        LastInputBefore = input.IdleSeconds;
        if (!float.IsFinite(LastInputBefore) || !input.PressKey())
        {
            Unavailable = true;
            Status = "Game-targeted activity input unavailable. Automation must stop.";
            return;
        }
        Sent++;
        held = pending = true;
        observed = false;
        releaseAt = now + 250;
        deadline = now + 1500;
        nextPulse = now + 30000;
        Status = "Waiting for the game to observe a targeted activity key.";
    }

    internal void Cancel()
    {
        if (held) input.ReleaseKey();
        held = pending = false;
    }

    internal void Reset()
    {
        Cancel();
        hasAcknowledgement = false;
        nextPulse = 0;
        Unavailable = false;
    }

    internal static object Snapshot(InputTimerModule* timer) => timer == null
        ? new { Available = false }
        : (object)new { Available = true, Territory = timer->TerritoryTypeId, AfkTimer = timer->AfkTimer,
            ContentInputTimer = timer->ContentInputTimer, InputTimer = timer->InputTimer,
            InstanceContentAfkTimeLimit = timer->InstanceContentAfkTimeLimit };
}
