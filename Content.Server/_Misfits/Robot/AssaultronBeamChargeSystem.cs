// Server-side emote + power drain handler for Assaultron beam charge-up events.
// Shot blocking / state machine logic lives in SharedAssaultronBeamChargeSystem.
// This system only exists server-side because ChatSystem and BatterySystem are server-only.

using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Misfits.Robot;
using Content.Shared.Chat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Robot;

public sealed class AssaultronBeamChargeEmoteSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AssaultronBeamChargeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AssaultronBeamChargeComponent, AssaultronBeamPreFireCheckEvent>(OnPreFireCheck);
        SubscribeLocalEvent<AssaultronBeamChargeComponent, AssaultronChargeStartedEvent>(OnChargeStarted);
        SubscribeLocalEvent<AssaultronBeamChargeComponent, AssaultronBeamFiredEvent>(OnBeamFired);
    }

    private void OnMapInit(EntityUid uid, AssaultronBeamChargeComponent comp, ref MapInitEvent args)
    {
        comp.IsCharging = false;
        comp.ReadyToFire = false;
        comp.ForcedCombatMode = false;
        comp.ChargeEndTime = TimeSpan.Zero;
        comp.CooldownEndTime = TimeSpan.Zero;

        if ((!HasComp<HitscanBatteryAmmoProviderComponent>(uid) &&
             !HasComp<ProjectileBatteryAmmoProviderComponent>(uid)) ||
            !TryComp<BatteryComponent>(uid, out var battery))
        {
            return;
        }

        var ev = new ChargeChangedEvent(battery.CurrentCharge, battery.MaxCharge);
        RaiseLocalEvent(uid, ref ev);
    }

    /// <summary>
    /// Server-side battery gate that runs after charge-up completes.
    /// Shared code raises this check right before allowing the shot.
    /// </summary>
    private void OnPreFireCheck(EntityUid uid, AssaultronBeamChargeComponent comp, ref AssaultronBeamPreFireCheckEvent args)
    {
        if (comp.FireDrainCharge <= 0f)
            return;

        var cellEntity = _itemSlots.GetItemOrNull(uid, comp.CellSlotId);
        if (cellEntity == null || !TryComp<BatteryComponent>(cellEntity.Value, out var battery))
        {
            args.Cancelled = true;
            return;
        }

        // Require at least FireDrainCharge available before allowing the shot.
        if (battery.CurrentCharge < comp.FireDrainCharge)
            args.Cancelled = true;
    }

    private void OnChargeStarted(EntityUid uid, AssaultronBeamChargeComponent comp, ref AssaultronChargeStartedEvent args)
    {
        var now = _timing.CurTime;
        if (now < comp.NextChargeEmoteTime)
            return;

        comp.NextChargeEmoteTime = now + TimeSpan.FromSeconds(comp.EmoteCooldown);

        _chat.TrySendInGameICMessage(
            args.User,
            Loc.GetString(args.EmoteLocale),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);
    }

    private void OnBeamFired(EntityUid uid, AssaultronBeamChargeComponent comp, ref AssaultronBeamFiredEvent args)
    {
        // Drain the robot's chassis battery on each shot.
        if (comp.FireDrainCharge > 0f)
            DrainCellSlot(uid, comp);

        var now = _timing.CurTime;
        if (now < comp.NextFireEmoteTime)
            return;

        comp.NextFireEmoteTime = now + TimeSpan.FromSeconds(comp.EmoteCooldown);

        _chat.TrySendInGameICMessage(
            args.User,
            Loc.GetString(args.EmoteLocale),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);
    }

    /// <summary>
    /// Drains charge from the battery entity stored in the robot's cell_slot.
    /// This makes the beam attack consume the robot's own power supply.
    /// </summary>
    private void DrainCellSlot(EntityUid uid, AssaultronBeamChargeComponent comp)
    {
        var cellEntity = _itemSlots.GetItemOrNull(uid, comp.CellSlotId);
        if (cellEntity == null)
            return;

        _battery.UseCharge(cellEntity.Value, comp.FireDrainCharge);
    }
}
