using Content.Server.Actions;
using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Warcry;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles.Jobs;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Warcry;

/// <summary>
/// Handles innate Legion and Tribal warcries.
/// </summary>
public sealed class WarcrySystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _targets = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WarcryComponent, ComponentStartup>(OnWarcryStartup);
        SubscribeLocalEvent<WarcryComponent, ComponentShutdown>(OnWarcryShutdown);
        SubscribeLocalEvent<WarcryComponent, PerformWarcryActionEvent>(OnWarcryAction);
    }

    // #Misfits Tweak - Gate expiry checks to 0.5 Hz; buff durations are seconds-scale so
    // 0.5 s resolution for RemComp is indistinguishable.
    private float _warcryAccumulator;
    private const float WarcryUpdateInterval = 0.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _warcryAccumulator += frameTime;
        if (_warcryAccumulator < WarcryUpdateInterval)
            return;
        _warcryAccumulator -= WarcryUpdateInterval;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WarcryBuffComponent>();

        while (query.MoveNext(out var uid, out var buff))
        {
            if (buff.ExpiresAt > now)
                continue;

            RemComp<WarcryBuffComponent>(uid);
        }

        var activeQuery = EntityQueryEnumerator<ActiveWarcryComponent>();
        while (activeQuery.MoveNext(out var uid, out var active))
        {
            if (active.ExpiresAt > now)
                continue;

            RemComp<ActiveWarcryComponent>(uid);
        }
    }

    private void OnWarcryStartup(EntityUid uid, WarcryComponent component, ComponentStartup args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.Action);
    }

    private void OnWarcryShutdown(EntityUid uid, WarcryComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnWarcryAction(EntityUid uid, WarcryComponent component, PerformWarcryActionEvent args)
    {
        if (args.Handled)
            return;

        if (!CanActivate(uid, component))
        {
            _popup.PopupEntity(Loc.GetString("warcry-popup-cannot-use"), uid, uid, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        args.Handled = true;

        var expiry = _timing.CurTime + component.Duration;
        _targets.Clear();
        _targets.Add(uid);
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, component.Range, _targets);

        var buffedAny = false;

        foreach (var target in _targets)
        {
            if (!IsValidTarget(target, component.TargetDepartment))
                continue;

            var buff = EnsureComp<WarcryBuffComponent>(target);
            buff.SpeedBonus = Math.Max(buff.SpeedBonus, component.SpeedBonus);
            if (expiry > buff.ExpiresAt)
                buff.ExpiresAt = expiry;

            Dirty(target, buff);
            _movementSpeed.RefreshMovementSpeedModifiers(target);
            _popup.PopupEntity(Loc.GetString(component.BuffPopup, ("user", uid)), target, target,
                component.CautionPopup ? PopupType.SmallCaution : PopupType.Small);
            buffedAny = true;
        }

        var active = EnsureComp<ActiveWarcryComponent>(uid);
        active.Radius = component.Range;
        active.Color = component.OverlayColor;
        active.ExpiresAt = expiry;
        Dirty(uid, active);

        var speechVerb = _prototype.Index<SpeechVerbPrototype>(component.SpeechVerb);
        _chat.TrySendInGameICMessage(
            uid,
            Loc.GetString(GetWarcryMessage(component)),
            InGameICChatType.Speak,
            false,
            speechVerbOverride: speechVerb);

        _popup.PopupCoordinates(
            Loc.GetString("warcry-popup-nearby", ("user", uid)),
            Transform(uid).Coordinates,
            component.CautionPopup ? PopupType.MediumCaution : PopupType.Medium);

        if (!buffedAny)
            _popup.PopupEntity(Loc.GetString("warcry-popup-no-allies"), uid, uid, PopupType.SmallCaution);
    }

    private string GetWarcryMessage(WarcryComponent component)
    {
        if (component.WarcryMessageCount <= 1)
            return component.WarcryMessage;

        var index = _random.Next(1, component.WarcryMessageCount + 1);
        return $"{component.WarcryMessage}-{index}";
    }

    private bool CanActivate(EntityUid uid, WarcryComponent component)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;

        if (!_jobs.MindTryGetJob(mindId, out _, out var prototype))
            return false;

        if (component.ActivatorJobs == null || component.ActivatorJobs.Count == 0)
            return true;

        return component.ActivatorJobs.Contains(prototype.ID);
    }

    private bool IsValidTarget(EntityUid uid, string departmentId)
    {
        if (_mobState.IsDead(uid))
            return false;

        if (!HasComp<MovementSpeedModifierComponent>(uid))
            return false;

        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;

        if (!_jobs.MindTryGetJob(mindId, out _, out var jobPrototype))
            return false;

        return _jobs.TryGetDepartment(jobPrototype.ID, out var department) && department.ID == departmentId;
    }
}