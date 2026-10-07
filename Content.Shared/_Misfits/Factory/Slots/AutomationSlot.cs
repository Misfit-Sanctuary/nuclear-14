// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared._Misfits.Factory.Filters;
using Content.Shared.Chemistry.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Misfits.Factory.Slots;

/// <summary>
/// An abstraction over some way to insert/take an item from a machine.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class AutomationSlot
{
    [DataField]
    public ProtoId<SinkPortPrototype>? Input;

    [DataField]
    public ProtoId<SourcePortPrototype>? Output;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [ViewVariables]
    public EntityUid Owner;

    [Dependency] public IEntityManager EntMan = default!;
    protected EntityWhitelistSystem _whitelist;
    protected SharedDeviceLinkSystem _device;

    public virtual void Initialize()
    {
        IoCManager.InjectDependencies(this);

        _whitelist = EntMan.System<EntityWhitelistSystem>();
        _device = EntMan.System<SharedDeviceLinkSystem>();
    }

    /// <summary>
    /// Try to insert an item into the slot, returning true if it was removed from its previous container.
    /// Inheritors must override this and use <c>if (!base.Insert(uid, item)) return false;</c>
    /// </summary>
    public virtual bool Insert(EntityUid item)
    {
        return CanInsert(item);
    }

    /// <summary>
    /// Check if an item can be inserted into the slot, returning true if it can.
    /// Inheritors must override this and use <c>if (!base.CanInsert(uid, item)) return false;</c>
    /// </summary>
    public virtual bool CanInsert(EntityUid item)
    {
        return !_whitelist.IsBlacklistPass(Blacklist, item) && _whitelist.IsWhitelistPassOrNull(Whitelist, item);
    }

    public virtual EntityUid? GetItem(EntityUid? filter)
    {
        return null;
    }

    /// <summary>
    /// Called to add all of this slot's ports to the machine.
    /// </summary>
    public virtual void AddPorts()
    {
        if (Input is {} input)
            _device.EnsureSinkPorts(Owner, input);
        if (Output is {} output)
            _device.EnsureSourcePorts(Owner, output);
    }

    public virtual void RemovePorts()
    {
        if (Input is {} input)
            _device.RemoveSinkPort(Owner, input);
        if (Output is {} output)
            _device.RemoveSourcePort(Owner, output);
    }

    public virtual Entity<SolutionComponent>? GetSolution() => null;

    protected bool IsAllowed(EntityUid? filter, EntityUid item)
    {
        if (filter is not {} uid)
            return true;

        var ev = new AutomationFilterEvent(item);
        EntMan.EventBus.RaiseLocalEvent(uid, ref ev);
        return ev.Allowed;
    }

    protected bool IsBlocked(EntityUid? filter, EntityUid item) => !IsAllowed(filter, item);
}
