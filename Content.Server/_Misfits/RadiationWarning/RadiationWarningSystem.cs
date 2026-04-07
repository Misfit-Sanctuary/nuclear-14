// #Misfits Change
using Content.Server._Misfits.RadiationWarning;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._Misfits.RadiationWarning;

/// <summary>
/// Sends private, nameless ambient-flavour messages (like /do) to humanoid
/// players as their accumulated Radiation damage increases. No name is attached
/// — the message appears in the local-chat box the same way a subtle admin
/// prayer does. Only the affected player sees their message.
///
/// Triggers on DamageChangedEvent so radiation from *any* source — proximity
/// to a rad field, ingested reagents, contaminated solutions — all register.
///
/// Tier thresholds (accumulated Radiation damage):
///   0 = mild       (≥10): faint unease
///   1 = moderate   (≥40): nausea / headache
///   2 = significant(≥80): hair loss / skin burns
///   3 = severe     (≥150): "this will kill you"
/// </summary>
public sealed class RadiationWarningSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ActorSystem _actor = default!;

    // Localisation keys per tier (chosen randomly each time)
    private static readonly string[][] TierMessages =
    {
        // Tier 0 — mild
        new[]
        {
            "rad-warning-tier0-a",
            "rad-warning-tier0-b",
            "rad-warning-tier0-c",
        },
        // Tier 1 — moderate
        new[]
        {
            "rad-warning-tier1-a",
            "rad-warning-tier1-b",
            "rad-warning-tier1-c",
        },
        // Tier 2 — significant
        new[]
        {
            "rad-warning-tier2-a",
            "rad-warning-tier2-b",
            "rad-warning-tier2-c",
        },
        // Tier 3 — severe
        new[]
        {
            "rad-warning-tier3-a",
            "rad-warning-tier3-b",
            "rad-warning-tier3-c",
        },
    };

    public override void Initialize()
    {
        base.Initialize();
        // Misfits Fix: split into two subscriptions to reduce hotpath cost.
        // — Fast path: entity already has RadiationWarningComponent (normal case after first exposure).
        //   Only fires for entities with both components, which is a much smaller set than "all humanoids".
        // — First-exposure path: fires for all humanoids but immediately returns if already has component,
        //   then on first radiation hit adds the component and delegates to ProcessRadiationDamage.
        SubscribeLocalEvent<RadiationWarningComponent, DamageChangedEvent>(OnRadiationWarningDamageChanged);
        SubscribeLocalEvent<HumanoidAppearanceComponent, DamageChangedEvent>(OnHumanoidFirstRadiation);
    }

    /// <summary>
    /// Fast path: entity already has RadiationWarningComponent — check and send tier messages.
    /// </summary>
    private void OnRadiationWarningDamageChanged(EntityUid uid, RadiationWarningComponent comp, DamageChangedEvent args)
    {
        ProcessRadiationDamage(uid, comp, args);
    }

    /// <summary>
    /// First-exposure path: fires for all humanoids. Skips immediately if already has component
    /// (handled above). On first radiation hit, adds the component and processes.
    /// </summary>
    private void OnHumanoidFirstRadiation(EntityUid uid, HumanoidAppearanceComponent _, DamageChangedEvent args)
    {
        // Misfits Fix: skip cheaply if already handled by the narrower RadiationWarningComponent subscription.
        if (HasComp<RadiationWarningComponent>(uid))
            return;

        // Only proceed if radiation damage actually increased (first-exposure fast-reject).
        if (args.DamageDelta == null ||
            !args.DamageDelta.DamageDict.TryGetValue("Radiation", out var delta) || delta <= 0)
            return;

        // First radiation hit — lazily add the component and process immediately.
        var comp = EnsureComp<RadiationWarningComponent>(uid);
        ProcessRadiationDamage(uid, comp, args);
    }

    // #Misfits Tweak - Gate cooldown ticks to 1 Hz; 1-second resolution is imperceptible for
    // visual/audio warning messages. Reduces per-tick component iteration by ~30×.
    private float _cooldownAccumulator;
    private const float CooldownTickInterval = 1.0f;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _cooldownAccumulator += frameTime;
        if (_cooldownAccumulator < CooldownTickInterval)
            return;
        _cooldownAccumulator -= CooldownTickInterval;

        // Decrement by the full 1-second interval so cooldowns drain at the same rate
        // regardless of tick rate.
        var query = EntityQueryEnumerator<RadiationWarningComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            for (var i = 0; i < comp.TierCooldowns.Length; i++)
            {
                if (comp.TierCooldowns[i] > 0f)
                    comp.TierCooldowns[i] = MathF.Max(0f, comp.TierCooldowns[i] - CooldownTickInterval);
            }
        }
    }

    private void ProcessRadiationDamage(EntityUid uid, RadiationWarningComponent comp, DamageChangedEvent args)
    {
        // Only react when radiation damage has actually increased.
        if (args.DamageDelta != null &&
            (!args.DamageDelta.DamageDict.TryGetValue("Radiation", out var delta) || delta <= 0))
            return;

        // Read current accumulated radiation damage from the damage component.
        if (!args.Damageable.Damage.DamageDict.TryGetValue("Radiation", out var currentRads))
            return;

        var currentFloat = (float) currentRads;
        if (currentFloat <= 0f)
            return;

        // Only send to living player-controlled humanoids.
        if (!_actor.TryGetSession(uid, out var session))
            return;

        // Determine highest eligible tier based on accumulated damage.
        var tier = -1;
        for (var i = comp.TierThresholds.Length - 1; i >= 0; i--)
        {
            if (currentFloat >= comp.TierThresholds[i])
            {
                tier = i;
                break;
            }
        }

        if (tier < 0)
            return;

        // Respect per-tier cooldown to avoid message spam.
        if (comp.TierCooldowns[tier] > 0f)
            return;

        comp.TierCooldowns[tier] = comp.TierCooldownTimes[tier];

        // Pick a random message from the tier list.
        var messages = TierMessages[tier];
        var key = messages[_random.Next(messages.Length)];
        var text = Loc.GetString(key);

        // Send as nameless local-chat message — identical channel to subtle prayer messages.
        // EntityUid.Invalid means no speech bubble and no entity name is prepended.
        _chatManager.ChatMessageToOne(
            ChatChannel.Local,
            text,
            text,
            EntityUid.Invalid,
            false,
            session!.Channel);
    }
}
