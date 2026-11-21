// #Misfits Add - the alpha gecko's nest: fed meat, it grows another gecko ghost role.
using Content.Server.Nutrition.Components;
using Content.Shared._Misfits.Events;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Events;

public sealed class MisfitsGeckoNestSystem : EntitySystem
{
    // The mound the alpha's action leaves behind.
    private static readonly EntProtoId Nest = "MisfitsGeckoNest";

    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsGeckoNestComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<MisfitsGeckoNestComponent, ComponentShutdown>(OnShutdown);

        // Only the alpha is granted the action, so a plain handler is enough.
        SubscribeLocalEvent<MisfitsMakeNestActionEvent>(OnMakeNest);
    }

    private void OnInteractUsing(Entity<MisfitsGeckoNestComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Any meat counts: raw, cooked, from any animal. Limbs and other body parts are left alone.
        if (!HasComp<FoodComponent>(args.Used))
            return;

        QueueDel(args.Used);
        args.Handled = true;
        ent.Comp.Fed++;

        if (ent.Comp.Fed < ent.Comp.FeedCost)
        {
            _popup.PopupEntity(
                Loc.GetString("misfits-gecko-nest-fed", ("fed", ent.Comp.Fed), ("cost", ent.Comp.FeedCost)),
                ent.Owner,
                args.User);
            return;
        }

        ent.Comp.Fed = 0;
        ent.Comp.Spawners.Add(Spawn(ent.Comp.Spawner, Transform(ent.Owner).Coordinates));
        _popup.PopupEntity(Loc.GetString("misfits-gecko-nest-grown"), ent.Owner, args.User);
    }

    // The nest is gone, so the roles it was offering go with it.
    private void OnShutdown(Entity<MisfitsGeckoNestComponent> ent, ref ComponentShutdown args)
    {
        foreach (var spawner in ent.Comp.Spawners)
        {
            if (!TerminatingOrDeleted(spawner))
                QueueDel(spawner);
        }

        ent.Comp.Spawners.Clear();
    }

    private void OnMakeNest(MisfitsMakeNestActionEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(args.Performer))
            return;

        Spawn(Nest, Transform(args.Performer).Coordinates);
        args.Handled = true;
    }
}
