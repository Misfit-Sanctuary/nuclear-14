using Content.Server._Misfits.Weapons.Ranged.Flamer;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Emp;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._RMC14.Mortar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.Server._RMC14.Mortar;

public sealed partial class MortarSystem : SharedMortarSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private FlamerLineSystem _flamer = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private SolutionContainerSystem _solution = default!;
    [Dependency] private ExpendableLightSystem _expendableLight = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    /// Seconds a shell payload lingers after burning out before it despawns.

    private const float DespawnGrace = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MortarShellComponent, MortarShellLandEvent>(OnShellLand);
        SubscribeLocalEvent<MortarChemicalShellComponent, MortarShellLandEvent>(OnChemicalShellLand);
    }

    private void OnShellLand(Entity<MortarShellComponent> shell, ref MortarShellLandEvent args)
    {
        // Nothing a shell carries gets through a roof, whatever brought it down.
        if (IsRoofed(_transform.ToMapCoordinates(args.Coordinates)))
            return;

        if (shell.Comp.FireTilePrototype is { } fire)
            _flamer.SpawnDiamond(fire, args.Coordinates, shell.Comp.FireRange);

        if (shell.Comp.Emp is { } emp)
            _emp.EmpPulse(_transform.ToMapCoordinates(args.Coordinates), emp.Range, emp.EnergyConsumption, emp.DisableDuration);

        if (shell.Comp.Smoke == null &&
            shell.Comp.Emp == null &&
            shell.Comp.ImpactSpawn == null &&
            shell.Comp.ImpactCamera == null &&
            shell.Comp.KineticDamage == null)
        {
            return;
        }

        if (!TryGetImpactTile(args.Coordinates, out var tile))
            return;

        if (shell.Comp.KineticDamage is { } kinetic)
            StrikeTile(kinetic, tile, shell.Owner);

        if (shell.Comp.Smoke is { } smoke)
            SpawnSmoke(smoke, tile);

        if (shell.Comp.ImpactCamera is { } camera && args.Mortar is { } mortar)
            SpawnImpactCamera(mortar, camera, tile);

        if (shell.Comp.ImpactSpawn is { } spawn)
        {
            var ent = Spawn(spawn.Id, tile);
            if (TryComp(ent, out ExpendableLightComponent? light))
            {
                _expendableLight.TryActivate((ent, light));

                // Cleaned up on the light's own burn time, so the two cannot drift apart.
                EnsureComp<TimedDespawnComponent>(ent).Lifetime =
                    light.GlowDuration + light.FadeOutDuration + DespawnGrace;
            }
        }
    }

    /// Hits everything on the impact tile, and nothing outside it.

    private void StrikeTile(DamageSpecifier damage, EntityCoordinates tile, EntityUid shell)
    {
        foreach (var uid in tile.GetEntitiesInTile(LookupFlags.Static | LookupFlags.Dynamic, _lookup))
        {
            if (uid == shell)
                continue;

            _damageable.TryChangeDamage(uid, damage, origin: shell);
        }
    }

    /// Leaves a camera on the impact tile for the mortar that fired the round.

    private void SpawnImpactCamera(EntityUid mortarUid, EntProtoId prototype, EntityCoordinates tile)
    {
        if (!TryComp(mortarUid, out MortarComponent? mortar))
            return;

        var camera = Spawn(prototype.Id, tile);
        if (!HasComp<MortarImpactCameraComponent>(camera))
        {
            Log.Error($"Mortar camera prototype {prototype} was missing {nameof(MortarImpactCameraComponent)}");
            Del(camera);
            return;
        }

        if (mortar.ImpactCamera is { } previous)
            QueueDel(previous);

        mortar.ImpactCamera = camera;
        Dirty(mortarUid, mortar);
    }

    /// Impact points are map relative, so floor spawns resolve to the grid that was hit.

    private bool TryGetImpactTile(EntityCoordinates coordinates, out EntityCoordinates tile)
    {
        tile = default;

        var map = _transform.ToMapCoordinates(coordinates);
        if (!_map.TryFindGridAt(map, out var gridUid, out var grid))
            return false;

        tile = _map.MapToGrid(gridUid, map);

        if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsSpace())
            return false;

        return true;
    }

    private void SpawnSmoke(MortarSmokePayload payload, EntityCoordinates coordinates)
    {
        SpawnSmoke(payload.Prototype, coordinates, payload.Solution, payload.Duration, payload.Spread);
    }

    private void SpawnSmoke(EntProtoId prototype, EntityCoordinates coordinates, Solution solution, float duration, int spread)
    {
        var ent = Spawn(prototype.Id, coordinates);
        if (!TryComp(ent, out SmokeComponent? smoke))
        {
            Log.Error($"Mortar smoke prototype {prototype} was missing {nameof(SmokeComponent)}");
            Del(ent);
            return;
        }

        // Cloned because the cloud takes ownership of the solution and spreads copies of it around.
        _smoke.StartSmoke(ent, solution.Clone(), duration, spread, smoke);
    }

    /// Pours one beaker into the other, reacts them, and releases what is left.

    private void OnChemicalShellLand(Entity<MortarChemicalShellComponent> shell, ref MortarShellLandEvent args)
    {
        if (IsRoofed(_transform.ToMapCoordinates(args.Coordinates)))
            return;

        if (shell.Comp.BeakerSlotA.Item is not { } beakerA ||
            shell.Comp.BeakerSlotB.Item is not { } beakerB ||
            !TryComp(beakerA, out FitsInDispenserComponent? fitsA) ||
            !TryComp(beakerB, out FitsInDispenserComponent? fitsB) ||
            !_solution.TryGetSolution(beakerA, fitsA.Solution, out var solutionEntityA, out var solutionA) ||
            !_solution.TryGetSolution(beakerB, fitsB.Solution, out var solutionEntityB, out var solutionB) ||
            solutionA.Volume == 0 ||
            solutionB.Volume == 0)
        {
            return;
        }

        // Same mixing as a chemical payload: pour B into A, then react the result.
        solutionA.MaxVolume += solutionB.MaxVolume;
        _solution.TryAddSolution(solutionEntityA.Value, solutionB);
        _solution.RemoveAllSolution(solutionEntityB.Value);
        _solution.UpdateChemicals(solutionEntityA.Value);
        solutionA.MaxVolume -= solutionB.MaxVolume;

        // A reaction that ate the whole mixture leaves nothing to release.
        if (solutionA.Volume <= 0)
            return;

        if (!TryGetImpactTile(args.Coordinates, out var tile))
            return;

        // No spread, so the mixture comes apart where it hit.
        SpawnSmoke(shell.Comp.SmokePrototype, tile, solutionA, shell.Comp.PuffDuration, 0);
    }

    protected override void ExplodeShell(EntityUid shell)
    {
        if (!TryComp(shell, out ExplosiveComponent? explosive))
            return;

        _explosion.TriggerExplosive(shell, explosive);
    }
}
