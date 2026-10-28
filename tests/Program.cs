using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivType;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Numerics;

unsafe class Program
{
    static int Main()
    {
        using var fixture = new CaptureFixture(Path.Combine(AppContext.BaseDirectory, "fixtures", "masked-full.txt"));
        var words = PromptReader.ReadWords(fixture.Addon, CaptureFixture.Shown);
        Console.WriteLine("Replay candidates: " + string.Join(", ", words.Select(w => $"{w.NodeId}={w.Text}")));
        if (!words.Any(w => w.NodeId == 20004 && w.Text == "mandragora" && w.Masked))
        {
            Console.WriteLine("FAIL: Active masked mandragora is missing; the existing 'man' prefix cannot continue.");
            return 1;
        }
        Console.WriteLine("PASS: Active masked prompt remains readable.");
        var checks = 1;
        foreach (var hiddenPath in new[] { "1/20004", "1/20004/4", "1/20004/4/10" })
        {
            var node = (AtkResNode*)fixture.Nodes[hiddenPath];
            var original = node->NodeFlags;
            node->NodeFlags &= ~NodeFlags.Visible;
            if (PromptReader.ReadWords(fixture.Addon, CaptureFixture.Shown).Any(w => w.NodeId == 20004))
                throw new Exception($"Stale hidden prompt became eligible when {hiddenPath} was hidden.");
            node->NodeFlags = original;
            checks++;
        }
        var firstTile = (AtkResNode*)fixture.Nodes["1/20004/4/10/17"];
        var secondTile = (AtkResNode*)fixture.Nodes["1/20004/4/10/18"];
        firstTile->NodeFlags &= ~NodeFlags.Visible;
        secondTile->NodeFlags &= ~NodeFlags.Visible;
        // ULD inspection identifies 11-16 as ordinary aura effects, not the noise texture.
        ((AtkResNode*)fixture.Nodes["1/20004/4/10/11"])->NodeFlags |= NodeFlags.Visible;
        if (PromptReader.ReadWords(fixture.Addon, CaptureFixture.Shown).Any(w => w.NodeId == 20004))
            throw new Exception("Hidden label without visible noise tiles became eligible.");
        checks++;
        if (words.Any(w => w.NodeId == 20019)) throw new Exception("Hidden inactive enemy text became eligible.");
        checks++;
        if (!words.Any(w => w.Text == "Marlene Wallace" && !w.Masked)) throw new Exception("Normal visible prompt disappeared.");
        checks++;
        if (!words.Any(w => w.Text.StartsWith("man", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Captured 'man' input still has no matching active candidate.");
        checks++;
        Console.WriteLine($"PASS: {checks} live-capture regression checks.");
        using var jammed = new InputFixture("input-jammed.txt");
        using var unlocked = new InputFixture("input-unlocked.txt");
        var blocked = InputAvailability.Read(jammed.Addon, true);
        if (blocked.Allowed || blocked.LockSeconds != 7)
        {
            Console.WriteLine("FAIL: Prohibited-input marker with 7 seconds remaining still permits an edit or submission.");
            return 1;
        }
        if (!InputAvailability.Read(unlocked.Addon, true).Allowed)
            throw new Exception("Input did not resume after permission returned and the lock countdown reached zero.");
        if (InputAvailability.Read(unlocked.Addon, false).Allowed)
            throw new Exception("Disabled native input component was allowed.");
        jammed.Addon->AtkValues[2].Bool = true;
        if (InputAvailability.Read(jammed.Addon, true).Allowed)
            throw new Exception("A positive countdown must block even if the permission flag is briefly true.");
        jammed.Addon->AtkValues[4].Int = 0;
        jammed.Addon->AtkValues[2].Bool = false;
        if (InputAvailability.Read(jammed.Addon, true).Allowed)
            throw new Exception("Zero countdown must not bypass a prohibition flag that is still false.");
        jammed.Addon->AtkValuesCount = 0;
        if (InputAvailability.Read(jammed.Addon, true).Allowed)
            throw new Exception("Unavailable permission data must not permit input.");
        Console.WriteLine("PASS: Prohibited-input marker pauses input and unlock resumes it.");
        if (!TestActivityKeepAlive()) return 1;
        TestEnemyInputLock();
        using var castFixture = new CaptureFixture(Path.Combine(AppContext.BaseDirectory, "fixtures", "castbar-layout.txt"), "KTGEnemyCastBar");
        var castLabels = JammerCastBarReader.ReadLabels(castFixture.Addon, CaptureFixture.Shown);
        if (JammerCastBarReader.LockingAction(castLabels) != 49532) throw new Exception("Jammer UI on a non-selected enemy was ignored.");
        var uiLock = new EnemyInputLock();
        uiLock.Observe(1000, true, JammerCastBarReader.LockingAction(castLabels) is var ui and not 0 ? [new(ui, true)] : []);
        if (!uiLock.Blocked) throw new Exception("Jammer UI did not block input when actor casting data was absent.");
        ((AtkResNode*)castFixture.Nodes["1/20001"])->NodeFlags &= ~NodeFlags.Visible;
        if (JammerCastBarReader.LockingAction(JammerCastBarReader.ReadLabels(castFixture.Addon, CaptureFixture.Shown)) != 0)
            throw new Exception("Hidden stale jammer caption blocked input.");
        if (JammerCastBarReader.LockingAction(["Burst", "Cursed Gaze"]) != 49529 || JammerCastBarReader.LockingAction(["Burst", "Tankbuster"]) != 0)
            throw new Exception("Cursed Gaze caption was not mapped to its locking action.");
        Console.WriteLine("PASS: Visible castbar fallback covers unselected enemies without actor casting data.");
        TestPromptSelection();
        return 0;
    }

    static bool TestActivityKeepAlive()
    {
        var backend = new FakeActivityInput();
        var keepAlive = new ActivityKeepAlive(backend);
        keepAlive.Update(1000, true, true, true, 1359);
        if (backend.Presses != 0) throw new Exception("Activity key sent before any acknowledged typing.");
        keepAlive.RecordAcknowledgement(1000);
        keepAlive.Update(1000, true, true, true, 1359);
        if (backend.Presses != 1) throw new Exception("Activity key was not sent after acknowledged typing.");
        backend.KeyObserved = true;
        backend.IdleSeconds = 0;
        keepAlive.Update(1100, true, true, true, 1359);
        if (backend.Releases != 0 || keepAlive.Acknowledged != 0) throw new Exception("Key completed before its hold duration.");
        keepAlive.Update(1250, true, true, true, 1359);
        if (backend.Releases != 1 || keepAlive.Acknowledged != 1) throw new Exception("Observed activity key was not released and acknowledged.");
        backend.KeyObserved = false;
        keepAlive.RecordAcknowledgement(2000);
        keepAlive.Update(2000, true, true, true, 1359);
        if (backend.Presses != 1) throw new Exception("Activity key interval was not enforced.");
        keepAlive.RecordAcknowledgement(6000);
        keepAlive.Update(6000, true, true, true, 1359);
        if (backend.Presses != 2) throw new Exception("Activity key did not repeat after its 5-second interval.");
        keepAlive.Cancel();
        foreach (var scenario in new[]
        {
            (Enabled: false, Running: true, Allowed: true, Territory: (ushort)1359),
            (Enabled: true, Running: false, Allowed: true, Territory: (ushort)1359),
            (Enabled: true, Running: true, Allowed: false, Territory: (ushort)1359),
            (Enabled: true, Running: true, Allowed: true, Territory: (ushort)1360),
        })
        {
            var blockedBackend = new FakeActivityInput();
            var blockedPulse = new ActivityKeepAlive(blockedBackend);
            blockedPulse.RecordAcknowledgement(20000);
            blockedPulse.Update(20000, scenario.Enabled, scenario.Running, scenario.Allowed, scenario.Territory);
            if (blockedBackend.Presses != 0) throw new Exception("Activity key ran outside its allowed scope.");
        }
        var cancellationBackend = new FakeActivityInput();
        var cancellation = new ActivityKeepAlive(cancellationBackend);
        cancellation.RecordAcknowledgement(0);
        cancellation.Update(0, true, true, true, 1359);
        cancellation.Update(100, true, true, false, 1359);
        if (cancellationBackend.Releases != 1 || cancellation.Acknowledged != 0)
            throw new Exception("A prohibition did not release the owned activity key.");
        var missingBackend = new FakeActivityInput();
        var missing = new ActivityKeepAlive(missingBackend);
        missing.RecordAcknowledgement(0);
        missing.Update(0, true, true, true, 1359);
        missing.Update(1600, true, true, true, 1359);
        if (!missing.Unavailable || missing.Acknowledged != 0 || missingBackend.Releases != 1)
            throw new Exception("An unobserved key was incorrectly reported as successful.");
        var ownedBackend = new FakeActivityInput { KeyObserved = true };
        var owned = new ActivityKeepAlive(ownedBackend);
        owned.RecordAcknowledgement(0);
        owned.Update(0, true, true, true, 1359);
        owned.Cancel();
        if (ownedBackend.Presses != 0 || ownedBackend.Releases != 0)
            throw new Exception("The pulse took ownership of an already held physical key.");
        Console.WriteLine("PASS: Targeted activity key scheduling, acknowledgement, release, timeout and scope guards; no timer writes.");
        return true;
    }

    static void TestEnemyInputLock()
    {
        foreach (var action in new uint[] { 49532, 49702, 49529 })
        {
            var enemyLock = new EnemyInputLock();
            enemyLock.Observe(1000, true, [new(49516, true), new(action, true)]);
            if (!enemyLock.Blocked || enemyLock.ActionId != action)
                throw new Exception("A jammer cast by a non-selected enemy was not blocked.");
            enemyLock.Observe(1500, true, []);
            if (!enemyLock.Blocked) throw new Exception("The jammer settling delay ended too early.");
            enemyLock.Observe(2900, true, []);
            if (!enemyLock.Blocked) throw new Exception("The 2s post-cast hold ended too early.");
            enemyLock.Observe(3100, true, []);
            if (enemyLock.Blocked) throw new Exception("The jammer block did not expire.");
            var early = new EnemyInputLock();
            early.Observe(1000, true, [new(action, true, 0.3f)]);
            if (early.Blocked) throw new Exception("Typing paused before the cast bar reached half.");
            early.Observe(1100, true, [new(action, true, 0.5f)]);
            if (!early.Blocked) throw new Exception("Typing continued past half the cast bar.");
            enemyLock.Observe(4000, true, [new(action, false)]);
            if (enemyLock.Blocked) throw new Exception("A stale, non-casting action blocked typing.");
            enemyLock.Observe(5000, true, [new(action, true)]);
            enemyLock.Observe(5001, false, [new(action, true)]);
            if (enemyLock.Blocked) throw new Exception("The jammer state leaked outside the activity.");
        }
        var ordinary = new EnemyInputLock();
        ordinary.Observe(1000, true, [new(49516, true)]);
        if (ordinary.Blocked) throw new Exception("An ordinary enemy cast blocked typing.");
        Console.WriteLine("PASS: Both jammer variants and Cursed Gaze, unselected enemies, settling, interruption and scope guards.");
    }

    static void TestPromptSelection()
    {
        Prompt[] prompts = [new(2, "long nearby enemy", Distance: 3), new(20001, "far", Distance: 30), new(20002, "missing")];
        if (PromptSelection.Choose(prompts, "", TargetPriority.NearestEnemy)?.NodeId != 2)
            throw new Exception("Nearest enemy did not outrank a shorter distant prompt.");
        if (PromptSelection.Choose(prompts, "", TargetPriority.ShortestPrompt)?.NodeId != 20001)
            throw new Exception("Shortest prompt priority changed.");
        if (PromptSelection.Choose(prompts, "f", TargetPriority.NearestEnemy)?.NodeId != 20001)
            throw new Exception("Nearest priority ignored the existing typed prefix.");
        if (PromptSelection.Choose(prompts, "not a match", TargetPriority.NearestEnemy) != null)
            throw new Exception("A mismatching existing input selected an enemy.");
        // Labels sit above their actor's projected position; enemies are 3-4-5 triangles from the origin.
        Prompt[] labels = [new(10, "left", ScreenX: 400, ScreenY: 300), new(11, "right", ScreenX: 800, ScreenY: 300),
            new(12, "stacked", ScreenX: 1200, ScreenY: 300), new(13, "nobody", ScreenX: 1600, ScreenY: 300)];
        ActorScreen[] actors = [new(100, new(405, 420), new(3, 0, 4)), new(101, new(790, 380), new(6, 0, 8)),
            new(102, new(1200, 420), new(0, 0, 1)), new(103, new(1205, 360), new(0, 0, 9)), new(104, new(1600, 200), new(0, 0, 2))];
        var located = PromptActorMap.WithDistances(labels, actors, Vector3.Zero);
        if (located[0].EntityId != 100 || located[0].Distance != 5) throw new Exception("Label above an actor did not take its world distance.");
        if (located[1].EntityId != 101 || located[1].Distance != 10) throw new Exception("Second label did not bind its own actor.");
        if (located[2].Distance != null) throw new Exception("Depth-stacked enemies were guessed instead of left unknown.");
        if (located[3].Distance != null) throw new Exception("An actor above the label was bound.");
        if (PromptActorMap.WithDistances([labels[0], labels[0] with { NodeId = 14 }], actors, Vector3.Zero).Any(p => p.Distance != null))
            throw new Exception("One actor carried two prompts.");
        if (PromptSelection.Typeable("Ätherhängen café") != "Atherhangen cafe" ||
            PromptSelection.Choose([new(40, "Hölle")], "ho", TargetPriority.ShortestPrompt)?.NodeId != 40)
            throw new Exception("Accented prompt letters were not typed as their base letters.");
        // Two enemies on one screen column stay unbound; the near label's scale must still win over a bound far enemy.
        Prompt[] stacked = [new(30, "crimsonite crystal", Scale: 1.14f, Distance: 40), new(31, "You should get yourself a reading!", Scale: 1.45f)];
        if (PromptSelection.Choose(stacked, "", TargetPriority.NearestEnemy)?.NodeId != 31)
            throw new Exception("A near unbound label lost to a bound distant enemy.");
        if (PromptActorMap.EstimateDistance(1.14f) != null || PromptActorMap.EstimateDistance(1.501f) is not (> 12 and < 14))
            throw new Exception("Scale estimate does not match the live calibration.");
        Console.WriteLine("PASS: Nearest/shortest priority, typed-prefix preservation, screen-position actor binding and scale fallback.");
    }
}

sealed class FakeActivityInput : IActivityInput
{
    public int Presses { get; private set; }
    public int Releases { get; private set; }
    public bool KeyObserved { get; set; }
    public float IdleSeconds { get; set; } = 120;
    public bool PressKey() { Presses++; return true; }
    public void ReleaseKey() => Releases++;
}

unsafe sealed class InputFixture : IDisposable
{
    public readonly AtkUnitBase* Addon;
    public InputFixture(string file)
    {
        Addon = (AtkUnitBase*)NativeMemory.AllocZeroed((nuint)sizeof(AtkUnitBase));
        Addon->AtkValuesCount = 5;
        Addon->AtkValues = (AtkValue*)NativeMemory.AllocZeroed((nuint)(5 * sizeof(AtkValue)));
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", file)))
        {
            var match = Regex.Match(line, @"^value\[(?<index>\d+)\] (?<type>Bool|Int): (?<value>\d+)");
            if (!match.Success) continue;
            var index = int.Parse(match.Groups["index"].Value);
            Addon->AtkValues[index].Type = match.Groups["type"].Value == "Bool" ? AtkValueType.Bool : AtkValueType.Int;
            Addon->AtkValues[index].Int = int.Parse(match.Groups["value"].Value);
        }
    }
    public void Dispose()
    {
        NativeMemory.Free(Addon->AtkValues);
        NativeMemory.Free(Addon);
    }
}

// Rebuild the actual captured node hierarchy in isolated native memory. No game process or
// signature resolution is needed; the injected visibility function replays captured visibility.
unsafe sealed class CaptureFixture : IDisposable
{
    private readonly List<nint> allocations = [];
    public readonly AtkUnitBase* Addon;
    public readonly Dictionary<string, nint> Nodes = [];

    public CaptureFixture(string path, string addonName = "KTGEWord")
    {
        Addon = (AtkUnitBase*)Allocate((nuint)sizeof(AtkUnitBase));
        var inWordAddon = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("Addon=")) inWordAddon = line.StartsWith($"Addon={addonName};");
            if (!inWordAddon) continue;
            var match = Regex.Match(line, @"^node (?<path>[\d/]+) type=(?<type>\d+) visible=(?<visible>True|False) text=(?<text>.*)$");
            if (!match.Success) continue;
            var node = (AtkResNode*)Allocate(512);
            var nodePath = match.Groups["path"].Value;
            node->NodeId = uint.Parse(nodePath.Split('/')[^1]);
            node->Type = (NodeType)ushort.Parse(match.Groups["type"].Value);
            node->NodeFlags = match.Groups["visible"].Value == "True" ? NodeFlags.Visible : 0;
            Nodes[nodePath] = (nint)node;
            if (node->Type == NodeType.Text)
            {
                var bytes = Encoding.UTF8.GetBytes(match.Groups["text"].Value);
                var buffer = (byte*)Allocate((nuint)bytes.Length + 1);
                bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
                var text = (AtkTextNode*)node;
                text->NodeText.StringPtr = buffer;
                text->NodeText.BufUsed = bytes.Length + 1;
                text->NodeText.StringLength = bytes.Length;
            }
        }
        foreach (var (nodePath, address) in Nodes)
        {
            var node = (AtkResNode*)address;
            var slash = nodePath.LastIndexOf('/');
            if (slash >= 0 && Nodes.TryGetValue(nodePath[..slash], out var parent)) node->ParentNode = (AtkResNode*)parent;
            if ((int)node->Type < 1000) continue;
            var component = (AtkComponentBase*)Allocate((nuint)sizeof(AtkComponentBase));
            ((AtkComponentNode*)node)->Component = component;
            SetNodes(&component->UldManager, Nodes.Where(p => p.Key.StartsWith(nodePath + "/")).Select(p => p.Value).ToArray());
        }
        SetNodes(&Addon->UldManager, Nodes.Where(p => p.Key.Count(c => c == '/') <= 1).Select(p => p.Value).ToArray());
    }

    private void SetNodes(AtkUldManager* manager, nint[] nodes)
    {
        manager->NodeListCount = (ushort)nodes.Length;
        manager->NodeList = (AtkResNode**)Allocate((nuint)(nodes.Length * sizeof(nint)));
        for (var i = 0; i < nodes.Length; i++) manager->NodeList[i] = (AtkResNode*)nodes[i];
    }

    private void* Allocate(nuint size)
    {
        var memory = NativeMemory.AllocZeroed(size);
        allocations.Add((nint)memory);
        return memory;
    }

    public static bool Shown(nint address)
    {
        var node = (AtkResNode*)address;
        for (var depth = 0; node != null && depth < 32; depth++, node = node->ParentNode)
            if ((node->NodeFlags & NodeFlags.Visible) == 0) return false;
        return node == null;
    }

    public void Dispose()
    {
        foreach (var address in allocations) NativeMemory.Free((void*)address);
    }
}
