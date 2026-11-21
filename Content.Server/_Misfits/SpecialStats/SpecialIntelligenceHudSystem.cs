using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Actions;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Overlays;

namespace Content.Server._Misfits.SpecialStats;

/// <summary>
/// Grants a medical HUD effect to characters with maximum Intelligence.
/// </summary>
public sealed class SpecialIntelligenceHudSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedSpecialSystem _special = default!;

    private const int MedicalHudIntelligenceThreshold = 10;
    private const string BiologicalDamageContainer = "Biological";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpecialChangedEvent>(OnSpecialChanged);
        SubscribeLocalEvent<SpecialStatsReadyEvent>(OnStatsReady);
        SubscribeLocalEvent<SpecialShutdownEvent>(OnSpecialShutdown);
        SubscribeLocalEvent<SpecialAppliedMedicalHudComponent, ComponentShutdown>(OnMedicalHudShutdown);
        SubscribeLocalEvent<SpecialAppliedMedicalHudComponent, SpecialMedicalHudToggleActionEvent>(OnMedicalHudToggle);
    }

    private void OnSpecialChanged(ref SpecialChangedEvent args)
    {
        if (TryComp<SpecialComponent>(args.ChangedEntity, out var special))
            ApplyMedicalHud((args.ChangedEntity, special));
    }

    private void OnStatsReady(ref SpecialStatsReadyEvent args)
    {
        if (TryComp<SpecialComponent>(args.Entity, out var special))
            ApplyMedicalHud((args.Entity, special));
    }

    private void OnSpecialShutdown(ref SpecialShutdownEvent args)
    {
        ClearMedicalHud(args.Entity);
    }

    private void ApplyMedicalHud(Entity<SpecialComponent> ent)
    {
        if (_special.GetEffective(ent.Owner, SpecialStat.Intelligence, ent.Comp) >= MedicalHudIntelligenceThreshold)
            EnsureMedicalHud(ent.Owner);
        else
            ClearMedicalHud(ent.Owner);
    }

    private void EnsureMedicalHud(EntityUid uid)
    {
        var applied = EnsureComp<SpecialAppliedMedicalHudComponent>(uid);
        _actions.AddAction(uid, ref applied.ActionEntity, applied.Action);
        SetMedicalHudEnabled(uid, applied, applied.Enabled);
    }

    private void SetMedicalHudEnabled(EntityUid uid, SpecialAppliedMedicalHudComponent applied, bool enabled)
    {
        applied.Enabled = enabled;
        _actions.SetToggled(applied.ActionEntity, enabled);

        if (!enabled)
        {
            ClearMedicalHudComponents(uid, applied);
            return;
        }

        if (!TryComp<ShowHealthBarsComponent>(uid, out var bars))
        {
            bars = EnsureComp<ShowHealthBarsComponent>(uid);
            applied.AddedHealthBars = true;
        }

        EnsureBiologicalContainer(bars.DamageContainers);
        Dirty(uid, bars);

        if (!TryComp<ShowHealthIconsComponent>(uid, out var icons))
        {
            icons = EnsureComp<ShowHealthIconsComponent>(uid);
            applied.AddedHealthIcons = true;
        }

        EnsureBiologicalContainer(icons.DamageContainers);
        Dirty(uid, icons);
    }

    private void ClearMedicalHud(EntityUid uid)
    {
        if (!TryComp<SpecialAppliedMedicalHudComponent>(uid, out var applied))
            return;

        ClearMedicalHudComponents(uid, applied);
        RemComp<SpecialAppliedMedicalHudComponent>(uid);
    }

    private void ClearMedicalHudComponents(EntityUid uid, SpecialAppliedMedicalHudComponent applied)
    {
        if (applied.AddedHealthBars)
        {
            RemComp<ShowHealthBarsComponent>(uid);
            applied.AddedHealthBars = false;
        }

        if (applied.AddedHealthIcons)
        {
            RemComp<ShowHealthIconsComponent>(uid);
            applied.AddedHealthIcons = false;
        }
    }

    private void OnMedicalHudShutdown(EntityUid uid, SpecialAppliedMedicalHudComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnMedicalHudToggle(EntityUid uid, SpecialAppliedMedicalHudComponent component, SpecialMedicalHudToggleActionEvent args)
    {
        if (args.Handled)
            return;

        SetMedicalHudEnabled(uid, component, !component.Enabled);

        args.Handled = true;
    }

    private static void EnsureBiologicalContainer(ICollection<string> damageContainers)
    {
        if (!damageContainers.Contains(BiologicalDamageContainer))
            damageContainers.Add(BiologicalDamageContainer);
    }
}
