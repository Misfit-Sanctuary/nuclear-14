using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics.Components;

// #Misfits Add - Remove physics from spent casings on landing to avoid accumulating 1000+ dynamic physics bodies during war

namespace Content.Server._Misfits.Weapons.Guns;

/// <summary>
/// Server-side optimisation system that drops spent bullet casings out of the
/// physics simulation the moment they land.
///
/// <para>
/// During large war scenarios casings accumulate rapidly (easily 1000+ entities).
/// Each casing carries a <see cref="PhysicsComponent"/> with a dynamic body that
/// remains "awake" in the physics engine until it fully comes to rest.  With many
/// casings this creates expensive constraint islands and broadphase queries every
/// tick.  Removing <see cref="PhysicsComponent"/> via
/// <c>RemCompDeferred</c> takes the entity out of the simulation entirely;
/// RobustToolbox's <c>SharedPhysicsSystem.OnPhysicsRemoved</c> cascades the
/// fixture / broadphase cleanup automatically so we do not need to touch
/// <c>FixturesComponent</c> directly.
/// </para>
///
/// <para>
/// <b>Known limitation:</b> casings ejected without a throw angle (e.g. manual
/// chamber cycling, revolver ejection — the <c>angle == null</c> branch in
/// <c>SharedGunSystem.EjectCartridge</c>) never receive a
/// <c>ThrownItemComponent</c>, so <c>LandEvent</c> is never raised for them.
/// Those casings retain their physics body for the full
/// <see cref="TimedDespawnComponent"/> lifetime (90 s after the Misfits tweak).
/// This edge case affects a small minority of weapons and is considered
/// acceptable.
/// </para>
/// </summary>
public sealed class CasingPhysicsOptSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // Filter to entities that carry CartridgeAmmoComponent so the handler
        // is never invoked for unrelated thrown items (tools, grenades, etc.).
        SubscribeLocalEvent<CartridgeAmmoComponent, LandEvent>(OnCasingLand);
    }

    /// <summary>
    /// Raised by <c>ThrownItemSystem</c> when a thrown entity comes to rest.
    /// If the cartridge is already spent we remove its physics body so the
    /// entity becomes a pure visual/timer entity and exits the physics
    /// simulation immediately.
    /// </summary>
    private void OnCasingLand(EntityUid uid, CartridgeAmmoComponent cartridge, ref LandEvent args)
    {
        // Only optimise spent casings. Live cartridges that are thrown
        // (e.g. thrown by hand) must keep physics so they can still be
        // picked up and used.
        if (!cartridge.Spent)
            return;

        // Deferred removal keeps us safely outside the physics engine's own
        // event stack.  RobustToolbox's SharedPhysicsSystem.OnPhysicsRemoved
        // handler fires when the component is actually removed and takes care
        // of unregistering all fixtures from the broadphase automatically —
        // we must NOT call RemCompDeferred<FixturesComponent> separately as
        // that would cause a double-teardown when PhysicsComponent removal
        // then tries to access already-gone fixtures.
        RemCompDeferred<PhysicsComponent>(uid);
    }
}
