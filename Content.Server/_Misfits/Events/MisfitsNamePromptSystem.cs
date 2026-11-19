// #Misfits Add - opens the naming prompt when a player takes over a mob.
using Content.Server.EUI;
using Content.Shared.Mind.Components;

namespace Content.Server._Misfits.Events;

public sealed class MisfitsNamePromptSystem : EntitySystem
{
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsNamePromptComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(Entity<MisfitsNamePromptComponent> ent, ref MindAddedMessage args)
    {
        if (args.Mind.Comp.Session is not { } session || TerminatingOrDeleted(ent.Owner))
            return;

        _eui.OpenEui(new MisfitsNamePromptEui(ent.Owner, this, ent.Comp.MaxLength), session);
    }

    // The mob's current name, for prefilling the prompt; empty when it is already gone.
    public string GetEntityName(EntityUid uid)
    {
        return TryComp<MetaDataComponent>(uid, out var meta) ? meta.EntityName : string.Empty;
    }

    // Renames the mob; false when it no longer exists.
    public bool TryRenameEntity(EntityUid uid, string name)
    {
        if (!TryComp<MetaDataComponent>(uid, out var meta))
            return false;

        _metaData.SetEntityName(uid, name, meta);
        return true;
    }
}
