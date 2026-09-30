// #Misfits Add - Outsiders who get lost in the bunker tunnels and wander back out the same hatch.
using Content.Server.Popups;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// Put on someone while they are lost in the bunker tunnels.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerTunnelLostComponent : Component
{
    /// <summary>
    /// The hatch they went down, and will come back out of.
    /// </summary>
    [ViewVariables]
    public EntityUid Hatch;

    /// <summary>
    /// When they find their way back out.
    /// </summary>
    [ViewVariables]
    public TimeSpan ReleaseAt;

    /// <summary>
    /// When to next remind them they are lost.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextReminder;

    /// <summary>
    /// How long they are safe from getting lost again once they are out.
    /// </summary>
    [ViewVariables]
    public TimeSpan Cooldown;
}

/// <summary>
/// Put on someone who just found their way out of the tunnels. Until it runs out they can't get
/// lost again; going down a hatch drops them in the mines like normal.
/// </summary>
[RegisterComponent]
public sealed partial class BunkerTunnelLostCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}

/// <summary>
/// Holds a lost outsider inside the hatch they went down (like someone shut in a closet: unseen and
/// unable to act), blinds them so it feels like the dark tunnels, and puts them back on top of the
/// same hatch when the time is up.
/// <para>
/// If the hatch is deleted while someone is lost in it (an admin re-roll, say), they are dropped
/// out onto the surface first so they are never deleted along with it.
/// </para>
/// </summary>
public sealed class BunkerTunnelLostSystem : EntitySystem
{
    public const string ContainerId = "bunker_tunnel_lost";
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromSeconds(5);

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BunkerTeleporterComponent, EntityTerminatingEvent>(OnHatchTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BunkerTunnelLostComponent>();
        while (query.MoveNext(out var uid, out var lost))
        {
            if (now >= lost.ReleaseAt)
            {
                Release(uid, lost);
                continue;
            }

            if (now < lost.NextReminder)
                continue;

            lost.NextReminder = now + ReminderInterval;
            var seconds = (int) Math.Ceiling((lost.ReleaseAt - now).TotalSeconds);
            _popup.PopupCursor(Loc.GetString("bunker-tunnel-lost-reminder", ("seconds", seconds)), uid, PopupType.MediumCaution);
        }
    }

    /// <summary>
    /// Lose <paramref name="user"/> in the tunnels under <paramref name="hatch"/> for
    /// <paramref name="time"/>. Returns false if they could not be put in (then nothing happened).
    /// </summary>
    public bool TryLose(EntityUid user, EntityUid hatch, TimeSpan time, TimeSpan cooldown)
    {
        if (HasComp<BunkerTunnelLostComponent>(user))
            return false;

        var container = _container.EnsureContainer<Container>(hatch, ContainerId);
        if (!_container.Insert(user, container))
            return false;

        var lost = EnsureComp<BunkerTunnelLostComponent>(user);
        lost.Hatch = hatch;
        lost.ReleaseAt = _timing.CurTime + time;
        lost.NextReminder = _timing.CurTime + ReminderInterval;
        lost.Cooldown = cooldown;

        _status.TryAddStatusEffect(user, TemporaryBlindnessSystem.BlindingStatusEffect, time, true,
            TemporaryBlindnessSystem.BlindingStatusEffect);
        // Cursor popups, because the entity they are attached to is shut inside the hatch.
        _popup.PopupCursor(Loc.GetString("bunker-tunnel-lost"), user, PopupType.LargeCaution);
        return true;
    }

    /// <summary>
    /// True while this person is still inside their "can't get lost again" window.
    /// </summary>
    public bool IsOnCooldown(EntityUid user)
    {
        if (!TryComp<BunkerTunnelLostCooldownComponent>(user, out var cooldown))
            return false;

        if (_timing.CurTime < cooldown.Until)
            return true;

        RemComp<BunkerTunnelLostCooldownComponent>(user);
        return false;
    }

    private void Release(EntityUid uid, BunkerTunnelLostComponent lost)
    {
        // The cooldown starts once they are back out, so all of it counts.
        EnsureComp<BunkerTunnelLostCooldownComponent>(uid).Until = _timing.CurTime + lost.Cooldown;
        RemComp<BunkerTunnelLostComponent>(uid);

        if (_container.TryGetContainingContainer((uid, null, null), out var container)
            && container.ID == ContainerId)
        {
            _container.Remove(uid, container, force: true);
        }

        _status.TryRemoveStatusEffect(uid, TemporaryBlindnessSystem.BlindingStatusEffect);
        _popup.PopupCursor(Loc.GetString("bunker-tunnel-lost-found"), uid, PopupType.Medium);
    }

    /// <summary>
    /// True if anyone is currently lost in the tunnels under this hatch. The hatch's examine text
    /// (in <see cref="BunkerTeleporterSystem"/>) uses this so people on the surface can hear them.
    /// </summary>
    public bool IsAnyoneLostIn(EntityUid hatch)
    {
        return _container.TryGetContainer(hatch, ContainerId, out var container) && container.ContainedEntities.Count > 0;
    }

    private void OnHatchTerminating(EntityUid uid, BunkerTeleporterComponent component, ref EntityTerminatingEvent args)
    {
        if (!_container.TryGetContainer(uid, ContainerId, out var container))
            return;

        var drop = _transform.GetMapCoordinates(uid);
        foreach (var lostUid in _container.EmptyContainer(container, force: true))
        {
            _transform.SetMapCoordinates(lostUid, drop);
            if (TryComp<BunkerTunnelLostComponent>(lostUid, out var lost))
                Release(lostUid, lost);
        }
    }
}
