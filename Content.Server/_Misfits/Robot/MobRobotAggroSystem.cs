// #Misfits Change: Keep hostile robot NPC mobs neutral to player robot species until a player robot attacks.
// Mirrors the MobGhoulAggroSystem pattern used for feral ghouls.
using Content.Server.GameTicking;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Robot;

public sealed class MobRobotAggroSystem : EntitySystem
{
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    // #Misfits Fix: Doubled from 5 s — O(mobs × players) sync; at 70 players 10 s is still imperceptible.
    private static readonly TimeSpan NeutralSyncInterval = TimeSpan.FromSeconds(10);
    private TimeSpan _nextNeutralSync;

    public override void Initialize()
    {
        base.Initialize();

        // #Misfits Fix: System defunct — O(mobs × players) sync was a notable spike at 70 players.
        // Robot NPCs will now aggro player robots normally (no special neutral treatment).
        // Re-enable by un-commenting the subscriptions here and the Update body below.
        // SubscribeLocalEvent<MobRobotAggroComponent, ComponentStartup>(OnMobRobotStartup);
        // SubscribeLocalEvent<MobRobotAggroComponent, DamageChangedEvent>(OnMobRobotDamaged);
        // SubscribeLocalEvent<MobRobotAggroComponent, DisarmedEvent>(OnMobRobotDisarmed);
        // SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    public override void Update(float frameTime)
    {
        // #Misfits Fix: Defunct — see Initialize comment above.
        // base.Update(frameTime);
        // if (_timing.CurTime < _nextNeutralSync)
        //     return;
        // _nextNeutralSync = _timing.CurTime + NeutralSyncInterval;
        // SyncNeutralPlayerRobots();
    }

    private void OnMobRobotStartup(Entity<MobRobotAggroComponent> ent, ref ComponentStartup args)
    {
        SyncNeutralPlayerRobots(ent);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (!IsPlayerRobot(args.Mob))
            return;

        SyncNeutralPlayerRobot(args.Mob);
    }

    private void OnMobRobotDamaged(Entity<MobRobotAggroComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } attacker)
            return;

        if (!IsPlayerRobot(attacker))
            return;

        ProvokeAllMobRobots(attacker);
    }

    private void OnMobRobotDisarmed(Entity<MobRobotAggroComponent> ent, ref DisarmedEvent args)
    {
        if (!IsPlayerRobot(args.Source))
            return;

        ProvokeAllMobRobots(args.Source);
    }

    private void SyncNeutralPlayerRobots(Entity<MobRobotAggroComponent> ent)
    {
        EnsureComp<FactionExceptionComponent>(ent);
        SyncNeutralPlayerRobots();
    }

    private void SyncNeutralPlayerRobots()
    {
        var playerRobots = new ValueList<EntityUid>();
        var playerQuery = EntityQueryEnumerator<ActorComponent, HumanoidAppearanceComponent>();
        while (playerQuery.MoveNext(out var playerUid, out _, out var humanoid))
        {
            if (IsRobotSpecies(humanoid))
                playerRobots.Add(playerUid);
        }

        // Misfits Fix: hoist snapshot buffer outside the NPC loop so we only allocate once
        // instead of once-per-NPC — eliminates inner-loop heap allocations at scale.
        var ignoredBuffer = new ValueList<EntityUid>();

        var robotQuery = EntityQueryEnumerator<MobRobotAggroComponent, FactionExceptionComponent>();
        while (robotQuery.MoveNext(out var robotUid, out var aggro, out var exception))
        {
            foreach (var playerRobot in playerRobots)
            {
                if (!aggro.ProvokedPlayerRobots.Contains(playerRobot))
                    _npcFaction.IgnoreEntity((robotUid, exception), playerRobot);
            }

            // Snapshot Ignored into our reused buffer so iteration is safe while UnignoreEntity mutates the set.
            ignoredBuffer.Clear();
            foreach (var ignored in exception.Ignored)
                ignoredBuffer.Add(ignored);

            foreach (var ignored in ignoredBuffer)
            {
                if (aggro.ProvokedPlayerRobots.Contains(ignored))
                    continue;

                if (!HasComp<ActorComponent>(ignored))
                    continue;

                if (TryComp<HumanoidAppearanceComponent>(ignored, out var ignoredHumanoid) && IsRobotSpecies(ignoredHumanoid))
                    continue;

                _npcFaction.UnignoreEntity((robotUid, exception), ignored);
            }
        }
    }

    private void SyncNeutralPlayerRobot(EntityUid playerRobot)
    {
        var robotQuery = EntityQueryEnumerator<MobRobotAggroComponent, FactionExceptionComponent>();
        while (robotQuery.MoveNext(out var robotUid, out var aggro, out var exception))
        {
            if (aggro.ProvokedPlayerRobots.Contains(playerRobot))
                continue;

            _npcFaction.IgnoreEntity((robotUid, exception), playerRobot);
        }
    }

    private void ProvokeAllMobRobots(EntityUid attacker)
    {
        var robotQuery = EntityQueryEnumerator<MobRobotAggroComponent, FactionExceptionComponent>();
        while (robotQuery.MoveNext(out var robotUid, out var aggro, out var exception))
        {
            aggro.ProvokedPlayerRobots.Add(attacker);
            _npcFaction.UnignoreEntity((robotUid, exception), attacker);
            _npcFaction.AggroEntity((robotUid, exception), attacker);
        }
    }

    private bool IsPlayerRobot(EntityUid uid)
    {
        return HasComp<ActorComponent>(uid)
            && TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
            && IsRobotSpecies(humanoid);
    }

    private static bool IsRobotSpecies(HumanoidAppearanceComponent humanoid)
    {
        return humanoid.Species == "RobotMrHandy"
            || humanoid.Species == "RobotMrHandyZAX"
            || humanoid.Species == "RobotProtectron"
            || humanoid.Species == "RobotProtectronPolice"
            || humanoid.Species == "RobotProtectronBuilder"
            || humanoid.Species == "RobotProtectronFire"
            || humanoid.Species == "RobotProtectronPoliceZAX"
            || humanoid.Species == "RobotProtectronBuilderZAX"
            || humanoid.Species == "RobotProtectronFireZAX"
            || humanoid.Species == "RobotMrGutsy"
            || humanoid.Species == "RobotMrGutsyZAX"
            || humanoid.Species == "RobotAssaultron"
            || humanoid.Species == "RobotAssaultronTesla"
            || humanoid.Species == "RobotAssaultronZAX"
            || humanoid.Species == "RobotAssaultronTeslaZAX"
            || humanoid.Species == "RobotSentryBot"
            || humanoid.Species == "RobotSentryBotLaser"
            || humanoid.Species == "RobotSentryBotZAX"
            || humanoid.Species == "RobotSentryBotLaserZAX"
            || humanoid.Species == "RobotRobobrain"
            || humanoid.Species == "RobotRobobrainLaser"
            || humanoid.Species == "RobotRobobrainZAX"
            || humanoid.Species == "RobotRobobrainLaserZAX";
    }
}
