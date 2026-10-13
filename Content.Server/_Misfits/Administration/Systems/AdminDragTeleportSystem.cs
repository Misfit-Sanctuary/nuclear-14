// #Misfits Change/Add: Server-side handler for admin self-teleport via drag-and-drop.
// Receives the AdminSelfDragTeleportEvent from the client, validates admin permissions,
// and teleports the sender's attached entity to the requested coordinates.
// #Misfits Tweak: Restricted to aghost mode only — sender must be a ghost with CanGhostInteract.
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared._Misfits.Administration;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Network;

namespace Content.Server._Misfits.Administration.Systems;

/// <summary>
/// Handles the <see cref="AdminSelfDragTeleportEvent"/> sent by an admin client
/// who drags their own controlled entity (in aghost mode) to empty space.
/// <para>
/// Security: admin privileges AND aghost state are verified server-side before any action
/// is taken, regardless of client-side checks.
/// </para>
/// </summary>
public sealed class AdminDragTeleportSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AdminSelfDragTeleportEvent>(OnAdminDragTeleport);
    }

    private void OnAdminDragTeleport(AdminSelfDragTeleportEvent ev, EntitySessionEventArgs args)
    {
        // Server-side admin check — never trust the client alone.
        if (!_adminManager.IsAdmin(args.SenderSession))
            return;

        // Restrict to aghost mode: the sender's attached entity must be a ghost with CanGhostInteract.
        // This mirrors the client-side gate and prevents regular-admin exploitation.
        var senderEntity = args.SenderSession.AttachedEntity;
        if (senderEntity == null || !TryComp<GhostComponent>(senderEntity.Value, out var ghostComp) || !ghostComp.CanGhostInteract)
            return;

        // Resolve the dragged entity from the network ID.
        var entity = GetEntity(ev.DraggedEntity);
        if (!Exists(entity))
            return;

        // Convert the networked coordinates back to local EntityCoordinates.
        var targetCoords = GetCoordinates(ev.TargetCoordinates);

        // Validate that the target map still exists.
        if (!targetCoords.IsValid(EntityManager))
            return;

        // Perform the teleport.
        _transform.SetCoordinates(entity, targetCoords);
        _transform.AttachToGridOrMap(entity);

        // Log the admin action for accountability.
        _adminLogger.Add(
            LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.SenderSession.AttachedEntity ?? entity):actor} drag-teleported {ToPrettyString(entity):subject} to {targetCoords}");
    }
}
