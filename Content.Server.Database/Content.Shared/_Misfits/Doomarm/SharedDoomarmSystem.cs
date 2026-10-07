// #Misfits Add - Doomarm: the surgery rules, the locked fist in the gloves slot, the metal colour fix,
// overheat bookkeeping and the "up in the sky" restrictions. Abilities live in the server system.

using System.Diagnostics.CodeAnalysis;
using Content.Shared._Shitmed.Autodoc.Components;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Examine;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Timing;

namespace Content.Shared._Misfits.Doomarm;

public abstract partial class SharedDoomarmSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected SharedBodySystem Body = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;

    public const string GlovesSlot = "gloves";

    public override void Initialize()
    {
        base.Initialize();

        // The fist in the gloves slot: only this system puts it on or takes it off.
        SubscribeLocalEvent<DoomarmFistComponent, BeingEquippedAttemptEvent>(OnFistEquipAttempt);
        SubscribeLocalEvent<DoomarmFistComponent, BeingUnequippedAttemptEvent>(OnFistUnequipAttempt);

        SubscribeLocalEvent<DoomarmPlatingComponent, BodyPartComponentsModifyEvent>(OnPlatingAttached);
        SubscribeLocalEvent<DoomarmComponent, ExaminedEvent>(OnExamined);

        // Every surgery step is relayed to the patient's body; this is where the attach rules are checked.
        SubscribeLocalEvent<HumanoidAppearanceComponent, SurgeryCanPerformStepEvent>(OnSurgeryCanPerform);

        // Up in the sky during Orbital Descent: you walk on air and can't attack.
        SubscribeLocalEvent<DoomarmAirborneComponent, IsWeightlessEvent>(OnAirborneWeightless);
        SubscribeLocalEvent<DoomarmAirborneComponent, AttackAttemptEvent>(OnAirborneAttack);
        SubscribeLocalEvent<DoomarmAirborneComponent, ShotAttemptedEvent>(OnAirborneShoot);
    }

    // ── Fist lock ────────────────────────────────────────────────────────

    private void OnFistEquipAttempt(Entity<DoomarmFistComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        // The Doomarm equips it with force, which skips this check. Nobody else can put it on.
        args.Reason = "doomarm-fist-locked";
        args.Cancel();
    }

    private void OnFistUnequipAttempt(Entity<DoomarmFistComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        args.Reason = "doomarm-fist-locked";
        args.Cancel();
    }

    // ── Appearance ───────────────────────────────────────────────────────

    private void OnPlatingAttached(Entity<DoomarmPlatingComponent> ent, ref BodyPartComponentsModifyEvent args)
    {
        // Mutant limbs carry the skin tint on their layer. Reset it to plain white so the metal art
        // keeps its own colours. The part's sprite id is set right after this and keeps the colour.
        if (!args.Add ||
            !TryComp<BodyPartComponent>(ent, out var part) ||
            part.ToHumanoidLayers() is not { } layer)
            return;

        _humanoid.SetBaseLayerColor(args.Body, layer, Color.White);
    }

    private void OnExamined(Entity<DoomarmComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var heat = (int) MathF.Round(GetHeat(ent.Comp));
        args.PushMarkup(Loc.GetString("doomarm-examine-heat", ("heat", heat)));
    }

    // ── Overheat ─────────────────────────────────────────────────────────

    /// <summary>
    /// Current heat after cooling since the last use.
    /// </summary>
    public float GetHeat(DoomarmComponent comp)
    {
        var cooled = (float) (Timing.CurTime - comp.HeatUpdatedAt).TotalSeconds * comp.CoolingPerSecond;
        return MathF.Max(0f, comp.Heat - cooled);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public bool IsAllowedSpecies(DoomarmComponent comp, EntityUid body)
    {
        return TryComp<HumanoidAppearanceComponent>(body, out var humanoid) &&
               comp.AllowedSpecies.Contains(humanoid.Species);
    }

    /// <summary>
    /// The working Doomarm attached to this body, if any. A crippled (disabled) arm doesn't count.
    /// </summary>
    public bool TryGetDoomarm(EntityUid body, [NotNullWhen(true)] out Entity<DoomarmComponent>? arm)
    {
        arm = null;
        foreach (var (partId, part) in Body.GetBodyChildrenOfType(body, BodyPartType.Arm))
        {
            if (!part.Enabled || !TryComp<DoomarmComponent>(partId, out var comp))
                continue;

            arm = (partId, comp);
            return true;
        }

        return false;
    }

    private bool HasAnyDoomarm(EntityUid body)
    {
        foreach (var (partId, _) in Body.GetBodyChildrenOfType(body, BodyPartType.Arm))
        {
            if (HasComp<DoomarmComponent>(partId))
                return true;
        }

        return false;
    }

    // ── Surgery ──────────────────────────────────────────────────────────

    private void OnSurgeryCanPerform(Entity<HumanoidAppearanceComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (args.Invalid != StepInvalidReason.None)
            return;

        // Only matters when the surgeon is holding a Doomarm, i.e. attaching one with the normal
        // "Attach Left/Right Arm" surgery.
        DoomarmComponent? doomarm = null;
        foreach (var tool in args.Tools)
        {
            if (TryComp(tool, out doomarm))
                break;
        }

        if (doomarm == null)
            return;

        string? reason = null;
        if (args.User == args.Body)
            reason = "doomarm-surgery-not-self";
        else if (HasComp<AutodocComponent>(args.User))
            reason = "doomarm-surgery-not-autodoc";
        else if (!IsAllowedSpecies(doomarm, args.Body))
            reason = "doomarm-surgery-wrong-species";
        else if (HasAnyDoomarm(args.Body))
            reason = "doomarm-surgery-already-has";

        if (reason == null)
            return;

        args.Invalid = StepInvalidReason.MissingSkills;
        args.Popup = Loc.GetString(reason);
    }

    // ── Airborne ─────────────────────────────────────────────────────────

    private void OnAirborneWeightless(Entity<DoomarmAirborneComponent> ent, ref IsWeightlessEvent args)
    {
        // The sky level is an empty map with no gravity. Treat them as standing so they can steer.
        args.IsWeightless = false;
        args.Handled = true;
    }

    private void OnAirborneAttack(Entity<DoomarmAirborneComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnAirborneShoot(Entity<DoomarmAirborneComponent> ent, ref ShotAttemptedEvent args)
    {
        args.Cancel();
    }
}
