// #Misfits Add - Shared campfire logic ported from RMC-14. Handles lighting,
// fuel consumption, extinguishing, and interaction routing for campfires/braziers.
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Temperature;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Campfire;

public abstract class SharedCampfireSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    // #Misfits Tweak - Only track lit campfires in Update so we iterate O(lit) not O(all).
    // Wendover may have hundreds of campfires; most are cold/unlit.
    private readonly HashSet<EntityUid> _litCampfires = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CampfireComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CampfireComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CampfireComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<CampfireComponent, CampfireExtinguishDoAfterEvent>(OnExtinguishDoAfter);
        // #Misfits Tweak - Remove from active set on component removal to prevent stale entries.
        SubscribeLocalEvent<CampfireComponent, ComponentShutdown>(OnCampfireShutdown);
    }

    private void OnCampfireShutdown(Entity<CampfireComponent> ent, ref ComponentShutdown args)
    {
        _litCampfires.Remove(ent.Owner);
    }

    private void OnStartup(Entity<CampfireComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.FuelRequired)
        {
            ent.Comp.Fuel = ent.Comp.MaxFuel;
            Dirty(ent);
        }

        // #Misfits Tweak - Populate active set if a campfire is YAML-spawned with Lit: true.
        if (ent.Comp.Lit)
            _litCampfires.Add(ent.Owner);

        UpdateAppearance(ent);
    }

    public override void Update(float frameTime)
    {
        // #Misfits Tweak - Fuel consumption is server-only simulation; skip on client entirely.
        if (_net.IsClient)
            return;

        base.Update(frameTime);

        // #Misfits Tweak - Iterate only lit campfires (active set) instead of all CampfireComponents.
        // Fuel timer uses wall-clock TimeSpan comparison so reduced iteration count is safe.
        var toExtinguish = new List<Entity<CampfireComponent>>();
        foreach (var uid in _litCampfires)
        {
            if (!TryComp<CampfireComponent>(uid, out var comp))
                continue;

            if (!comp.Lit || comp.LitAt == null || !comp.FuelRequired)
                continue;

            var elapsed = _timing.CurTime - comp.LitAt.Value;
            if (elapsed < comp.BurnDuration)
                continue;

            toExtinguish.Add((uid, comp));
        }

        foreach (var ent in toExtinguish)
        {
            // Consume one fuel unit.
            ent.Comp.Fuel--;
            Dirty(ent.Owner, ent.Comp);

            if (ent.Comp.Fuel > 0)
            {
                // Reset timer for next fuel unit.
                ent.Comp.LitAt = _timing.CurTime;
            }
            else
            {
                // Out of fuel — extinguish.
                SetLit((ent.Owner, ent.Comp), false);
                if (_net.IsServer)
                    _popup.PopupEntity("The fire goes out.", ent.Owner);
            }
        }
    }

    private void OnActivateInWorld(Entity<CampfireComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !ent.Comp.Lit)
            return;

        args.Handled = true;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.ExtinguishDelay,
            new CampfireExtinguishDoAfterEvent(), ent, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            if (_net.IsServer)
                _popup.PopupEntity("You start extinguishing the fire...", ent, args.User);
        }
    }

    private void OnExtinguishDoAfter(Entity<CampfireComponent> ent, ref CampfireExtinguishDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        SetLit(ent, false, args.User);

        if (_net.IsServer)
            _popup.PopupEntity("You extinguish the fire.", ent, args.User);
    }

    private void OnInteractUsing(Entity<CampfireComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // Refueling: allow adding CampfireFuel items even if fire is lit
        if (TryComp<CampfireFuelComponent>(args.Used, out var fuelComp))
        {
            if (!ent.Comp.FuelRequired)
                return;

            if (ent.Comp.Fuel >= ent.Comp.MaxFuel)
            {
                if (_net.IsServer)
                    _popup.PopupEntity("It looks fully fueled.", ent, args.User);
                return;
            }

            args.Handled = true;

            var fuelToAdd = Math.Min(fuelComp.FuelAmount, ent.Comp.MaxFuel - ent.Comp.Fuel);
            ent.Comp.Fuel += fuelToAdd;
            Dirty(ent);

            // Consume from stack if stackable, otherwise delete
            if (TryComp<StackComponent>(args.Used, out var stack))
                _stack.Use(args.Used, 1, stack);
            else if (_net.IsServer)
                QueueDel(args.Used);

            if (_net.IsServer)
                _popup.PopupEntity("You add fuel to the fire.", ent, args.User);

            return;
        }

        // Lighting: only if not already lit
        if (ent.Comp.Lit)
            return;

        // Check if the used item is a heat source (lighter, match, etc.)
        var isHotEvent = new IsHotEvent();
        RaiseLocalEvent(args.Used, isHotEvent);

        if (!isHotEvent.IsHot)
            return;

        if (ent.Comp.FuelRequired && ent.Comp.Fuel <= 0)
        {
            if (_net.IsServer)
                _popup.PopupEntity("The fire needs fuel. Add something to fuel it.", ent, args.User);
            return;
        }

        args.Handled = true;
        SetLit(ent, true, args.User);
    }

    public void SetLit(Entity<CampfireComponent> ent, bool lit, EntityUid? user = null)
    {
        if (ent.Comp.Lit == lit)
            return;

        ent.Comp.Lit = lit;

        if (lit)
        {
            ent.Comp.LitAt = _timing.CurTime;
            // #Misfits Tweak - Register in active set so Update only iterates lit campfires.
            _litCampfires.Add(ent.Owner);
        }
        else
        {
            ent.Comp.LitAt = null;
            // #Misfits Tweak - Remove from active set when extinguished.
            _litCampfires.Remove(ent.Owner);
        }

        Dirty(ent);

        if (_net.IsClient)
            return;

        if (lit)
        {
            if (ent.Comp.LitSound != null)
                _audio.PlayPvs(ent.Comp.LitSound, ent);

            if (user != null)
                _popup.PopupEntity("You light the fire.", ent, user.Value);
        }

        UpdateAppearance(ent);
    }

    protected virtual void UpdateAppearance(Entity<CampfireComponent> ent) { }
}
