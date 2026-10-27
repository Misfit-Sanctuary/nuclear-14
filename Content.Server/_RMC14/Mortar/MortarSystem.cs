using Content.Server._Misfits.Weapons.Ranged.Flamer;
using Content.Server.Explosion.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._RMC14.Mortar;

namespace Content.Server._RMC14.Mortar;

public sealed partial class MortarSystem : SharedMortarSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private FlamerLineSystem _flamer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MortarShellComponent, MortarShellLandEvent>(OnShellLand);
    }

    private void OnShellLand(Entity<MortarShellComponent> shell, ref MortarShellLandEvent args)
    {
        if (shell.Comp.FireTilePrototype is { } fire)
            _flamer.SpawnDiamond(fire, args.Coordinates, shell.Comp.FireRange);
    }

    protected override bool ExplodeShell(EntityUid shell)
    {
        if (!TryComp(shell, out ExplosiveComponent? explosive))
            return false;

        _explosion.TriggerExplosive(shell, explosive);
        return true;
    }
}
