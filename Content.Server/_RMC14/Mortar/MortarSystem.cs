using Content.Server._Misfits.Weapons.Ranged.Flamer;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._RMC14.Mortar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._RMC14.Mortar;

public sealed partial class MortarSystem : SharedMortarSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private FlamerLineSystem _flamer = default!;
    [Dependency] private SmokeSystem _smoke = default!;
    [Dependency] private ExpendableLightSystem _expendableLight = default!;

    /// <summary>
    ///     How long a shell payload lingers after it has finished burning, in seconds, before it despawns.
    /// </summary>
    private const float DespawnGrace = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MortarShellComponent, MortarShellLandEvent>(OnShellLand);
    }

    private void OnShellLand(Entity<MortarShellComponent> shell, ref MortarShellLandEvent args)
    {
        // Nothing a shell carries gets through a roof, whatever brought it down.
        if (IsRoofed(_transform.ToMapCoordinates(args.Coordinates)))
            return;

        if (shell.Comp.FireTilePrototype is { } fire)
            _flamer.SpawnDiamond(fire, args.Coordinates, shell.Comp.FireRange);

        if (shell.Comp.Smoke == null && shell.Comp.ImpactSpawn == null)
            return;

        if (!TryGetImpactTile(args.Coordinates, out var tile))
            return;

        if (shell.Comp.Smoke is { } smoke)
            SpawnSmoke(smoke, tile);

        if (shell.Comp.ImpactSpawn is { } spawn)
        {
            var ent = Spawn(spawn.Id, tile);
            if (TryComp(ent, out ExpendableLightComponent? light))
            {
                _expendableLight.TryActivate((ent, light));

                // Anything a shell drops cleans itself up, whether or not its prototype remembers to say so.
                // Derived from the light's own burn time, so the two can never drift apart.
                EnsureComp<TimedDespawnComponent>(ent).Lifetime =
                    light.GlowDuration + light.FadeOutDuration + DespawnGrace;
            }
        }
    }

    /// <summary>
    ///     The impact point is stored relative to the map entity rather than a grid, so anything spawned
    ///     on the floor there has to be resolved to the grid the shell landed on.
    /// </summary>
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
        var ent = Spawn(payload.Prototype.Id, coordinates);
        if (!TryComp(ent, out SmokeComponent? smoke))
        {
            Log.Error($"Mortar smoke prototype {payload.Prototype} was missing {nameof(SmokeComponent)}");
            Del(ent);
            return;
        }

        _smoke.StartSmoke(ent, new Solution(), payload.Duration, payload.Spread, smoke);
    }

    protected override void ExplodeShell(EntityUid shell)
    {
        if (!TryComp(shell, out ExplosiveComponent? explosive))
            return;

        _explosion.TriggerExplosive(shell, explosive);
    }
}
