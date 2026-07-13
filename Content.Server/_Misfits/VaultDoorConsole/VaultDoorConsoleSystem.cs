using System.Linq;
using System.Text;
using Content.Server._Misfits.RaidRequest;
using Content.Server.Doors.Systems;
using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Popups;
using Content.Shared._Misfits.VaultDoorConsole;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.VaultDoorConsole;

public sealed class VaultDoorConsoleSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly DeviceLinkSystem _deviceLink = default!;
    [Dependency] private readonly DoorSystem _door = default!;
    [Dependency] private readonly RaidRequestSystem _raidRequest = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float UpdateInterval = 1f;
    private float _accumulator;
    private static readonly string[] WordBank =
    {
        "OVERSEER", "RADIATOR", "TERMINAL", "SECURITY", "PROTOCOL",
        "DIRECTOR", "CHEMICAL", "DATABASE", "FIREARMS", "PASSWORD",
        "CRUSADER", "SCAVENGE", "OUTLAWED", "INDUSTRY", "ELECTRIC",
        "MATERIAL", "PORTABLE", "STANDARD", "SANCTION", "SOLITARY",
        "MILITARY", "STRATAGY", "FUNCTION", "ABSOLUTE", "ACADEMIC",
        "ACCURATE", "ACTIVATE", "ADEQUATE", "ADJACENT", "AIRPLANE",
        "ALPHABET", "ANALYSIS", "ANIMATED", "ARMAMENT", "ASSEMBLY",
        "ATTACKER", "ATTITUDE", "AUDIENCE", "BACKBONE", "BACTERIA",
        "BASELINE", "BLACKOUT", "BOUNDERY", "BRIEFING", "BUILDING",
        "BUSINESS", "CAMPAIGN", "CAPACITY", "CATALYST", "CAUTIOUS",
        "CEREMONY", "CHILDREN", "CIRCULAR", "CITIZENS", "CIVILIAN",
        "CLASSIFY", "COLLAPSE", "COLONIAL", "COMMANDO", "COMPOUND",
        "COMPUTER", "CONCRETE", "CONFLICT", "CONTRACT", "CORRIDOR",
        "COVERAGE", "CRIMINAL", "CRITICAL", "CYLINDER", "DEADLOCK",
        "DEADLINE", "DECIPHER", "DEDICATE", "DEFENDER", "DELIVERY",
        "DESOLATE", "DETECTOR", "DISASTER", "DISORDER", "DISPATCH",
        "DISTANCE", "DIVISION", "DOCUMENT", "DOMINANT", "DOMINATE",
        "DOWNFALL", "DRAINAGE", "DURATION", "ECONOMIC", "EDUCATED",
        "ELECTRON", "ELEMENTS", "ENCODING", "ENDEAVOR", "ENGINEER",
        "ENTRANCE", "EQUALITY", "EQUATION", "ERUPTION", "EVACUATE",
    };

    private static readonly string[] DudBrackets = { "()", "[]", "{}", "<>" };

    private const string NoiseChars = "!@#$%^&*-_=+;:,.?/\\|~";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VaultDoorConsoleComponent, BeforeActivatableUIOpenEvent>(OnBeforeOpen);
        SubscribeLocalEvent<VaultDoorConsoleComponent, VaultDoorConsoleGuessMessage>(OnGuess);

        SubscribeLocalEvent<VaultDoorPendingBoltComponent, DoorStateChangedEvent>(OnPendingDoorStateChanged);

        SubscribeLocalEvent<AutoLinkTransmitterComponent, ActivateInWorldEvent>(OnVaultButtonActivate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<VaultDoorConsoleComponent, VaultDoorConsoleGateComponent>();
        while (query.MoveNext(out var uid, out var comp, out var gate))
        {
            CheckExpiry((uid, comp));

            var raidActive = _raidRequest.IsFactionUnderActiveRaid(comp.RaidFaction);
            if (gate.RaidActive != raidActive)
            {
                gate.RaidActive = raidActive;
                Dirty(uid, gate);
            }
        }
    }

    private void CheckExpiry(Entity<VaultDoorConsoleComponent> ent)
    {
        var comp = ent.Comp;
        var now = _timing.CurTime;
        var changed = false;

        if (comp.SolvedUntil is { } solvedUntil && now >= solvedUntil)
        {
            UnboltDoors(comp);
            comp.Solved = false;
            comp.SolvedUntil = null;
            comp.ColumnA.Clear();
            comp.ColumnB.Clear();
            changed = true;
        }

        if (comp.LockedOutUntil is { } lockedUntil && now >= lockedUntil)
        {
            comp.LockedOutUntil = null;
            comp.ColumnA.Clear();
            comp.ColumnB.Clear();
            changed = true;
        }

        if (changed)
            UpdateUi(ent);
    }

    private void OnBeforeOpen(Entity<VaultDoorConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        var comp = ent.Comp;

        if (comp.LockedOutUntil == null && comp.SolvedUntil == null && comp.ColumnA.Count == 0 && comp.ColumnB.Count == 0)
            GeneratePuzzle(comp);

        UpdateUi(ent);
    }

    private void OnGuess(Entity<VaultDoorConsoleComponent> ent, ref VaultDoorConsoleGuessMessage args)
    {
        var comp = ent.Comp;

        if (comp.Solved || comp.LockedOutUntil != null)
            return;

        if (comp.Duds.ContainsKey(args.Token))
        {
            HandleDudClick(ent, args.Token, args.Actor);
            return;
        }

        if (!comp.WordPool.Contains(args.Token) || comp.RemovedWords.Contains(args.Token))
            return;

        HandleWordGuess(ent, args.Token, args.Actor);
    }

    private void HandleDudClick(Entity<VaultDoorConsoleComponent> ent, string token, EntityUid actor)
    {
        var comp = ent.Comp;
        if (comp.ConsumedDuds.Contains(token))
            return;

        comp.ConsumedDuds.Add(token);

        switch (comp.Duds[token])
        {
            case VaultDoorConsoleDudEffect.ResetAttempts:
                comp.AttemptsRemaining = comp.MaxAttempts;
                AppendLog(comp, "> BRACKET EXPANSION FOUND", "TRIES RESET.");
                _popup.PopupEntity("Dud removed. Tries reset.", ent, actor);
                break;

            case VaultDoorConsoleDudEffect.RemoveDud:
                var candidate = comp.WordPool.FirstOrDefault(w => w != comp.TargetWord && !comp.RemovedWords.Contains(w));
                if (candidate != null)
                {
                    comp.RemovedWords.Add(candidate);
                    AppendLog(comp, "> BRACKET EXPANSION FOUND", $"DUD REMOVED: {candidate}");
                    _popup.PopupEntity("Dud removed.", ent, actor);
                }
                break;
        }

        UpdateUi(ent);
    }

    private void HandleWordGuess(Entity<VaultDoorConsoleComponent> ent, string word, EntityUid actor)
    {
        var comp = ent.Comp;

        if (word == comp.TargetWord)
        {
            comp.Solved = true;
            comp.SolvedUntil = _timing.CurTime + comp.SuccessLockDuration;
            AppendLog(comp, $"> {HighlightGuess(word, comp.TargetWord)}", "ACCESS GRANTED",
                $"DOOR BOLTED OPEN FOR {comp.SuccessLockDuration.TotalMinutes:0} MINUTES");
            _popup.PopupEntity("ACCESS GRANTED. Unlocking vault door...", ent, actor);
            _deviceLink.InvokePort(ent, comp.SignalPort);
            MarkLinkedDoorsForBolting(ent);
            UpdateUi(ent);
            return;
        }

        var likeness = ComputeLikeness(word, comp.TargetWord);
        comp.AttemptsRemaining--;
        AppendLog(comp, $"> {HighlightGuess(word, comp.TargetWord)}", "ENTRY DENIED", $"LIKENESS={likeness}/{comp.WordLength}");

        if (comp.AttemptsRemaining <= 0)
        {
            comp.LockedOutUntil = _timing.CurTime + comp.LockoutDuration;
            AppendLog(comp, "TERMINAL LOCKED");
            _popup.PopupEntity("TERMINAL LOCKED. Try again later.", ent, actor);
        }

        UpdateUi(ent);
    }

    private void MarkLinkedDoorsForBolting(Entity<VaultDoorConsoleComponent> ent)
    {
        if (!TryComp<AutoLinkTransmitterComponent>(ent, out var transmitter))
            return;

        var grid = Transform(ent).GridUid;

        var query = EntityQueryEnumerator<AutoLinkReceiverComponent, DoorComponent>();
        while (query.MoveNext(out var doorUid, out var receiver, out var door))
        {
            if (receiver.AutoLinkChannel != transmitter.AutoLinkChannel)
                continue;

            if (Transform(doorUid).GridUid != grid)
                continue;

            if (door.State == DoorState.Open)
                BoltDoor((doorUid, ent));
            else
                AddComp(doorUid, new VaultDoorPendingBoltComponent { Console = ent.Owner });
        }
    }

    private void OnPendingDoorStateChanged(Entity<VaultDoorPendingBoltComponent> ent, ref DoorStateChangedEvent args)
    {
        if (args.State != DoorState.Open)
            return;

        var consoleUid = ent.Comp.Console;
        RemComp<VaultDoorPendingBoltComponent>(ent);

        if (TryComp<VaultDoorConsoleComponent>(consoleUid, out var comp))
            BoltDoor((ent.Owner, (consoleUid, comp)));
    }

    private void BoltDoor((EntityUid Door, Entity<VaultDoorConsoleComponent> Console) args)
    {
        var (doorUid, console) = args;
        var comp = console.Comp;

        var bolt = EnsureComp<DoorBoltComponent>(doorUid);
        _door.SetBoltsDown((doorUid, bolt), true);
        comp.BoltedDoors.Add(doorUid);

        var hackLock = EnsureComp<VaultDoorHackLockComponent>(doorUid);
        hackLock.LockedUntil = comp.SolvedUntil ?? _timing.CurTime + comp.SuccessLockDuration;
    }

    private void UnboltDoors(VaultDoorConsoleComponent comp)
    {
        foreach (var doorUid in comp.BoltedDoors)
        {
            if (TryComp<DoorBoltComponent>(doorUid, out var bolt))
                _door.SetBoltsDown((doorUid, bolt), false);

            RemComp<VaultDoorHackLockComponent>(doorUid);
        }

        comp.BoltedDoors.Clear();
    }

    /// Intercepts clicks on any *other* button/switch wired to a hack-locked vault door
    private void OnVaultButtonActivate(EntityUid uid, AutoLinkTransmitterComponent transmitter, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        if (HasComp<VaultDoorConsoleComponent>(uid))
            return;

        var grid = Transform(uid).GridUid;

        var query = EntityQueryEnumerator<VaultDoorHackLockComponent, AutoLinkReceiverComponent>();
        while (query.MoveNext(out var doorUid, out var hackLock, out var receiver))
        {
            if (receiver.AutoLinkChannel != transmitter.AutoLinkChannel)
                continue;

            if (Transform(doorUid).GridUid != grid)
                continue;

            var remaining = hackLock.LockedUntil - _timing.CurTime;
            var minutes = Math.Max(0, Math.Ceiling(remaining.TotalMinutes));
            _popup.PopupEntity($"The vault door is locked open by a security override. {minutes} minute(s) remaining.", uid, args.User);
            args.Handled = true;
            return;
        }
    }

    private void GeneratePuzzle(VaultDoorConsoleComponent comp)
    {
        comp.WordPool = _random.GetItems(WordBank, Math.Min(comp.PoolSize, WordBank.Length), allowDuplicates: false).ToList();
        comp.TargetWord = _random.Pick(comp.WordPool);
        comp.AttemptsRemaining = comp.MaxAttempts;
        comp.RemovedWords.Clear();
        comp.Duds.Clear();
        comp.ConsumedDuds.Clear();
        comp.Log.Clear();
        comp.Solved = false;

        var rowJobs = new List<(VaultDoorConsoleTokenKind Kind, string Token, string Display)>();

        foreach (var word in comp.WordPool)
            rowJobs.Add((VaultDoorConsoleTokenKind.Word, word, word));

        for (var i = 0; i < comp.DudCount; i++)
        {
            var id = $"DUD{i}";
            var bracket = _random.Pick(DudBrackets);
            comp.Duds[id] = _random.Prob(0.5f) ? VaultDoorConsoleDudEffect.ResetAttempts : VaultDoorConsoleDudEffect.RemoveDud;
            rowJobs.Add((VaultDoorConsoleTokenKind.Dud, id, bracket));
        }

        for (var i = 0; i < comp.NoiseRowCount; i++)
            rowJobs.Add((VaultDoorConsoleTokenKind.None, string.Empty, string.Empty));

        _random.Shuffle(rowJobs);

        var rows = rowJobs.Select(BuildRow).ToList();
        var half = (rows.Count + 1) / 2;
        comp.ColumnA = rows.Take(half).ToList();
        comp.ColumnB = rows.Skip(half).ToList();

        AppendLog(comp, "ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL", $"{comp.AttemptsRemaining} ATTEMPT(S) LEFT");
    }

    private List<VaultDoorConsoleSegment> BuildRow((VaultDoorConsoleTokenKind Kind, string Token, string Display) job)
    {
        var segments = new List<VaultDoorConsoleSegment>();
        var address = $"0x{_random.Next(0x1000, 0xFFFF):X4} ";
        segments.Add(new VaultDoorConsoleSegment(address, VaultDoorConsoleTokenKind.None, string.Empty, false));

        if (job.Kind == VaultDoorConsoleTokenKind.None)
        {
            segments.Add(new VaultDoorConsoleSegment(GenerateNoise(_random.Next(6, 14)), VaultDoorConsoleTokenKind.None, string.Empty, false));
            return segments;
        }

        var before = GenerateNoise(_random.Next(1, 6));
        var after = GenerateNoise(_random.Next(1, 6));

        segments.Add(new VaultDoorConsoleSegment(before, VaultDoorConsoleTokenKind.None, string.Empty, false));
        segments.Add(new VaultDoorConsoleSegment(job.Display, job.Kind, job.Token, false));
        segments.Add(new VaultDoorConsoleSegment(after, VaultDoorConsoleTokenKind.None, string.Empty, false));

        return segments;
    }

    private string GenerateNoise(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = NoiseChars[_random.Next(NoiseChars.Length)];
        return new string(chars);
    }

    private static int ComputeLikeness(string guess, string target)
    {
        var likeness = 0;
        for (var i = 0; i < guess.Length && i < target.Length; i++)
        {
            if (guess[i] == target[i])
                likeness++;
        }
        return likeness;
    }

    private static string HighlightGuess(string guess, string target)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < guess.Length; i++)
        {
            var match = i < target.Length && guess[i] == target[i];
            if (match)
                sb.Append("[color=#ffe066]").Append(guess[i]).Append("[/color]");
            else
                sb.Append(guess[i]);
        }
        return sb.ToString();
    }

    private void AppendLog(VaultDoorConsoleComponent comp, params string[] lines)
    {
        comp.Log.AddRange(lines);
    }

    private List<List<VaultDoorConsoleSegment>> BuildDisplayColumn(VaultDoorConsoleComponent comp, List<List<VaultDoorConsoleSegment>> column)
    {
        var result = new List<List<VaultDoorConsoleSegment>>(column.Count);
        foreach (var row in column)
        {
            var newRow = new List<VaultDoorConsoleSegment>(row.Count);
            foreach (var seg in row)
            {
                var used = seg.Kind switch
                {
                    VaultDoorConsoleTokenKind.Word => comp.RemovedWords.Contains(seg.Token),
                    VaultDoorConsoleTokenKind.Dud => comp.ConsumedDuds.Contains(seg.Token),
                    _ => false,
                };
                newRow.Add(new VaultDoorConsoleSegment(seg.Text, seg.Kind, seg.Token, used));
            }
            result.Add(newRow);
        }
        return result;
    }

    private void UpdateUi(Entity<VaultDoorConsoleComponent> ent)
    {
        var comp = ent.Comp;
        var now = _timing.CurTime;

        TimeSpan? lockedRemaining = comp.LockedOutUntil is { } lockedUntil && lockedUntil > now
            ? lockedUntil - now
            : null;

        TimeSpan? solvedRemaining = comp.SolvedUntil is { } solvedUntil && solvedUntil > now
            ? solvedUntil - now
            : null;

        var state = new VaultDoorConsoleBoundUserInterfaceState(
            BuildDisplayColumn(comp, comp.ColumnA),
            BuildDisplayColumn(comp, comp.ColumnB),
            comp.AttemptsRemaining,
            comp.MaxAttempts,
            comp.Log,
            comp.Solved,
            solvedRemaining,
            lockedRemaining != null,
            lockedRemaining);

        _ui.SetUiState(ent.Owner, VaultDoorConsoleUiKey.Key, state);
    }
}
