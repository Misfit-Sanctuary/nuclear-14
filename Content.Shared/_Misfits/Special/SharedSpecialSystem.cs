using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.Special.Prototypes;
using Content.Shared.Chemistry;
using Content.Shared.Ghost;
using Content.Shared.Movement.Systems;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Special;

public sealed class SharedSpecialSystem : EntitySystem
{
    private const string TuningPrototypeId = "MisfitsSpecialTuning";
    private const int IntelligenceSolutionScanThreshold = 8;

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    // Temporary modifiers are tracked outside the component so expiration remains a
    // server-authoritative timed effect while the summed modifier values still network.
    private readonly List<TemporaryModifierEntry> _temporaryModifiers = new();

    public override void Initialize()
    {
        base.Initialize();

        UpdatesOutsidePrediction = true;

        SubscribeLocalEvent<SpecialComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SpecialComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SpecialComponent, SolutionScanEvent>(OnSolutionScan);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        // Iterate backwards because expired entries remove themselves in-place.
        for (var i = _temporaryModifiers.Count - 1; i >= 0; i--)
        {
            var entry = _temporaryModifiers[i];
            if (!entry.Expires || entry.ExpiresAt > now)
                continue;

            _temporaryModifiers.RemoveAt(i);
            if (TryComp<SpecialComponent>(entry.Entity, out var comp))
                ApplyTemporary(entry.Entity, comp, entry.Stat, -entry.Value);
        }
    }

    private void OnStartup(Entity<SpecialComponent> ent, ref ComponentStartup args)
    {
        Normalize(ent.Owner, ent.Comp);
    }

    private void OnShutdown(Entity<SpecialComponent> ent, ref ComponentShutdown args)
    {
        _temporaryModifiers.RemoveAll(entry => entry.Entity == ent.Owner);

        // Consumers that cache applied SPECIAL side effects can clean up here.
        var ev = new SpecialShutdownEvent(ent.Owner);
        RaiseLocalEvent(ent.Owner, ref ev, true);
    }

    private void OnSolutionScan(Entity<SpecialComponent> ent, ref SolutionScanEvent args)
    {
        // Intelligence grants access to detailed chemistry scans without making
        // the chemistry system depend on the SPECIAL implementation.
        if (GetEffective(ent.Owner, SpecialStat.Intelligence, ent.Comp) >= IntelligenceSolutionScanThreshold)
            args.CanScan = true;
    }

    /// <summary>
    /// Returns server tuning values, falling back to code defaults if the prototype is missing.
    /// </summary>
    public SpecialTuningPrototype GetTuning()
    {
        return _prototype.TryIndex<SpecialTuningPrototype>(TuningPrototypeId, out var tuning)
            ? tuning
            : SpecialTuningPrototype.Fallback;
    }

    /// <summary>
    /// Returns whether this entity should participate in SPECIAL gameplay effects.
    /// </summary>
    public bool UsesSpecialStats(EntityUid uid)
    {
        // Observer ghosts may be player-controlled, but they should not inherit
        // character SPECIAL gates, bonuses, or penalties.
        return !HasComp<GhostComponent>(uid);
    }

