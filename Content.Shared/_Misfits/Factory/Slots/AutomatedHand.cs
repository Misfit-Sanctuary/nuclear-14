// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;

namespace Content.Shared._Misfits.Factory.Slots;

/// <summary>
/// Abstraction over a specific hand of the machine.
/// </summary>
public sealed partial class AutomatedHand : AutomationSlot
{
    /// <summary>
    /// The name of the hand to use
    /// </summary>
    [DataField(required: true)]
    public string HandName = string.Empty;

    private SharedHandsSystem _hands;

    private Hand? _hand;

    [ViewVariables]
    public Hand? Hand
    {
        get
        {
            if (_hand != null)
                return _hand;

            _hands.TryGetHand(Owner, HandName, out _hand);
            return _hand;
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        _hands = EntMan.System<SharedHandsSystem>();
    }

    public override bool Insert(EntityUid item)
    {
        return base.Insert(item)
            && _hands.TryPickup(Owner, item, HandName);
    }

    public override bool CanInsert(EntityUid item)
    {
        return base.CanInsert(item)
            && Hand is {} hand
            && _hands.CanPickupToHand(Owner, item, hand);
    }

    public override EntityUid? GetItem(EntityUid? filter)
    {
        if (!_hands.TryGetActiveItem(Owner, out var item)
            || IsBlocked(filter, item.Value))
            return null;

        return item;
    }
}
