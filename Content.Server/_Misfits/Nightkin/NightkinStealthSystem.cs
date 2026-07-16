// #Misfits Add - Server registration for Nightkin passive Stealth Boy implant behavior.
using Content.Shared._Misfits.Nightkin;
using Content.Shared._Misfits.StealthBoy;
using Content.Server._Misfits.StealthBoy;

namespace Content.Server._Misfits.Nightkin;

public sealed class NightkinStealthSystem : SharedNightkinStealthSystem
{
    [Dependency] private readonly StealthBoySystem _stealthBoy = default!;

    private static readonly TimeSpan PassiveDuration = TimeSpan.FromDays(3650);

    protected override void ActivateNightkinStealth(EntityUid uid, NightkinPassiveStealthComponent component)
    {
        _stealthBoy.ActivateStealth(
            uid,
            PassiveDuration,
            component.Visibility,
            component.FadeInTime,
            component.FadeOutTime,
            component.ActivateMessage,
            component.DeactivateMessage,
            component.StillVisibility);
    }

    protected override void DeactivateNightkinStealth(
        EntityUid uid,
        NightkinPassiveStealthComponent component,
        StealthBoyActiveComponent active)
    {
        active.ReappearMessage = component.DeactivateMessage;
        Dirty(uid, active);
        _stealthBoy.TryBeginFadeOut(uid, active);
    }
}
