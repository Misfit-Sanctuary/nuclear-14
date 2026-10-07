// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Server.Stack;
using Content.Shared._Misfits.Factory.Filters;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Labels.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._Misfits.Factory.Filters;

public sealed partial class AutomationFilterSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private SharedCuffableSystem _cuffable = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private EntityQuery<AnchorableComponent> _anchorableQuery = default!;
    [Dependency] private EntityQuery<CuffableComponent> _cuffableQuery = default!;
    [Dependency] private EntityQuery<FilterSlotComponent> _slotQuery = default!;
    [Dependency] private EntityQuery<ItemSlotsComponent> _slotsQuery = default!;
    [Dependency] private EntityQuery<LabelComponent> _labelQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobQuery = default!;
    [Dependency] private EntityQuery<StackComponent> _stackQuery = default!;

    public static readonly int GateCount = Enum.GetValues(typeof(LogicGate)).Length;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LabelFilterComponent, LabelFilterSetLabelMessage>(OnLabelSet);
        SubscribeLocalEvent<LabelFilterComponent, ExaminedEvent>(OnLabelExamined);
        SubscribeLocalEvent<LabelFilterComponent, AutomationFilterEvent>(OnLabelFilter);

        SubscribeLocalEvent<NameFilterComponent, NameFilterSetNameMessage>(OnNameSet);
        SubscribeLocalEvent<NameFilterComponent, NameFilterSetModeMessage>(OnNameSetMode);
        SubscribeLocalEvent<NameFilterComponent, ExaminedEvent>(OnNameExamined);
        SubscribeLocalEvent<NameFilterComponent, AutomationFilterEvent>(OnNameFilter);

        SubscribeLocalEvent<StackFilterComponent, StackFilterSetMinMessage>(OnStackSetMin);
        SubscribeLocalEvent<StackFilterComponent, StackFilterSetSizeMessage>(OnStackSetSize);
        SubscribeLocalEvent<StackFilterComponent, ExaminedEvent>(OnStackExamined);
        SubscribeLocalEvent<StackFilterComponent, AutomationFilterEvent>(OnStackFilter);
        SubscribeLocalEvent<StackFilterComponent, AutomationFilterSplitEvent>(OnStackSplit);

        SubscribeLocalEvent<PressureFilterComponent, PressureFilterSetMinMessage>(OnPressureSetMin);
        SubscribeLocalEvent<PressureFilterComponent, PressureFilterSetMaxMessage>(OnPressureSetMax);
        SubscribeLocalEvent<PressureFilterComponent, ExaminedEvent>(OnPressureExamined);

        SubscribeLocalEvent<MobFilterComponent, MobFilterToggleMessage>(OnMobToggle);
        SubscribeLocalEvent<MobFilterComponent, AutomationFilterEvent>(OnMobFilter);
        SubscribeLocalEvent<MobFilterComponent, ExaminedEvent>(OnMobExamined);

        SubscribeLocalEvent<CombinedFilterComponent, ComponentInit>(OnCombinedInit);
        SubscribeLocalEvent<CombinedFilterComponent, UseInHandEvent>(OnCombinedUse);
        SubscribeLocalEvent<CombinedFilterComponent, ExaminedEvent>(OnCombinedExamined);
        SubscribeLocalEvent<CombinedFilterComponent, AutomationFilterEvent>(OnCombinedFilter);
        SubscribeLocalEvent<CombinedFilterComponent, AutomationFilterSplitEvent>(OnCombinedSplit);

        SubscribeLocalEvent<AnchorFilterComponent, AutomationFilterEvent>(OnAnchorFilter);

        SubscribeLocalEvent<CuffFilterComponent, AutomationFilterEvent>(OnCuffFilter);

        SubscribeLocalEvent<FilterSlotComponent, ComponentInit>(OnSlotInit);
    }

    /* Label filter */

    private void OnLabelSet(Entity<LabelFilterComponent> ent, ref LabelFilterSetLabelMessage args)
    {
        var label = args.Label.Trim();
        if (label.Length > ent.Comp.MaxLength)
            return;

        ent.Comp.Label = label;
        Dirty(ent);
    }

    private void OnLabelExamined(Entity<LabelFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (string.IsNullOrEmpty(ent.Comp.Label))
        {
            args.PushMarkup(Loc.GetString("automation-filter-examine-empty"));
            return;
        }

        args.PushText(Loc.GetString("automation-filter-examine-string", ("name", ent.Comp.Label)));
    }

    private void OnLabelFilter(Entity<LabelFilterComponent> ent, ref AutomationFilterEvent args)
    {
        args.Allowed = _labelQuery.CompOrNull(args.Item)?.CurrentLabel == ent.Comp.Label;
        args.CouldAllow = true; // hand labelers can change the label
    }

    /* Name filter */

    private void OnNameSet(Entity<NameFilterComponent> ent, ref NameFilterSetNameMessage args)
    {
        var name = args.Name.Trim();
        if (name.Length > ent.Comp.MaxLength || ent.Comp.Name == name)
            return;

        ent.Comp.Name = name;
        Dirty(ent);
    }

    private void OnNameSetMode(Entity<NameFilterComponent> ent, ref NameFilterSetModeMessage args)
    {
        if (ent.Comp.Mode == args.Mode)
            return;

        ent.Comp.Mode = args.Mode;
        Dirty(ent);
    }

    private void OnNameExamined(Entity<NameFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (string.IsNullOrEmpty(ent.Comp.Name))
        {
            args.PushMarkup(Loc.GetString("automation-filter-examine-empty"));
            return;
        }

        args.PushText(Loc.GetString("automation-filter-examine-string", ("name", ent.Comp.Name)));
    }

    private void OnNameFilter(Entity<NameFilterComponent> ent, ref AutomationFilterEvent args)
    {
        var name = Name(args.Item);
        var check = ent.Comp.Name;
        args.Allowed = ent.Comp.Mode switch
        {
            NameFilterMode.Contain => name.Contains(check),
            NameFilterMode.Start => name.StartsWith(check),
            NameFilterMode.End => name.EndsWith(check),
            NameFilterMode.Match => name == check,
            _ => false
        };
        // entity names usually don't change except for the end including a label
        args.CouldAllow = ent.Comp.Mode switch
        {
            NameFilterMode.End | NameFilterMode.Match => true,
            _ => false
        };
    }

    /* Stack filter */

    private void OnStackSetMin(Entity<StackFilterComponent> ent, ref StackFilterSetMinMessage args)
    {
        if (args.Min < 1 || ent.Comp.Min == args.Min)
            return;

        ent.Comp.Min = args.Min;
        Dirty(ent);
    }

    private void OnStackSetSize(Entity<StackFilterComponent> ent, ref StackFilterSetSizeMessage args)
    {
        if (args.Size < 0 || ent.Comp.Size == args.Size)
            return;

        ent.Comp.Size = args.Size;
        Dirty(ent);
    }

    private void OnStackExamined(Entity<StackFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("stack-filter-examine", ("size", ent.Comp.Size)));
    }

    private void OnStackFilter(Entity<StackFilterComponent> ent, ref AutomationFilterEvent args)
    {
        args.Allowed = _stackQuery.CompOrNull(args.Item)?.Count >= ent.Comp.Min;
        args.CouldAllow = true;
    }

    private void OnStackSplit(Entity<StackFilterComponent> ent, ref AutomationFilterSplitEvent args)
    {
        args.Size = ent.Comp.Size;
    }

    /* Combined filter */

    private void OnCombinedInit(Entity<CombinedFilterComponent> ent, ref ComponentInit args)
    {
        if (!_slotsQuery.TryComp(ent, out var slots))
            return;

        if (!_slots.TryGetSlot(ent.Owner, CombinedFilterComponent.FilterAName, out var filterA, slots) ||
            !_slots.TryGetSlot(ent.Owner, CombinedFilterComponent.FilterBName, out var filterB, slots))
        {
            Log.Error($"{ToPrettyString(ent)} was missing filter slots!");
            RemCompDeferred<CombinedFilterComponent>(ent);
            return;
        }

        ent.Comp.FilterA = filterA;
        ent.Comp.FilterB = filterB;
    }

    private void OnCombinedUse(Entity<CombinedFilterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var gate = (int) ent.Comp.Gate;
        gate = ++gate % GateCount;
        ent.Comp.Gate = (LogicGate) gate;
        Dirty(ent);

        var msg = Loc.GetString("logic-gate-cycle", ("gate", ent.Comp.Gate.ToString().ToUpper()));
        _popup.PopupEntity(msg, ent, args.User);
    }

    private void OnCombinedExamined(Entity<CombinedFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("combined-filter-examine", ("gate", ent.Comp.Gate.ToString().ToUpper())));
    }

    private void OnCombinedFilter(Entity<CombinedFilterComponent> ent, ref AutomationFilterEvent args)
    {
        var a = IsAllowed(ent.Comp.FilterA.Item, args.Item, out var couldAllowA);
        var b = IsAllowed(ent.Comp.FilterB.Item, args.Item, out var couldAllowB);
        args.Allowed = ent.Comp.Gate switch
        {
            LogicGate.Or => a || b,
            LogicGate.And => a && b,
            LogicGate.Xor => a != b,
            LogicGate.Nor => !(a || b),
            LogicGate.Nand => !(a && b),
            LogicGate.Xnor => a == b,
            _ => false
        };
        args.CouldAllow = couldAllowA || couldAllowB; // if any subfilter could allow it, this could allow it too
    }

    private void OnCombinedSplit(Entity<CombinedFilterComponent> ent, ref AutomationFilterSplitEvent args)
    {
        var a = GetSplitSize(ent.Comp.FilterA.Item);
        var b = GetSplitSize(ent.Comp.FilterB.Item);
        args.Size = Math.Max(a, b);
    }

    /* Pressure filter */

    private void OnPressureSetMin(Entity<PressureFilterComponent> ent, ref PressureFilterSetMinMessage args)
    {
        var min = args.Min;
        if (min == ent.Comp.Min || min > ent.Comp.Max || min < 0f)
            return;

        ent.Comp.Min = min;
        Dirty(ent);
    }

    private void OnPressureSetMax(Entity<PressureFilterComponent> ent, ref PressureFilterSetMaxMessage args)
    {
        var max = args.Max;
        if (max == ent.Comp.Max || max < ent.Comp.Min)
            return;

        ent.Comp.Max = max;
        Dirty(ent);
    }

    private void OnPressureExamined(Entity<PressureFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("pressure-filter-examine", ("min", ent.Comp.Min), ("max", ent.Comp.Max)));
    }

    /* Anchor filter */

    private void OnAnchorFilter(Entity<AnchorFilterComponent> ent, ref AutomationFilterEvent args)
    {
        // only care about anchorable objects, not walls etc which aren't useful to filter
        if (!_anchorableQuery.HasComp(args.Item))
            return;

        var setting = _toggle.IsActivated(ent.Owner);
        args.Allowed = Transform(args.Item).Anchored == setting;
        args.CouldAllow = true; // wrench
    }

    /* Mob filter */

    private void OnMobToggle(Entity<MobFilterComponent> ent, ref MobFilterToggleMessage args)
    {
        // no chudding out
        if (args is not { State: MobState.Alive or MobState.Dead or MobState.Critical or MobState.SoftCritical })
            return;

        if (!ent.Comp.States.Remove(args.State))
            ent.Comp.States.Add(args.State);
        Dirty(ent);
    }

    private void OnMobFilter(Entity<MobFilterComponent> ent, ref AutomationFilterEvent args)
    {
        if (!_mobQuery.TryComp(args.Item, out var mob))
            return;

        args.Allowed = ent.Comp.States.Contains(mob.CurrentState);
        args.CouldAllow = true; // dying and defibbing etc
    }

    private void OnMobExamined(Entity<MobFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(ent.Comp.States.Count == 0
            ? Loc.GetString("mob-filter-examine-unset")
            : Loc.GetString("mob-filter-examine-set", ("states", string.Join(", ", ent.Comp.States))));
    }

    /* Cuff filter */

    private void OnCuffFilter(Entity<CuffFilterComponent> ent, ref AutomationFilterEvent args)
    {
        if (!_cuffableQuery.TryComp(args.Item, out var cuffable))
            return;

        var setting = _toggle.IsActivated(ent.Owner);
        args.Allowed = _cuffable.IsCuffed((args.Item, cuffable)) == setting;
        args.CouldAllow = true;
    }

    /* Filter slot */

    private void OnSlotInit(Entity<FilterSlotComponent> ent, ref ComponentInit args)
    {
        if (!_slotsQuery.TryComp(ent, out var slots))
            return; // hopefully this is the add all comps test...

        // See the comment in OnCombinedInit about this call shape's difference from Goobstation's.
        if (!_slots.TryGetSlot(ent.Owner, ent.Comp.FilterSlotId, out var filterSlot, slots))
        {
            Log.Error($"Missing filter slot {ent.Comp.FilterSlotId} on {ToPrettyString(ent)}");
            RemCompDeferred<FilterSlotComponent>(ent);
            return;
        }

        ent.Comp.FilterSlot = filterSlot;
    }

    #region Public API
    /// <summary>
    /// Returns true if an item is allowed by the filter, false if it's blocked.
    /// If there is no filter, items are always allowed.
    /// </summary>
    public bool IsAllowed(EntityUid? filter, EntityUid item, out bool couldAllow)
    {
        couldAllow = false;
        if (filter is not {} uid)
            return true;

        var ev = new AutomationFilterEvent(item);
        RaiseLocalEvent(uid, ref ev);
        couldAllow = ev.CouldAllow;
        return ev.Allowed;
    }

    public bool IsAllowed(EntityUid? filter, EntityUid item) => IsAllowed(filter, item, out _);

    /// <summary>
    /// Inverse of <see cref="IsAllowed"/>.
    /// </summary>
    public bool IsBlocked(EntityUid? filter, EntityUid item, out bool couldAllow) => !IsAllowed(filter, item, out couldAllow);

    public bool IsBlocked(EntityUid? filter, EntityUid item) => IsBlocked(filter, item, out _);

    /// <summary>
    /// Returns true if an item can never be allowed by a filter, even if some data about it changes.
    /// </summary>
    public bool IsAlwaysBlocked(EntityUid? filter, EntityUid item) => IsBlocked(filter, item, out var couldAllow) && !couldAllow;

    public int GetSplitSize(EntityUid? filter)
    {
        if (filter is not {} uid)
            return 0;

        var ev = new AutomationFilterSplitEvent();
        RaiseLocalEvent(uid, ref ev);
        return ev.Size;
    }

    public EntityUid? TrySplit(EntityUid? filter, EntityUid item)
    {
        // if it's 0 don't need to split, take the item out directly
        var split = GetSplitSize(filter);
        if (split == 0)
            return item;

        // don't need to split if it's already a multiple of the split size
        var stack = _stackQuery.Comp(item);
        var excess = stack.Count % split;
        if (excess == 0)
            return item;

        // have to split it, client will return null here
        // nuclear-14's StackSystem.Split takes (uid, amount, spawnPosition, stack = null) rather than
        // Goobstation's (Entity<StackComponent>, amount, spawnPosition) - adapted accordingly.
        var coords = Transform(item).Coordinates;
        return _stack.Split(item, stack.Count - excess, coords, stack);
    }

    /// <summary>
    /// Get the filter in a machine's filter slot, or null if it has none.
    /// </summary>
    public EntityUid? GetSlot(EntityUid uid)
    {
        return _slotQuery.CompOrNull(uid)?.Filter;
    }
    #endregion
}
