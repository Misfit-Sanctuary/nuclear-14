using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared._Misfits.Expeditions;
using Content.Shared.EntityTable;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Expeditions;

/// <summary>
/// Gives generated expedition guardians a seeded identity, readable presentation,
/// bounded regeneration, and a single corpse-tied reward resolution.
/// </summary>
public sealed class ExpeditionBossSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityTableSystem _entityTables = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly MobThresholdSystem _mobThresholds = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private static readonly string[] DeathclawTitles = ["The Salt-Flat Alpha", "Graveclaw", "The Bone Warden"];
    private static readonly string[] GhoulTitles = ["Grave-Eater", "The Last Tenant", "The Hollow Mayor"];
    private static readonly string[] SuperMutantTitles = ["The War Chief", "Ironhide", "The FEV Warlord"];
    private static readonly string[] RobotTitles = ["The Rusted Warden", "Sentinel Zero", "The Lockdown Protocol"];
    private static readonly string[] MirelurkTitles = ["The Flooded Matriarch", "Shellbreaker", "The Brood Guardian"];
    private static readonly string[] NightstalkerTitles = ["The Tunnel Howler", "Nightfang", "The Dark Alpha"];
    private static readonly string[] RadscorpionTitles = ["The Irradiated Stinger", "Saltspine", "The Burrow King"];
    private static readonly string[] AntTitles = ["The Ash Queen", "The Fire Mound", "The Colony Heart"];
    private static readonly string[] RaiderTitles = ["The Platform Butcher", "The Last Conductor", "The Tunnel Reaver"];
    private static readonly string[] WildlifeTitles = ["The Wasteland Apex", "The Salt Maw", "The Old Predator"];

    public override void Initialize()
    {
        base.Initialize();
        // This is deliberately unrestricted. SharedStunSystem owns the directed
        // MobState subscription, while this system filters to its own runtime boss component.
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    public void ConfigureFinalGuardian(EntityUid uid, ExpeditionMobFamily family, string prototype, int partySize, Random rng)
    {
        ConfigureGuardian(uid, family, prototype, partySize, rng, isFinalGuardian: true);
    }

    /// <summary>
    /// Promotes a capped deep-room threat into a readable optional elite. Its
    /// reward remains attached to the corpse, but is smaller than the finale.
    /// </summary>
    public void ConfigureOptionalGuardian(EntityUid uid, ExpeditionMobFamily family, string prototype, int partySize, Random rng)
    {
        ConfigureGuardian(uid, family, prototype, partySize, rng, isFinalGuardian: false);
    }

    private void ConfigureGuardian(
        EntityUid uid,
        ExpeditionMobFamily family,
        string prototype,
        int partySize,
        Random rng,
        bool isFinalGuardian)
    {
        var profile = GetPresentation(family, partySize, rng, isFinalGuardian);
        var boss = EnsureComp<ExpeditionBossComponent>(uid);
        boss.DisplayName = profile.Name;
        boss.IsFinalGuardian = isFinalGuardian;
        boss.HealthFloor = RaiseHealthFloor(uid, prototype, partySize, isFinalGuardian);
        boss.RewardTable = isFinalGuardian
            ? family is ExpeditionMobFamily.Deathclaw or ExpeditionMobFamily.SuperMutant
                ? "N14ExpeditionHighRiskBossReward"
                : "N14ExpeditionBossReward"
            : "N14ExpeditionGuardianReward";
        boss.RewardSeed = rng.Next();
        boss.Regenerative = profile.Regenerative;
        boss.NextRegen = _timing.CurTime + TimeSpan.FromSeconds(2);
        Dirty(uid, boss);

        _metadata.SetEntityName(uid, profile.Name);

        var visual = EnsureComp<ExpeditionBossVisualsComponent>(uid);
        visual.ScaleMultiplier = new Vector2(profile.Scale, profile.Scale);
        Dirty(uid, visual);

        var light = _pointLight.EnsureLight(uid);
        _pointLight.SetColor(uid, profile.AuraColor, light);
        _pointLight.SetRadius(uid, profile.AuraRadius, light);
        _pointLight.SetEnergy(uid, profile.AuraEnergy, light);
        _pointLight.SetCastShadows(uid, false, light);
        _pointLight.SetEnabled(uid, true, light);

        Log.Info($"[N14 ProcGen] {(isFinalGuardian ? "final" : "optional")}-guardian='{profile.Name}', " +
                 $"family={family}, party={partySize}, healthFloor={boss.HealthFloor}, regenerative={profile.Regenerative}");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ExpeditionBossComponent, DamageableComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var boss, out var damageable, out var mobState))
        {
            if (!boss.Regenerative || mobState.CurrentState != MobState.Alive ||
                damageable.TotalDamage <= FixedPoint2.Zero || boss.NextRegen > now)
                continue;

            var healing = new DamageSpecifier();
            foreach (var (damageType, amount) in damageable.Damage.DamageDict)
            {
                if (amount > FixedPoint2.Zero)
                    healing.DamageDict[damageType] = FixedPoint2.New(-1);
            }

            if (healing.Empty)
                continue;

            _damageable.TryChangeDamage(uid, healing, ignoreResistances: true, interruptsDoAfters: false);
            boss.NextRegen = now + TimeSpan.FromSeconds(2);
            Dirty(uid, boss);
        }
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || !TryComp<ExpeditionBossComponent>(args.Target, out var boss) || boss.RewardClaimed)
            return;

        boss.RewardClaimed = true;
        Dirty(args.Target, boss);

        if (!_prototypes.TryIndex<EntityTablePrototype>(boss.RewardTable, out var rewardTable))
        {
            Log.Error($"[N14 ProcGen] Boss '{boss.DisplayName}' has missing reward table '{boss.RewardTable}'.");
            return;
        }

        var rewardPosition = Transform(args.Target).Coordinates;
        var rewards = _entityTables.GetSpawns(rewardTable.Table, new Random(boss.RewardSeed)).ToArray();
        foreach (var reward in rewards)
            Spawn(reward, rewardPosition);

        Log.Info($"[N14 ProcGen] boss-reward='{boss.DisplayName}', table='{boss.RewardTable}', count={rewards.Length}");
    }

    private int RaiseHealthFloor(EntityUid uid, string prototype, int partySize, bool isFinalGuardian)
    {
        // Sentries are already lethally accurate and armored; Maypoles and
        // Behemoths already have true boss thresholds. Do not make those fights
        // longer merely because they were promoted by procgen.
        if (prototype is "N14MobGhoulMaypole" or "N14MobBehemoth" ||
            prototype.StartsWith("N14MobRobotSentryBot", StringComparison.Ordinal))
            return 0;

        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds) || thresholds.Thresholds.Count == 0)
            return 0;

        var currentDeathThreshold = thresholds.Thresholds.Last().Key;
        var partyStep = Math.Min(Math.Max(partySize, 1) - 1, 4);
        var requestedFloor = isFinalGuardian
            ? 600 + partyStep * 100
            : 400 + partyStep * 50;
        if (currentDeathThreshold >= FixedPoint2.New(requestedFloor))
            return (int) currentDeathThreshold.Float();

        var multiplier = requestedFloor / currentDeathThreshold.Float();
        // MobThresholdSystem owns writes to the component; using its public API
        // retains replication and state validation rather than bypassing access
        // permissions on a networked gameplay component.
        foreach (var (threshold, state) in thresholds.Thresholds.ToArray())
            _mobThresholds.SetMobStateThreshold(uid, FixedPoint2.New(MathF.Round(threshold.Float() * multiplier)), state, thresholds);
        return requestedFloor;
    }

    private static BossPresentation GetPresentation(ExpeditionMobFamily family, int partySize, Random rng, bool isFinalGuardian)
    {
        var (titles, color) = family switch
        {
            ExpeditionMobFamily.Deathclaw => (DeathclawTitles, Color.FromHex("#e84a3f")),
            ExpeditionMobFamily.Ghoul => (GhoulTitles, Color.FromHex("#79d34f")),
            ExpeditionMobFamily.SuperMutant => (SuperMutantTitles, Color.FromHex("#d69b3d")),
            ExpeditionMobFamily.Robot => (RobotTitles, Color.FromHex("#4fb9f5")),
            ExpeditionMobFamily.Mirelurk => (MirelurkTitles, Color.FromHex("#39b8ad")),
            ExpeditionMobFamily.Nightstalker => (NightstalkerTitles, Color.FromHex("#8c59c7")),
            ExpeditionMobFamily.Radscorpion => (RadscorpionTitles, Color.FromHex("#b8d63b")),
            ExpeditionMobFamily.Ant => (AntTitles, Color.FromHex("#f07d2f")),
            ExpeditionMobFamily.Raider => (RaiderTitles, Color.FromHex("#d64f32")),
            _ => (WildlifeTitles, Color.FromHex("#d6bf63")),
        };

        var regenerativeChance = family is ExpeditionMobFamily.Ghoul or ExpeditionMobFamily.Mirelurk or ExpeditionMobFamily.Radscorpion
            ? isFinalGuardian ? 55 : 25
            : isFinalGuardian ? 20 : 8;
        var regenerative = rng.Next(100) < regenerativeChance;
        var major = isFinalGuardian && partySize >= 5;
        var rarity = isFinalGuardian ? major ? "Legendary" : "Champion" : "Elite";
        var name = $"{rarity} {titles[rng.Next(titles.Length)]}";
        if (regenerative)
            name = $"Regenerating {name}";

        return new BossPresentation(
            name,
            color,
            isFinalGuardian ? major ? 1.45f : 1.28f : 1.14f,
            isFinalGuardian ? major ? 3.6f : 3f : 2.4f,
            isFinalGuardian ? major ? 1.8f : 1.5f : 1.1f,
            regenerative);
    }

    private readonly record struct BossPresentation(
        string Name,
        Color AuraColor,
        float Scale,
        float AuraRadius,
        float AuraEnergy,
        bool Regenerative);
}
