using Content.Shared.Ghost;
using Content.Shared.IdentityManagement.Components;

namespace Content.Shared.IdentityManagement;

/// <summary>
///     Static content API for getting the identity entities/names for a given entity.
///     This should almost always be used in favor of metadata name, if the entity in question is a human player that
///     can have identity.
/// </summary>
public static class Identity
{
    /// <summary>
    ///     Returns the name that should be used for this entity for identity purposes.
    /// </summary>
    public static string Name(EntityUid uid, IEntityManager ent, EntityUid? viewer=null)
    {
        if (!ent.TryGetComponent<MetaDataComponent>(uid, out var metadata))
            return uid.ToString();

        var uidName = metadata.EntityName;

        if (!ent.TryGetComponent<IdentityComponent>(uid, out var identity))
            return uidName;

        var ident = identity.IdentityEntitySlot.ContainedEntity;
        if (ident is null)
            return uidName;

        if (!ent.TryGetComponent<MetaDataComponent>(ident.Value, out var identityMetadata))
            return uidName;

        var identName = identityMetadata.EntityName;
        if (viewer == null || !CanSeeThroughIdentity(uid, viewer.Value, ent))
        {
            return identName;
        }
        if (uidName == identName)
        {
            return uidName;
        }

        return uidName + $" ({identName})";
    }

    /// <summary>
    ///     Returns the entity that should be used for identity purposes, for example to pass into localization.
    ///     This is an extension method because of its simplicity, and if it was any harder to call it might not
    ///     be used enough for loc.
    /// </summary>
    public static EntityUid Entity(EntityUid uid, IEntityManager ent)
    {
        if (!ent.TryGetComponent<IdentityComponent>(uid, out var identity))
            return uid;

        return identity.IdentityEntitySlot.ContainedEntity ?? uid;
    }

    public static bool CanSeeThroughIdentity(EntityUid uid, EntityUid viewer, IEntityManager ent)
    {
        // Would check for uid == viewer here but I think it's better for you to see yourself
        // how everyone else will see you, otherwise people will probably get confused and think they aren't disguised
        return ent.HasComponent<GhostComponent>(viewer);
    }

}
