using System.Numerics;
using Content.Server.Storage.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Server.Power.Generator;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Power.Generator;
using Content.Shared._Misfits.MaterialExtractor;
using Content.Shared.Storage;
using Content.Shared.Spawning;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Verbs;
using Robust.Shared;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Server.GameObjects;

namespace Content.Server._Misfits.MaterialExtractor;

/// <summary>Runs the low-frequency seismic pulse and deposits raw materials in the extractor hopper.</summary>
public sealed partial class MaterialExtractorSystem : EntitySystem
{
    private static readonly SoundPathSpecifier ThumpSound = new("/Audio/Effects/Footsteps/largethud.ogg");
    private const float LifecycleEmoteRange = 30f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private GeneratorSystem _generator = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(GeneratorSystem));
        SubscribeLocalEvent<MaterialExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MaterialExtractorComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<MaterialExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStartMessage>(OnStartMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorStopMessage>(OnStopMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, MaterialExtractorEjectFuelMessage>(OnEjectFuelMessage);
        SubscribeLocalEvent<MaterialExtractorComponent, GetVerbsEvent<AlternativeVerb>>(GetAlternativeVerb);
    }

    private void GetAlternativeVerb(Entity<MaterialExtractorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => _ui.OpenUi(ent.Owner, MaterialExtractorUiKey.Key, user),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/zap.svg.192dpi.png")),
            Text = "Material extractor controls",
        });
    }

    private void OnStartMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorStartMessage args)
    {
        if (!Transform(ent).Anchored || !TryComp<FuelGeneratorComponent>(ent, out var generator)
            || generator.On || _generator.GetFuel(ent) <= 0f || _generator.GetIsClogged(ent))
            return;

        _generator.SetFuelGeneratorOn(ent, true, generator);
    }

    private void OnStopMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorStopMessage args)
    {
        if (TryComp<FuelGeneratorComponent>(ent, out var generator))
            _generator.SetFuelGeneratorOn(ent, false, generator);
    }

    private void OnEjectFuelMessage(Entity<MaterialExtractorComponent> ent, ref MaterialExtractorEjectFuelMessage args)
    {
        _generator.EmptyGenerator(ent);
    }

    private void OnDamageChanged(Entity<MaterialExtractorComponent> ent, ref DamageChangedEvent args)
    {
        ent.Comp.DamagePauseUntil = _timing.CurTime + TimeSpan.FromSeconds(30);
        SetBeacon(ent.Owner, ent.Comp, true);
    }

    private void OnExamined(Entity<MaterialExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("material-extractor-examine", ("quality", ent.Comp.DepositQuality.ToLowerInvariant())));
    }

    private void OnMapInit(Entity<MaterialExtractorComponent> ent, ref MapInitEvent args)
    {
        var qualityRoll = _random.NextFloat();
        if (qualityRoll < ent.Comp.PoorDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("POOR", ent.Comp.PoorYieldMultiplier);
        }
        else if (qualityRoll > 1f - ent.Comp.RichDepositChance)
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("RICH", ent.Comp.RichYieldMultiplier);
        }
        else
        {
            (ent.Comp.DepositQuality, ent.Comp.YieldMultiplier) = ("FAIR", 1f);
        }
        _lights.SetEnabled(ent.Owner, false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MaterialExtractorComponent, StorageComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var storage))
        {
            // This is a self-contained welding-fuel generator. Its normal generator
            // start/stop state is the extractor's operating switch.
            if (!TryComp<FuelGeneratorComponent>(uid, out var generator) || !generator.On)
            {
                var fuelExhausted = !TryComp<FuelGeneratorComponent>(uid, out _) || _generator.GetFuel(uid) <= 0f;
                if (extractor.WasRunning)
                {
                    SendLifecycleEmote(uid, fuelExhausted
                        ? "material-extractor-fuel-depleted"
                        : "material-extractor-stopped");
                }

                extractor.WasRunning = false;
                SetBeacon(uid, extractor, false);
                UpdateUi(uid, extractor, false, false, fuelExhausted);
                continue;
            }

            // The extractor is not an unattended income source. A living player must
            // remain close enough to actively work its controls or it shuts itself off.
            if (!HasNearbyOperator((uid, extractor)))
            {
                _generator.SetFuelGeneratorOn(uid, false, generator);
                if (extractor.WasRunning)
                    SendLifecycleEmote(uid, "material-extractor-unattended");

                extractor.WasRunning = false;
                SetBeacon(uid, extractor, false);
                UpdateUi(uid, extractor, false, true, false);
                continue;
            }

            if (!extractor.WasRunning)
            {
                extractor.WasRunning = true;
                SendLifecycleEmote(uid, "material-extractor-started");
                extractor.WarningSent = false;
                extractor.NextPulse = _timing.CurTime;
                extractor.NextWave = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(extractor.FirstWaveMinSeconds, extractor.FirstWaveMaxSeconds + 1));
            }

            UpdateLowFuelWarning(uid, extractor);
            UpdateUi(uid, extractor, true, false, false);

            extractor.ActiveAttackers.RemoveWhere(attacker => Deleted(attacker));

            if (_timing.CurTime < extractor.DamagePauseUntil)
            {
                SetBeacon(uid, extractor, true);
                continue;
            }

            if (!extractor.WarningSent && _timing.CurTime >= extractor.NextWave - TimeSpan.FromSeconds(extractor.WaveWarningSeconds))
            {
                extractor.WarningSent = true;
                SetBeacon(uid, extractor, true);
                _audio.PlayPvs(ThumpSound, uid,
                    AudioParams.Default.WithVolume(-3f).WithMaxDistance(30f));
            }

            if (_timing.CurTime >= extractor.NextWave)
                StartWave(uid, extractor);

            if (_timing.CurTime >= extractor.NextPulse)
            {
                SetBeacon(uid, extractor, !extractor.BeaconOn);
                _audio.PlayPvs(ThumpSound, uid,
                    AudioParams.Default.WithVolume(-7f).WithMaxDistance(22f));
                ProduceOutput(uid, extractor, storage);
                extractor.NextPulse = _timing.CurTime + PulseDelay(extractor);
            }
        }
    }

    private void UpdateUi(EntityUid uid, MaterialExtractorComponent extractor, bool running, bool unattended, bool fuelDepleted)
    {
        if (!_ui.IsUiOpen(uid, MaterialExtractorUiKey.Key))
            return;

        var fuel = TryComp<FuelGeneratorComponent>(uid, out _) ? _generator.GetFuel(uid) : 0f;
        var capacity = 0f;
        if (_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var tank))
            capacity = tank.MaxVolume.Float();

        _ui.SetUiState(uid, MaterialExtractorUiKey.Key,
            new MaterialExtractorUiState
            {
                Running = running,
                Fuel = fuel,
                FuelCapacity = capacity,
                Unattended = unattended,
                FuelDepleted = fuelDepleted,
            });
    }

    private void SendLifecycleEmote(EntityUid uid, string localizationId)
    {
        _chat.SendAreaEmote(uid, Loc.GetString(localizationId), LifecycleEmoteRange);
    }

    private void ProduceOutput(EntityUid extractorUid, MaterialExtractorComponent extractor, StorageComponent storage)
    {
        var output = Spawn(SelectOutput(extractor), Transform(extractorUid).Coordinates);
        if (_storage.Insert(extractorUid, output, out _, storageComp: storage, playSound: false))
            return;

        Del(output);
        SetBeacon(extractorUid, extractor, true);
    }

    private void UpdateLowFuelWarning(EntityUid uid, MaterialExtractorComponent extractor)
    {
        if (!_solution.TryGetSolution(uid, extractor.FuelSolution, out _, out var fuelTank) || fuelTank.MaxVolume <= 0)
            return;

        var fraction = fuelTank.GetTotalPrototypeQuantity(extractor.FuelReagent).Float() / fuelTank.MaxVolume.Float();
        if (fraction > extractor.LowFuelWarningFraction)
        {
            extractor.LowFuelWarningIssued = false;
            return;
        }

        if (extractor.LowFuelWarningIssued)
            return;

        extractor.LowFuelWarningIssued = true;
        _chat.TrySendInGameICMessage(uid,
            Loc.GetString("material-extractor-low-fuel", ("percent", MathF.Round(fraction * 100f))),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);
        SetBeacon(uid, extractor, true);
    }

    private static TimeSpan PulseDelay(MaterialExtractorComponent extractor)
        => TimeSpan.FromSeconds(extractor.PulseIntervalSeconds / extractor.YieldMultiplier);

    private string SelectOutput(MaterialExtractorComponent extractor)
    {
        var totalWeight = 0;
        foreach (var weight in extractor.OutputWeights.Values)
            totalWeight += weight;

        if (totalWeight <= 0)
            throw new InvalidOperationException("Material extractor output weights must have a positive total.");

        var roll = _random.Next(totalWeight);
        string? fallback = null;
        foreach (var (prototype, weight) in extractor.OutputWeights)
        {
            fallback = prototype;
            roll -= weight;
            if (roll < 0)
                return prototype;
        }

        return fallback!;
    }

    private bool HasNearbyOperator(Entity<MaterialExtractorComponent> extractor)
    {
        var origin = Transform(extractor);
        var originPosition = _transform.GetWorldPosition(extractor);
        var query = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var playerUid, out _, out var mobState, out var player))
        {
            if (mobState.CurrentState == MobState.Alive
                && player.MapID == origin.MapID
                && Vector2.DistanceSquared(_transform.GetWorldPosition(playerUid), originPosition) <= extractor.Comp.OperatorRadius * extractor.Comp.OperatorRadius)
                return true;
        }

        return false;
    }

    private void StartWave(EntityUid extractorUid, MaterialExtractorComponent extractor)
    {
        _chat.TrySendInGameICMessage(extractorUid,
            Loc.GetString("material-extractor-rumble"),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);

        var count = _random.Next(extractor.WaveMinMobCount, extractor.WaveMaxMobCount + 1);
        var prototype = SelectWaveMob(extractor);

        for (var i = 0; i < count; i++)
        {
            if (!TryFindWaveSpawnCoordinates(extractorUid, extractor, out var spawnCoordinates))
                continue;

            var attacker = EntityManager.SpawnIfUnobstructed(prototype, spawnCoordinates, CollisionGroup.MobMask);
            if (attacker == null)
                continue;

            extractor.ActiveAttackers.Add(attacker.Value);

            if (TryComp<HTNComponent>(attacker.Value, out var htn))
            {
                _npc.SetBlackboard(attacker.Value, NPCBlackboard.FollowTarget,
                    new EntityCoordinates(extractorUid, Vector2.Zero), htn);
                _htn.Replan(htn);
            }
        }

        extractor.WarningSent = false;
        extractor.NextWave = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(extractor.WaveMinSeconds, extractor.WaveMaxSeconds + 1));
        SetBeacon(extractorUid, extractor, true);
    }

    private bool TryFindWaveSpawnCoordinates(EntityUid extractorUid, MaterialExtractorComponent extractor, out EntityCoordinates spawnCoordinates)
    {
        spawnCoordinates = EntityCoordinates.Invalid;
        var extractorTransform = Transform(extractorUid);
        var extractorPosition = _transform.GetWorldPosition(extractorUid);

        for (var attempt = 0; attempt < extractor.WaveSpawnAttempts; attempt++)
        {
            var angle = _random.NextFloat() * MathF.Tau;
            var distance = _random.NextFloat(extractor.WaveSpawnMinDistance, extractor.WaveSpawnMaxDistance);
            var candidateMapCoordinates = new MapCoordinates(
                extractorPosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance,
                extractorTransform.MapID);

            if (IsInAnyPlayerPvs(candidateMapCoordinates, extractor.WaveSpawnPvsBuffer)
                || !_map.TryFindGridAt(candidateMapCoordinates, out var gridUid, out var grid))
                continue;

            var tile = _map.CoordinatesToTile(gridUid, grid, candidateMapCoordinates);
            var candidateCoordinates = _map.GridTileToLocal(gridUid, grid, tile);
            var tileRef = _map.GetTileRef(gridUid, grid, candidateCoordinates);
            if (tileRef.Tile.IsSpace() || _turf.IsTileBlocked(tileRef, CollisionGroup.MobMask))
                continue;

            spawnCoordinates = candidateCoordinates;
            return true;
        }

        return false;
    }

    private bool IsInAnyPlayerPvs(MapCoordinates candidate, float buffer)
    {
        var basePvsRange = _cfg.GetCVar(CVars.NetMaxUpdateRange);
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out var playerUid, out _, out var playerTransform))
        {
            if (playerTransform.MapID != candidate.MapId)
                continue;

            var pvsScale = TryComp<EyeComponent>(playerUid, out var eye) ? MathF.Max(eye.PvsScale, 0.1f) : 1f;
            var halfPvsRange = basePvsRange * pvsScale / 2f + buffer;
            var playerPosition = _transform.GetWorldPosition(playerUid) + (eye?.Offset ?? Vector2.Zero);
            var delta = Vector2.Abs(candidate.Position - playerPosition);
            if (delta.X <= halfPvsRange && delta.Y <= halfPvsRange)
                return true;
        }

        return false;
    }

    private string SelectWaveMob(MaterialExtractorComponent extractor)
    {
        var totalWeight = 0;
        foreach (var weight in extractor.WaveMobWeights.Values)
            totalWeight += weight;

        if (totalWeight <= 0)
            throw new InvalidOperationException("Material extractor wave mob weights must have a positive total.");

        var roll = _random.Next(totalWeight);
        string? fallback = null;
        foreach (var (prototype, weight) in extractor.WaveMobWeights)
        {
            fallback = prototype;
            roll -= weight;
            if (roll < 0)
                return prototype;
        }

        return fallback!;
    }

    private void SetBeacon(EntityUid uid, MaterialExtractorComponent extractor, bool enabled)
    {
        extractor.BeaconOn = enabled;
        _lights.SetEnabled(uid, enabled);
    }
}
