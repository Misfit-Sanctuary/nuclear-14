using Content.Server.Chat.Systems;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Misfits.SpecialStats;

public sealed class SpecialSecondWindSystem : SharedSpecialSecondWindSystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    private const string ScreamEmote = "Scream";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<SpecialComponent>();
        while (query.MoveNext(out var uid, out var special))
        {
            if (IsActive(special) && now >= special.SecondWindActiveUntil)
                End((uid, special));
        }
    }

    protected override void OnStarted(Entity<SpecialComponent> ent)
    {
        _chat.TryEmoteWithChat(ent.Owner, ScreamEmote, ignoreActionBlocker: true, forceEmote: true);

        _popup.PopupEntity(Loc.GetString("special-second-wind-start"), ent.Owner, ent.Owner, PopupType.Large);
        _popup.PopupEntity(
            Loc.GetString("special-second-wind-start-others", ("user", Identity.Entity(ent.Owner, EntityManager))),
            ent.Owner,
            Filter.PvsExcept(ent.Owner),
            true,
            PopupType.MediumCaution);
    }

    private void End(Entity<SpecialComponent> ent)
    {
        var bonus = ent.Comp.SecondWindAppliedBonus;
        ent.Comp.SecondWindAppliedBonus = FixedPoint2.Zero;
        Dirty(ent);

        if (HasComp<MobThresholdsComponent>(ent.Owner))
            Thresholds.AdjustMobStateThresholds(ent.Owner, -bonus, IncapacitatedStates);

        MovementSpeed.RefreshMovementSpeedModifiers(ent.Owner);

        if (_mobState.IsAlive(ent.Owner))
            _popup.PopupEntity(Loc.GetString("special-second-wind-end"), ent.Owner, ent.Owner, PopupType.Medium);
    }
}
