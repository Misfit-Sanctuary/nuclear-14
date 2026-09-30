// #Misfits Add - Locking, unlocking, prying and auto-locking of surface bunker hatches.
using Content.Server.Popups;
using Content.Shared._Misfits.Warps;
using Content.Shared.Access.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Misfits.Warps;

/// <summary>
/// Raised (broadcast) whenever a hatch locks or unlocks, so open tunnel door menus can refresh.
/// </summary>
public readonly record struct BunkerHatchLockChangedEvent(EntityUid Hatch, bool Locked);

/// <summary>
/// Handles the lock on a surface bunker hatch.
/// <list type="bullet">
/// <item>Anyone with the hatch's access (Enclave NCO and up) can lock or unlock it with a verb, or
/// from the Enclave tac map.</item>
/// <item>Locking by hand has a cooldown per hatch.</item>
/// <item>Anyone can pry a locked hatch open with a crowbar, which takes a long do-after.</item>
/// <item>An open hatch locks itself again after <see cref="BunkerHatchLockComponent.AutoLockDelay"/>.</item>
/// </list>
/// </summary>
public sealed class BunkerHatchLockSystem : EntitySystem
{
    private const string PryQuality = "Prying";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BunkerHatchLockComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BunkerHatchLockComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BunkerHatchLockComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<BunkerHatchLockComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BunkerHatchLockComponent, BunkerHatchPryDoAfterEvent>(OnPryDoAfter);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BunkerHatchLockComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Locked || comp.AutoLockAt is not { } lockAt || now < lockAt)
                continue;

            SetLocked((uid, comp), true);
            _popup.PopupEntity(Loc.GetString("bunker-hatch-auto-locked"), uid, PopupType.Medium);
        }
    }

    private void OnMapInit(Entity<BunkerHatchLockComponent> ent, ref MapInitEvent args)
    {
        // Put the sprite and timer in line with whatever the prototype or map says.
        SetLocked(ent.Owner, ent.Comp.Locked);
    }

    private void OnExamined(Entity<BunkerHatchLockComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Locked ? "bunker-hatch-examine-locked" : "bunker-hatch-examine-open"));
    }

    private void OnGetVerbs(Entity<BunkerHatchLockComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !CanOperate(args.User, ent))
            return;

        var user = args.User;
        var locked = ent.Comp.Locked;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(locked ? "bunker-hatch-verb-unlock" : "bunker-hatch-verb-lock"),
            Icon = new SpriteSpecifier.Texture(new ResPath(locked
                ? "/Textures/Interface/VerbIcons/unlock.svg.192dpi.png"
                : "/Textures/Interface/VerbIcons/lock.svg.192dpi.png")),
            Act = () =>
            {
                if (locked)
                    TryUnlock(ent.Owner, user);
                else
                    TryLock(ent.Owner, user);
            },
        });
    }

    private void OnInteractUsing(Entity<BunkerHatchLockComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Locked || !_tool.HasQuality(args.Used, PryQuality))
            return;

        if (_tool.UseTool(args.Used, args.User, ent, (float) ent.Comp.PryTime.TotalSeconds, PryQuality,
                new BunkerHatchPryDoAfterEvent()))
        {
            _popup.PopupEntity(Loc.GetString("bunker-hatch-pry-start"), ent, args.User);
        }

        args.Handled = true;
    }

    private void OnPryDoAfter(Entity<BunkerHatchLockComponent> ent, ref BunkerHatchPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Locked)
            return;

        args.Handled = true;
        SetLocked(ent.Owner, false);
        _popup.PopupEntity(Loc.GetString("bunker-hatch-pry-done"), ent, PopupType.Medium);
    }

    public bool IsLocked(EntityUid hatch)
    {
        return TryComp<BunkerHatchLockComponent>(hatch, out var comp) && comp.Locked;
    }

    /// <summary>
    /// True if this user has the hatch's access (Enclave NCO and up) and may lock or unlock it.
    /// </summary>
    public bool CanOperate(EntityUid user, EntityUid hatch)
    {
        return _access.IsAllowed(user, hatch);
    }

    /// <summary>
    /// Lock the hatch by hand. Checks access and the lock cooldown, and tells the user why not.
    /// </summary>
    public bool TryLock(Entity<BunkerHatchLockComponent?> ent, EntityUid user)
    {
        if (!Resolve(ent, ref ent.Comp, false) || ent.Comp.Locked)
            return false;

        if (!CanOperate(user, ent))
        {
            _popup.PopupEntity(Loc.GetString("bunker-hatch-no-access"), user, user);
            return false;
        }

        var now = _timing.CurTime;
        if (now < ent.Comp.NextLockAllowed)
        {
            var seconds = (int) Math.Ceiling((ent.Comp.NextLockAllowed - now).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("bunker-hatch-lock-cooldown", ("seconds", seconds)), user, user);
            return false;
        }

        ent.Comp.NextLockAllowed = now + ent.Comp.LockCooldown;
        SetLocked((ent, ent.Comp), true);
        _popup.PopupEntity(Loc.GetString("bunker-hatch-locked"), user, user);
        return true;
    }

    /// <summary>
    /// Unlock the hatch by hand. Checks access.
    /// </summary>
    public bool TryUnlock(Entity<BunkerHatchLockComponent?> ent, EntityUid user)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Locked)
            return false;

        if (!CanOperate(user, ent))
        {
            _popup.PopupEntity(Loc.GetString("bunker-hatch-no-access"), user, user);
            return false;
        }

        SetLocked((ent, ent.Comp), false);
        _popup.PopupEntity(Loc.GetString("bunker-hatch-unlocked"), user, user);
        return true;
    }

    /// <summary>
    /// Sets the lock with no checks: used by the timer, by Enclave members passing through, and
    /// after a pry. Opening starts the auto-lock timer.
    /// </summary>
    public void SetLocked(Entity<BunkerHatchLockComponent?> ent, bool locked)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        var changed = ent.Comp.Locked != locked;
        ent.Comp.Locked = locked;
        ent.Comp.AutoLockAt = locked ? null : _timing.CurTime + ent.Comp.AutoLockDelay;
        _appearance.SetData(ent, BunkerHatchVisuals.Locked, locked);

        if (changed)
            RaiseLocalEvent(new BunkerHatchLockChangedEvent(ent, locked));
    }
}
