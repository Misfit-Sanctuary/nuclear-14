using System.Linq;
using Content.Server.Body.Systems;
using Content.Shared._Misfits.Gallows;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Gallows;

public sealed class GallowsSystem : SharedGallowsSystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BlindableSystem _blindable = default!;
    [Dependency] private readonly SharedStutteringSystem _stuttering = default!;
    [Dependency] private readonly Content.Server.Chat.Systems.ChatSystem _chat = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GallowsComponent, GallowsAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<GallowsComponent, GallowsHangDoAfterEvent>(OnHangDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<GallowsComponent, StrapComponent>();
        while (query.MoveNext(out var uid, out var gallows, out var strap))
        {
            if (!gallows.Hanging || strap.BuckledEntities.Count == 0)
                continue;

            var victim = strap.BuckledEntities.First();
            if (!CanBeHanged(victim) || !TryComp<DamageableComponent>(victim, out var damageable))
                continue;

            if (_prototype.TryIndex(gallows.DamageType, out var typeProto))
            {
                var dmg = new DamageSpecifier(typeProto, gallows.SuffocationDamagePerSecond * frameTime);
                _damageable.TryChangeDamage(victim, dmg, true, origin: uid, damageable: damageable);
            }

            if (now >= gallows.NextStruggleTime)
            {
                gallows.NextStruggleTime = now + gallows.StruggleCooldown;

                _audio.PlayPvs(gallows.IdleSound, uid);

                if (gallows.StruggleEmotes.Count > 0)
                {
                    var line = Loc.GetString(_random.Pick(gallows.StruggleEmotes));
                    _chat.TrySendInGameICMessage(victim, line, InGameICChatType.Emote, ChatTransmitRange.Normal,
                        hideLog: true, ignoreActionBlocker: true);
                }
            }

            if (now >= gallows.NextCoughBloodTime)
            {
                gallows.NextCoughBloodTime = now + gallows.CoughBloodCooldown;

                _chat.TrySendInGameICMessage(victim, Loc.GetString("gallows-struggle-blood"), InGameICChatType.Emote,
                    ChatTransmitRange.Normal, hideLog: true, ignoreActionBlocker: true);
                _bloodstream.TryModifyBloodLevel(victim, -gallows.CoughBloodAmount);
            }
        }
    }

    protected override void AttemptAttach(Entity<GallowsComponent> ent, EntityUid user, EntityUid victim)
    {
        if (ent.Comp.AttachDoAfter != null || !CanBeHanged(victim))
            return;

        var self = user == victim;

        _popup.PopupEntity(
            Loc.GetString(self ? "gallows-attach-initial-self" : "gallows-attach-initial-other", ("user", user), ("victim", victim)),
            ent,
            PopupType.MediumCaution);

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.AttachDuration, new GallowsAttachDoAfterEvent(), ent, target: victim, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        DoAfter.TryStartDoAfter(args, out ent.Comp.AttachDoAfter);
    }

    private void OnAttachDoAfter(Entity<GallowsComponent> ent, ref GallowsAttachDoAfterEvent args)
    {
        ent.Comp.AttachDoAfter = null;

        if (args.Cancelled || args.Handled || args.Args.Target is not { } victim || !CanBeHanged(victim))
            return;

        var user = args.Args.User;

        ent.Comp.ForceBuckle = true;
        var buckled = _buckle.TryBuckle(victim, user, ent, popup: false);
        ent.Comp.ForceBuckle = false;

        if (!buckled)
            return;

        args.Handled = true;

        var self = user == victim;
        _adminLogger.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(user):player} fitted {ToPrettyString(victim):target} into the noose on {ToPrettyString(ent):gallows}");

        _blindable.AdjustEyeDamage((victim, null), (int) ent.Comp.BlurMagnitude);
        _stuttering.DoStutter(victim, ent.Comp.StutterDuration, refresh: true);
        _chat.TryEmoteWithChat(victim, "Scream", ignoreActionBlocker: true, forceEmote: true);

        _popup.PopupEntity(
            Loc.GetString(self ? "gallows-attach-complete-self" : "gallows-attach-complete-other", ("user", user), ("victim", victim)),
            ent,
            PopupType.MediumCaution);
    }

    protected override void AttemptHang(Entity<GallowsComponent> ent, EntityUid user, EntityUid victim)
    {
        if (ent.Comp.HangDoAfter != null || !CanBeHanged(victim))
            return;

        var self = user == victim;

        _popup.PopupEntity(
            Loc.GetString(self ? "gallows-hang-initial-self" : "gallows-hang-initial-other", ("user", user), ("victim", victim)),
            ent,
            PopupType.LargeCaution);

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.HangDuration, new GallowsHangDoAfterEvent(), ent, target: victim, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        DoAfter.TryStartDoAfter(args, out ent.Comp.HangDoAfter);
    }

    private void OnHangDoAfter(Entity<GallowsComponent> ent, ref GallowsHangDoAfterEvent args)
    {
        ent.Comp.HangDoAfter = null;

        if (args.Cancelled || args.Handled || args.Args.Target is not { } victim || !CanBeHanged(victim))
            return;

        var user = args.Args.User;
        var self = user == victim;

        _adminLogger.Add(LogType.Action, LogImpact.Extreme, $"{ToPrettyString(user):player} hanged {ToPrettyString(victim):target} on {ToPrettyString(ent):gallows}");

        _audio.PlayPvs(ent.Comp.HangSound, ent);
        _popup.PopupEntity(
            Loc.GetString(self ? "gallows-hang-complete-self" : "gallows-hang-complete-other", ("user", user), ("victim", victim)),
            ent,
            PopupType.LargeCaution);

        var now = _timing.CurTime;
        ent.Comp.Hanging = true;
        ent.Comp.NextStruggleTime = now + ent.Comp.StruggleCooldown;
        ent.Comp.NextCoughBloodTime = now + ent.Comp.CoughBloodCooldown;

        args.Handled = true;
    }

    protected override void OnVictimRemoved(Entity<GallowsComponent> ent, EntityUid victim)
    {
        _blindable.AdjustEyeDamage((victim, null), -(int) ent.Comp.BlurMagnitude);
        _stuttering.DoRemoveStutter(victim, ent.Comp.StutterDuration.TotalSeconds);
    }
}