    /// <summary>
    /// Gets the character's saved stat value before temporary modifiers.
    /// </summary>
    public int GetBase(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat) || !UsesSpecialStats(uid))
            return SpecialProfile.DefaultValue;

        if (!Resolve(uid, ref component, false))
            return SpecialProfile.DefaultValue;

        return GetBase(component, stat);
    }

    /// <summary>
    /// Gets the sum of active temporary modifiers for a stat.
    /// </summary>
    public int GetModifier(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat) || !UsesSpecialStats(uid))
            return 0;

        if (!Resolve(uid, ref component, false))
            return 0;

        return GetModifier(component, stat);
    }

    /// <summary>
    /// Gets base plus modifiers before clamping to the valid SPECIAL range.
    /// </summary>
    public int GetUnclampedEffective(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        if (!SpecialStats.IsEnabled(stat) || !UsesSpecialStats(uid))
            return SpecialProfile.DefaultValue;

        if (!Resolve(uid, ref component, false))
            return SpecialProfile.DefaultValue;

        return GetBase(component, stat) + GetModifier(component, stat);
    }

    /// <summary>
    /// Gets the gameplay-facing stat value after clamping to 1-10.
    /// </summary>
    public int GetEffective(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return Math.Clamp(GetUnclampedEffective(uid, stat, component), SpecialProfile.Minimum, SpecialProfile.Maximum);
    }

    /// <summary>
    /// Returns the linear distance from the default value of 5.
    /// </summary>
    public int GetEffectDelta(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return GetEffective(uid, stat, component) - SpecialProfile.DefaultValue;
    }

    /// <summary>
    /// Returns the non-linear distance from 5 used by most gameplay effects.
    /// </summary>
    public float GetCurvedEffectDelta(EntityUid uid, SpecialStat stat, SpecialComponent? component = null)
    {
        return GetCurvedEffectDelta(GetEffective(uid, stat, component));
    }

    public static float GetCurvedEffectDelta(int effective)
    {
        // Low and high stats should feel more distinct than a flat +/-1 scale.
        // The endpoints are intentionally stronger while 5 remains neutral.
        return Math.Clamp(effective, SpecialProfile.Minimum, SpecialProfile.Maximum) switch
        {
            1 => -5f,
            2 => -3.5f,
            3 => -2.25f,
            4 => -1f,
            5 => 0f,
            6 => 1f,
            7 => 2.25f,
            8 => 3.75f,
            9 => 5.5f,
            _ => 7.5f,
        };
    }

    public float GetCurvedEffectModifier(
        EntityUid uid,
        SpecialStat stat,
        float multiplierPerPoint,
        SpecialComponent? component = null)
    {
        return GetCurvedEffectModifier(GetCurvedEffectDelta(uid, stat, component), multiplierPerPoint);
    }

    public static float GetCurvedEffectModifier(float curvedDelta, float multiplierPerPoint)
    {
        return curvedDelta * multiplierPerPoint;
    }

    public static int GetCharismaLoadoutPointModifier(int charisma)
    {
        return (int) Math.Round(GetCurvedEffectDelta(charisma) * 2f, MidpointRounding.AwayFromZero);
    }

    public float GetStrengthThrowSpeedMultiplier(EntityUid uid, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return 1f;

        var tuning = GetTuning();
        var modifier = GetCurvedEffectModifier(uid, SpecialStat.Strength, tuning.StrengthThrowSpeedMultiplierPerPoint, component);
        return MathF.Max(0.1f, 1f + modifier);
    }

    /// <summary>
    /// Returns the Intelligence speed multiplier used by medical actions with do-after timers.
    /// </summary>
    public float GetIntelligenceMedicalActionSpeed(EntityUid uid, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return 1f;

        return GetIntelligenceMedicalActionSpeed(GetEffective(uid, SpecialStat.Intelligence, component));
    }

    public static float GetIntelligenceMedicalActionSpeed(int intelligence)
    {
        return MathF.Max(0.1f, 1f + (intelligence - SpecialProfile.DefaultValue) * 0.1f);
    }

    /// <summary>
    /// Applies the Intelligence medical speed multiplier to a timed medical action.
    /// </summary>
    public TimeSpan GetIntelligenceMedicalActionDelay(EntityUid uid, TimeSpan baseDelay, SpecialComponent? component = null)
    {
        return baseDelay / GetIntelligenceMedicalActionSpeed(uid, component);
    }

    public float GetIntelligenceTopicalHealingMultiplier(EntityUid uid, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return 1f;

        return GetIntelligenceTopicalHealingMultiplier(GetEffective(uid, SpecialStat.Intelligence, component));
    }

    public static float GetIntelligenceTopicalHealingMultiplier(int intelligence)
    {
        return Math.Clamp(1f + (intelligence - SpecialProfile.DefaultValue) * 0.1f, 1f, 1.3f);
    }

    /// <summary>
    /// Applies the Intelligence lathe material discount to an existing material-use multiplier.
    /// The discount only rewards above-average Intelligence; low Intelligence is already gated elsewhere.
    /// </summary>
    public float GetIntelligenceLatheMaterialUseMultiplier(EntityUid uid, float baseMultiplier, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return baseMultiplier;

        var tuning = GetTuning();
        return GetIntelligenceLatheMaterialUseMultiplier(
            GetEffective(uid, SpecialStat.Intelligence, component),
            baseMultiplier,
            tuning.IntelligenceLatheMaterialUseMultiplierPerPoint);
    }

    public static float GetIntelligenceLatheMaterialUseMultiplier(int intelligence, float baseMultiplier, float multiplierPerPoint)
    {
        var delta = MathF.Max(0f, GetCurvedEffectDelta(intelligence));
        var discount = Math.Clamp(delta * multiplierPerPoint, 0f, 0.5f);

        return MathF.Max(0.1f, baseMultiplier * (1f - discount));
    }

    public int GetCharismaChatFontSize(EntityUid uid, int baseFontSize, SpecialComponent? component = null)
    {
        var charisma = GetEffective(uid, SpecialStat.Charisma, component);

        if (charisma >= 7)
            return baseFontSize + 2;

        if (charisma <= 2)
            return Math.Max(8, baseFontSize - 1);

        return baseFontSize;
    }

    public float GetCharismaWarcryRange(EntityUid uid, float baseRange, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return baseRange;

        var tuning = GetTuning();
        var modifier = GetCurvedEffectModifier(
            uid,
            SpecialStat.Charisma,
            tuning.CharismaWarcryRangeMultiplierPerPoint,
            component);

        return MathF.Max(0.5f, baseRange * (1f + modifier));
    }

    public TimeSpan GetCharismaWarcryDuration(EntityUid uid, TimeSpan baseDuration, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return baseDuration;

        var tuning = GetTuning();
        var modifier = GetCurvedEffectModifier(
            uid,
            SpecialStat.Charisma,
            tuning.CharismaWarcryDurationMultiplierPerPoint,
            component);
        var scaledTicks = Math.Max(TimeSpan.TicksPerSecond, (long) Math.Round(baseDuration.Ticks * (1f + modifier)));

        return TimeSpan.FromTicks(scaledTicks);
    }

    public float GetCharismaWarcrySpeedBonus(EntityUid uid, float baseSpeedBonus, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return baseSpeedBonus;

        var tuning = GetTuning();
        var modifier = GetCurvedEffectModifier(
            uid,
            SpecialStat.Charisma,
            tuning.CharismaWarcrySpeedMultiplierPerPoint,
            component);

        return MathF.Max(0f, baseSpeedBonus * (1f + modifier));
    }

    public bool HasRequirement(EntityUid uid, SpecialStat stat, int minimum, SpecialComponent? component = null)
    {
        return UsesSpecialStats(uid) && GetEffective(uid, stat, component) >= minimum;
    }

    public bool TrySetBase(EntityUid uid, SpecialStat stat, int value, SpecialComponent? component = null)
    {
        if (!UsesSpecialStats(uid) ||
            !SpecialStats.IsEnabled(stat) ||
            !SpecialProfile.IsWithinBounds(value) ||
            !Resolve(uid, ref component, false))
            return false;

        SetBase(component, stat, value);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
        return true;
    }

    public bool TrySetBaseValues(EntityUid uid, SpecialProfile profile, SpecialComponent? component = null)
    {
        // Character profiles are player-controlled input, so sanitize the full
        // profile before copying it into the runtime component.
        profile = SpecialProfile.EnsureValid(profile);

        if (!UsesSpecialStats(uid) || !Resolve(uid, ref component, false))
            return false;

        component.BaseStrength = profile.Strength;
        component.BasePerception = profile.Perception;
        component.BaseEndurance = profile.Endurance;
        component.BaseCharisma = profile.Charisma;
        component.BaseIntelligence = profile.Intelligence;
        component.BaseAgility = profile.Agility;
        component.BaseLuck = profile.Luck;

        Normalize(uid, component);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
        return true;
    }

    /// <summary>
    /// Ensures SPECIAL runtime state exists and copies authoritative base values from a character profile.
    /// </summary>
    public bool TryApplyProfileBaseValues(EntityUid uid, HumanoidCharacterProfile profile, SpecialComponent? component = null)
    {
        if (!UsesSpecialStats(uid))
            return false;

        component ??= EnsureComp<SpecialComponent>(uid);
        return TrySetBaseValues(uid, profile.Special, component);
    }

    public bool TryModifyTemporary(
        EntityUid uid,
        SpecialStat stat,
        int modifier,
        TimeSpan? duration = null,
        string source = "",
        SpecialComponent? component = null)
    {
        if (modifier == 0 ||
            !UsesSpecialStats(uid) ||
            !SpecialStats.IsEnabled(stat) ||
            !Resolve(uid, ref component, false))
            return false;

        // The component stores the aggregate modifier per stat; this list stores
        // the individual entries so a single source/duration can be removed later.
        ApplyTemporary(uid, component, stat, modifier);

        _temporaryModifiers.Add(new TemporaryModifierEntry(
            uid,
            stat,
            modifier,
            source,
            duration.HasValue,
            duration.HasValue ? _timing.CurTime + duration.Value : TimeSpan.Zero));

        return true;
    }

    public void ClearTemporaryModifiers(EntityUid uid, string? source = null, SpecialComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        for (var i = _temporaryModifiers.Count - 1; i >= 0; i--)
        {
            var entry = _temporaryModifiers[i];
            if (entry.Entity != uid || source != null && entry.Source != source)
                continue;

            _temporaryModifiers.RemoveAt(i);
            ApplyTemporary(uid, component, entry.Stat, -entry.Value);
        }
    }

    public float GetLuckRollChance(EntityUid uid, float baseChance, float chancePerPoint, SpecialComponent? component = null)
    {
        // Luck rolls share the same curved scale as other SPECIAL effects and
        // clamp at probability bounds to keep callers simple.
        var delta = GetCurvedEffectDelta(uid, SpecialStat.Luck, component);
        return Math.Clamp(baseChance + delta * chancePerPoint, 0f, 1f);
    }

    public SpecialProfile ToProfile(EntityUid uid, SpecialComponent? component = null)
    {
        if (!UsesSpecialStats(uid) || !Resolve(uid, ref component, false))
            return SpecialProfile.Default();

        return new SpecialProfile
        {
            Strength = GetBase(component, SpecialStat.Strength),
            Perception = GetBase(component, SpecialStat.Perception),
            Endurance = GetBase(component, SpecialStat.Endurance),
            Charisma = GetBase(component, SpecialStat.Charisma),
            Intelligence = GetBase(component, SpecialStat.Intelligence),
            Agility = GetBase(component, SpecialStat.Agility),
            Luck = GetBase(component, SpecialStat.Luck),
        };
    }

    private void Normalize(EntityUid uid, SpecialComponent component)
    {
        // DataFields may come from YAML, saves, or older data. Clamp once on
        // startup/load so every later getter can assume sane base values.
        component.BaseStrength = Math.Clamp(component.BaseStrength, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BasePerception = Math.Clamp(component.BasePerception, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseEndurance = Math.Clamp(component.BaseEndurance, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseCharisma = Math.Clamp(component.BaseCharisma, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseIntelligence = Math.Clamp(component.BaseIntelligence, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseAgility = Math.Clamp(component.BaseAgility, SpecialProfile.Minimum, SpecialProfile.Maximum);
        component.BaseLuck = Math.Clamp(component.BaseLuck, SpecialProfile.Minimum, SpecialProfile.Maximum);
        Dirty(uid, component);
    }

    private void ApplyTemporary(EntityUid uid, SpecialComponent component, SpecialStat stat, int modifier)
    {
        SetModifier(component, stat, GetModifier(component, stat) + modifier);
        Dirty(uid, component);
        RaiseSpecialChanged(uid);
    }

    private void RaiseSpecialChanged(EntityUid uid)
    {
        // Movement speed is one of the live stat consumers, so refresh it after
        // every SPECIAL change instead of requiring each caller to remember.
        var ev = new SpecialChangedEvent(uid);
        RaiseLocalEvent(uid, ref ev, true);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private static int GetBase(SpecialComponent component, SpecialStat stat)
    {
        return stat switch
        {
            SpecialStat.Strength => component.BaseStrength,
            SpecialStat.Perception => component.BasePerception,
            SpecialStat.Endurance => component.BaseEndurance,
            SpecialStat.Charisma => component.BaseCharisma,
            SpecialStat.Intelligence => component.BaseIntelligence,
            SpecialStat.Agility => component.BaseAgility,
            SpecialStat.Luck => component.BaseLuck,
            _ => SpecialProfile.DefaultValue,
        };
    }

    private static int GetModifier(SpecialComponent component, SpecialStat stat)
    {
        return stat switch
        {
            SpecialStat.Strength => component.TemporaryStrengthModifier,
            SpecialStat.Perception => component.TemporaryPerceptionModifier,
            SpecialStat.Endurance => component.TemporaryEnduranceModifier,
            SpecialStat.Charisma => component.TemporaryCharismaModifier,
            SpecialStat.Intelligence => component.TemporaryIntelligenceModifier,
            SpecialStat.Agility => component.TemporaryAgilityModifier,
            SpecialStat.Luck => component.TemporaryLuckModifier,
            _ => 0,
        };
    }

    private static void SetBase(SpecialComponent component, SpecialStat stat, int value)
    {
        switch (stat)
        {
            case SpecialStat.Strength:
                component.BaseStrength = value;
                break;
            case SpecialStat.Perception:
                component.BasePerception = value;
                break;
            case SpecialStat.Endurance:
                component.BaseEndurance = value;
                break;
            case SpecialStat.Charisma:
                component.BaseCharisma = value;
                break;
            case SpecialStat.Intelligence:
                component.BaseIntelligence = value;
                break;
            case SpecialStat.Agility:
                component.BaseAgility = value;
                break;
            case SpecialStat.Luck:
                component.BaseLuck = value;
                break;
        }
    }

    private static void SetModifier(SpecialComponent component, SpecialStat stat, int value)
    {
        switch (stat)
        {
            case SpecialStat.Strength:
                component.TemporaryStrengthModifier = value;
                break;
            case SpecialStat.Perception:
                component.TemporaryPerceptionModifier = value;
                break;
            case SpecialStat.Endurance:
                component.TemporaryEnduranceModifier = value;
                break;
            case SpecialStat.Charisma:
                component.TemporaryCharismaModifier = value;
                break;
            case SpecialStat.Intelligence:
                component.TemporaryIntelligenceModifier = value;
                break;
            case SpecialStat.Agility:
                component.TemporaryAgilityModifier = value;
                break;
            case SpecialStat.Luck:
                component.TemporaryLuckModifier = value;
                break;
        }
    }

    private readonly record struct TemporaryModifierEntry(
        EntityUid Entity,
        SpecialStat Stat,
        int Value,
        string Source,
        bool Expires,
        TimeSpan ExpiresAt);
}
