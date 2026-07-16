// #Misfits Add - admin "mysterious stranger" system: spawn an admin-controlled figure only one player can see.
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Eye;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.Administration.MysteriousStranger;

/// <summary>
/// Lets admins spawn in as a "mysterious stranger" visible and audible only to one chosen player
/// (plus admin observers). The admin leaves by running aghost, which deletes the stranger and
/// restores the target's vision.
/// </summary>
public sealed class MysteriousStrangerSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedGodmodeSystem _godmode = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly VisibilitySystem _visibility = default!;

    [ValidatePrototypeId<EntityPrototype>]
    public const string StrangerPrototype = "N14MobMysteriousStranger";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<MysteriousStrangerComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MysteriousStrangerComponent, ComponentShutdown>(OnStrangerShutdown);
        SubscribeLocalEvent<MysteriousStrangerVisionComponent, ComponentShutdown>(OnVisionShutdown);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var player = actor.PlayerSession;
        if (!_adminManager.HasAdminFlag(player, AdminFlags.Admin))
            return;

        // Vanish verb on the stranger the admin is currently playing.
        if (args.User == args.Target && TryComp<MysteriousStrangerComponent>(args.Target, out var stranger))
        {
            var strangerUid = args.Target;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("mysterious-stranger-vanish-verb-text"),
                Message = Loc.GetString("mysterious-stranger-vanish-verb-message"),
                Category = VerbCategory.Admin,
                Act = () => Vanish(player, strangerUid, stranger),
                Impact = LogImpact.Medium,
            });
            return;
        }

        // Only offer it on other players' living entities.
        if (args.User == args.Target
            || !HasComp<ActorComponent>(args.Target)
            || HasComp<GhostComponent>(args.Target)
            || HasComp<MysteriousStrangerComponent>(args.Target))
            return;

        var target = args.Target;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("mysterious-stranger-verb-text"),
            Message = Loc.GetString("mysterious-stranger-verb-message"),
            Category = VerbCategory.Admin,
            Act = () => SpawnStranger(player, target),
            Impact = LogImpact.High,
        });
    }

    /// <summary>
    /// Spawns a mysterious stranger next to <paramref name="target"/>, makes it perceivable only by the
    /// target (and admin observers), and transfers <paramref name="admin"/>'s mind into it.
    /// </summary>
    public bool SpawnStranger(ICommonSession admin, EntityUid target)
    {
        if (Deleted(target) || !HasComp<ActorComponent>(target))
            return false;

        if (!_mind.TryGetMind(admin, out var mindId, out var mind))
            return false;

        var stranger = Spawn(StrangerPrototype, Transform(target).Coordinates);

        // Remember a non-ghost body to come back to; ghosts are deleted the moment the session leaves them.
        EntityUid? returnEntity = null;
        if (admin.AttachedEntity is { } adminEntity && !HasComp<GhostComponent>(adminEntity))
            returnEntity = adminEntity;

        // The stranger's only visibility layer is its own flag, so PVS never sends it to sessions
        // without that flag in their eye mask (see PvsSystem: viewer mask must contain all layer bits).
        var visibility = EnsureComp<VisibilityComponent>(stranger);
        _visibility.AddLayer((stranger, visibility), (int) VisibilityFlags.MysteriousStranger, false);
        _visibility.RemoveLayer((stranger, visibility), (int) VisibilityFlags.Normal, false);
        _visibility.RefreshVisibility(stranger, visibility);

        // Intangible flavor entity: shouldn't starve, suffocate or die while the admin is puppeting it.
        _godmode.EnableGodmode(stranger);

        var strangerComp = EnsureComp<MysteriousStrangerComponent>(stranger);
        strangerComp.Target = target;
        strangerComp.ReturnEntity = returnEntity;

        var vision = EnsureComp<MysteriousStrangerVisionComponent>(target);
        vision.Strangers.Add(stranger);
        if (TryComp<EyeComponent>(target, out var eye))
            _eye.SetVisibilityMask(target, eye.VisibilityMask | (int) VisibilityFlags.MysteriousStranger, eye);

        _mind.TransferTo(mindId, stranger, mind: mind);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{admin.Name} spawned in as a mysterious stranger {ToPrettyString(stranger):stranger} targeting {ToPrettyString(target):target}");
        return true;
    }

    /// <summary>
    /// Returns the admin to the body they used the verb from (or a fresh admin observer if it's gone).
    /// The session detaching from the stranger triggers <see cref="OnPlayerDetached"/>, which deletes it.
    /// </summary>
    private void Vanish(ICommonSession admin, EntityUid stranger, MysteriousStrangerComponent component)
    {
        if (!_mind.TryGetMind(admin, out var mindId, out var mind))
            return;

        EntityUid destination;
        if (component.ReturnEntity is { } returnEntity && !Deleted(returnEntity) && !Terminating(returnEntity))
        {
            destination = returnEntity;
        }
        else
        {
            destination = Spawn(GameTicker.AdminObserverPrototypeName, Transform(stranger).Coordinates);
            _metaData.SetEntityName(destination, admin.Name);
        }

        _mind.TransferTo(mindId, destination, mind: mind);
    }

    private void OnPlayerDetached(EntityUid uid, MysteriousStrangerComponent component, PlayerDetachedEvent args)
    {
        // The admin aghosted away (or disconnected): the stranger vanishes, like a ghost would.
        QueueDel(uid);
    }

    private void OnStrangerShutdown(EntityUid uid, MysteriousStrangerComponent component, ComponentShutdown args)
    {
        if (!TryComp<MysteriousStrangerVisionComponent>(component.Target, out var vision))
            return;

        vision.Strangers.Remove(uid);
        if (vision.Strangers.Count == 0)
            RemCompDeferred<MysteriousStrangerVisionComponent>(component.Target);
    }

    private void OnVisionShutdown(EntityUid uid, MysteriousStrangerVisionComponent component, ComponentShutdown args)
    {
        if (TryComp<EyeComponent>(uid, out var eye))
            _eye.SetVisibilityMask(uid, eye.VisibilityMask & ~(int) VisibilityFlags.MysteriousStranger, eye);
    }
}
