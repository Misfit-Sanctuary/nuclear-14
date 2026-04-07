// Handles the overheat cooldown mechanic for player-controlled Sentry Bot chassis.
// When the built-in weapon's ammo runs dry the system locks out firing, announces
// "PURGING FUSION CORE OVERHEAT" to nearby players, and after a configurable
// cooldown refills the magazine and announces "SYSTEMS NOMINAL".

using Content.Shared._Misfits.Robot;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.Robot;

public sealed class SentryBotOverheatSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Detect when the sentry bot fires its gun and check ammo status.
        SubscribeLocalEvent<SentryBotChassisComponent, GunShotEvent>(OnGunShot);
    }

    private void OnGunShot(EntityUid uid, SentryBotChassisComponent comp, ref GunShotEvent args)
    {
        if (comp.Overheating)
            return;

        // Check if the built-in ammo provider is now empty.
        if (!TryComp<BasicEntityAmmoProviderComponent>(uid, out var ammo))
            return;

        if (ammo.Count > 0)
            return;

        // Ammo depleted — enter overheat state.
        comp.Overheating = true;
        comp.OverheatEndTime = _timing.CurTime + TimeSpan.FromSeconds(comp.OverheatDuration);

        _popup.PopupEntity(
            Loc.GetString("sentrybot-overheat-warning"),
            uid,
            PopupType.LargeCaution);
    }

    // #Misfits Tweak - Gate overheat polling to 0.5 Hz; overheat timers are seconds-scale
    // and use IGameTiming.CurTime so no drift occurs from reduced update frequency.
    private float _overheatAccumulator;
    private const float OverheatUpdateInterval = 0.5f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _overheatAccumulator += frameTime;
        if (_overheatAccumulator < OverheatUpdateInterval)
            return;
        _overheatAccumulator -= OverheatUpdateInterval;

        var query = EntityQueryEnumerator<SentryBotChassisComponent, BasicEntityAmmoProviderComponent>();
        while (query.MoveNext(out var uid, out var comp, out var ammo))
        {
            if (!comp.Overheating)
                continue;

            if (_timing.CurTime < comp.OverheatEndTime)
                continue;

            // Overheat period complete — refill ammo and announce.
            comp.Overheating = false;
            ammo.Count = ammo.Capacity;
            Dirty(uid, ammo);

            _popup.PopupEntity(
                Loc.GetString("sentrybot-systems-nominal"),
                uid,
                PopupType.Medium);
        }
    }
}
