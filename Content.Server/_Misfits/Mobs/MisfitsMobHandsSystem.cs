// #Misfits Add - hands for plain mobs, so a gecko can pick things up like a monkey.
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;

namespace Content.Server._Misfits.Mobs;

public sealed class MisfitsMobHandsSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsMobHandsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<MisfitsMobHandsComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<HandsComponent>(ent, out var hands))
            return;

        _hands.AddHand(ent, "left", HandLocation.Left, hands);
        _hands.AddHand(ent, "right", HandLocation.Right, hands);
    }
}

// Gives a mob a left and a right hand on spawn.
[RegisterComponent]
public sealed partial class MisfitsMobHandsComponent : Component;
