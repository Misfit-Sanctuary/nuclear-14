// #Misfits Add - the sentient ghouls of the horde event: a shout that sends the pack at the nearest prey.
using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Misfits.Events;
using Content.Shared.Actions;
using Content.Shared.Chat;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Misfits.Ghoul;

public sealed class MisfitsGhoulRallySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private readonly HashSet<EntityUid> _nearby = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MisfitsGhoulRallyComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MisfitsGhoulRallyComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MisfitsGhoulRallyComponent, MisfitsGhoulRallyActionEvent>(OnRally);
    }

    private void OnStartup(EntityUid uid, MisfitsGhoulRallyComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.Action);
    }

    private void OnShutdown(EntityUid uid, MisfitsGhoulRallyComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnRally(EntityUid uid, MisfitsGhoulRallyComponent component, MisfitsGhoulRallyActionEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(uid))
            return;

        args.Handled = true;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, component.Range, _nearby);

        var answered = 0;
        foreach (var other in _nearby)
        {
            if (other == uid ||
                HasComp<ActorComponent>(other) ||
                !_factions.ContainsFaction(other, component.Faction.Id) ||
                !HasComp<HTNComponent>(other) ||
                !TryComp(other, out MobStateComponent? mobState) ||
                !_mobState.IsAlive(other, mobState) ||
                HasComp<FollowerAutoRecruitComponent>(other))
            {
                continue;
            }

            // Already someone else's follower: the shout does not steal them.
            if (TryComp(other, out RecruitedFollowerComponent? recruited) && recruited.Commander != uid)
                continue;

            if (TryFindPrey(other, component.TargetRange, out var prey))
                _factions.AggroEntity(other, prey.Value);

            // The horde answers: the ghoul takes the caller as its commander, so it follows and keeps fighting.
            AddComp(other, new FollowerAutoRecruitComponent { Commander = uid });
            answered++;
        }

        var message = answered == 0 ? "misfits-ghoul-rally-none" : "misfits-ghoul-rally-answered";
        _popup.PopupEntity(Loc.GetString(message, ("count", answered)), uid, uid);
        _chat.TrySendInGameICMessage(uid, Loc.GetString("misfits-ghoul-rally-cry"), InGameICChatType.Emote, false);
    }

    // The closest living thing this ghoul is already meant to hunt.
    private bool TryFindPrey(EntityUid ghoul, float range, [NotNullWhen(true)] out EntityUid? prey)
    {
        prey = null;
        var origin = Transform(ghoul).MapPosition;
        var closest = float.MaxValue;

        foreach (var hostile in _factions.GetNearbyHostiles(ghoul, range))
        {
            if (!TryComp(hostile, out MobStateComponent? mobState) || !_mobState.IsAlive(hostile, mobState))
                continue;

            var coords = Transform(hostile).MapPosition;
            if (coords.MapId != origin.MapId)
                continue;

            var distance = (coords.Position - origin.Position).LengthSquared();
            if (distance >= closest)
                continue;

            closest = distance;
            prey = hostile;
        }

        return prey != null;
    }
}
